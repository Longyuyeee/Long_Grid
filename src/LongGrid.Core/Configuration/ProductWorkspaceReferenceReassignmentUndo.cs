namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceReferenceReassignmentUndoToken(
    Guid OperationId,
    long ReassignmentEditRevision,
    string ReassignedConfigurationFingerprint,
    string RestoreConfigurationFingerprint);

public enum ProductWorkspaceReferenceReassignmentUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceReassignmentUndoResult(
    ProductWorkspaceReferenceReassignmentUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceReassignmentUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceReferenceReassignmentUndo
{
    public static ProductWorkspaceReferenceReassignmentUndoToken? Prepare(
        ProductWorkspaceState restoreState,
        ProductWorkspaceState reassignedState,
        long reassignmentEditRevision,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(reassignedState);
        if (reassignmentEditRevision <= 0 || operationId == Guid.Empty)
        {
            return null;
        }

        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        ProductWorkspaceProjectionResult reassigned =
            ProductWorkspaceConfigurationProjector.Project(reassignedState);
        if (!restore.IsSuccess || !reassigned.IsSuccess)
        {
            return null;
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        string reassignedFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(reassigned.Document!);
        if (string.Equals(
            restoreFingerprint,
            reassignedFingerprint,
            StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            operationId,
            reassignmentEditRevision,
            reassignedFingerprint,
            restoreFingerprint);
    }

    public static ProductWorkspaceReferenceReassignmentUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceReferenceReassignmentUndoToken token,
        ProductWorkspaceReferenceReassignmentUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.ReassignmentEditRevision != currentEditRevision)
        {
            return Failure(
                ProductWorkspaceReferenceReassignmentUndoStatus.EditRevisionChanged);
        }

        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceReferenceReassignmentUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceReferenceReassignmentUndoStatus.InvalidState);
        }

        if (!string.Equals(
            ProductWorkspaceConfigurationFingerprint.Compute(current.Document!),
            token.ReassignedConfigurationFingerprint,
            StringComparison.Ordinal)
            || !string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!),
                token.RestoreConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceReferenceReassignmentUndoStatus
                    .CurrentConfigurationChanged);
        }

        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceReferenceReassignmentUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceReferenceReassignmentUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceReferenceReassignmentUndoResult Failure(
        ProductWorkspaceReferenceReassignmentUndoStatus status) => new(status, null);
}
