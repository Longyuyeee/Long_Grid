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

    [Fact]
    public async Task BatchRemovalIsAtomicAndCanBeUndoneOnceWithoutChangingFiles()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ResolvedReferenceBatchRemoval.Tests",
            Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(sandbox, "first.txt");
        string secondPath = Path.Combine(sandbox, "second.txt");
        string thirdPath = Path.Combine(sandbox, "third.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(firstPath, "first-original");
        await File.WriteAllTextAsync(secondPath, "second-original");
        await File.WriteAllTextAsync(thirdPath, "third-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            ProductWorkspaceState before = State(Container("container-1", "Work") with
            {
                Items =
                [
                    ProductItemReferenceState.CreateResolved("item-1", Entry(firstPath)),
                    ProductItemReferenceState.CreateResolved("item-2", Entry(secondPath)),
                    ProductItemReferenceState.CreateResolved("item-3", Entry(thirdPath)),
                ],
            });

            ProductWorkspaceResolvedReferenceBatchRemovalCommitResult removal =
                coordinator.CommitResolvedReferenceBatchRemoval(
                    before,
                    new(revision, 1, [1, 3]));
            ProductWorkspaceReferenceRemovalUndoCommitResult undo =
                coordinator.CommitReferenceRemovalUndo(
                    removal.State!,
                    removal.UndoToken!,
                    confirmed: true);
            ProductWorkspaceReferenceRemovalUndoCommitResult repeated =
                coordinator.CommitReferenceRemovalUndo(
                    undo.State!,
                    removal.UndoToken!,
                    confirmed: true);
            _ = await saves.CompleteAsync();

            Assert.True(removal.IsAccepted);
            Assert.Equal("item-2", Assert.Single(removal.State!.Containers[0].Items).Id);
            Assert.Equal(2, removal.EditRevision);
            Assert.True(undo.IsAccepted);
            Assert.Equal(3, undo.State!.Containers[0].Items.Count);
            Assert.Equal(
                ProductWorkspaceReferenceRemovalUndoStatus.Unavailable,
                repeated.UndoStatus);
            Assert.InRange(workflow.SaveCalls, 1, 2);
            Assert.Equal("first-original", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second-original", await File.ReadAllTextAsync(secondPath));
            Assert.Equal("third-original", await File.ReadAllTextAsync(thirdPath));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidBatchRemovalNeverSubmitsPartialState()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "one.txt"));
        ProductWorkspaceState state = State(Container("container-1", "Work") with
        {
            Items = [ProductItemReferenceState.CreateResolved("item-1", entry)],
        });

        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult stale =
            coordinator.CommitResolvedReferenceBatchRemoval(
                state,
                new(revision - 1, 1, [1]));
        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult duplicate =
            coordinator.CommitResolvedReferenceBatchRemoval(
                state,
                new(revision, 1, [1, 1]));
        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult missing =
            coordinator.CommitResolvedReferenceBatchRemoval(
                state,
                new(revision, 1, [1, 2]));
        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult tooLarge =
            coordinator.CommitResolvedReferenceBatchRemoval(
                state,
                new(revision, 1, Enumerable.Range(1, 257).ToArray()));
        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult locked =
            coordinator.CommitResolvedReferenceBatchRemoval(
                State(state.Containers[0] with { IsLocked = true }),
                new(revision, 1, [1]));

        Assert.Equal(
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                .StaleEditRevision,
            stale.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.InvalidRequest,
            duplicate.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.InvalidRequest,
            missing.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.InvalidRequest,
            tooLarge.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, locked.EditError);
        Assert.Equal(revision, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task ReassignmentMovesOnceAndCanBeUndoneWithoutChangingFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ResolvedReferenceReassignment.Tests",
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
            ProductContainerState source = Container("container-1", "Source") with
            {
                Items =
                [
                    ProductItemReferenceState.CreateResolved("item-1", entry),
                ],
            };
            ProductWorkspaceState before =
                State(source, Container("container-2", "Target"));

            ProductWorkspaceResolvedReferenceReassignmentCommitResult reassignment =
                coordinator.CommitResolvedReferenceReassignment(
                    before,
                    new(revision, 1, 1, 2));
            Assert.Equal(
                reassignment.UndoToken,
                coordinator.CurrentReferenceReassignmentUndoToken);
            ProductWorkspaceReferenceReassignmentUndoCommitResult undo =
                coordinator.CommitReferenceReassignmentUndo(
                    reassignment.State!,
                    reassignment.UndoToken!,
                    confirmed: true);
            ProductWorkspaceReferenceReassignmentUndoCommitResult secondUndo =
                coordinator.CommitReferenceReassignmentUndo(
                    undo.State!,
                    reassignment.UndoToken!,
                    confirmed: true);
            _ = await saves.CompleteAsync();

            Assert.True(reassignment.IsAccepted);
            Assert.Empty(reassignment.State!.Containers[0].Items);
            Assert.Equal(
                "item-1",
                Assert.Single(reassignment.State.Containers[1].Items).Id);
            Assert.True(undo.IsAccepted);
            Assert.Equal("item-1", Assert.Single(undo.State!.Containers[0].Items).Id);
            Assert.Empty(undo.State.Containers[1].Items);
            Assert.Equal(
                ProductWorkspaceReferenceReassignmentUndoStatus.Unavailable,
                secondUndo.UndoStatus);
            Assert.InRange(workflow.SaveCalls, 1, 2);
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task BatchReassignmentIsAtomicAndCanBeUndoneOnceWithoutChangingFiles()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ResolvedReferenceBatchReassignment.Tests",
            Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(sandbox, "first.txt");
        string secondPath = Path.Combine(sandbox, "second.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(firstPath, "first-original");
        await File.WriteAllTextAsync(secondPath, "second-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            ProductContainerState source = Container("container-1", "Source") with
            {
                Items =
                [
                    ProductItemReferenceState.CreateResolved(
                        "item-1",
                        Entry(firstPath)),
                    ProductItemReferenceState.CreateResolved(
                        "item-2",
                        Entry(secondPath)),
                ],
            };
            ProductWorkspaceState before =
                State(source, Container("container-2", "Target"));

            ProductWorkspaceResolvedReferenceReassignmentCommitResult reassignment =
                coordinator.CommitResolvedReferenceReassignment(
                    before,
                    new(revision, 1, [2, 1], 2));
            ProductWorkspaceReferenceReassignmentUndoCommitResult undo =
                coordinator.CommitReferenceReassignmentUndo(
                    reassignment.State!,
                    reassignment.UndoToken!,
                    confirmed: true);
            ProductWorkspaceReferenceReassignmentUndoCommitResult repeated =
                coordinator.CommitReferenceReassignmentUndo(
                    undo.State!,
                    reassignment.UndoToken!,
                    confirmed: true);
            _ = await saves.CompleteAsync();

            Assert.True(reassignment.IsAccepted);
            Assert.Empty(reassignment.State!.Containers[0].Items);
            Assert.Equal(
                ["item-1", "item-2"],
                reassignment.State.Containers[1].Items.Select(item => item.Id));
            Assert.Equal(revision + 1, reassignment.EditRevision);
            Assert.True(undo.IsAccepted);
            Assert.Equal(
                ["item-1", "item-2"],
                undo.State!.Containers[0].Items.Select(item => item.Id));
            Assert.Empty(undo.State.Containers[1].Items);
            Assert.Equal(revision + 2, undo.EditRevision);
            Assert.Equal(
                ProductWorkspaceReferenceReassignmentUndoStatus.Unavailable,
                repeated.UndoStatus);
            Assert.InRange(workflow.SaveCalls, 1, 2);
            Assert.Equal("first-original", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second-original", await File.ReadAllTextAsync(secondPath));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task ReassignmentRejectsStaleSameAndLockedContainers()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "one.txt"));
        ProductContainerState source = Container("container-1", "Source") with
        {
            Items =
            [
                ProductItemReferenceState.CreateResolved("item-1", entry),
            ],
        };
        ProductContainerState locked = Container("container-2", "Locked") with
        {
            IsLocked = true,
        };
        ProductWorkspaceState state = State(source, locked);

        ProductWorkspaceResolvedReferenceReassignmentCommitResult stale =
            coordinator.CommitResolvedReferenceReassignment(
                state,
                new(revision - 1, 1, 1, 2));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult same =
            coordinator.CommitResolvedReferenceReassignment(
                state,
                new(revision, 1, 1, 1));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult lockedResult =
            coordinator.CommitResolvedReferenceReassignment(
                state,
                new(revision, 1, 1, 2));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult lockedSource =
            coordinator.CommitResolvedReferenceReassignment(
                State(
                    source with { IsLocked = true },
                    Container("container-2", "Target")),
                new(revision, 1, 1, 2));
        ProductWorkspaceState unlocked =
            State(source, Container("container-2", "Target"));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult duplicate =
            coordinator.CommitResolvedReferenceReassignment(
                unlocked,
                new(revision, 1, [1, 1], 2));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult missing =
            coordinator.CommitResolvedReferenceReassignment(
                unlocked,
                new(revision, 1, [1, 2], 2));
        ProductWorkspaceResolvedReferenceReassignmentCommitResult tooLarge =
            coordinator.CommitResolvedReferenceReassignment(
                unlocked,
                new(revision, 1, Enumerable.Range(1, 257).ToArray(), 2));

        Assert.Equal(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                .StaleEditRevision,
            stale.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.InvalidRequest,
            same.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedSource.EditError);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.InvalidRequest,
            duplicate.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.InvalidRequest,
            missing.Status);
        Assert.Equal(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.InvalidRequest,
            tooLarge.Status);
        Assert.Equal(revision, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task BatchAdditionSubmitsOnceAndCanBeUndoneOnceWithoutChangingFiles()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ReferenceBatch.Tests",
            Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(sandbox, "first.txt");
        string secondPath = Path.Combine(sandbox, "second.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(firstPath, "first-original");
        await File.WriteAllTextAsync(secondPath, "second-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();
            ProductWorkspaceState before = State(Container("container-1", "Work"));

            ProductWorkspaceResolvedReferenceBatchCommitResult added =
                coordinator.CommitResolvedReferenceBatch(
                    before,
                    9,
                    [Entry(firstPath), Entry(secondPath)],
                    new(revision, 9, 1, [0, 1]));
            Assert.True(
                added.IsAccepted,
                $"Batch failed: {added.Status}/{added.EditError}/{added.SubmissionStatus}");
            ProductWorkspaceReferenceBatchAdditionUndoCommitResult undone =
                coordinator.CommitReferenceBatchAdditionUndo(
                    added.State!,
                    added.UndoToken!,
                    confirmed: true);
            Assert.True(
                undone.IsAccepted,
                $"Undo failed: {undone.Status}/{undone.UndoStatus}/{undone.SubmissionStatus}");
            ProductWorkspaceReferenceBatchAdditionUndoCommitResult repeated =
                coordinator.CommitReferenceBatchAdditionUndo(
                    undone.State!,
                    added.UndoToken!,
                    confirmed: true);
            _ = await saves.CompleteAsync();

            Assert.True(added.IsAccepted);
            Assert.Equal(2, added.State!.Containers[0].Items.Count);
            Assert.Equal(2, added.EditRevision);
            Assert.True(undone.IsAccepted);
            Assert.Empty(undone.State!.Containers[0].Items);
            Assert.Equal(3, undone.EditRevision);
            Assert.Equal(
                ProductWorkspaceReferenceBatchAdditionUndoStatus.Unavailable,
                repeated.UndoStatus);
            Assert.InRange(workflow.SaveCalls, 1, 2);
            Assert.Equal("first-original", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second-original", await File.ReadAllTextAsync(secondPath));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidBatchRequestsAreRejectedWithoutPartialSave()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        DesktopCatalogEntry entry = Entry(Path.Combine(Path.GetTempPath(), "one.txt"));
        ProductWorkspaceState state = State(Container("container-1", "Work"));

        ProductWorkspaceResolvedReferenceBatchCommitResult empty =
            coordinator.CommitResolvedReferenceBatch(
                state,
                9,
                [entry],
                new(revision, 9, 1, []));
        ProductWorkspaceResolvedReferenceBatchCommitResult duplicate =
            coordinator.CommitResolvedReferenceBatch(
                state,
                9,
                [entry],
                new(revision, 9, 1, [0, 0]));
        ProductWorkspaceResolvedReferenceBatchCommitResult stale =
            coordinator.CommitResolvedReferenceBatch(
                state,
                9,
                [entry],
                new(revision, 8, 1, [0]));
        ProductWorkspaceResolvedReferenceBatchCommitResult tooLarge =
            coordinator.CommitResolvedReferenceBatch(
                state,
                9,
                [entry],
                new(revision, 9, 1, Enumerable.Range(0, 257).ToArray()));
        ProductWorkspaceResolvedReferenceBatchCommitResult locked =
            coordinator.CommitResolvedReferenceBatch(
                State(Container("container-1", "Locked") with { IsLocked = true }),
                9,
                [entry],
                new(revision, 9, 1, [0]));

        Assert.Equal(ProductWorkspaceResolvedReferenceBatchCommitStatus.InvalidRequest, empty.Status);
        Assert.Equal(ProductWorkspaceResolvedReferenceBatchCommitStatus.InvalidRequest, duplicate.Status);
        Assert.Equal(ProductWorkspaceResolvedReferenceBatchCommitStatus.StaleCatalogGeneration, stale.Status);
        Assert.Equal(ProductWorkspaceResolvedReferenceBatchCommitStatus.InvalidRequest, tooLarge.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, locked.EditError);
        Assert.Equal(revision, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task LaterSuccessfulEditInvalidatesBatchAdditionUndo()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceState state = State(Container("container-1", "Work"));
        ProductWorkspaceResolvedReferenceBatchCommitResult added =
            coordinator.CommitResolvedReferenceBatch(
                state,
                9,
                [Entry(Path.Combine(Path.GetTempPath(), "one.txt"))],
                new(revision, 9, 1, [0]));

        ProductWorkspaceContainerCommitResult renamed = coordinator.CommitContainer(
            added.State!,
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                added.EditRevision,
                1,
                "Renamed"));

        Assert.True(renamed.IsAccepted);
        Assert.Null(coordinator.CurrentReferenceBatchAdditionUndoToken);
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
