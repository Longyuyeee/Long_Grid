using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopSearchNavigationStatus
{
    Applied,
    StaleAuthority,
    TargetUnavailable,
    Invalid,
}

public sealed record ProductDesktopSearchNavigationRequest(
    long ExpectedWorkspaceRevision,
    long ExpectedTopologyGeneration,
    int ContainerOrdinal,
    int? ItemOrdinal);

public sealed record ProductDesktopSearchNavigationTarget(
    ProductDesktopSearchNavigationStatus Status,
    long WorkspaceRevision,
    long TopologyGeneration,
    string? DisplayId,
    string? ContainerId,
    string? ItemId,
    int ViewportStart,
    bool TemporarilyExpandsContainer)
{
    public bool IsApplied => Status == ProductDesktopSearchNavigationStatus.Applied;
}

public static class ProductDesktopSearchNavigation
{
    public static ProductDesktopSearchNavigationTarget Resolve(
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceState state,
        ProductWorkspaceReadSnapshot snapshot,
        ProductDesktopSearchNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        if (currentWorkspaceRevision < 0
            || request.ExpectedWorkspaceRevision < 0
            || request.ExpectedTopologyGeneration < 0
            || request.ContainerOrdinal <= 0
            || request.ItemOrdinal is <= 0
            || state.Containers.Count != snapshot.Containers.Count)
        {
            return Result(ProductDesktopSearchNavigationStatus.Invalid);
        }

        if (request.ExpectedWorkspaceRevision != currentWorkspaceRevision
            || request.ExpectedTopologyGeneration != topology.Generation)
        {
            return Result(ProductDesktopSearchNavigationStatus.StaleAuthority);
        }

        if (!topology.IsAuthoritative
            || topology.Displays.Count == 0
            || topology.Displays.Count(display => display.IsPrimary) != 1
            || request.ContainerOrdinal > state.Containers.Count)
        {
            return Result(ProductDesktopSearchNavigationStatus.TargetUnavailable);
        }

        int containerIndex = request.ContainerOrdinal - 1;
        ProductContainerState container = state.Containers[containerIndex];
        ProductWorkspaceReadContainer visible = snapshot.Containers[containerIndex];
        ProductWorkspaceReadItem? item = request.ItemOrdinal is { } itemOrdinal
            && itemOrdinal <= visible.Items.Count
                ? visible.Items[itemOrdinal - 1]
                : null;
        if (request.ItemOrdinal is not null && item is null)
        {
            return Result(ProductDesktopSearchNavigationStatus.TargetUnavailable);
        }

        DisplayTopologyNode display = topology.Displays.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.StableId,
                    container.Placement.DisplayKey,
                    StringComparison.Ordinal))
            ?? topology.Displays.Single(candidate => candidate.IsPrimary);
        int viewportStart = item is null
            ? 0
            : ProductDesktopItemViewportPolicy.ClampStart(
                item.Ordinal - 1,
                visible.Items.Count,
                visible.ContentDensity);
        return new(
            ProductDesktopSearchNavigationStatus.Applied,
            currentWorkspaceRevision,
            topology.Generation,
            display.StableId,
            container.Id,
            item?.ItemId ?? (item is null ? null : $"item:{item.Ordinal}"),
            viewportStart,
            visible.IsCollapsed);

        ProductDesktopSearchNavigationTarget Result(
            ProductDesktopSearchNavigationStatus status) =>
            new(
                status,
                currentWorkspaceRevision,
                topology.Generation,
                null,
                null,
                null,
                0,
                false);
    }
}
