namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceReferenceBatchAdditionUndoToken(
    Guid OperationId,
    long AdditionEditRevision,
    string AddedConfigurationFingerprint,
    string RestoreConfigurationFingerprint);

public enum ProductWorkspaceReferenceBatchAdditionUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceBatchAdditionUndoResult(
    ProductWorkspaceReferenceBatchAdditionUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceBatchAdditionUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceReferenceBatchAdditionUndo
{
    public static ProductWorkspaceReferenceBatchAdditionUndoToken? Prepare(
        ProductWorkspaceState restoreState,
        ProductWorkspaceState addedState,
        long additionEditRevision,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(addedState);
        if (additionEditRevision <= 0 || operationId == Guid.Empty)
        {
            return null;
        }

        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        ProductWorkspaceProjectionResult added =
            ProductWorkspaceConfigurationProjector.Project(addedState);
        if (!restore.IsSuccess || !added.IsSuccess)
        {
            return null;
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        string addedFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(added.Document!);
        if (string.Equals(restoreFingerprint, addedFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            operationId,
            additionEditRevision,
            addedFingerprint,
            restoreFingerprint);
    }

    public static ProductWorkspaceReferenceBatchAdditionUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceReferenceBatchAdditionUndoToken token,
        ProductWorkspaceReferenceBatchAdditionUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.AdditionEditRevision != currentEditRevision)
        {
            return Failure(ProductWorkspaceReferenceBatchAdditionUndoStatus.EditRevisionChanged);
        }

        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceReferenceBatchAdditionUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceReferenceBatchAdditionUndoStatus.InvalidState);
        }

        if (!string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(current.Document!),
                token.AddedConfigurationFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!),
                token.RestoreConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceReferenceBatchAdditionUndoStatus.CurrentConfigurationChanged);
        }

        if (!confirmed)
        {
            return Failure(ProductWorkspaceReferenceBatchAdditionUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceReferenceBatchAdditionUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceReferenceBatchAdditionUndoResult Failure(
        ProductWorkspaceReferenceBatchAdditionUndoStatus status) => new(status, null);
}
