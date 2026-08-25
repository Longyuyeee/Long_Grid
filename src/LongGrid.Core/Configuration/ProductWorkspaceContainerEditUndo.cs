namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerEditUndoKind
{
    Rename,
    Locked,
    Collapsed,
    Appearance,
    Placement,
}

public sealed record ProductWorkspaceContainerEditUndoToken(
    Guid OperationId,
    long EditRevision,
    ProductWorkspaceContainerEditUndoKind Kind,
    string EditedConfigurationFingerprint,
    string RestoreConfigurationFingerprint);

public enum ProductWorkspaceContainerEditUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceContainerEditUndoResult(
    ProductWorkspaceContainerEditUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerEditUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceContainerEditUndo
{
    public static ProductWorkspaceContainerEditUndoToken? Prepare(
        ProductWorkspaceState restoreState,
        ProductWorkspaceState editedState,
        long editRevision,
        ProductWorkspaceContainerEditUndoKind kind,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(editedState);
        if (editRevision <= 0
            || operationId == Guid.Empty
            || !Enum.IsDefined(kind))
        {
            return null;
        }

        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        ProductWorkspaceProjectionResult edited =
            ProductWorkspaceConfigurationProjector.Project(editedState);
        if (!restore.IsSuccess || !edited.IsSuccess)
        {
            return null;
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        string editedFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(edited.Document!);
        if (string.Equals(
            restoreFingerprint,
            editedFingerprint,
            StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            operationId,
            editRevision,
            kind,
            editedFingerprint,
            restoreFingerprint);
    }

    public static ProductWorkspaceContainerEditUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceContainerEditUndoToken token,
        ProductWorkspaceContainerEditUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.EditRevision != currentEditRevision)
        {
            return Failure(
                ProductWorkspaceContainerEditUndoStatus.EditRevisionChanged);
        }
        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceContainerEditUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceContainerEditUndoStatus.InvalidState);
        }
        if (!string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(current.Document!),
                token.EditedConfigurationFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!),
                token.RestoreConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceContainerEditUndoStatus
                    .CurrentConfigurationChanged);
        }
        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceContainerEditUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceContainerEditUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceContainerEditUndoResult Failure(
        ProductWorkspaceContainerEditUndoStatus status) => new(status, null);
}
