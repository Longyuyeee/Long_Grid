using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductQuickStartSuggestionTests
{
    [Fact]
    public void PlannerUsesRealUnicodeFilesWithoutChangingTheirContent()
    {
        using var sandbox = new TemporaryDirectory();
        string first = CreateFile(sandbox.Path, "项目甲.txt", "真实内容-甲");
        string second = CreateFile(sandbox.Path, "演示文稿.pptx", "真实内容-乙");
        string[] before = [Hash(first), Hash(second)];

        ProductQuickStartSuggestionSnapshot suggestion =
            ProductQuickStartSuggestionPlanner.Create(
                EmptyState(), 1, 7, true, Catalog(first, second));

        Assert.True(suggestion.CanCommit);
        Assert.Equal(["项目甲.txt", "演示文稿.pptx"],
            suggestion.Items.Select(item => item.DisplayName));
        Assert.Equal(64, suggestion.WorkspaceFingerprint.Length);
        Assert.Equal(64, suggestion.CatalogFingerprint.Length);
        Assert.Equal(before, new[] { Hash(first), Hash(second) });
    }

    [Fact]
    public void PlannerHasFiniteUnavailableEmptyAndTruncatedOutcomes()
    {
        ProductWorkspaceState nonEmpty = EmptyState() with
        {
            Containers = [Container("existing", "已有方格")],
        };
        Assert.Equal(ProductQuickStartSuggestionStatus.WorkspaceNotEmpty,
            ProductQuickStartSuggestionPlanner.Create(nonEmpty, 1, 1, true, []).Status);
        Assert.Equal(ProductQuickStartSuggestionStatus.CatalogUnavailable,
            ProductQuickStartSuggestionPlanner.Create(EmptyState(), 1, 0, false, []).Status);
        Assert.Equal(ProductQuickStartSuggestionStatus.NoItems,
            ProductQuickStartSuggestionPlanner.Create(EmptyState(), 1, 1, true, []).Status);

        DesktopCatalogEntry[] many = Enumerable.Range(0, 260)
            .Select(index => Entry($"C:\\真实桌面\\项目-{index}.txt"))
            .ToArray();
        ProductQuickStartSuggestionSnapshot truncated =
            ProductQuickStartSuggestionPlanner.Create(EmptyState(), 1, 1, true, many);
        Assert.Equal(ProductQuickStartSuggestionPlanner.MaximumSuggestedItemCount,
            truncated.Items.Count);
        Assert.Equal(260, truncated.TotalCandidateCount);
        Assert.True(truncated.IsTruncated);
    }

    [Fact]
    public async Task ConfirmPersistsRealReferencesAndSupportsUndoRedo()
    {
        using var sandbox = new TemporaryDirectory();
        string desktop = Directory.CreateDirectory(
            Path.Combine(sandbox.Path, "实际桌面")).FullName;
        string first = CreateFile(desktop, "项目甲.txt", "不可修改-甲");
        string second = CreateFile(desktop, "项目乙.txt", "不可修改-乙");
        string[] before = [Hash(first), Hash(second)];
        DesktopCatalogEntry[] catalog = Catalog(first, second);
        var store = new ProductConfigurationStore(Path.Combine(sandbox.Path, "配置"));
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState empty = EmptyState();
        long revision = commits.AdvanceExternalRevision();
        ProductQuickStartSuggestionSnapshot preview =
            ProductQuickStartSuggestionPlanner.Create(empty, revision, 9, true, catalog);

        ProductQuickStartCommitResult committed = commits.CommitQuickStart(
            empty, 9, catalog, new(preview, Container("quick-start", preview.ContainerName)));
        await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved);

        Assert.True(committed.IsAccepted);
        Assert.Equal(2, committed.State!.Containers[0].Items.Count);
        Assert.Equal(1, saves.Snapshot.CurrentRevision);
        ProductConfigurationLoadResult persisted = await store.LoadAsync();
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, persisted.Status);
        Assert.Equal(2, persisted.Document!.Containers[0].Items.Count);
        ProductWorkspaceSessionHistorySnapshot history =
            commits.GetSessionHistorySnapshot(committed.State);
        Assert.Single(history.Items);
        Assert.Equal(ProductWorkspaceSessionHistoryActionKind.QuickStart,
            history.Items[0].Kind);

        ProductWorkspaceSessionHistoryCommitResult undone =
            commits.CommitSessionHistoryNavigation(
                committed.State, ProductWorkspaceSessionHistoryDirection.Undo);
        Assert.True(undone.IsAccepted);
        Assert.Empty(undone.State!.Containers);
        ProductWorkspaceSessionHistoryCommitResult redone =
            commits.CommitSessionHistoryNavigation(
                undone.State, ProductWorkspaceSessionHistoryDirection.Redo);
        Assert.True(redone.IsAccepted);
        Assert.Equal(2, redone.State!.Containers[0].Items.Count);
        await saves.CompleteAsync();
        Assert.Equal(before, new[] { Hash(first), Hash(second) });
    }

    [Fact]
    public async Task CancelAndStalePreviewNeverSubmit()
    {
        using var sandbox = new TemporaryDirectory();
        string file = CreateFile(sandbox.Path, "真实项目.txt", "不可修改");
        var workflow = new CountingWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState empty = EmptyState();
        long revision = commits.AdvanceExternalRevision();
        DesktopCatalogEntry[] catalog = Catalog(file);
        ProductQuickStartSuggestionSnapshot preview =
            ProductQuickStartSuggestionPlanner.Create(empty, revision, 3, true, catalog);

        Assert.Equal(0, workflow.SaveCalls); // preview then cancel
        ProductQuickStartCommitResult staleCatalog = commits.CommitQuickStart(
            empty, 4, catalog, new(preview, Container("one", "桌面项目")));
        ProductQuickStartCommitResult staleRevision = commits.CommitQuickStart(
            empty, 3, catalog, new(preview with { WorkspaceRevision = 0 },
                Container("two", "桌面项目")));
        ProductQuickStartCommitResult staleFingerprint = commits.CommitQuickStart(
            empty, 3, [Entry(file, "已变化.txt")],
            new(preview, Container("three", "桌面项目")));

        Assert.Equal(ProductQuickStartCommitStatus.StaleCatalogGeneration,
            staleCatalog.Status);
        Assert.Equal(ProductQuickStartCommitStatus.StaleEditRevision,
            staleRevision.Status);
        Assert.Equal(ProductQuickStartCommitStatus.StalePreview,
            staleFingerprint.Status);
        Assert.Equal(0, workflow.SaveCalls);
        Assert.Equal("不可修改", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task FailedSaveCompensatesWholeQuickStartWithoutChangingFiles()
    {
        using var sandbox = new TemporaryDirectory();
        string file = CreateFile(sandbox.Path, "真实项目.txt", "补偿后保持");
        string before = Hash(file);
        var workflow = new FailOnceWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState empty = EmptyState();
        long revision = commits.AdvanceExternalRevision();
        DesktopCatalogEntry[] catalog = Catalog(file);
        ProductQuickStartSuggestionSnapshot preview =
            ProductQuickStartSuggestionPlanner.Create(empty, revision, 5, true, catalog);
        ProductQuickStartCommitResult committed = commits.CommitQuickStart(
            empty, 5, catalog, new(preview, Container("quick", "桌面项目")));
        await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Failed);

        ProductWorkspaceReferenceBatchAdditionUndoCommitResult compensated =
            commits.CommitReferenceBatchAdditionUndo(
                committed.State!, committed.CompensationToken!, true);
        await saves.CompleteAsync();

        Assert.True(compensated.IsAccepted);
        Assert.Empty(compensated.State!.Containers);
        Assert.Empty(commits.GetSessionHistorySnapshot(compensated.State).Items);
        Assert.Equal(2, workflow.SaveCalls);
        Assert.Equal(before, Hash(file));
    }

    private static ProductWorkspaceState EmptyState() => new()
    {
        ProfileId = "default",
        Containers = [],
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

    private class CountingWorkflow : IProductConfigurationSaveWorkflow
    {
        private int calls;
        public int SaveCalls => Volatile.Read(ref calls);
        public virtual Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.Saved, null, false));
        }
        protected void Count() => Interlocked.Increment(ref calls);
        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.NoRetryAvailable, null, false));
        public void DiscardRetry() { }
        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FailOnceWorkflow : CountingWorkflow
    {
        public override Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Count();
            return Task.FromResult(SaveCalls == 1
                ? new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    ProductConfigurationSaveError.IoFailure, true)
                : new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.Saved, null, false));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "LongGrid.QuickStart.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
