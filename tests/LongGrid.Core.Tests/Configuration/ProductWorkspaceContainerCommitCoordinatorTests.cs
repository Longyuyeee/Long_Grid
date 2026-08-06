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
    public async Task RealStoreReloadsRenameWithoutChangingReferencedFile()
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
            _ = await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(result.IsAccepted);
            Assert.Equal("After", reloaded.Document!.Containers[0].Name);
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
