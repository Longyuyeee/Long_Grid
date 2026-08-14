using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCommitCoordinatorTests
{
    [Fact]
    public async Task ExternalStateAdvancesAMonotonicEditRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);

        Assert.Equal(0, coordinator.CurrentEditRevision);
        Assert.Equal(1, coordinator.AdvanceExternalRevision());
        Assert.Equal(2, coordinator.AdvanceExternalRevision());
        Assert.Equal(2, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task KeepDoesNotSubmitOrAdvanceRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            generation: 3,
            revision: coordinator.AdvanceExternalRevision());

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            3,
            [],
            new(token, ProductWorkspaceReferenceAction.Keep));

        Assert.Equal(ProductWorkspaceReferenceCommitStatus.Kept, result.Status);
        Assert.Equal(1, result.EditRevision);
        Assert.Equal(0, workflow.SaveCalls);
        Assert.Equal(1, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task ConfirmedRemoveSubmitsExactlyOnceAndAdvancesRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            5,
            coordinator.AdvanceExternalRevision());

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            5,
            [],
            new(
                token,
                ProductWorkspaceReferenceAction.Remove,
                Confirmed: true));

        Assert.True(result.IsAccepted);
        Assert.Equal(ProductWorkspaceReferenceCommitStatus.Accepted, result.Status);
        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.Accepted, result.SubmissionStatus);
        Assert.Equal(2, result.EditRevision);
        Assert.Empty(result.State!.Containers[0].Items);
        Assert.Empty(result.Document!.Containers[0].Items);
        Assert.Equal(2, coordinator.CurrentEditRevision);
        Assert.Equal(1, saves.Snapshot.CurrentRevision);
        ProductWorkspaceSaveCompletionResult completion = await saves.CompleteAsync();
        Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, completion.Status);
        Assert.Equal(1, workflow.SaveCalls);
        Assert.Empty(workflow.LastSavedDocument!.Containers[0].Items);
    }

    [Fact]
    public async Task ExplicitReplacementSubmitsResolvedDocument()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        DesktopCatalogEntry candidate = CatalogEntry("Replacement");
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            2,
            coordinator.AdvanceExternalRevision());

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            2,
            [candidate],
            new(
                token,
                ProductWorkspaceReferenceAction.Replace,
                Confirmed: true,
                Replacement: candidate));

        Assert.True(result.IsAccepted);
        Assert.Equal(
            ProductItemReferenceResolution.Resolved,
            result.State!.Containers[0].Items[0].Resolution);
        Assert.Equal(
            candidate.Identity.CanonicalTarget,
            result.Document!.Containers[0].Items[0].Target);
        Assert.Equal(1, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task GateRejectionNeverSubmitsOrAdvancesRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            1,
            coordinator.AdvanceExternalRevision());

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            1,
            [],
            new(token, ProductWorkspaceReferenceAction.Remove));

        Assert.Equal(
            ProductWorkspaceReferenceCommitStatus.GateRejected,
            result.Status);
        Assert.Equal(
            ProductWorkspaceReferenceGateError.ConfirmationRequired,
            result.GateError);
        Assert.Equal(1, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
    }

    [Fact]
    public async Task ExternalReplacementInvalidatesAnOpenReviewToken()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            1,
            coordinator.AdvanceExternalRevision());
        coordinator.AdvanceExternalRevision();

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            1,
            [],
            new(token, ProductWorkspaceReferenceAction.Keep));

        Assert.Equal(
            ProductWorkspaceReferenceGateError.StaleEditRevision,
            result.GateError);
        Assert.Equal(0, workflow.SaveCalls);
        Assert.Equal(2, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task CompletedSaveControllerRejectsWithoutAdvancingEditRevision()
    {
        var workflow = new FakeWorkflow();
        await using ProductWorkspaceSaveController saves = CreateSaves(workflow);
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceState state = MissingState();
        ProductWorkspaceReferenceReviewToken token = Token(
            state,
            1,
            coordinator.AdvanceExternalRevision());
        _ = await saves.CompleteAsync();

        ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
            state,
            1,
            [],
            new(
                token,
                ProductWorkspaceReferenceAction.Remove,
                Confirmed: true));

        Assert.Equal(
            ProductWorkspaceReferenceCommitStatus.SaveRejected,
            result.Status);
        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.Completed, result.SubmissionStatus);
        Assert.Equal(1, coordinator.CurrentEditRevision);
    }

    [Fact]
    public async Task RealStoreReloadsCommittedRemovalWithoutDeletingReferencedFile()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ReferenceCommit.Integration",
            Guid.NewGuid().ToString("N"));
        string referencedDirectory = Path.Combine(sandbox, "desktop");
        string referencedPath = Path.Combine(referencedDirectory, "keep.txt");
        string storeDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(referencedDirectory);
        await File.WriteAllTextAsync(referencedPath, "keep-original-file");
        try
        {
            ProductWorkspaceState resolved = ResolvedState(
                "item-1",
                referencedPath,
                DesktopItemKind.File);
            ProductConfigurationDocument initial =
                ProductWorkspaceConfigurationProjector.Project(resolved).Document!;
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(initial);
            ProductWorkspaceState missing =
                ProductWorkspaceConfigurationResolver.Resolve(initial, []).State!;
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            ProductWorkspaceReferenceReviewToken token = Token(
                missing,
                1,
                coordinator.AdvanceExternalRevision());

            ProductWorkspaceReferenceCommitResult result = coordinator.Commit(
                missing,
                1,
                [],
                new(
                    token,
                    ProductWorkspaceReferenceAction.Remove,
                    Confirmed: true));
            ProductWorkspaceSaveCompletionResult completion =
                await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(result.IsAccepted);
            Assert.Equal(
                ProductWorkspaceSaveCompletionStatus.Completed,
                completion.Status);
            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            Assert.Empty(reloaded.Document!.Containers[0].Items);
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

    private static ProductWorkspaceReferenceReviewToken Token(
        ProductWorkspaceState state,
        long generation,
        long revision) =>
        ProductWorkspaceReferenceReview.Create(state, generation, revision)
            .Snapshot!.Items[0].Token;

    private static ProductWorkspaceState MissingState()
    {
        ProductWorkspaceState resolved = ResolvedState(
            "item-1",
            CatalogEntry("Missing").Identity.CanonicalTarget,
            DesktopItemKind.Directory);
        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(resolved).Document!;
        return ProductWorkspaceConfigurationResolver.Resolve(document, []).State!;
    }

    private static ProductWorkspaceState ResolvedState(
        string itemId,
        string target,
        DesktopItemKind kind) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        WidthDip = 320,
                        HeightDip = 240,
                    },
                    Items =
                    [
                        ProductItemReferenceState.CreateResolved(
                            itemId,
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity("filesystem", target),
                                "user-desktop",
                                "Anonymous",
                                kind)),
                    ],
                },
            ],
        };

    private static DesktopCatalogEntry CatalogEntry(string name) =>
        new(
            new DesktopItemIdentity(
                "filesystem",
                Path.Combine(
                    Path.GetTempPath(),
                    "LongGrid.ReferenceCommit.Tests",
                    name)),
            "user-desktop",
            name,
            DesktopItemKind.Directory);

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
        private ProductConfigurationDocument? lastSavedDocument;

        public int SaveCalls => Volatile.Read(ref saveCalls);

        public ProductConfigurationDocument? LastSavedDocument =>
            Volatile.Read(ref lastSavedDocument);

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref lastSavedDocument, document);
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
