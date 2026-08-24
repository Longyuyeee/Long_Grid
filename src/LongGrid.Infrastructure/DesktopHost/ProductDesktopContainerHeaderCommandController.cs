using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopContainerHeaderCommandStatus
{
    Accepted,
    AwaitingSave,
    Published,
    Compensated,
    Superseded,
    Rejected,
}

public sealed record ProductDesktopContainerHeaderCommandResult(
    ProductDesktopContainerHeaderCommandStatus Status,
    ProductDesktopContainerHeaderCommandKind Kind,
    string ContainerId,
    long EditRevision,
    ProductWorkspaceEditError EditError = ProductWorkspaceEditError.None,
    ProductWorkspaceSaveFailure SourceFailure =
        ProductWorkspaceSaveFailure.None,
    ProductWorkspaceState? State = null,
    ProductConfigurationDocument? Document = null)
{
    public bool IsAccepted =>
        Status == ProductDesktopContainerHeaderCommandStatus.Accepted
        && State is not null
        && Document is not null;

    public bool IsCompensated =>
        Status == ProductDesktopContainerHeaderCommandStatus.Compensated
        && State is not null
        && Document is not null;
}

public sealed class ProductDesktopContainerHeaderCommandController
{
    private readonly object gate = new();
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private readonly ProductWorkspaceSaveController saves;
    private PendingPublication? publication;

    public ProductDesktopContainerHeaderCommandController(
        ProductWorkspaceCommitCoordinator workspaceCommits,
        ProductWorkspaceSaveController saves)
    {
        ArgumentNullException.ThrowIfNull(workspaceCommits);
        ArgumentNullException.ThrowIfNull(saves);
        this.workspaceCommits = workspaceCommits;
        this.saves = saves;
    }

