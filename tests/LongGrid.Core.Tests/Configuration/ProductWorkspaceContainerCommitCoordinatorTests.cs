using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerCommitCoordinatorTests
{
    [Fact]
    public async Task CreateSubmitsExactlyOnceAndAdvancesSharedRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = EmptyState();
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.Create,
                revision,
                0,
                "Work",
                Container("container-1", "Work")));

        Assert.True(result.IsAccepted);
        Assert.Equal(2, result.EditRevision);
        Assert.Single(result.State!.Containers);
        Assert.Single(result.Document!.Containers);
        Assert.Equal(1, saves.Snapshot.CurrentRevision);
        Assert.Equal(2, coordinator.CurrentEditRevision);
        _ = await saves.CompleteAsync();
        Assert.Equal(1, workflow.SaveCalls);
    }

    [Fact]
    public async Task RenameUsesOrdinalWithoutExposingContainerId()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State(Container("private-id", "Before"));
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                revision,
                1,
                "After"));

        Assert.True(result.IsAccepted);
        Assert.Equal("After", result.State!.Containers[0].Name);
        Assert.Equal("Before", state.Containers[0].Name);
    }

    [Fact]
    public async Task StaleRevisionNeverSubmits()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            EmptyState(),
            new(
                ProductWorkspaceContainerCommitAction.Create,
                ExpectedEditRevision: 0,
                ContainerOrdinal: 0,
                Name: "Work",
                NewContainer: Container("container-1", "Work")));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.StaleEditRevision,
            result.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task NoOpRenameDoesNotSubmitOrAdvanceRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            State(Container("container-1", "Work")),
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                revision,
                1,
                "Work"));

        Assert.Equal(ProductWorkspaceContainerCommitStatus.NoChange, result.Status);
        Assert.Equal(revision, coordinator.CurrentEditRevision);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task ContainerCommitInvalidatesOpenReferenceReviewToken()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingReferenceState();
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceReferenceReviewToken token =
            ProductWorkspaceReferenceReview.Create(state, 7, revision)
                .Snapshot!.Items[0].Token;

        ProductWorkspaceContainerCommitResult rename = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                revision,
                1,
                "After"));
        ProductWorkspaceReferenceCommitResult staleReference = coordinator.Commit(
            state,
            7,
            [],
            new(token, ProductWorkspaceReferenceAction.Keep));

        Assert.True(rename.IsAccepted);
        Assert.Equal(
            ProductWorkspaceReferenceGateError.StaleEditRevision,
            staleReference.GateError);
        Assert.Equal(rename.EditRevision, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task LockedOrMissingContainerIsRejectedWithoutSubmission()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductContainerState locked = Container("container-1", "Work") with
        {
            IsLocked = true,
        };

        ProductWorkspaceContainerCommitResult lockedResult =
            coordinator.CommitContainer(
                State(locked),
                new(
                    ProductWorkspaceContainerCommitAction.Rename,
                    revision,
                    1,
                    "After"));
        ProductWorkspaceContainerCommitResult missingResult =
            coordinator.CommitContainer(
                State(locked),
                new(
                    ProductWorkspaceContainerCommitAction.Rename,
                    revision,
                    2,
                    "After"));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            lockedResult.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.InvalidRequest,
            missingResult.Status);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task ExplicitLockBlocksRenameAndExplicitUnlockStillSucceeds()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State(Container("container-1", "Work"));
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult locked = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.SetLocked,
                revision,
                1,
                string.Empty,
                StateValue: true));
        ProductWorkspaceContainerCommitResult rename = coordinator.CommitContainer(
            locked.State!,
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                locked.EditRevision,
                1,
                "Blocked"));
        ProductWorkspaceContainerCommitResult unlocked = coordinator.CommitContainer(
            locked.State!,
            new(
                ProductWorkspaceContainerCommitAction.SetLocked,
                locked.EditRevision,
                1,
                string.Empty,
                StateValue: false));

        Assert.True(locked.IsAccepted);
        Assert.True(locked.State!.Containers[0].IsLocked);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, rename.EditError);
        Assert.True(unlocked.IsAccepted);
        Assert.False(unlocked.State!.Containers[0].IsLocked);
    }

    [Fact]
    public async Task CollapseChangesOnlyAppearanceAndNoOpDoesNotAdvance()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingReferenceState();
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult collapsed = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.SetCollapsed,
                revision,
                1,
                string.Empty,
                StateValue: true));
        ProductWorkspaceContainerCommitResult noChange = coordinator.CommitContainer(
            collapsed.State!,
            new(
                ProductWorkspaceContainerCommitAction.SetCollapsed,
                collapsed.EditRevision,
                1,
                string.Empty,
                StateValue: true));

        Assert.True(collapsed.IsAccepted);
        Assert.True(collapsed.State!.Containers[0].Appearance.Collapsed);
        Assert.Single(collapsed.State.Containers[0].Items);
        Assert.Equal(ProductWorkspaceContainerCommitStatus.NoChange, noChange.Status);
        Assert.Equal(collapsed.EditRevision, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task LockedContainerRejectsCollapseWithoutSubmission()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductContainerState locked = Container("container-1", "Work") with
        {
            IsLocked = true,
        };
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            State(locked),
            new(
                ProductWorkspaceContainerCommitAction.SetCollapsed,
                revision,
                1,
                string.Empty,
                StateValue: true));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            result.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, result.EditError);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task AppearancePresetUsesFiniteValuesAndPreservesOtherState()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingReferenceState();
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult changed = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                revision,
                1,
                string.Empty,
                ColorPreset: ProductWorkspaceContainerColorPreset.Emerald,
                OpacityPreset: ProductWorkspaceContainerOpacityPreset.Soft));
        ProductWorkspaceContainerCommitResult noChange = coordinator.CommitContainer(
            changed.State!,
            new(
                ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                changed.EditRevision,
                1,
                string.Empty,
                ColorPreset: ProductWorkspaceContainerColorPreset.Emerald,
                OpacityPreset: ProductWorkspaceContainerOpacityPreset.Soft));

        Assert.True(changed.IsAccepted);
        Assert.Equal("#059669", changed.State!.Containers[0].Appearance.Color);
        Assert.Equal(0.72, changed.State.Containers[0].Appearance.Opacity);
        Assert.False(changed.State.Containers[0].Appearance.Collapsed);
        Assert.Single(changed.State.Containers[0].Items);
        Assert.Equal(ProductWorkspaceContainerCommitStatus.NoChange, noChange.Status);
        Assert.Equal(changed.EditRevision, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task LockedOrUndefinedAppearancePresetNeverSubmits()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductContainerState locked = Container("container-1", "Work") with
        {
            IsLocked = true,
        };

        ProductWorkspaceContainerCommitResult lockedResult =
            coordinator.CommitContainer(
                State(locked),
                new(
                    ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                    revision,
                    1,
                    string.Empty,
                    ColorPreset: ProductWorkspaceContainerColorPreset.Azure,
                    OpacityPreset: ProductWorkspaceContainerOpacityPreset.Strong));
        ProductWorkspaceContainerCommitResult undefinedResult =
            coordinator.CommitContainer(
                State(Container("container-1", "Work")),
                new(
                    ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                    revision,
                    1,
                    string.Empty,
                    ColorPreset: (ProductWorkspaceContainerColorPreset)999,
                    OpacityPreset: ProductWorkspaceContainerOpacityPreset.Strong));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            lockedResult.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.InvalidRequest,
            undefinedResult.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task PlacementPresetUsesDipAndPreservesDisplayAndContent()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingReferenceState();
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult changed = coordinator.CommitContainer(
            state,
            new(
                ProductWorkspaceContainerCommitAction.SetPlacementPreset,
                revision,
                1,
                string.Empty,
                PositionPreset: ProductWorkspaceContainerPositionPreset.OffsetTwo,
                SizePreset: ProductWorkspaceContainerSizePreset.Wide));
        ProductWorkspaceContainerCommitResult noChange = coordinator.CommitContainer(
            changed.State!,
            new(
                ProductWorkspaceContainerCommitAction.SetPlacementPreset,
                changed.EditRevision,
                1,
                string.Empty,
                PositionPreset: ProductWorkspaceContainerPositionPreset.OffsetTwo,
                SizePreset: ProductWorkspaceContainerSizePreset.Wide));

        Assert.True(changed.IsAccepted);
        ProductContainerState container = changed.State!.Containers[0];
        Assert.Equal("display-unassigned", container.Placement.DisplayKey);
        Assert.Equal(80, container.Placement.XDip);
        Assert.Equal(96, container.Placement.YDip);
        Assert.Equal(480, container.Placement.WidthDip);
        Assert.Equal(280, container.Placement.HeightDip);
        Assert.Equal("#2563EB", container.Appearance.Color);
        Assert.Single(container.Items);
        Assert.Equal(ProductWorkspaceContainerCommitStatus.NoChange, noChange.Status);
        Assert.Equal(changed.EditRevision, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task LockedOrUndefinedPlacementPresetNeverSubmits()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();
        ProductContainerState locked = Container("container-1", "Work") with
        {
            IsLocked = true,
        };

        ProductWorkspaceContainerCommitResult lockedResult =
            coordinator.CommitContainer(
                State(locked),
                new(
                    ProductWorkspaceContainerCommitAction.SetPlacementPreset,
                    revision,
                    1,
                    string.Empty,
                    PositionPreset: ProductWorkspaceContainerPositionPreset.Start,
                    SizePreset: ProductWorkspaceContainerSizePreset.Compact));
        ProductWorkspaceContainerCommitResult undefinedResult =
            coordinator.CommitContainer(
                State(Container("container-1", "Work")),
                new(
                    ProductWorkspaceContainerCommitAction.SetPlacementPreset,
                    revision,
                    1,
                    string.Empty,
                    PositionPreset: ProductWorkspaceContainerPositionPreset.Start,
                    SizePreset: (ProductWorkspaceContainerSizePreset)999));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            lockedResult.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.InvalidRequest,
            undefinedResult.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidNameIsRejectedByFormalContract(string name)
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
            State(Container("container-1", "Work")),
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                revision,
                1,
                name));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.ReducerRejected,
            result.Status);
        Assert.Equal(ProductWorkspaceEditError.ConfigurationRejected, result.EditError);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task BindAndUnbindFolderUseSharedRevisionAndPreserveUserFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.FolderBindingCommit.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        string userFile = Path.Combine(sandbox, "keep.txt");
        await File.WriteAllTextAsync(userFile, "keep-original");
        try
        {
            ProductContainerFolderBindingState binding =
                WindowsProductContainerFolderBinding.CreateResolved(
                    WindowsProductContainerFolderBinding.Probe(sandbox));
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            ProductWorkspaceState state = State(Container("container-1", "Work"));
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult bound = coordinator.CommitContainer(
                state,
                new(
                    ProductWorkspaceContainerCommitAction.BindFolder,
                    revision,
                    1,
                    string.Empty,
                    FolderBinding: binding));
            Assert.True(bound.IsAccepted);
            Assert.Equal(binding.FileId, bound.State!.Containers[0].FolderBinding!.FileId);
            Assert.Equal(
                ProductWorkspaceContainerEditUndoKind.FolderBinding,
                bound.EditUndoToken!.Kind);
            ProductWorkspaceContainerEditUndoCommitResult undo =
                coordinator.CommitContainerEditUndo(
                    bound.State,
                    bound.EditUndoToken,
                    confirmed: true);
            ProductWorkspaceContainerCommitResult rebound = coordinator.CommitContainer(
                undo.State!,
                new(
                    ProductWorkspaceContainerCommitAction.BindFolder,
                    undo.EditRevision,
                    1,
                    string.Empty,
                    FolderBinding: binding));
            ProductWorkspaceContainerCommitResult sorted =
                coordinator.CommitContainer(
                    rebound.State!,
                    new(
                        ProductWorkspaceContainerCommitAction.BindFolder,
                        rebound.EditRevision,
                        1,
                        string.Empty,
                        FolderBinding: binding with
                        {
                            SortMode = ProductContainerFolderSortMode
                                .NameDescending,
                        }));
            ProductWorkspaceContainerEditUndoCommitResult sortUndo =
                coordinator.CommitContainerEditUndo(
                    sorted.State!,
                    sorted.EditUndoToken!,
                    confirmed: true);
            ProductWorkspaceContainerCommitResult unbound = coordinator.CommitContainer(
                sortUndo.State!,
                new(
                    ProductWorkspaceContainerCommitAction.UnbindFolder,
                    sortUndo.EditRevision,
                    1,
                    string.Empty,
                    Confirmed: true));

            Assert.True(undo.IsAccepted);
            Assert.Null(undo.State!.Containers[0].FolderBinding);
            Assert.True(rebound.IsAccepted);
            Assert.True(sorted.IsAccepted);
            Assert.Equal(
                ProductContainerFolderSortMode.NameDescending,
                sorted.State!.Containers[0].FolderBinding!.SortMode);
            Assert.True(sortUndo.IsAccepted);
            Assert.Equal(
                ProductContainerFolderSortMode.FoldersFirstNameAscending,
                sortUndo.State!.Containers[0].FolderBinding!.SortMode);
            Assert.True(unbound.IsAccepted);
            Assert.Null(unbound.State!.Containers[0].FolderBinding);
            Assert.Equal(
                ProductWorkspaceContainerEditUndoKind.FolderBinding,
                unbound.EditUndoToken!.Kind);
            Assert.Equal("keep-original", await File.ReadAllTextAsync(userFile));
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
    public async Task FolderBindingRejectsStaleLockedAndMalformedRequestsWithoutSubmission()
    {
        string target = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.FolderBindingCommit.Validation");
        var binding = new ProductContainerFolderBindingState
        {
            PersistedTarget = target,
            VolumeSerialNumber = 1,
            FileId = new string('A', 32),
            Resolution = ProductContainerFolderBindingResolution.Resolved,
            ResolvedTarget = target,
        };
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult stale = coordinator.CommitContainer(
            State(Container("container-1", "Work")),
            new(
                ProductWorkspaceContainerCommitAction.BindFolder,
                revision - 1,
                1,
                string.Empty,
                FolderBinding: binding));
        ProductWorkspaceContainerCommitResult locked = coordinator.CommitContainer(
            State(Container("container-1", "Work") with { IsLocked = true }),
            new(
                ProductWorkspaceContainerCommitAction.BindFolder,
                revision,
                1,
                string.Empty,
                FolderBinding: binding));
        ProductWorkspaceContainerCommitResult smuggled = coordinator.CommitContainer(
            State(Container("container-1", "Work")),
            new(
                ProductWorkspaceContainerCommitAction.Rename,
                revision,
                1,
                "After",
                FolderBinding: binding));

        Assert.Equal(ProductWorkspaceContainerCommitStatus.StaleEditRevision, stale.Status);
        Assert.Equal(ProductWorkspaceContainerCommitStatus.ReducerRejected, locked.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, locked.EditError);
        Assert.Equal(ProductWorkspaceContainerCommitStatus.InvalidRequest, smuggled.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task RealStorePersistsFolderBindingWithoutChangingDirectoryContent()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.FolderBindingCommit.Persistence",
            Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(sandbox, "bound");
        string storeDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(folder);
        string userFile = Path.Combine(folder, "user.txt");
        await File.WriteAllTextAsync(userFile, "unchanged-content");
        try
        {
            ProductContainerFolderBindingState binding =
                WindowsProductContainerFolderBinding.CreateResolved(
                    WindowsProductContainerFolderBinding.Probe(folder));
            ProductWorkspaceState state = State(Container("container-1", "Work"));
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult result = coordinator.CommitContainer(
                state,
                new(
                    ProductWorkspaceContainerCommitAction.BindFolder,
                    revision,
                    1,
                    string.Empty,
                    FolderBinding: binding));
            _ = await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(result.IsAccepted);
            Assert.Equal(
                Path.GetFullPath(folder),
                reloaded.Document!.Containers[0].FolderBinding!.Target);
            Assert.Equal(binding.FileId,
                reloaded.Document.Containers[0].FolderBinding!.FileId);
            Assert.Equal("unchanged-content", await File.ReadAllTextAsync(userFile));
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
    public async Task RealStoreReloadsContainerEditsWithoutChangingReferencedFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ContainerCommit.Integration",
            Guid.NewGuid().ToString("N"));
        string referencedPath = Path.Combine(sandbox, "keep.txt");
        string storeDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(referencedPath, "keep-original-file");
        try
        {
            ProductContainerState container = Container("container-1", "Before") with
            {
                Items =
                [
                    ProductItemReferenceState.CreateResolved(
                        "item-1",
                        new(
                            new DesktopItemIdentity("filesystem", referencedPath),
                            "user-desktop",
                            "keep.txt",
                            DesktopItemKind.File)),
                ],
            };
            ProductWorkspaceState state = State(container);
            ProductConfigurationDocument initial =
                ProductWorkspaceConfigurationProjector.Project(state).Document!;
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(initial);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult result =
                coordinator.CommitContainer(
                    state,
                    new(
                        ProductWorkspaceContainerCommitAction.Rename,
                        revision,
                        1,
                        "After"));
            ProductWorkspaceContainerCommitResult collapsed =
                coordinator.CommitContainer(
                    result.State!,
                    new(
                        ProductWorkspaceContainerCommitAction.SetCollapsed,
                        result.EditRevision,
                        1,
                        string.Empty,
                        StateValue: true));
            ProductWorkspaceContainerCommitResult appearance =
                coordinator.CommitContainer(
                    collapsed.State!,
                    new(
                        ProductWorkspaceContainerCommitAction.SetAppearancePreset,
                        collapsed.EditRevision,
                        1,
                        string.Empty,
                        ColorPreset: ProductWorkspaceContainerColorPreset.Amber,
                        OpacityPreset: ProductWorkspaceContainerOpacityPreset.Subtle));
            ProductWorkspaceContainerCommitResult placement =
                coordinator.CommitContainer(
                    appearance.State!,
                    new(
                        ProductWorkspaceContainerCommitAction.SetPlacementPreset,
                        appearance.EditRevision,
                        1,
                        string.Empty,
                        PositionPreset: ProductWorkspaceContainerPositionPreset.OffsetThree,
                        SizePreset: ProductWorkspaceContainerSizePreset.Large));
            _ = await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(result.IsAccepted);
            Assert.True(collapsed.IsAccepted);
            Assert.True(appearance.IsAccepted);
            Assert.True(placement.IsAccepted);
            Assert.Equal("After", reloaded.Document!.Containers[0].Name);
            Assert.True(reloaded.Document.Containers[0].Appearance.Collapsed);
            Assert.Equal("#D97706", reloaded.Document.Containers[0].Appearance.Color);
            Assert.Equal(0.56, reloaded.Document.Containers[0].Appearance.Opacity);
            Assert.Equal(
                "display-unassigned",
                reloaded.Document.Containers[0].Placement.DisplayKey);
            Assert.Equal(104, reloaded.Document.Containers[0].Placement.XDip);
            Assert.Equal(120, reloaded.Document.Containers[0].Placement.YDip);
            Assert.Equal(560, reloaded.Document.Containers[0].Placement.WidthDip);
            Assert.Equal(360, reloaded.Document.Containers[0].Placement.HeightDip);
            Assert.True(File.Exists(referencedPath));
            Assert.Equal(
                "keep-original-file",
                await File.ReadAllTextAsync(referencedPath));
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
    public async Task ConfirmedRemovalSubmitsOnceAndCanBeUndoneOnceWithoutChangingFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ContainerRemoval.Tests",
            Guid.NewGuid().ToString("N"));
        string desktopFile = Path.Combine(sandbox, "keep.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            var workflow = new FakeWorkflow();
            await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            ProductItemReferenceState item =
                ProductItemReferenceState.CreateResolved(
                    "item-1",
                    new(
                        new DesktopItemIdentity("filesystem", desktopFile),
                        "user-desktop",
                        "keep.txt",
                        DesktopItemKind.File));
            ProductContainerState container = Container("container-1", "Work") with
            {
                Items = [item],
            };
            ProductWorkspaceState state = State(container);
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult removal =
                coordinator.CommitContainer(
                    state,
                    new(
                        ProductWorkspaceContainerCommitAction.Remove,
                        revision,
                        1,
                        string.Empty,
                        Confirmed: true));
            ProductWorkspaceContainerRemovalUndoToken token =
                Assert.IsType<ProductWorkspaceContainerRemovalUndoToken>(
                    coordinator.CurrentContainerRemovalUndoToken);
            ProductWorkspaceContainerRemovalUndoCommitResult undo =
                coordinator.CommitContainerRemovalUndo(
                    removal.State!, token, confirmed: true);
            ProductWorkspaceContainerRemovalUndoCommitResult secondUndo =
                coordinator.CommitContainerRemovalUndo(
                    undo.State!, token, confirmed: true);

            Assert.True(removal.IsAccepted);
            Assert.Empty(removal.State!.Containers);
            Assert.Equal(token, removal.RemovalUndoToken);
            Assert.True(undo.IsAccepted);
            Assert.Single(undo.State!.Containers);
            Assert.Equal(
                ProductWorkspaceContainerRemovalUndoStatus.Unavailable,
                secondUndo.UndoStatus);
            Assert.Equal(2, saves.Snapshot.CurrentRevision);
            Assert.True(File.Exists(desktopFile));
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

    [Fact]
    public async Task RemovalRejectsMissingConfirmationLockedAndStaleRequests()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductContainerState unlocked = Container("container-1", "Work");
        ProductContainerState locked = unlocked with { IsLocked = true };
        long revision = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerCommitResult unconfirmed =
            coordinator.CommitContainer(
                State(unlocked),
                new(
                    ProductWorkspaceContainerCommitAction.Remove,
                    revision,
                    1,
                    string.Empty));
        ProductWorkspaceContainerCommitResult lockedResult =
            coordinator.CommitContainer(
                State(locked),
                new(
                    ProductWorkspaceContainerCommitAction.Remove,
                    revision,
                    1,
                    string.Empty,
                    Confirmed: true));
        ProductWorkspaceContainerCommitResult stale =
            coordinator.CommitContainer(
                State(unlocked),
                new(
                    ProductWorkspaceContainerCommitAction.Remove,
                    revision - 1,
                    1,
                    string.Empty,
                    Confirmed: true));

        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.InvalidRequest,
            unconfirmed.Status);
        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, lockedResult.EditError);
        Assert.Equal(
            ProductWorkspaceContainerCommitStatus.StaleEditRevision,
            stale.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task LaterSuccessfulEditInvalidatesContainerRemovalUndo()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = State(Container("container-1", "Work"));
        long revision = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerCommitResult removal =
            coordinator.CommitContainer(
                state,
                new(
                    ProductWorkspaceContainerCommitAction.Remove,
                    revision,
                    1,
                    string.Empty,
                    Confirmed: true));
        ProductWorkspaceContainerRemovalUndoToken token =
            removal.RemovalUndoToken!;

        ProductWorkspaceContainerCommitResult create =
            coordinator.CommitContainer(
                removal.State!,
                new(
                    ProductWorkspaceContainerCommitAction.Create,
                    removal.EditRevision,
                    0,
                    "New",
                    Container("container-2", "New")));
        ProductWorkspaceContainerRemovalUndoCommitResult staleUndo =
            coordinator.CommitContainerRemovalUndo(create.State!, token, true);

        Assert.True(create.IsAccepted);
        Assert.Null(coordinator.CurrentContainerRemovalUndoToken);
        Assert.Equal(
            ProductWorkspaceContainerRemovalUndoStatus.Unavailable,
            staleUndo.UndoStatus);
        Assert.Equal(2, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task RealStoreContainerEditsUndoAndReloadWithoutDifference()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ContainerEditUndo.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            ProductWorkspaceContainerCommitAction[] actions =
            [
                ProductWorkspaceContainerCommitAction.Rename,
                ProductWorkspaceContainerCommitAction.SetLocked,
                ProductWorkspaceContainerCommitAction.SetCollapsed,
                ProductWorkspaceContainerCommitAction.SetAppearancePreset,
            ];
            foreach (ProductWorkspaceContainerCommitAction action in actions)
            {
                string storeDirectory = Path.Combine(sandbox, action.ToString());
                ProductWorkspaceState expected = State(Container("container-1", "Before"));
                ProductConfigurationDocument expectedDocument =
                    ProductWorkspaceConfigurationProjector.Project(expected).Document!;
                var store = new ProductConfigurationStore(storeDirectory);
                await store.SaveAsync(expectedDocument);
                var workflow = new ProductConfigurationSaveWorkflow(
                    new ProductConfigurationSaveCoordinator(store));
                await using var saves = new ProductWorkspaceSaveController(
                    workflow,
                    new ImmediateScheduler(),
                    TimeSpan.FromMilliseconds(1));
                var coordinator = new ProductWorkspaceCommitCoordinator(saves);
                long revision = coordinator.AdvanceExternalRevision();
                ProductWorkspaceContainerCommitRequest request = action switch
                {
                    ProductWorkspaceContainerCommitAction.Rename =>
                        new(action, revision, 1, "After"),
                    ProductWorkspaceContainerCommitAction.SetLocked =>
                        new(action, revision, 1, string.Empty, StateValue: true),
                    ProductWorkspaceContainerCommitAction.SetCollapsed =>
                        new(action, revision, 1, string.Empty, StateValue: true),
                    ProductWorkspaceContainerCommitAction.SetAppearancePreset =>
                        new(
                            action,
                            revision,
                            1,
                            string.Empty,
                            ColorPreset: ProductWorkspaceContainerColorPreset.Amber,
                            OpacityPreset: ProductWorkspaceContainerOpacityPreset.Subtle,
                            TitleVisibility: ProductContainerTitleVisibilityPolicy.Hover,
                            TitleDoubleClickAction:
                                ProductContainerTitleDoubleClickAction.None),
                    _ => throw new InvalidOperationException(),
                };

                ProductWorkspaceContainerCommitResult edited =
                    coordinator.CommitContainer(expected, request);
                Assert.True(edited.IsAccepted);
                Assert.NotNull(edited.EditUndoToken);
                ProductWorkspaceContainerEditUndoCommitResult undone =
                    coordinator.CommitContainerEditUndo(
                        edited.State!,
                        edited.EditUndoToken!,
                        confirmed: true);
                Assert.True(
                    undone.IsAccepted,
                    $"{action}:Status={undone.Status}:Undo={undone.UndoStatus}");
                _ = await saves.CompleteAsync();
                ProductConfigurationLoadResult actual = await store.LoadAsync();

                Assert.Equal(
                    ProductWorkspaceConfigurationFingerprint.Compute(expectedDocument),
                    ProductWorkspaceConfigurationFingerprint.Compute(actual.Document!));
            }
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
    public async Task RealStoreCreateOverviewRenameUndoJourneyKeepsDesktopGuardUnchanged()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.BoxManagementJourney.Tests",
            Guid.NewGuid().ToString("N"));
        string configurationDirectory = Path.Combine(sandbox, "Configuration");
        string desktopDirectory = Path.Combine(sandbox, "DesktopGuard");
        Directory.CreateDirectory(desktopDirectory);
        string desktopGuardPath = Path.Combine(desktopDirectory, "keep.txt");
        await File.WriteAllTextAsync(desktopGuardPath, "LongGrid must not change me.");
        string expectedDesktopHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(desktopGuardPath)));

        try
        {
            var store = new ProductConfigurationStore(configurationDirectory);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult created =
                coordinator.CommitContainer(
                    EmptyState(),
                    new(
                        ProductWorkspaceContainerCommitAction.Create,
                        revision,
                        0,
                        "Inbox",
                        Container("container-1", "Inbox")));
            Assert.True(created.IsAccepted);
            ProductWorkspaceReadResult overview =
                ProductWorkspaceReadModel.Create(created.State!);
            Assert.True(overview.IsSuccess);
            Assert.Single(overview.Snapshot!.Containers);
            Assert.Equal("Inbox", overview.Snapshot.Containers[0].UserVisibleName);

            ProductWorkspaceContainerCommitResult renamed =
                coordinator.CommitContainer(
                    created.State!,
                    new(
                        ProductWorkspaceContainerCommitAction.Rename,
                        created.EditRevision,
                        1,
                        "Projects"));
            Assert.True(renamed.IsAccepted);
            Assert.Equal("Projects", renamed.State!.Containers[0].Name);
            Assert.NotNull(renamed.EditUndoToken);

            ProductWorkspaceContainerEditUndoCommitResult undone =
                coordinator.CommitContainerEditUndo(
                    renamed.State,
                    renamed.EditUndoToken!,
                    confirmed: true);
            Assert.True(undone.IsAccepted);
            Assert.Equal("Inbox", undone.State!.Containers[0].Name);
            _ = await saves.CompleteAsync();

            ProductConfigurationLoadResult reloaded = await store.LoadAsync();
            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            ProductWorkspaceProjectionResult projected =
                ProductWorkspaceConfigurationProjector.Project(undone.State);
            Assert.True(projected.IsSuccess);
            Assert.Equal(
                ProductWorkspaceConfigurationFingerprint.Compute(projected.Document!),
                ProductWorkspaceConfigurationFingerprint.Compute(reloaded.Document!));

            string actualDesktopHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(desktopGuardPath)));
            Assert.Equal(expectedDesktopHash, actualDesktopHash);
            Assert.Equal("LongGrid must not change me.", await File.ReadAllTextAsync(
                desktopGuardPath));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceSaveController CreateSaves(
        IProductConfigurationSaveWorkflow workflow) =>
        new(
            workflow,
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));

    private static ProductWorkspaceState EmptyState() =>
        new()
        {
            ProfileId = "default",
            Containers = Array.Empty<ProductContainerState>(),
        };

    private static ProductWorkspaceState State(ProductContainerState container) =>
        new()
        {
            ProfileId = "default",
            Containers = [container],
        };

    private static ProductWorkspaceState MissingReferenceState()
    {
        string target = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ContainerCommit.Tests",
            "missing");
        ProductContainerState resolved = Container("container-1", "Before") with
        {
            Items =
            [
                ProductItemReferenceState.CreateResolved(
                    "item-1",
                    new(
                        new DesktopItemIdentity("filesystem", target),
                        "user-desktop",
                        "Missing",
                        DesktopItemKind.Directory)),
            ],
        };
        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(State(resolved)).Document!;
        return ProductWorkspaceConfigurationResolver.Resolve(document, []).State!;
    }

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

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
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

        public void DiscardRetry()
        {
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
