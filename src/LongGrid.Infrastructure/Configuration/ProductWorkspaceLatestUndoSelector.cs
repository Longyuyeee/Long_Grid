using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductWorkspaceLatestUndoKind
{
    Unavailable,
    Conflict,
    LayoutRecovery,
    ContainerRemoval,
    ReferenceBatchAddition,
    ReferenceRemoval,
    ReferenceReassignment,
}

public sealed record ProductWorkspaceLatestUndoSelection(
    ProductWorkspaceLatestUndoKind Kind,
    long EditRevision)
{
    public bool CanUndo => Kind is not ProductWorkspaceLatestUndoKind.Unavailable
        and not ProductWorkspaceLatestUndoKind.Conflict;
}

public static class ProductWorkspaceLatestUndoSelector
{
    public static ProductWorkspaceLatestUndoSelection Select(
        ProductWorkspaceLayoutRecoveryUndoToken? layoutRecovery,
        ProductWorkspaceContainerRemovalUndoToken? containerRemoval,
        ProductWorkspaceReferenceBatchAdditionUndoToken? referenceBatchAddition,
        ProductWorkspaceReferenceRemovalUndoToken? referenceRemoval,
        ProductWorkspaceReferenceReassignmentUndoToken? referenceReassignment)
    {
        (ProductWorkspaceLatestUndoKind Kind, bool IsPresent, Guid OperationId,
            long Revision)[]
            candidates =
            [
                Candidate(
                    ProductWorkspaceLatestUndoKind.LayoutRecovery,
                    layoutRecovery?.OperationId,
                    layoutRecovery?.RecoveryEditRevision),
                Candidate(
                    ProductWorkspaceLatestUndoKind.ContainerRemoval,
                    containerRemoval?.OperationId,
                    containerRemoval?.RemovalEditRevision),
                Candidate(
                    ProductWorkspaceLatestUndoKind.ReferenceBatchAddition,
                    referenceBatchAddition?.OperationId,
                    referenceBatchAddition?.AdditionEditRevision),
                Candidate(
                    ProductWorkspaceLatestUndoKind.ReferenceRemoval,
                    referenceRemoval?.OperationId,
                    referenceRemoval?.RemovalEditRevision),
                Candidate(
                    ProductWorkspaceLatestUndoKind.ReferenceReassignment,
                    referenceReassignment?.OperationId,
                    referenceReassignment?.ReassignmentEditRevision),
            ];

        (ProductWorkspaceLatestUndoKind Kind, bool IsPresent, Guid OperationId,
            long Revision)[]
            available = candidates
                .Where(candidate => candidate.IsPresent)
                .ToArray();
        if (available.Length == 0)
        {
            return new(ProductWorkspaceLatestUndoKind.Unavailable, 0);
        }

        if (available.Length != 1
            || available[0].OperationId == Guid.Empty
            || available[0].Revision <= 0)
        {
            return new(ProductWorkspaceLatestUndoKind.Conflict, 0);
        }

        return new(available[0].Kind, available[0].Revision);
    }

    private static (
        ProductWorkspaceLatestUndoKind Kind,
        bool IsPresent,
        Guid OperationId,
        long Revision) Candidate(
            ProductWorkspaceLatestUndoKind kind,
            Guid? operationId,
            long? revision) =>
        (kind, operationId.HasValue, operationId ?? Guid.Empty, revision ?? 0);
}
