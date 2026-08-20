using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSelectedReferenceContainerCommitTests
{
    [Fact]
    public async Task RealStoreMovesSelectedReferencesAtomicallyWithoutChangingFiles()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.SelectedCreate.RealStore",
            Guid.NewGuid().ToString("N"));
        string storeDirectory = Path.Combine(sandbox, "store");
        string firstPath = Path.Combine(sandbox, "first.txt");
        string secondPath = Path.Combine(sandbox, "second.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(firstPath, "first-original");
        await File.WriteAllTextAsync(secondPath, "second-original");
        try
        {
            ProductItemReferenceState first = Item("item-1", firstPath);
            ProductItemReferenceState second = Item("item-2", secondPath);
            ProductWorkspaceState before = State(
                Container("source", "Source") with { Items = [first, second] });
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(before).Document!);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long revision = commits.AdvanceExternalRevision();

            ProductWorkspaceSelectedReferenceContainerCommitResult created =
                commits.CommitSelectedReferenceContainer(
                    before,
                    new(
                        revision,
                        SourceContainerOrdinal: 1,
                        ItemIds: ["item-1", "item-2"],
                        NewContainer: Container("selected", "Selected")));
            await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved, 1);
            ProductConfigurationLoadResult loadedCreated = await store.LoadAsync();

            Assert.True(created.IsAccepted);
            Assert.Empty(created.State!.Containers[0].Items);
            Assert.Equal(
                ["item-1", "item-2"],
                created.State.Containers[1].Items.Select(item => item.Id));
            Assert.Empty(loadedCreated.Document!.Containers[0].Items);
            Assert.Equal(2, loadedCreated.Document.Containers[1].Items.Count);
            Assert.Equal(firstPath, loadedCreated.Document.Containers[1].Items[0].Target);
            Assert.Equal(secondPath, loadedCreated.Document.Containers[1].Items[1].Target);
            Assert.Equal("first-original", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second-original", await File.ReadAllTextAsync(secondPath));

            ProductWorkspaceReferenceBatchAdditionUndoCommitResult undone =
                commits.CommitReferenceBatchAdditionUndo(
                    created.State,
                    created.UndoToken!,
                    confirmed: true);
            await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved, 2);
            ProductConfigurationLoadResult loadedUndone = await store.LoadAsync();

            Assert.True(undone.IsAccepted);
            Assert.Single(undone.State!.Containers);
            Assert.Equal(2, undone.State.Containers[0].Items.Count);
            Assert.Single(loadedUndone.Document!.Containers);
            Assert.Equal(2, loadedUndone.Document.Containers[0].Items.Count);
            Assert.Equal("first-original", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second-original", await File.ReadAllTextAsync(secondPath));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvalidSelectionsNeverSubmitPartialState()
    {
        var workflow = new CountingWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow,
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var commits = new ProductWorkspaceCommitCoordinator(saves);
        long revision = commits.AdvanceExternalRevision();
        ProductWorkspaceState state = State(
            Container("source", "Source") with
            {
                Items = [Item("item-1", Path.Combine(Path.GetTempPath(), "one.txt"))],
            });

        ProductWorkspaceSelectedReferenceContainerCommitResult empty = Commit(
            commits, state, revision, []);
        ProductWorkspaceSelectedReferenceContainerCommitResult duplicate = Commit(
            commits, state, revision, ["item-1", "item-1"]);
        ProductWorkspaceSelectedReferenceContainerCommitResult missing = Commit(
            commits, state, revision, ["missing"]);
        ProductWorkspaceSelectedReferenceContainerCommitResult stale = Commit(
            commits, state, revision - 1, ["item-1"]);
        ProductWorkspaceSelectedReferenceContainerCommitResult locked = Commit(
            commits,
            state with
            {
                Containers = [state.Containers[0] with { IsLocked = true }],
            },
            revision,
            ["item-1"]);

        Assert.Equal(
            ProductWorkspaceSelectedReferenceContainerCommitStatus.InvalidRequest,
            empty.Status);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceContainerCommitStatus.InvalidRequest,
            duplicate.Status);
        Assert.Equal(ProductWorkspaceEditError.ItemNotFound, missing.EditError);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceContainerCommitStatus.StaleEditRevision,
            stale.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, locked.EditError);
        Assert.Equal(0, workflow.SaveCalls);
        Assert.Equal(revision, commits.CurrentEditRevision);
    }

    [Fact]
    public async Task RealWriteLeaseFailureKeepsDiskBaselineAndUndoRestoresMemoryIntent()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.SelectedCreate.RealLeaseFailure",
            Guid.NewGuid().ToString("N"));
        string storeDirectory = Path.Combine(sandbox, "store");
        string desktopFile = Path.Combine(sandbox, "keep.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            ProductWorkspaceState before = State(
                Container("source", "Source") with
                {
                    Items = [Item("item-1", desktopFile)],
                });
            var store = new ProductConfigurationStore(
                storeDirectory,
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(before).Document!);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long revision = commits.AdvanceExternalRevision();

            ProductWorkspaceSelectedReferenceContainerCommitResult created;
            await using (var lease = new FileStream(
                store.WriteLeasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                created = commits.CommitSelectedReferenceContainer(
                    before,
                    new(
                        revision,
                        SourceContainerOrdinal: 1,
                        ItemIds: ["item-1"],
                        NewContainer: Container("selected", "Selected")));
                await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Failed, 1);
                Assert.Equal(
                    ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                    saves.Snapshot.Failure);
                ProductConfigurationLoadResult stillBaseline = await store.LoadAsync();
                Assert.Single(stillBaseline.Document!.Containers);
                Assert.Single(stillBaseline.Document.Containers[0].Items);
            }

            ProductWorkspaceReferenceBatchAdditionUndoCommitResult undone =
                commits.CommitReferenceBatchAdditionUndo(
                    created.State!,
                    created.UndoToken!,
                    confirmed: true);
            await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved, 2);
            ProductConfigurationLoadResult restored = await store.LoadAsync();

            Assert.True(created.IsAccepted);
            Assert.True(undone.IsAccepted);
            Assert.Single(restored.Document!.Containers);
            Assert.Single(restored.Document.Containers[0].Items);
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceSelectedReferenceContainerCommitResult Commit(
        ProductWorkspaceCommitCoordinator commits,
        ProductWorkspaceState state,
        long revision,
        IReadOnlyList<string> itemIds) =>
        commits.CommitSelectedReferenceContainer(
            state,
            new(
                revision,
                SourceContainerOrdinal: 1,
                itemIds,
                Container("selected", "Selected")));

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus status,
        long revision)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (saves.Snapshot.Status != status
            || saves.Snapshot.CurrentRevision != revision)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static ProductWorkspaceState State(params ProductContainerState[] containers) =>
        new()
        {
            ProfileId = "default",
            Containers = containers,
        };

    private static ProductContainerState Container(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Appearance = new()
            {
                Color = "#2563EB",
                Opacity = 0.88,
            },
            Placement = new()
            {
                DisplayKey = "display-unassigned",
                WidthDip = 360,
                HeightDip = 240,
            },
            Items = Array.Empty<ProductItemReferenceState>(),
        };

    private static ProductItemReferenceState Item(string id, string path) =>
        ProductItemReferenceState.CreateResolved(
            id,
            new(
                new DesktopItemIdentity("filesystem", path),
                "user-desktop",
                Path.GetFileName(path),
                DesktopItemKind.File));

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CountingWorkflow : IProductConfigurationSaveWorkflow
    {
        private int saveCalls;

        internal int SaveCalls => Volatile.Read(ref saveCalls);

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref saveCalls);
            return Task.FromResult(new ProductConfigurationSaveAttemptResult(
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
