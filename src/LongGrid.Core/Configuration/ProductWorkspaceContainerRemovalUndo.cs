namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceContainerRemovalUndoToken(
    Guid OperationId,
    long RemovalEditRevision,
    string RemovedConfigurationFingerprint,
    string RestoreConfigurationFingerprint);

public enum ProductWorkspaceContainerRemovalUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceContainerRemovalUndoResult(
    ProductWorkspaceContainerRemovalUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerRemovalUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceContainerRemovalUndo
{
    public static ProductWorkspaceContainerRemovalUndoToken? Prepare(
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

    public static ProductWorkspaceContainerRemovalUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceContainerRemovalUndoToken token,
        ProductWorkspaceContainerRemovalUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.RemovalEditRevision != currentEditRevision)
        {
            return Failure(
                ProductWorkspaceContainerRemovalUndoStatus.EditRevisionChanged);
        }

        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceContainerRemovalUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceContainerRemovalUndoStatus.InvalidState);
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
                ProductWorkspaceContainerRemovalUndoStatus
                    .CurrentConfigurationChanged);
        }

        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceContainerRemovalUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceContainerRemovalUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceContainerRemovalUndoResult Failure(
        ProductWorkspaceContainerRemovalUndoStatus status) => new(status, null);
}
