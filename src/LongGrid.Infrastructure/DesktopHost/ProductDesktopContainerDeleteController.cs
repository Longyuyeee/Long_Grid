using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopContainerDeleteStatus
{
    Accepted,
    AwaitingSave,
    Published,
    Compensated,
    Superseded,
    Rejected,
}

public sealed record ProductDesktopContainerDeleteResult(
    ProductDesktopContainerDeleteStatus Status,
    string ContainerId,
    long EditRevision,
    ProductWorkspaceEditError EditError = ProductWorkspaceEditError.None,
    ProductWorkspaceSaveFailure SourceFailure = ProductWorkspaceSaveFailure.None,
    ProductWorkspaceState? State = null,
    ProductConfigurationDocument? Document = null,
    ProductWorkspaceContainerRemovalUndoToken? RemovalUndoToken = null)
{
    public bool IsAccepted =>
        Status == ProductDesktopContainerDeleteStatus.Accepted
        && State is not null
        && Document is not null
        && RemovalUndoToken is not null;

    public bool IsCompensated =>
        Status == ProductDesktopContainerDeleteStatus.Compensated
        && State is not null
        && Document is not null;
}

public sealed class ProductDesktopContainerDeleteController
{
    private readonly object gate = new();
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private readonly ProductWorkspaceSaveController saves;
    private PendingDelete? pending;

    public ProductDesktopContainerDeleteController(
        ProductWorkspaceCommitCoordinator workspaceCommits,
        ProductWorkspaceSaveController saves)
    {
        ArgumentNullException.ThrowIfNull(workspaceCommits);
        ArgumentNullException.ThrowIfNull(saves);
        this.workspaceCommits = workspaceCommits;
        this.saves = saves;
    }

    public bool CanStart
    {
        get
        {
            lock (gate)
            {
                return pending is null;
            }
        }
    }

    public ProductDesktopContainerDeleteResult CommitConfirmed(
        ProductDesktopContainerMenuNavigationResult confirmation,
        ProductWorkspaceState? state)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        lock (gate)
        {
            if (pending is not null
                || state is null
                || !confirmation.IsAccepted
                || confirmation.Action !=
                    ProductDesktopContainerMenuAction.DeleteContainerConfiguration
                || confirmation.EditRevision != workspaceCommits.CurrentEditRevision
                || confirmation.ContainerOrdinal <= 0
                || confirmation.ContainerOrdinal > state.Containers.Count)
            {
                return Reject(confirmation.ContainerId, workspaceCommits.CurrentEditRevision);
            }

            ProductContainerState target =
                state.Containers[confirmation.ContainerOrdinal - 1];
            if (target.IsLocked
                || !string.Equals(
                    target.Id,
                    confirmation.ContainerId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    target.Placement.DisplayKey,
                    confirmation.DisplayId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    confirmation.ContainerId,
                    workspaceCommits.CurrentEditRevision,
                    target.IsLocked
                        ? ProductWorkspaceEditError.ContainerLocked
                        : ProductWorkspaceEditError.InvalidState);
            }

            ProductWorkspaceContainerCommitResult committed =
                workspaceCommits.CommitContainer(
                    state,
                    new(
                        ProductWorkspaceContainerCommitAction.Remove,
                        confirmation.EditRevision,
                        confirmation.ContainerOrdinal,
                        string.Empty,
                        Confirmed: true));
            if (!committed.IsAccepted || committed.RemovalUndoToken is null)
            {
                return Reject(
                    confirmation.ContainerId,
                    committed.EditRevision,
                    committed.EditError);
            }

            pending = new(
                confirmation.ContainerId,
                committed.EditRevision,
                saves.Snapshot.CurrentRevision,
                committed.RemovalUndoToken);
            return new(
                ProductDesktopContainerDeleteStatus.Accepted,
                confirmation.ContainerId,
                committed.EditRevision,
                State: committed.State,
                Document: committed.Document,
                RemovalUndoToken: committed.RemovalUndoToken);
        }
    }

    public ProductDesktopContainerDeleteResult ObserveSave(
        ProductWorkspaceState? state,
        long currentEditRevision,
        ProductWorkspaceSaveSnapshot save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (gate)
        {
            if (pending is not { } publication || state is null)
            {
                return Empty(currentEditRevision);
            }

            bool removed = state.Containers.All(container => !string.Equals(
                container.Id,
                publication.ContainerId,
                StringComparison.Ordinal));
            if (!removed
                || currentEditRevision != publication.EditRevision
                || save.CurrentRevision != publication.SaveRevision)
            {
                pending = null;
                return Result(
                    ProductDesktopContainerDeleteStatus.Superseded,
                    publication,
                    currentEditRevision);
            }

            if (save.Status is ProductWorkspaceSaveStatus.WaitingForDebounce
                or ProductWorkspaceSaveStatus.Saving)
            {
                return Result(
                    ProductDesktopContainerDeleteStatus.AwaitingSave,
                    publication,
                    currentEditRevision);
            }
            if (save.Status == ProductWorkspaceSaveStatus.Saved
                && save.SavedRevision == publication.SaveRevision)
            {
                pending = null;
                return Result(
                    ProductDesktopContainerDeleteStatus.Published,
                    publication,
                    currentEditRevision);
            }
            if (save.Status != ProductWorkspaceSaveStatus.Failed)
            {
                pending = null;
                return Result(
                    ProductDesktopContainerDeleteStatus.Superseded,
                    publication,
                    currentEditRevision);
            }

            ProductWorkspaceContainerRemovalUndoCommitResult compensated =
                workspaceCommits.CommitContainerRemovalUndo(
                    state,
                    publication.UndoToken,
                    confirmed: true);
            if (!compensated.IsAccepted)
            {
                return new(
                    ProductDesktopContainerDeleteStatus.Rejected,
                    publication.ContainerId,
                    compensated.EditRevision,
                    ProductWorkspaceEditError.InvalidState,
                    save.Failure);
            }

            pending = null;
            return new(
                ProductDesktopContainerDeleteStatus.Compensated,
                publication.ContainerId,
                compensated.EditRevision,
                SourceFailure: save.Failure,
                State: compensated.State,
                Document: compensated.Document);
        }
    }

    private static ProductDesktopContainerDeleteResult Empty(long revision) =>
        new(
            ProductDesktopContainerDeleteStatus.Superseded,
            string.Empty,
            revision);

    private static ProductDesktopContainerDeleteResult Reject(
        string containerId,
        long revision,
        ProductWorkspaceEditError error = ProductWorkspaceEditError.InvalidState) =>
        new(
            ProductDesktopContainerDeleteStatus.Rejected,
            containerId,
            revision,
            error);

    private static ProductDesktopContainerDeleteResult Result(
        ProductDesktopContainerDeleteStatus status,
        PendingDelete publication,
        long revision) =>
        new(status, publication.ContainerId, revision);

    private sealed record PendingDelete(
        string ContainerId,
        long EditRevision,
        long SaveRevision,
        ProductWorkspaceContainerRemovalUndoToken UndoToken);
}
