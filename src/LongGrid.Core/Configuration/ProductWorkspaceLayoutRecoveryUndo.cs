namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceLayoutRecoveryUndoToken(
    Guid OperationId,
    long RecoveryEditRevision,
    string RecoveredConfigurationFingerprint,
    string RestoreConfigurationFingerprint,
    int ContainerCount);

public enum ProductWorkspaceLayoutRecoveryUndoStatus
{
    Accepted,
    ConfirmationRequired,
    Unavailable,
    EditRevisionChanged,
    TokenMismatch,
    CurrentConfigurationChanged,
    InvalidState,
}

public sealed record ProductWorkspaceLayoutRecoveryUndoResult(
    ProductWorkspaceLayoutRecoveryUndoStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceLayoutRecoveryUndoStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceLayoutRecoveryUndo
{
    public static ProductWorkspaceLayoutRecoveryUndoToken? Prepare(
        ProductWorkspaceState restoreState,
        ProductWorkspaceState recoveredState,
        long recoveryEditRevision,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(recoveredState);
        if (recoveryEditRevision <= 0 || operationId == Guid.Empty)
        {
            return null;
        }

        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        ProductWorkspaceProjectionResult recovered =
            ProductWorkspaceConfigurationProjector.Project(recoveredState);
        if (!restore.IsSuccess || !recovered.IsSuccess)
        {
            return null;
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        string recoveredFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(recovered.Document!);
        if (string.Equals(
            restoreFingerprint,
            recoveredFingerprint,
            StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            operationId,
            recoveryEditRevision,
            recoveredFingerprint,
            restoreFingerprint,
            restoreState.Containers.Count);
    }

    public static ProductWorkspaceLayoutRecoveryUndoResult Confirm(
        ProductWorkspaceState currentState,
        ProductWorkspaceState restoreState,
        long currentEditRevision,
        ProductWorkspaceLayoutRecoveryUndoToken token,
        ProductWorkspaceLayoutRecoveryUndoToken expectedToken,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(restoreState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (token.RecoveryEditRevision != currentEditRevision)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryUndoStatus.EditRevisionChanged);
        }

        if (token != expectedToken)
        {
            return Failure(ProductWorkspaceLayoutRecoveryUndoStatus.TokenMismatch);
        }

        ProductWorkspaceProjectionResult current =
            ProductWorkspaceConfigurationProjector.Project(currentState);
        ProductWorkspaceProjectionResult restore =
            ProductWorkspaceConfigurationProjector.Project(restoreState);
        if (!current.IsSuccess || !restore.IsSuccess)
        {
            return Failure(ProductWorkspaceLayoutRecoveryUndoStatus.InvalidState);
        }

        string currentFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(current.Document!);
        if (!string.Equals(
            currentFingerprint,
            token.RecoveredConfigurationFingerprint,
            StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryUndoStatus
                    .CurrentConfigurationChanged);
        }

        string restoreFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(restore.Document!);
        if (!string.Equals(
                restoreFingerprint,
                token.RestoreConfigurationFingerprint,
                StringComparison.Ordinal)
            || token.ContainerCount != restoreState.Containers.Count)
        {
            return Failure(ProductWorkspaceLayoutRecoveryUndoStatus.TokenMismatch);
        }

        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryUndoStatus.ConfirmationRequired);
        }

        return new(
            ProductWorkspaceLayoutRecoveryUndoStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                restoreState,
                Changed: true));
    }

    private static ProductWorkspaceLayoutRecoveryUndoResult Failure(
        ProductWorkspaceLayoutRecoveryUndoStatus status) => new(status, null);
}
