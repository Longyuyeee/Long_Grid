using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductQuickStartCommitStatus
{
    Accepted,
    StaleEditRevision,
    StaleCatalogGeneration,
    StalePreview,
    WorkspaceNotEmpty,
    AlreadyReferenced,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductQuickStartCommitRequest(
    ProductQuickStartSuggestionSnapshot Preview,
    ProductContainerState NewContainer);

public sealed record ProductQuickStartCommitResult(
    ProductQuickStartCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceBatchAdditionUndoToken? CompensationToken)
{
    public bool IsAccepted =>
        Status == ProductQuickStartCommitStatus.Accepted
        && State is not null
        && Document is not null
        && CompensationToken is not null;
}

public sealed partial class ProductWorkspaceCommitCoordinator
{
    public ProductQuickStartCommitResult CommitQuickStart(
        ProductWorkspaceState state,
        long currentCatalogGeneration,
        IReadOnlyList<DesktopCatalogEntry> catalog,
        ProductQuickStartCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Preview);
        ArgumentNullException.ThrowIfNull(request.NewContainer);

        lock (gate)
        {
            ProductQuickStartSuggestionSnapshot preview = request.Preview;
            if (preview.WorkspaceRevision != editRevision)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.StaleEditRevision);
            }
            if (preview.CatalogGeneration != currentCatalogGeneration)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.StaleCatalogGeneration);
            }
            if (!preview.CanCommit
                || request.NewContainer.Items.Count != 0
                || preview.Items.Count > MaximumResolvedReferenceBatchSize
                || preview.Items.Select(item => item.CatalogIndex).Distinct().Count()
                    != preview.Items.Count
                || preview.Items.Any(item => item.CatalogIndex < 0
                    || item.CatalogIndex >= catalog.Count))
            {
                return QuickStartFailure(ProductQuickStartCommitStatus.InvalidRequest);
            }
            if (state.Containers.Count != 0)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.WorkspaceNotEmpty);
            }

            ProductWorkspaceProjectionResult beforeProjection =
                ProductWorkspaceConfigurationProjector.Project(state);
            if (!beforeProjection.IsSuccess
                || !string.Equals(
                    ProductWorkspaceConfigurationFingerprint.Compute(
                        beforeProjection.Document!),
                    preview.WorkspaceFingerprint,
                    StringComparison.Ordinal))
            {
                return QuickStartFailure(ProductQuickStartCommitStatus.StalePreview);
            }

            DesktopCatalogEntry[] entries = preview.Items
                .Select(item => catalog[item.CatalogIndex])
                .ToArray();
            if (!string.Equals(
                    ProductQuickStartSuggestionPlanner.ComputeCatalogFingerprint(entries),
                    preview.CatalogFingerprint,
                    StringComparison.Ordinal))
            {
                return QuickStartFailure(ProductQuickStartCommitStatus.StalePreview);
            }
            string[] targets = entries
                .Select(entry => entry.Identity.CanonicalTarget)
                .ToArray();
            if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != targets.Length)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.AlreadyReferenced);
            }

            ProductItemReferenceState[] items = entries
                .Select(entry => ProductItemReferenceState.CreateResolved(
                    $"item-{Guid.NewGuid():N}",
                    entry))
                .ToArray();
            ProductWorkspaceEditResult edit = ProductWorkspaceReducer.CreateContainer(
                state,
                request.NewContainer with { Items = items });
            if (!edit.IsSuccess || !edit.Changed)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceBatchAdditionUndoToken? compensationToken =
                projection.IsSuccess
                    ? ProductWorkspaceReferenceBatchAdditionUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        Guid.NewGuid())
                    : null;
            if (!projection.IsSuccess || compensationToken is null)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return QuickStartFailure(
                    ProductQuickStartCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = new(
                compensationToken,
                state,
                CreatesContainer: true);
            RecordSessionHistoryAction(
                state,
                edit.State!,
                ProductWorkspaceSessionHistoryActionKind.QuickStart,
                "完成首次整理",
                "桌面项目",
                request.NewContainer.Name,
                items.Length);
            return new(
                ProductQuickStartCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                compensationToken);
        }
    }

    private ProductQuickStartCommitResult QuickStartFailure(
        ProductQuickStartCommitStatus status,
        ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
        ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) => new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);
}
