using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceLayoutRecoveryCommitCoordinatorTests
{
    [Fact]
    public async Task AcceptedReviewUsesSharedRevisionAndOnlySaveController()
    {
        var workflow = new FakeWorkflow();
        await using var saves = new ProductWorkspaceSaveController(
            workflow,
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        (ProductWorkspaceState state, DisplayTopologyNode current) = Fixture();
        ProductWorkspaceLayoutRecoveryReviewToken token =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state, [current], true, 5, 0).Token!;

        ProductWorkspaceLayoutRecoveryCommitResult result =
            coordinator.CommitLayoutRecovery(
                state, [current], true, 5, token, confirmed: true);
        await saves.CompleteAsync();

        Assert.True(result.IsAccepted);
        Assert.Equal(1, result.EditRevision);
        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.Accepted, result.SubmissionStatus);
        Assert.Equal(1, workflow.SaveCalls);
        Assert.Equal(2, result.Document!.SchemaVersion);
        Assert.Equal(144u, result.Document.SavedDisplayTopology![0].EffectiveDpi);
    }

    [Fact]
    public async Task GateAndCompletedControllerFailuresDoNotAdvanceRevision()
    {
        var workflow = new FakeWorkflow();
        var saves = new ProductWorkspaceSaveController(
            workflow,
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        (ProductWorkspaceState state, DisplayTopologyNode current) = Fixture();
        ProductWorkspaceLayoutRecoveryReviewToken token =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state, [current], true, 5, 0).Token!;

        ProductWorkspaceLayoutRecoveryCommitResult rejected =
            coordinator.CommitLayoutRecovery(
                state, [current], true, 5, token, confirmed: false);
        Assert.Equal(ProductWorkspaceLayoutRecoveryCommitStatus.GateRejected, rejected.Status);
        Assert.Equal(0, coordinator.CurrentEditRevision);

        await saves.CompleteAsync();
        ProductWorkspaceLayoutRecoveryCommitResult saveRejected =
            coordinator.CommitLayoutRecovery(
                state, [current], true, 5, token, confirmed: true);

        Assert.Equal(ProductWorkspaceLayoutRecoveryCommitStatus.SaveRejected, saveRejected.Status);
        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.Completed, saveRejected.SubmissionStatus);
        Assert.Equal(0, coordinator.CurrentEditRevision);
        Assert.Equal(0, workflow.SaveCalls);
        await saves.DisposeAsync();
    }

    [Fact]
    public async Task NullInputsAreRejected()
    {
        await using var saves = new ProductWorkspaceSaveController(
            new FakeWorkflow(),
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        ProductWorkspaceLayoutRecoveryReviewToken token = new(
            1, 0, "saved", "current", "configuration", 1, 1, 0);
        Assert.Throws<ArgumentNullException>(
            () => coordinator.CommitLayoutRecovery(
                null!, null, false, 1, token, true));
        Assert.Throws<ArgumentNullException>(
            () => coordinator.CommitLayoutRecovery(
                Fixture().State, null, false, 1, null!, true));
    }

    private static (ProductWorkspaceState State, DisplayTopologyNode Current) Fixture()
    {
        DisplayTopologyNode saved = new(
            "display-a",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            true);
        ProductWorkspaceState state = new()
        {
            ProfileId = "default",
            SavedDisplayTopology = ProductSavedDisplayTopology.Capture([saved]),
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };
        return (state, saved with { EffectiveDpi = 144 });
    }

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
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

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
