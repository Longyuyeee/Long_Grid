namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceReferenceRemovalUndoToken(
    Guid OperationId,
    long RemovalEditRevision,
    string RemovedConfigurationFingerprint,
    string RestoreConfigurationFingerprint);

public enum ProductWorkspaceReferenceRemovalUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceRemovalUndoResult(
    ProductWorkspaceReferenceRemovalUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceRemovalUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceReferenceRemovalUndo
{
    public static ProductWorkspaceReferenceRemovalUndoToken? Prepare(
        ProductWorkspaceState restoreState,
        ProductWorkspaceState removedState,
        long removalEditRevision,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(removedState);
        if (removalEditRevision <= 0 || operationId == Guid.Empty)
        {
            return null;
        }

        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        ProductWorkspaceProjectionResult removed =
            ProductWorkspaceConfigurationProjector.Project(removedState);
        if (!restore.IsSuccess || !removed.IsSuccess)
        {
            return null;
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        string removedFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(removed.Document!);
        if (string.Equals(
            restoreFingerprint,
            removedFingerprint,
            StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            operationId,
            removalEditRevision,
            removedFingerprint,
            restoreFingerprint);
    }

    public static ProductWorkspaceReferenceRemovalUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceReferenceRemovalUndoToken token,
        ProductWorkspaceReferenceRemovalUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.RemovalEditRevision != currentEditRevision)
        {
            return Failure(ProductWorkspaceReferenceRemovalUndoStatus.EditRevisionChanged);
        }

        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceReferenceRemovalUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceReferenceRemovalUndoStatus.InvalidState);
        }

        if (!string.Equals(
            ProductWorkspaceConfigurationFingerprint.Compute(current.Document!),
            token.RemovedConfigurationFingerprint,
            StringComparison.Ordinal)
            || !string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!),
                token.RestoreConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceReferenceRemovalUndoStatus.CurrentConfigurationChanged);
        }

        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceReferenceRemovalUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceReferenceRemovalUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceReferenceRemovalUndoResult Failure(
        ProductWorkspaceReferenceRemovalUndoStatus status) => new(status, null);
}