    public ProductDesktopContainerHeaderCommandResult Handle(
        ProductDesktopContainerHeaderCommandRequest request,
        ProductWorkspaceState? state,
        bool isReadOnly,
        long currentEditRevision,
        ProductDisplayTopologySnapshot topology)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(topology);
        lock (gate)
        {
            if (publication is not null
                || state is null
                || isReadOnly
                || !topology.IsAuthoritative
                || !Enum.IsDefined(request.Kind)
                || !request.SourceAttested
                || request.IsInjected
                || request.IsAutoRepeat
                || request.ExpectedWorkspaceRevision != currentEditRevision
                || request.ExpectedTopologyGeneration != topology.Generation
                || string.IsNullOrWhiteSpace(request.ContainerId)
                || string.IsNullOrWhiteSpace(request.DisplayId))
            {
                return Reject(request, currentEditRevision);
            }

            (ProductContainerState Container, int Ordinal)[] targets = state.Containers
                .Select((container, index) => (Container: container, Ordinal: index + 1))
                .Where(candidate => string.Equals(
                    candidate.Container.Id,
                    request.ContainerId,
                    StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Container.Placement.DisplayKey,
                        request.DisplayId,
                        StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (targets.Length != 1
                || (request.Kind ==
                        ProductDesktopContainerHeaderCommandKind.ToggleCollapsed
                    && targets[0].Container.IsLocked))
            {
                return Reject(
                    request,
                    currentEditRevision,
                    targets.Length == 1 && targets[0].Container.IsLocked
                        ? ProductWorkspaceEditError.ContainerLocked
                        : ProductWorkspaceEditError.InvalidState);
            }

            bool originalValue = CurrentValue(targets[0].Container, request.Kind);
            ProductWorkspaceContainerCommitResult committed =
                workspaceCommits.CommitContainer(
                    state,
                    CommitRequest(
                        request.Kind,
                        currentEditRevision,
                        targets[0].Ordinal,
                        !originalValue));
            if (!committed.IsAccepted)
            {
                return Reject(
                    request,
                    committed.EditRevision,
                    committed.EditError);
            }

            publication = new(
                request.Kind,
                request.ContainerId,
                committed.EditRevision,
                saves.Snapshot.CurrentRevision,
                originalValue,
                !originalValue);
            return new(
                ProductDesktopContainerHeaderCommandStatus.Accepted,
                request.Kind,
                request.ContainerId,
                committed.EditRevision,
                State: committed.State,
                Document: committed.Document);
        }
    }

    public ProductDesktopContainerHeaderCommandResult ObserveSave(
        ProductWorkspaceState? state,
        long currentEditRevision,
        ProductWorkspaceSaveSnapshot save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (gate)
        {
            if (publication is not { } pending || state is null)
            {
                return Empty(currentEditRevision);
            }

            (ProductContainerState Container, int Ordinal)[] targets = state.Containers
                .Select((container, index) => (Container: container, Ordinal: index + 1))
                .Where(candidate => string.Equals(
                    candidate.Container.Id,
                    pending.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (targets.Length != 1
                || currentEditRevision != pending.EditRevision
                || save.CurrentRevision != pending.SaveRevision
                || CurrentValue(targets[0].Container, pending.Kind)
                    != pending.CommittedValue)
            {
                publication = null;
                return Result(
                    ProductDesktopContainerHeaderCommandStatus.Superseded,
                    pending,
                    currentEditRevision);
            }

            if (save.Status is ProductWorkspaceSaveStatus.WaitingForDebounce
                or ProductWorkspaceSaveStatus.Saving)
            {
                return Result(
                    ProductDesktopContainerHeaderCommandStatus.AwaitingSave,
                    pending,
                    currentEditRevision);
            }
            if (save.Status == ProductWorkspaceSaveStatus.Saved
                && save.SavedRevision == pending.SaveRevision)
            {
                publication = null;
                return Result(
                    ProductDesktopContainerHeaderCommandStatus.Published,
                    pending,
                    currentEditRevision);
            }
            if (save.Status != ProductWorkspaceSaveStatus.Failed)
            {
                publication = null;
                return Result(
                    ProductDesktopContainerHeaderCommandStatus.Superseded,
                    pending,
                    currentEditRevision);
            }

            ProductWorkspaceContainerCommitResult compensated =
                workspaceCommits.CommitContainer(
                    state,
                    CommitRequest(
                        pending.Kind,
                        currentEditRevision,
                        targets[0].Ordinal,
                        pending.OriginalValue));
            if (!compensated.IsAccepted)
            {
                return Result(
                    ProductDesktopContainerHeaderCommandStatus.Rejected,
                    pending,
                    compensated.EditRevision,
                    compensated.EditError,
                    save.Failure);
            }

            publication = null;
            return new(
                ProductDesktopContainerHeaderCommandStatus.Compensated,
                pending.Kind,
                pending.ContainerId,
                compensated.EditRevision,
                SourceFailure: save.Failure,
                State: compensated.State,
                Document: compensated.Document);
        }
    }

    private static bool CurrentValue(
        ProductContainerState container,
        ProductDesktopContainerHeaderCommandKind kind) => kind switch
        {
            ProductDesktopContainerHeaderCommandKind.ToggleCollapsed =>
                container.Appearance.Collapsed,
            ProductDesktopContainerHeaderCommandKind.ToggleLocked =>
                container.IsLocked,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static ProductWorkspaceContainerCommitRequest CommitRequest(
        ProductDesktopContainerHeaderCommandKind kind,
        long expectedEditRevision,
        int ordinal,
        bool value) =>
        new(
            kind == ProductDesktopContainerHeaderCommandKind.ToggleCollapsed
                ? ProductWorkspaceContainerCommitAction.SetCollapsed
                : ProductWorkspaceContainerCommitAction.SetLocked,
            expectedEditRevision,
            ordinal,
            string.Empty,
            NewContainer: null,
            value,
            ColorPreset: null,
            OpacityPreset: null,
            PositionPreset: null,
            SizePreset: null,
            Confirmed: false);

    private static ProductDesktopContainerHeaderCommandResult Empty(
        long revision) =>
        new(
            ProductDesktopContainerHeaderCommandStatus.Superseded,
            ProductDesktopContainerHeaderCommandKind.ToggleCollapsed,
            string.Empty,
            revision);

    private static ProductDesktopContainerHeaderCommandResult Reject(
        ProductDesktopContainerHeaderCommandRequest request,
        long revision,
        ProductWorkspaceEditError error = ProductWorkspaceEditError.InvalidState) =>
        new(
            ProductDesktopContainerHeaderCommandStatus.Rejected,
            request.Kind,
            request.ContainerId,
            revision,
            error);

    private static ProductDesktopContainerHeaderCommandResult Result(
        ProductDesktopContainerHeaderCommandStatus status,
        PendingPublication pending,
        long revision,
        ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
        ProductWorkspaceSaveFailure sourceFailure =
            ProductWorkspaceSaveFailure.None) =>
        new(
            status,
            pending.Kind,
            pending.ContainerId,
            revision,
            editError,
            sourceFailure);

    private sealed record PendingPublication(
        ProductDesktopContainerHeaderCommandKind Kind,
        string ContainerId,
        long EditRevision,
        long SaveRevision,
        bool OriginalValue,
        bool CommittedValue);
}
