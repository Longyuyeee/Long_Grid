using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopContainerMenuNavigationStatus
{
    Accepted,
    Rejected,
}

public sealed record ProductDesktopContainerMenuNavigationResult(
    ProductDesktopContainerMenuNavigationStatus Status,
    ProductDesktopContainerMenuAction Action,
    int ContainerOrdinal,
    long EditRevision,
    string ContainerId,
    string DisplayId,
    long TopologyGeneration)
{
    public bool IsAccepted =>
        Status == ProductDesktopContainerMenuNavigationStatus.Accepted
        && ContainerOrdinal > 0;
}

public static class ProductDesktopContainerMenuNavigationController
{
    public static ProductDesktopContainerMenuAvailability EvaluateAvailability(
        ProductWorkspaceState? state,
        bool isReadOnly,
        ProductWorkspaceSaveSnapshot save,
        string containerId,
        string displayId)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (state is null
            || string.IsNullOrWhiteSpace(containerId)
            || string.IsNullOrWhiteSpace(displayId))
        {
            return ProductDesktopContainerMenuAvailability.Unavailable;
        }

        ProductContainerState[] targets = state.Containers
            .Where(container => string.Equals(
                container.Id,
                containerId,
                StringComparison.Ordinal)
                && string.Equals(
                    container.Placement.DisplayKey,
                    displayId,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (targets.Length != 1)
        {
            return ProductDesktopContainerMenuAvailability.Unavailable;
        }

        bool editingAvailable = !isReadOnly
            && !targets[0].IsLocked
            && save.Status != ProductWorkspaceSaveStatus.Failed;
        return new(
            CanOpenRename: editingAvailable,
            CanOpenAppearance: editingAvailable,
            CanOpenSort: true,
            CanDeleteContainerConfiguration: editingAvailable);
    }

    public static ProductDesktopContainerMenuNavigationResult Handle(
        ProductDesktopContainerMenuRequest request,
        ProductWorkspaceState? state,
        bool isReadOnly,
        long currentEditRevision,
        ProductWorkspaceSaveSnapshot save,
        ProductDisplayTopologySnapshot topology)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(topology);
        if (state is null
            || !topology.IsAuthoritative
            || !Enum.IsDefined(request.Action)
            || request.ExpectedWorkspaceRevision != currentEditRevision
            || request.ExpectedTopologyGeneration != topology.Generation
            || !request.SourceAttested
            || request.IsInjected
            || request.IsAutoRepeat)
        {
            return Reject(request.Action, currentEditRevision);
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
        if (targets.Length != 1)
        {
            return Reject(request.Action, currentEditRevision);
        }

        ProductDesktopContainerMenuAvailability availability =
            EvaluateAvailability(
                state,
                isReadOnly,
                save,
                request.ContainerId,
                request.DisplayId);
        bool accepted = request.Action switch
        {
            ProductDesktopContainerMenuAction.OpenRename =>
                availability.CanOpenRename,
            ProductDesktopContainerMenuAction.OpenAppearance =>
                availability.CanOpenAppearance,
            ProductDesktopContainerMenuAction.OpenSort =>
                availability.CanOpenSort,
            ProductDesktopContainerMenuAction.DeleteContainerConfiguration =>
                availability.CanDeleteContainerConfiguration,
            _ => false,
        };
        return accepted
            ? new(
                ProductDesktopContainerMenuNavigationStatus.Accepted,
                request.Action,
                targets[0].Ordinal,
                currentEditRevision,
                request.ContainerId,
                request.DisplayId,
                topology.Generation)
            : Reject(request.Action, currentEditRevision);
    }

    private static ProductDesktopContainerMenuNavigationResult Reject(
        ProductDesktopContainerMenuAction action,
        long revision) => new(
            ProductDesktopContainerMenuNavigationStatus.Rejected,
            action,
            ContainerOrdinal: 0,
            revision,
            ContainerId: string.Empty,
            DisplayId: string.Empty,
            TopologyGeneration: 0);
}
