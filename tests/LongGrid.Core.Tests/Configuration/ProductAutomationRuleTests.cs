using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductAutomationRuleTests
{
    [Fact]
    public void PreviewUsesRealUnicodeMetadataWithoutChangingFileContent()
    {
        using var sandbox = new TemporaryDirectory();
        string matching = CreateFile(sandbox.Path, "项目-甲.txt", "真实内容-甲");
        string ignored = CreateFile(sandbox.Path, "项目-乙.png", "真实内容-乙");
        string[] before = [Hash(matching), Hash(ignored)];
        ProductAutomationRulePreviewSnapshot preview =
            ProductAutomationRulePreviewPlanner.Create(
                State(), 1, 8, true, Catalog(matching, ignored), Rule());

        Assert.True(preview.CanApply);
        Assert.Equal(ProductAutomationRulePreviewStatus.Ready, preview.Status);
        Assert.Single(preview.Matches);
        Assert.Equal("项目-甲.txt", preview.Matches[0].DisplayName);
        Assert.All(new[] { preview.WorkspaceFingerprint, preview.CatalogFingerprint,
            preview.RuleFingerprint }, fingerprint => Assert.Equal(64, fingerprint.Length));
        Assert.Equal(before, new[] { Hash(matching), Hash(ignored) });
    }

    [Fact]
    public void PreviewReportsZeroConflictAndMissingTargetWithoutMutation()
    {
        DesktopCatalogEntry entry = Entry("C:\\真实桌面\\项目.txt");
        ProductWorkspaceState state = State();
        ProductAutomationRulePreviewSnapshot zero =
            ProductAutomationRulePreviewPlanner.Create(
                state, 1, 1, true, [entry], Rule(value: ".pdf"));
        ProductAutomationRulePreviewSnapshot missing =
            ProductAutomationRulePreviewPlanner.Create(
                state, 1, 1, true, [entry], Rule(target: "missing"));
        ProductWorkspaceState conflicting = state with
        {
            Rules = [Rule(id: "existing", target: "other")],
            Containers = [.. state.Containers, Container("other", "其他方格")],
        };
        ProductAutomationRulePreviewSnapshot conflict =
            ProductAutomationRulePreviewPlanner.Create(
                conflicting, 1, 1, true, [entry], Rule());

        Assert.Equal(ProductAutomationRulePreviewStatus.ZeroMatches, zero.Status);
        Assert.Equal(ProductAutomationRulePreviewStatus.TargetMissing, missing.Status);
        Assert.Equal(ProductAutomationRulePreviewStatus.Conflict, conflict.Status);
        Assert.False(zero.CanApply || missing.CanApply || conflict.CanApply);
        Assert.Empty(state.Rules);
        Assert.Empty(state.Containers[0].Items);
    }

    [Fact]
    public async Task AtomicApplyPersistsRestartsAndSupportsUndoRedo()
    {
        using var sandbox = new TemporaryDirectory();
        string desktop = Directory.CreateDirectory(
            Path.Combine(sandbox.Path, "真实桌面")).FullName;
        string first = CreateFile(desktop, "资料-甲.txt", "不可修改-甲");
        string second = CreateFile(desktop, "资料-乙.txt", "不可修改-乙");
        string[] before = [Hash(first), Hash(second)];
        DesktopCatalogEntry[] catalog = Catalog(first, second);
        var store = new ProductConfigurationStore(Path.Combine(sandbox.Path, "配置"));
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State();
        long revision = commits.AdvanceExternalRevision();
        ProductAutomationRulePreviewSnapshot preview =
            ProductAutomationRulePreviewPlanner.Create(
                state, revision, 9, true, catalog, Rule());

        ProductAutomationRuleCommitResult committed =
            commits.CommitAutomationRule(state, 9, catalog, preview);
        await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved);

        Assert.True(committed.IsAccepted);
        Assert.Single(committed.State!.Rules);
        Assert.Equal(2, committed.State.Containers[0].Items.Count);
        ProductConfigurationLoadResult loaded = await store.LoadAsync();
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, loaded.Status);
        Assert.Single(loaded.Document!.Rules);
        Assert.Equal(2, loaded.Document.Containers[0].Items.Count);
        ProductWorkspaceResolutionResult restarted =
            ProductWorkspaceConfigurationResolver.Resolve(loaded.Document, catalog);
        Assert.True(restarted.IsSuccess);
        Assert.Single(restarted.State!.Rules);

        ProductWorkspaceSessionHistoryCommitResult undone =
            commits.CommitSessionHistoryNavigation(
                committed.State, ProductWorkspaceSessionHistoryDirection.Undo);
        Assert.True(undone.IsAccepted);
        Assert.Empty(undone.State!.Rules);
        Assert.Empty(undone.State.Containers[0].Items);
        ProductWorkspaceSessionHistoryCommitResult redone =
            commits.CommitSessionHistoryNavigation(
                undone.State, ProductWorkspaceSessionHistoryDirection.Redo);
        Assert.True(redone.IsAccepted);
        Assert.Single(redone.State!.Rules);
        Assert.Equal(2, redone.State.Containers[0].Items.Count);
        await saves.CompleteAsync();
        Assert.Equal(before, new[] { Hash(first), Hash(second) });
    }

    [Fact]
    public async Task PreviewCancelAndStaleInputsSubmitNothing()
    {
        using var sandbox = new TemporaryDirectory();
        string file = CreateFile(sandbox.Path, "真实项目.txt", "保持不变");
        var workflow = new CountingWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State();
        long revision = commits.AdvanceExternalRevision();
        DesktopCatalogEntry[] catalog = Catalog(file);
        ProductAutomationRulePreviewSnapshot preview =
            ProductAutomationRulePreviewPlanner.Create(
                state, revision, 4, true, catalog, Rule());

        Assert.Equal(0, workflow.SaveCalls);
        Assert.Equal(ProductAutomationRuleCommitStatus.StaleCatalogGeneration,
            commits.CommitAutomationRule(state, 5, catalog, preview).Status);
        Assert.Equal(ProductAutomationRuleCommitStatus.StaleEditRevision,
            commits.CommitAutomationRule(
                state, 4, catalog, preview with { WorkspaceRevision = 0 }).Status);
        Assert.Equal(ProductAutomationRuleCommitStatus.StalePreview,
            commits.CommitAutomationRule(
                state, 4, [Entry(file, "已变化.txt")], preview).Status);
        Assert.Equal(0, workflow.SaveCalls);
        Assert.Empty(state.Rules);
        Assert.Empty(state.Containers[0].Items);
        Assert.Equal("保持不变", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public void VersionFiveMigratesEmptyRulesAndVersionSixRoundTripsRules()
    {
        byte[] versionFive = Encoding.UTF8.GetBytes(
            """
            { "schemaVersion": 5, "profileId": "default", "containers": [] }
            """);
        ProductConfigurationDocument migrated =
            ProductConfigurationJson.Deserialize(versionFive);
        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion,
            migrated.SchemaVersion);
        Assert.Empty(migrated.Rules);

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(State() with
            {
                Rules = [Rule()],
            });
        Assert.True(projection.IsSuccess);
        Assert.Single(ProductConfigurationJson.Deserialize(
            ProductConfigurationJson.SerializeToUtf8Bytes(projection.Document!)).Rules);
    }

    [Fact]
    public void OldSchemaCannotSmuggleRulesAndTargetDeletionDisablesRepairableRule()
    {
        byte[] smuggled = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 5,
              "profileId": "default",
              "containers": [],
              "rules": [{
                "id":"r", "name":"r", "enabled":false, "priority":0,
                "targetContainerId":"missing", "matchMode":"all",
                "action":"assignSafeReference",
                "conditions":[{"kind":"extension","value":".txt"}]
              }]
            }
            """);
        Assert.Equal(ProductConfigurationError.UnsupportedSchema,
            Assert.Throws<ProductConfigurationContractException>(
                () => ProductConfigurationJson.Deserialize(smuggled)).Error);

        ProductWorkspaceState state = State() with { Rules = [Rule()] };
        ProductWorkspaceEditResult removed =
            ProductWorkspaceReducer.RemoveContainer(state, "target");
        Assert.True(removed.IsSuccess);
        Assert.Empty(removed.State!.Containers);
        Assert.False(Assert.Single(removed.State.Rules).Enabled);
        Assert.True(ProductWorkspaceConfigurationProjector.Project(removed.State).IsSuccess);
    }

    [Fact]
    public void LifecycleReducerEditsCopiesTogglesRemovesAndOrdersRules()
    {
        ProductAutomationRuleState first = Rule(id: "first");
        ProductAutomationRuleState second = Rule(id: "second") with
        {
            Name = "第二条",
            Enabled = false,
        };
        ProductWorkspaceState state = State() with { Rules = [first, second] };

        ProductWorkspaceEditResult updated = ProductWorkspaceReducer.EditAutomationRule(
            state,
            new(ProductAutomationRuleLifecycleAction.Update, first.Id,
                first with { Name = "已编辑", Priority = 20 }));
        Assert.True(updated.IsSuccess);
        Assert.Equal("已编辑", updated.State!.Rules[0].Name);

        ProductAutomationRuleState copy = second with
        {
            Id = "copy",
            Name = "第二条 副本",
            Enabled = false,
        };
        ProductWorkspaceEditResult copied = ProductWorkspaceReducer.EditAutomationRule(
            updated.State,
            new(ProductAutomationRuleLifecycleAction.Duplicate, second.Id, copy));
        Assert.Equal(3, copied.State!.Rules.Count);
        Assert.False(copied.State.Rules[2].Enabled);

        ProductWorkspaceEditResult enabled = ProductWorkspaceReducer.EditAutomationRule(
            copied.State,
            new(ProductAutomationRuleLifecycleAction.SetEnabled, second.Id,
                Enabled: true));
        Assert.True(enabled.State!.Rules[1].Enabled);

        ProductWorkspaceEditResult moved = ProductWorkspaceReducer.EditAutomationRule(
            enabled.State,
            new(ProductAutomationRuleLifecycleAction.MoveEarlier, "copy"));
        Assert.Equal("copy", moved.State!.Rules[1].Id);

        ProductWorkspaceEditResult removed = ProductWorkspaceReducer.EditAutomationRule(
            moved.State,
            new(ProductAutomationRuleLifecycleAction.Remove, first.Id));
        Assert.Equal(["copy", "second"], removed.State!.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task LifecycleCommitPersistsOneRevisionAndSupportsUnifiedUndoRedo()
    {
        var workflow = new CountingWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State() with { Rules = [Rule()] };
        long revision = commits.AdvanceExternalRevision();
        ProductAutomationRuleState replacement = state.Rules[0] with
        {
            Name = "重命名规则",
            Priority = 99,
        };

        ProductAutomationRuleLifecycleCommitResult committed =
            commits.CommitAutomationRuleLifecycle(
                state,
                new(revision, new(ProductAutomationRuleLifecycleAction.Update,
                    replacement.Id, replacement)));

        Assert.True(committed.IsAccepted);
        Assert.Equal(revision + 1, committed.EditRevision);
        Assert.Equal("重命名规则", committed.State!.Rules[0].Name);
        Assert.Equal(1, workflow.SaveCalls);
        ProductWorkspaceSessionHistorySnapshot history =
            commits.GetSessionHistorySnapshot(committed.State);
        Assert.Equal(ProductWorkspaceSessionHistoryActionKind.RuleEdit,
            Assert.Single(history.Items).Kind);
        ProductWorkspaceSessionHistoryCommitResult undone =
            commits.CommitSessionHistoryNavigation(
                committed.State, ProductWorkspaceSessionHistoryDirection.Undo);
        Assert.True(undone.IsAccepted);
        Assert.Equal("文本资料", undone.State!.Rules[0].Name);
        ProductWorkspaceSessionHistoryCommitResult redone =
            commits.CommitSessionHistoryNavigation(
                undone.State, ProductWorkspaceSessionHistoryDirection.Redo);
        Assert.True(redone.IsAccepted);
        Assert.Equal("重命名规则", redone.State!.Rules[0].Name);
    }

    [Fact]
    public async Task LifecycleRejectsStaleBoundaryAndUnsafeEnableWithoutSaving()
    {
        var workflow = new CountingWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductAutomationRuleState disabled = Rule(target: "missing") with
        {
            Enabled = false,
        };
        ProductWorkspaceState state = State() with { Rules = [disabled] };
        long revision = commits.AdvanceExternalRevision();

        Assert.Equal(ProductAutomationRuleLifecycleCommitStatus.StaleEditRevision,
            commits.CommitAutomationRuleLifecycle(state,
                new(revision - 1, new(ProductAutomationRuleLifecycleAction.Remove,
                    disabled.Id))).Status);
        Assert.Equal(ProductAutomationRuleLifecycleCommitStatus.ReducerRejected,
            commits.CommitAutomationRuleLifecycle(state,
                new(revision, new(ProductAutomationRuleLifecycleAction.SetEnabled,
                    disabled.Id, Enabled: true))).Status);
        Assert.Equal(ProductAutomationRuleLifecycleCommitStatus.ReducerRejected,
            commits.CommitAutomationRuleLifecycle(state,
                new(revision, new(ProductAutomationRuleLifecycleAction.MoveEarlier,
                    disabled.Id))).Status);
        ProductWorkspaceState full = State() with
        {
            Rules = Enumerable.Range(0, ProductConfigurationLimits.MaximumRules)
                .Select(index => Rule(id: $"rule-{index}") with { Enabled = false })
                .ToArray(),
        };
        ProductWorkspaceEditResult overflow = ProductWorkspaceReducer.EditAutomationRule(
            full,
            new(ProductAutomationRuleLifecycleAction.Duplicate, "rule-0",
                Rule(id: "overflow") with { Enabled = false }));
        Assert.False(overflow.IsSuccess);
        ProductWorkspaceEditResult duplicateId =
            ProductWorkspaceReducer.EditAutomationRule(
                state,
                new(ProductAutomationRuleLifecycleAction.Duplicate, disabled.Id,
                    disabled));
        Assert.False(duplicateId.IsSuccess);
        Assert.Equal(0, workflow.SaveCalls);
    }

    private static ProductWorkspaceState State() => new()
    {
        ProfileId = "default",
        Containers = [Container("target", "目标方格")],
    };

    private static ProductContainerState Container(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
        Placement = new()
        {
            DisplayKey = "display-unassigned",
            WidthDip = 360,
            HeightDip = 240,
        },
        Items = [],
    };

    private static ProductAutomationRuleState Rule(
        string id = "rule-text",
        string target = "target",
        string value = ".txt") => new()
        {
            Id = id,
            Name = "文本资料",
            Enabled = true,
            Priority = 10,
            TargetContainerId = target,
            MatchMode = ProductAutomationRuleMatchMode.All,
            Action = ProductAutomationRuleActionKind.AssignSafeReference,
            Conditions = [new()
        {
            Kind = ProductAutomationRuleConditionKind.Extension,
            Value = value,
        }],
        };

    private static DesktopCatalogEntry[] Catalog(params string[] paths) =>
        paths.Select(path => Entry(path)).ToArray();

    private static DesktopCatalogEntry Entry(string path, string? name = null) =>
        new(new DesktopItemIdentity("filesystem", path), "user-desktop",
            name ?? Path.GetFileName(path), DesktopItemKind.File);

    private static string CreateFile(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus expected)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (saves.Snapshot.Status == expected) return;
            await Task.Delay(5);
        }
        Assert.Equal(expected, saves.Snapshot.Status);
    }

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CountingWorkflow : IProductConfigurationSaveWorkflow
    {
        private int calls;
        public int SaveCalls => Volatile.Read(ref calls);
        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.Saved, null, false));
        }
        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.NoRetryAvailable, null, false));
        public void DiscardRetry() { }
        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "LongGrid.AutomationRule.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
