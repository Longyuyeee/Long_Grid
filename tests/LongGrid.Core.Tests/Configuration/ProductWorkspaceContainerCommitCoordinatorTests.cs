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
