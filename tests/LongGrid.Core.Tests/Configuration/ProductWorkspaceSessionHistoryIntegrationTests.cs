using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSessionHistoryIntegrationTests
{
    [Fact]
    public async Task ConsecutiveEditsSupportApplyUndoRedoUndo()
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State("Before");
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult renamed = Rename(
            coordinator, state, revision, "After");
        ProductWorkspaceContainerCommitResult collapsed = Collapse(
            coordinator, renamed.State!, renamed.EditRevision, true);
        ProductWorkspaceSessionHistoryCommitResult undo = Navigate(
            coordinator, collapsed.State!, ProductWorkspaceSessionHistoryDirection.Undo);
        ProductWorkspaceSessionHistoryCommitResult redo = Navigate(
            coordinator, undo.State!, ProductWorkspaceSessionHistoryDirection.Redo);
        ProductWorkspaceSessionHistoryCommitResult finalUndo = Navigate(
            coordinator, redo.State!, ProductWorkspaceSessionHistoryDirection.Undo);
        ProductWorkspaceSessionHistorySnapshot snapshot =
            coordinator.GetSessionHistorySnapshot(finalUndo.State);

        Assert.True(renamed.IsAccepted);
        Assert.True(collapsed.IsAccepted);
        Assert.True(undo.IsAccepted);
        Assert.Equal("After", undo.State!.Containers[0].Name);
        Assert.False(undo.State.Containers[0].Appearance.Collapsed);
        Assert.True(redo.State!.Containers[0].Appearance.Collapsed);
        Assert.False(finalUndo.State!.Containers[0].Appearance.Collapsed);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal(1, snapshot.Cursor);
        Assert.True(snapshot.CanUndo);
        Assert.True(snapshot.CanRedo);
    }

    [Fact]
    public async Task CreateLockAndAppearanceAreFormalHistoryItems()
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerCommitResult created = coordinator.CommitContainer(
            EmptyState(),
            new(ProductWorkspaceContainerCommitAction.Create, revision, 0, "Inbox",
                Container("container-1", "Inbox")));
        ProductWorkspaceContainerCommitResult locked = coordinator.CommitContainer(
            created.State!,
            new(ProductWorkspaceContainerCommitAction.SetLocked,
                created.EditRevision, 1, string.Empty, StateValue: true));
        ProductWorkspaceContainerCommitResult unlocked = coordinator.CommitContainer(
            locked.State!,
            new(ProductWorkspaceContainerCommitAction.SetLocked,
                locked.EditRevision, 1, string.Empty, StateValue: false));
        ProductWorkspaceContainerCommitResult appearance =
            coordinator.CommitContainer(
                unlocked.State!,
                new(ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                    unlocked.EditRevision, 1, string.Empty,
                    ColorPreset: ProductWorkspaceContainerColorPreset.Emerald,
                    OpacityPreset: ProductWorkspaceContainerOpacityPreset.Soft));

        ProductWorkspaceSessionHistorySnapshot snapshot =
            coordinator.GetSessionHistorySnapshot(appearance.State);
        Assert.Equal(4, snapshot.Items.Count);
        Assert.Equal(
            [
                ProductWorkspaceSessionHistoryActionKind.Appearance,
                ProductWorkspaceSessionHistoryActionKind.Locked,
                ProductWorkspaceSessionHistoryActionKind.Locked,
                ProductWorkspaceSessionHistoryActionKind.Create,
            ],
            snapshot.Items.Select(item => item.Kind));
        Assert.All(snapshot.Items, item =>
        {
            Assert.Equal("方格", item.TargetType);
            Assert.Equal(1, item.TargetCount);
        });
    }

    [Theory]
    [InlineData(ProductWorkspaceContainerCommitAction.Create)]
    [InlineData(ProductWorkspaceContainerCommitAction.Rename)]
    [InlineData(ProductWorkspaceContainerCommitAction.SetLocked)]
    [InlineData(ProductWorkspaceContainerCommitAction.SetCollapsed)]
    [InlineData(ProductWorkspaceContainerCommitAction.SetAppearancePreset)]
    public async Task EachIncludedActionSupportsApplyUndoRedoUndo(
        ProductWorkspaceContainerCommitAction action)
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState before = action ==
            ProductWorkspaceContainerCommitAction.Create
                ? EmptyState()
                : State("Before");
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerCommitRequest request = action switch
        {
            ProductWorkspaceContainerCommitAction.Create =>
                new(action, revision, 0, "Inbox", Container("created", "Inbox")),
            ProductWorkspaceContainerCommitAction.Rename =>
                new(action, revision, 1, "After"),
            ProductWorkspaceContainerCommitAction.SetLocked =>
                new(action, revision, 1, string.Empty, StateValue: true),
            ProductWorkspaceContainerCommitAction.SetCollapsed =>
                new(action, revision, 1, string.Empty, StateValue: true),
            ProductWorkspaceContainerCommitAction.SetAppearancePreset =>
                new(action, revision, 1, string.Empty,
                    ColorPreset: ProductWorkspaceContainerColorPreset.Emerald,
                    OpacityPreset: ProductWorkspaceContainerOpacityPreset.Soft),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        ProductWorkspaceContainerCommitResult applied =
            coordinator.CommitContainer(before, request);
        ProductWorkspaceSessionHistoryCommitResult undo = Navigate(
            coordinator, applied.State!, ProductWorkspaceSessionHistoryDirection.Undo);
        ProductWorkspaceSessionHistoryCommitResult redo = Navigate(
            coordinator, undo.State!, ProductWorkspaceSessionHistoryDirection.Redo);
        ProductWorkspaceSessionHistoryCommitResult finalUndo = Navigate(
            coordinator, redo.State!, ProductWorkspaceSessionHistoryDirection.Undo);

        Assert.True(applied.IsAccepted);
        Assert.True(undo.IsAccepted);
        Assert.True(redo.IsAccepted);
        Assert.True(finalUndo.IsAccepted);
        Assert.Equal(Fingerprint(before), Fingerprint(finalUndo.State!));
    }

    [Fact]
    public async Task RejectedAndNoChangeEditsDoNotCreateSuccessfulHistory()
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State("Before");
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult noChange = Rename(
            coordinator, state, revision, "Before");
        ProductWorkspaceContainerCommitResult rejected = Rename(
            coordinator,
            state with
            {
                Containers = [state.Containers[0] with { IsLocked = true }],
            },
            revision,
            "Blocked");

        Assert.Equal(ProductWorkspaceContainerCommitStatus.NoChange, noChange.Status);
        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            rejected.Status);
        Assert.Empty(coordinator.GetSessionHistorySnapshot(state).Items);
    }

    [Fact]
    public async Task FiftyStepCapacityEvictsOnlyOldestSuccessfulAction()
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State("Name-0");
        long revision = coordinator.AdvanceExternalRevision();
        for (int index = 1; index <= 51; index++)
        {
            ProductWorkspaceContainerCommitResult result = Rename(
                coordinator, state, revision, $"Name-{index}");
            Assert.True(result.IsAccepted);
            state = result.State!;
            revision = result.EditRevision;
        }

        ProductWorkspaceSessionHistorySnapshot snapshot =
            coordinator.GetSessionHistorySnapshot(state);
        Assert.Equal(50, snapshot.Capacity);
        Assert.Equal(50, snapshot.Items.Count);
        Assert.Equal(50, snapshot.Cursor);
        Assert.Equal("Name-51", snapshot.Items[0].TargetName);
        Assert.Equal("Name-2", snapshot.Items[^1].TargetName);
    }

    [Fact]
    public async Task NewActionAfterUndoTruncatesRedoAndExternalChangeHasReason()
    {
        await using ProductWorkspaceSaveController saves = Saves(new Workflow());
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State("Before");
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerCommitResult renamed = Rename(
            coordinator, state, revision, "After");
        ProductWorkspaceContainerCommitResult collapsed = Collapse(
            coordinator, renamed.State!, renamed.EditRevision, true);
        ProductWorkspaceSessionHistoryCommitResult undo = Navigate(
            coordinator, collapsed.State!, ProductWorkspaceSessionHistoryDirection.Undo);
        ProductWorkspaceContainerCommitResult appearance = coordinator.CommitContainer(
            undo.State!,
            new(ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                undo.EditRevision, 1, string.Empty,
                ColorPreset: ProductWorkspaceContainerColorPreset.Amber,
                OpacityPreset: ProductWorkspaceContainerOpacityPreset.Subtle));
        ProductWorkspaceSessionHistorySnapshot branched =
            coordinator.GetSessionHistorySnapshot(appearance.State);
        ProductWorkspaceState external = State("Outside history");
        ProductWorkspaceSessionHistorySnapshot invalid =
            coordinator.GetSessionHistorySnapshot(external);

        Assert.Equal(2, branched.Items.Count);
        Assert.False(branched.CanRedo);
        Assert.DoesNotContain(branched.Items, item => item.ActionText == "折叠方格");
        Assert.NotNull(invalid.UnavailableReason);
        Assert.False(invalid.CanUndo);
        Assert.Equal(
            ProductWorkspaceSessionHistoryNavigationStatus.CurrentConfigurationChanged,
            Navigate(coordinator, external,
                ProductWorkspaceSessionHistoryDirection.Undo).Status);
    }

    [Fact]
    public async Task FailedUndoSaveCompensatesToPreUndoState()
    {
        var workflow = new Workflow(failSaveCall: 3);
        await using ProductWorkspaceSaveController saves = Saves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State("Before");
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerCommitResult renamed = Rename(
            coordinator, state, revision, "After");
        await WaitForSavedRevisionAsync(saves, 1);
        ProductWorkspaceContainerCommitResult collapsed = Collapse(
            coordinator, renamed.State!, renamed.EditRevision, true);
        await WaitForSavedRevisionAsync(saves, 2);
        ProductWorkspaceSessionHistoryCommitResult undo = Navigate(
            coordinator, collapsed.State!, ProductWorkspaceSessionHistoryDirection.Undo);
        await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Failed);

        ProductWorkspaceSessionHistoryCommitResult compensation =
            coordinator.CompensateSessionHistoryNavigation(
                undo.State!, undo.NavigationToken!);
        ProductWorkspaceSaveCompletionResult completion = await saves.CompleteAsync();

        Assert.False(undo.State!.Containers[0].Appearance.Collapsed);
        Assert.True(compensation.IsAccepted);
        Assert.True(compensation.IsCompensation);
        Assert.True(compensation.State!.Containers[0].Appearance.Collapsed);
        Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, completion.Status);
        Assert.Equal(4, workflow.SaveCalls);
    }

    [Fact]
    public async Task RealStorePersistsApplyUndoRedoUndoWithoutChangingUserFile()
    {
        string sandbox = Path.Combine(Path.GetTempPath(),
            "LongGrid.SessionHistory.Integration", Guid.NewGuid().ToString("N"));
        string storeDirectory = Path.Combine(sandbox, "store");
        string userFile = Path.Combine(sandbox, "用户资料.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(userFile, "keep-real-content");
        string expectedHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(userFile)));
        try
        {
            ProductItemReferenceState item = ProductItemReferenceState.CreateResolved(
                "item-1",
                new DesktopCatalogEntry(
                    new DesktopItemIdentity("filesystem", userFile),
                    "user-desktop", "用户资料.txt", DesktopItemKind.File));
            ProductWorkspaceState state = State("Before") with
            {
                Containers = [Container("container-1", "Before") with { Items = [item] }],
            };
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using ProductWorkspaceSaveController saves = Saves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            ProductWorkspaceContainerCommitResult renamed = Rename(
                coordinator, state, revision, "After");
            ProductWorkspaceSessionHistoryCommitResult undo = Navigate(
                coordinator, renamed.State!, ProductWorkspaceSessionHistoryDirection.Undo);
            ProductWorkspaceSessionHistoryCommitResult redo = Navigate(
                coordinator, undo.State!, ProductWorkspaceSessionHistoryDirection.Redo);
            ProductWorkspaceSessionHistoryCommitResult finalUndo = Navigate(
                coordinator, redo.State!, ProductWorkspaceSessionHistoryDirection.Undo);
            _ = await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();
            string actualHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(userFile)));

            Assert.Equal("Before", finalUndo.State!.Containers[0].Name);
            Assert.Equal("Before", reloaded.Document!.Containers[0].Name);
            Assert.Equal(expectedHash, actualHash);
            Assert.Equal("keep-real-content", await File.ReadAllTextAsync(userFile));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceSessionHistoryCommitResult Navigate(
        ProductWorkspaceCommitCoordinator coordinator,
        ProductWorkspaceState state,
        ProductWorkspaceSessionHistoryDirection direction) =>
        coordinator.CommitSessionHistoryNavigation(state, direction);

    private static string Fingerprint(ProductWorkspaceState state) =>
        ProductWorkspaceConfigurationFingerprint.Compute(
            ProductWorkspaceConfigurationProjector.Project(state).Document!);

    private static ProductWorkspaceContainerCommitResult Rename(
        ProductWorkspaceCommitCoordinator coordinator,
        ProductWorkspaceState state,
        long revision,
        string name) => coordinator.CommitContainer(
            state,
            new(ProductWorkspaceContainerCommitAction.Rename, revision, 1, name));

    private static ProductWorkspaceContainerCommitResult Collapse(
        ProductWorkspaceCommitCoordinator coordinator,
        ProductWorkspaceState state,
        long revision,
        bool collapsed) => coordinator.CommitContainer(
            state,
            new(ProductWorkspaceContainerCommitAction.SetCollapsed,
                revision, 1, string.Empty, StateValue: collapsed));

    private static ProductWorkspaceSaveController Saves(
        IProductConfigurationSaveWorkflow workflow) =>
        new(workflow, new ImmediateScheduler(), TimeSpan.FromMilliseconds(1));

    private static ProductWorkspaceState EmptyState() => new()
    {
        ProfileId = "default",
        Containers = Array.Empty<ProductContainerState>(),
    };

    private static ProductWorkspaceState State(string name) => new()
    {
        ProfileId = "default",
        Containers = [Container("container-1", name)],
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
        Items = Array.Empty<ProductItemReferenceState>(),
    };

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus expected)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (saves.Snapshot.Status == expected)
            {
                return;
            }
            await Task.Delay(5);
        }
        Assert.Equal(expected, saves.Snapshot.Status);
    }

    private static async Task WaitForSavedRevisionAsync(
        ProductWorkspaceSaveController saves,
        long revision)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (saves.Snapshot.Status == ProductWorkspaceSaveStatus.Saved
                && saves.Snapshot.SavedRevision == revision)
            {
                return;
            }
            await Task.Delay(5);
        }
        Assert.Equal(revision, saves.Snapshot.SavedRevision);
    }

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class Workflow(int failSaveCall = 0)
        : IProductConfigurationSaveWorkflow
    {
        private int saveCalls;
        public int SaveCalls => Volatile.Read(ref saveCalls);

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref saveCalls);
            return Task.FromResult(call == failSaveCall
                ? new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    ProductConfigurationSaveError.IoFailure,
                    CanRetry: true)
                : new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.Saved,
                    null,
                    CanRetry: false));
        }

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                null,
                CanRetry: false));

        public void DiscardRetry()
        {
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
