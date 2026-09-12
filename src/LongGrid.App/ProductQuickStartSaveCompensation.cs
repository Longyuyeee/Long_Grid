using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed class ProductQuickStartSaveCompensation
{
    private ProductQuickStartCommitResult? pending;
    private long saveRevision;

    internal void Track(ProductQuickStartCommitResult result, long revision)
    {
        pending = result.IsAccepted ? result : null;
        saveRevision = revision;
    }

    internal ProductWorkspaceReferenceBatchAdditionUndoCommitResult? Observe(
        ProductWorkspaceState? state,
        ProductWorkspaceSaveSnapshot snapshot,
        ProductWorkspaceCommitCoordinator commits)
    {
        if (pending is not { } candidate) return null;
        if (commits.CurrentEditRevision != candidate.EditRevision
            || snapshot.CurrentRevision != saveRevision
            || (snapshot.Status == ProductWorkspaceSaveStatus.Saved
                && snapshot.SavedRevision >= saveRevision))
        {
            pending = null;
            return null;
        }
        if (snapshot.Status != ProductWorkspaceSaveStatus.Failed || state is null) return null;

        // Consume before compensation, which can synchronously publish another save event.
        pending = null;
        return commits.CommitReferenceBatchAdditionUndo(state, candidate.CompensationToken!, true);
    }
}
