using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

internal enum ProductWorkspaceCatalogRevisionSyncStatus
{
    BaselineReset,
    Unchanged,
    Advanced,
    StaleIgnored,
}

internal sealed record ProductWorkspaceCatalogRevisionSyncResult(
    ProductWorkspaceCatalogRevisionSyncStatus Status,
    long EditRevision,
    long CatalogGeneration,
    ProductDesktopCatalogStatus CatalogStatus);

internal sealed class ProductWorkspaceCatalogRevisionSynchronizer
{
    private readonly object sync = new();
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private ProductDesktopCatalogSnapshot? current;

    internal ProductWorkspaceCatalogRevisionSynchronizer(
        ProductWorkspaceCommitCoordinator workspaceCommits)
    {
        ArgumentNullException.ThrowIfNull(workspaceCommits);
        this.workspaceCommits = workspaceCommits;
    }

    internal ProductWorkspaceCatalogRevisionSyncResult ResetBaseline(
        ProductDesktopCatalogSnapshot snapshot)
    {
        Validate(snapshot);
        lock (sync)
        {
            current = snapshot;
            return Result(
                ProductWorkspaceCatalogRevisionSyncStatus.BaselineReset,
                snapshot,
                workspaceCommits.CurrentEditRevision);
        }
    }

    internal ProductWorkspaceCatalogRevisionSyncResult Observe(
        ProductDesktopCatalogSnapshot snapshot)
    {
        Validate(snapshot);
        lock (sync)
        {
            if (current is null)
            {
                current = snapshot;
                return Result(
                    ProductWorkspaceCatalogRevisionSyncStatus.BaselineReset,
                    snapshot,
                    workspaceCommits.CurrentEditRevision);
            }

            if (snapshot.Generation < current.Generation)
            {
                return Result(
                    ProductWorkspaceCatalogRevisionSyncStatus.StaleIgnored,
                    snapshot,
                    workspaceCommits.CurrentEditRevision);
            }

            if (snapshot.Generation == current.Generation
                && snapshot.Status == current.Status)
            {
                return Result(
                    ProductWorkspaceCatalogRevisionSyncStatus.Unchanged,
                    snapshot,
                    workspaceCommits.CurrentEditRevision);
            }

            current = snapshot;
            long revision = workspaceCommits.AdvanceExternalRevision();
            return Result(
                ProductWorkspaceCatalogRevisionSyncStatus.Advanced,
                snapshot,
                revision);
        }
    }

    private static void Validate(ProductDesktopCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.Generation);
    }

    private static ProductWorkspaceCatalogRevisionSyncResult Result(
        ProductWorkspaceCatalogRevisionSyncStatus status,
        ProductDesktopCatalogSnapshot snapshot,
        long editRevision) =>
        new(status, editRevision, snapshot.Generation, snapshot.Status);
}
