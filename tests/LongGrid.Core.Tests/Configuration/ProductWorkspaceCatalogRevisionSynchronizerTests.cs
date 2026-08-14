using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCatalogRevisionSynchronizerTests
{
    [Fact]
    public void ResetEstablishesBaselineWithoutInvalidatingWorkspaceTokens()
    {
        ProductWorkspaceCommitCoordinator commits = CreateCommits();
        Assert.Equal(1, commits.AdvanceExternalRevision());
        var synchronizer = new ProductWorkspaceCatalogRevisionSynchronizer(commits);

        ProductWorkspaceCatalogRevisionSyncResult result =
            synchronizer.ResetBaseline(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 4));

        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.BaselineReset,
            result.Status);
        Assert.Equal(1, result.EditRevision);
        Assert.Equal(1, commits.CurrentEditRevision);
    }

    [Fact]
    public void RefreshLifecycleAdvancesRevisionForEveryDistinctProjectionIdentity()
    {
        ProductWorkspaceCommitCoordinator commits = CreateCommits();
        var synchronizer = new ProductWorkspaceCatalogRevisionSynchronizer(commits);
        _ = synchronizer.ResetBaseline(Snapshot(
            ProductDesktopCatalogStatus.Ready,
            generation: 7));

        ProductWorkspaceCatalogRevisionSyncResult refreshing =
            synchronizer.Observe(Snapshot(
                ProductDesktopCatalogStatus.Refreshing,
                generation: 8));
        ProductWorkspaceCatalogRevisionSyncResult ready =
            synchronizer.Observe(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 8));

        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.Advanced,
            refreshing.Status);
        Assert.Equal(1, refreshing.EditRevision);
        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.Advanced,
            ready.Status);
        Assert.Equal(2, ready.EditRevision);
        Assert.Equal(2, commits.CurrentEditRevision);
    }

    [Fact]
    public void DuplicateSnapshotIsIdempotent()
    {
        ProductWorkspaceCommitCoordinator commits = CreateCommits();
        var synchronizer = new ProductWorkspaceCatalogRevisionSynchronizer(commits);
        ProductDesktopCatalogSnapshot ready = Snapshot(
            ProductDesktopCatalogStatus.Ready,
            generation: 3);
        _ = synchronizer.ResetBaseline(ready);

        ProductWorkspaceCatalogRevisionSyncResult result =
            synchronizer.Observe(ready with { Entries = [] });

        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.Unchanged,
            result.Status);
        Assert.Equal(0, result.EditRevision);
    }

    [Fact]
    public void StaleGenerationCannotRollWorkspaceProjectionBack()
    {
        ProductWorkspaceCommitCoordinator commits = CreateCommits();
        var synchronizer = new ProductWorkspaceCatalogRevisionSynchronizer(commits);
        _ = synchronizer.ResetBaseline(Snapshot(
            ProductDesktopCatalogStatus.Ready,
            generation: 9));

        ProductWorkspaceCatalogRevisionSyncResult result =
            synchronizer.Observe(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 8));

        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.StaleIgnored,
            result.Status);
        Assert.Equal(0, commits.CurrentEditRevision);
    }

    [Fact]
    public void ResetAfterConfigurationReloadDoesNotDoubleAdvanceRevision()
    {
        ProductWorkspaceCommitCoordinator commits = CreateCommits();
        var synchronizer = new ProductWorkspaceCatalogRevisionSynchronizer(commits);
        _ = synchronizer.ResetBaseline(Snapshot(
            ProductDesktopCatalogStatus.Refreshing,
            generation: 2));
        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.Advanced,
            synchronizer.Observe(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 2)).Status);
        Assert.Equal(2, commits.AdvanceExternalRevision());

        ProductWorkspaceCatalogRevisionSyncResult reset =
            synchronizer.ResetBaseline(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 2));

        Assert.Equal(2, reset.EditRevision);
        Assert.Equal(
            ProductWorkspaceCatalogRevisionSyncStatus.Unchanged,
            synchronizer.Observe(Snapshot(
                ProductDesktopCatalogStatus.Ready,
                generation: 2)).Status);
        Assert.Equal(2, commits.CurrentEditRevision);
    }

    private static ProductWorkspaceCommitCoordinator CreateCommits() =>
        new(new ProductWorkspaceSaveController(new SuccessfulSaveWorkflow()));

    private static ProductDesktopCatalogSnapshot Snapshot(
        ProductDesktopCatalogStatus status,
        long generation) =>
        new(status, generation, [], []);

    private sealed class SuccessfulSaveWorkflow : IProductConfigurationSaveWorkflow
    {
        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.Saved,
                null,
                CanRetry: false));

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                null,
                CanRetry: false));

        public Task CompleteAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
