namespace LongGrid.Core.Configuration;

public enum ProductDesktopWorkspaceCreatePublicationDecision
{
    AwaitingSave,
    Published,
    RollbackRequired,
    Superseded,
}

public sealed record ProductDesktopWorkspaceCreatePublicationToken(
    string ContainerId,
    long WorkspaceRevision,
    long SaveRevision,
    ProductWorkspaceReferenceBatchAdditionUndoToken? RestoreToken = null);

public static class ProductDesktopWorkspaceCreatePublication
{
    public static ProductDesktopWorkspaceCreatePublicationDecision Evaluate(
        ProductDesktopWorkspaceCreatePublicationToken token,
        ProductWorkspaceSaveSnapshot save,
        long currentWorkspaceRevision,
        bool createdContainerStillPresent)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.ContainerId);
        if (token.WorkspaceRevision <= 0 || token.SaveRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        if (currentWorkspaceRevision != token.WorkspaceRevision
            || save.CurrentRevision != token.SaveRevision
            || !createdContainerStillPresent)
        {
            return ProductDesktopWorkspaceCreatePublicationDecision.Superseded;
        }

        return save.Status switch
        {
            ProductWorkspaceSaveStatus.Saved
                when save.SavedRevision == token.SaveRevision =>
                ProductDesktopWorkspaceCreatePublicationDecision.Published,
            ProductWorkspaceSaveStatus.Failed =>
                ProductDesktopWorkspaceCreatePublicationDecision.RollbackRequired,
            ProductWorkspaceSaveStatus.WaitingForDebounce
                or ProductWorkspaceSaveStatus.Saving =>
                ProductDesktopWorkspaceCreatePublicationDecision.AwaitingSave,
            _ => ProductDesktopWorkspaceCreatePublicationDecision.Superseded,
        };
    }
}
