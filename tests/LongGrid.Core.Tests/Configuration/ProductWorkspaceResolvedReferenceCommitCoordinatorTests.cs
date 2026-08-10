using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceResolvedReferenceCommitCoordinatorTests
{
    [Fact]
    public async Task AcceptedReferenceSubmitsOnceAndNeverChangesDesktopFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ResolvedReferenceCommit.Tests",
            Guid.NewGuid().ToString("N"));
        string desktopFile = Path.Combine(sandbox, "keep.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            DesktopCatalogEntry entry = Entry(desktopFile);

            ProductWorkspaceResolvedReferenceCommitResult result =
                coordinator.CommitResolvedReference(
                    State(Container("container-1", "Work")),
                    currentCatalogGeneration: 7,
                    [entry],
                    new(revision, 7, ContainerOrdinal: 1, CatalogIndex: 0));
            _ = await saves.CompleteAsync();

            Assert.True(result.IsAccepted);
            Assert.Equal(2, result.EditRevision);
            ProductItemReferenceState item =
                Assert.Single(result.State!.Containers[0].Items);
            Assert.Equal(ProductItemReferenceResolution.Resolved, item.Resolution);
            Assert.Equal(entry, item.CatalogEntry);
            Assert.StartsWith("item-", item.Id, StringComparison.Ordinal);
            Assert.Single(result.Document!.Containers[0].Items);
            Assert.Equal(1, workflow.SaveCalls);
            Assert.True(File.Exists(desktopFile));
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task StaleEditOrCatalogGenerationNeverSubmits()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceState state = State(Container("container-1", "Work"));
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "one.txt"));

        ProductWorkspaceResolvedReferenceCommitResult staleEdit =
            coordinator.CommitResolvedReference(
                state,
                7,
                [entry],
                new(revision - 1, 7, 1, 0));
        ProductWorkspaceResolvedReferenceCommitResult staleCatalog =
            coordinator.CommitResolvedReference(
                state,
                7,
                [entry],
                new(revision, 6, 1, 0));

        Assert.Equal(
            ProductWorkspaceResolvedReferenceCommitStatus.StaleEditRevision,
            staleEdit.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceCommitStatus.StaleCatalogGeneration,
            staleCatalog.Status);
        Assert.Equal(revision, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task LockedContainerAndDuplicateWorkspaceReferenceAreRejected()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "same.txt"));
        ProductContainerState locked = Container("container-1", "Locked") with
        {
            IsLocked = true,
        };
        ProductContainerState existing = Container("container-2", "Existing") with
        {
            Items =
            [
                ProductItemReferenceState.CreateResolved("item-existing", entry),
            ],
        };

        ProductWorkspaceResolvedReferenceCommitResult lockedResult =
            coordinator.CommitResolvedReference(
                State(locked),
                7,
                [entry],
                new(revision, 7, 1, 0));
        ProductWorkspaceResolvedReferenceCommitResult duplicateResult =
            coordinator.CommitResolvedReference(
                State(Container("container-1", "Target"), existing),
                7,
                [entry],
                new(revision, 7, 1, 0));

        Assert.Equal(
            ProductWorkspaceResolvedReferenceCommitStatus.ReducerRejected,
            lockedResult.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceCommitStatus.AlreadyReferenced,
            duplicateResult.Status);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    public async Task InvalidOrdinalsAndIndexesNeverSubmit(
        int containerOrdinal,
        int catalogIndex)
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceResolvedReferenceCommitResult result =
            coordinator.CommitResolvedReference(
                State(Container("container-1", "Work")),
                7,
                [Entry(Path.Combine(Path.GetTempPath(), "one.txt"))],
                new(revision, 7, containerOrdinal, catalogIndex));

        Assert.Equal(
            ProductWorkspaceResolvedReferenceCommitStatus.InvalidRequest,
            result.Status);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task ResolvedReferenceRemovalCanBeUndoneOnceWithoutChangingFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ResolvedReferenceRemoval.Tests",
            Guid.NewGuid().ToString("N"));
        string desktopFile = Path.Combine(sandbox, "keep.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            DesktopCatalogEntry entry = Entry(desktopFile);
            ProductContainerState container = Container("container-1", "Work") with
            {
                Items =
                [
                    ProductItemReferenceState.CreateResolved("item-1", entry),
                ],
            };
            ProductWorkspaceState before = State(container);

            ProductWorkspaceResolvedReferenceRemovalCommitResult removal =
                coordinator.CommitResolvedReferenceRemoval(
                    before,
                    new(revision, ContainerOrdinal: 1, ItemOrdinal: 1));
            Assert.Equal(
                removal.UndoToken,
                coordinator.CurrentReferenceRemovalUndoToken);
            ProductWorkspaceReferenceRemovalUndoCommitResult undo =
                coordinator.CommitReferenceRemovalUndo(
                    removal.State!,
                    removal.UndoToken!,
                    confirmed: true);
            ProductWorkspaceReferenceRemovalUndoCommitResult secondUndo =
                coordinator.CommitReferenceRemovalUndo(
                    undo.State!,
                    removal.UndoToken!,
                    confirmed: true);
            _ = await saves.CompleteAsync();

            Assert.True(removal.IsAccepted);
            Assert.Empty(removal.State!.Containers[0].Items);
            Assert.True(undo.IsAccepted);
            Assert.Single(undo.State!.Containers[0].Items);
            Assert.Equal(
                ProductWorkspaceReferenceRemovalUndoStatus.Unavailable,
                secondUndo.UndoStatus);
            Assert.InRange(workflow.SaveCalls, 1, 2);
            Assert.True(File.Exists(desktopFile));
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task RemovalRejectsStaleAndLockedTargets()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "one.txt"));
        ProductContainerState resolved = Container("container-1", "Work") with
        {
            Items =
            [
                ProductItemReferenceState.CreateResolved("item-1", entry),
            ],
        };
        ProductContainerState locked = resolved with { IsLocked = true };
        ProductWorkspaceResolvedReferenceRemovalCommitResult stale =
            coordinator.CommitResolvedReferenceRemoval(
                State(resolved),
                new(revision - 1, 1, 1));
        ProductWorkspaceResolvedReferenceRemovalCommitResult lockedResult =
            coordinator.CommitResolvedReferenceRemoval(
                State(locked),
                new(revision, 1, 1));
        Assert.Equal(
            ProductWorkspaceResolvedReferenceRemovalCommitStatus.StaleEditRevision,
            stale.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(0, workflow.SaveCalls);
    }

    private static ProductWorkspaceSaveController CreateSaves(
        IProductConfigurationSaveWorkflow workflow) =>
        new(
            workflow,
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));

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

    private static DesktopCatalogEntry Entry(string target) =>
        new(
            new DesktopItemIdentity("filesystem", target),
            "user-desktop",
            Path.GetFileName(target),
            DesktopItemKind.File);

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeWorkflow : IProductConfigurationSaveWorkflow
    {
        private int saveCalls;

        public int SaveCalls => Volatile.Read(ref saveCalls);

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref saveCalls);
            return Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.Saved,
                    null,
                    CanRetry: false));
        }

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                    null,
                    CanRetry: false));

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
