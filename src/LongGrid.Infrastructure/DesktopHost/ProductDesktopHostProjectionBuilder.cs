using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public static class ProductDesktopHostProjectionBuilder
{
    public static ProductDesktopHostProjectionUpdate BuildUpdate(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceRevision);

        ProductDesktopHostProjectionDisposition unavailableDisposition =
            topology.Status == ProductDisplayTopologyStatus.Refreshing
                ? ProductDesktopHostProjectionDisposition.TopologyRefreshing
                : ProductDesktopHostProjectionDisposition.TopologyUnavailable;
        if (!topology.IsAuthoritative)
        {
            return ProductDesktopHostProjectionUpdate.Create(
                workspaceRevision,
                topology.Generation,
                unavailableDisposition);
        }

        if (state is null
            || readSnapshot is null
            || readSnapshot.Containers.Count != state.Containers.Count
            || topology.Displays.Count == 0
            || topology.Displays.Count(display => display.IsPrimary) != 1
            || topology.Displays.Count >
                ProductDesktopHostProjectionBatch.MaximumDisplays)
        {
            return ProductDesktopHostProjectionUpdate.Create(
                workspaceRevision,
                topology.Generation,
                ProductDesktopHostProjectionDisposition.Invalid);
        }

        if (state.Containers.Count == 0)
        {
            DisplayTopologyNode primary = topology.Displays.Single(display =>
                display.IsPrimary);
            ProductDesktopHostDisplayProjection emptyDisplay =
                ProductDesktopHostDisplayProjection.Create(
                    primary.StableId,
                    primary.WorkArea,
                    primary.EffectiveDpi,
                    Array.Empty<ProductDesktopHostReadOnlyProjection>(),
                    isPrimary: true,
                    workspaceIsEmpty: true);
            ProductDesktopHostProjectionBatch emptyBatch =
                ProductDesktopHostProjectionBatch.Create(
                    workspaceRevision,
                    topology.Generation,
                    DisplayTopologyFingerprint.Compute(topology.Displays),
                    [emptyDisplay]);
            return ProductDesktopHostProjectionUpdate.Create(
                workspaceRevision,
                topology.Generation,
                ProductDesktopHostProjectionDisposition.EmptyWorkspace,
                emptyBatch);
        }

        ProductDesktopHostProjectionBatch? batch = Build(
            state,
            readSnapshot,
            topology,
            workspaceRevision);
        return ProductDesktopHostProjectionUpdate.Create(
            workspaceRevision,
            topology.Generation,
            batch is null
                ? ProductDesktopHostProjectionDisposition.Invalid
                : ProductDesktopHostProjectionDisposition.Ready,
            batch);
    }

    public static ProductDesktopHostProjectionBatch? Build(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (state is null
            || readSnapshot is null
            || !topology.IsAuthoritative
            || state.Containers.Count == 0
            || readSnapshot.Containers.Count != state.Containers.Count
            || topology.Displays.Count > ProductDesktopHostProjectionBatch.MaximumDisplays)
        {
            return null;
        }

        DisplayTopologyNode primary = topology.Displays.Single(display =>
            display.IsPrimary);
        var byDisplay = topology.Displays.ToDictionary(
            display => display.StableId,
            _ => new List<ProductDesktopHostReadOnlyProjection>(),
            StringComparer.Ordinal);
        for (int index = 0; index < state.Containers.Count; index++)
        {
            ProductContainerState source = state.Containers[index];
            ProductWorkspaceReadContainer visible = readSnapshot.Containers[index];
            IEnumerable<string> itemNames = visible.Items.Select(item =>
            {
                string name = item.UserVisibleName
                    ?? $"待审查项目 {item.Ordinal}";
                return name.Length <=
                    ProductDesktopHostReadOnlyProjection.MaximumVisibleNameLength
                    ? name
                    : name[..ProductDesktopHostReadOnlyProjection.MaximumVisibleNameLength];
            });
            IEnumerable<string> itemIds = visible.Items.Select(item =>
                $"item:{item.Ordinal}");
            ProductDesktopHostReadOnlyProjection container =
                ProductDesktopHostReadOnlyProjection.Create(
                    source.Id,
                    visible.UserVisibleName,
                    itemNames,
                    visible.Color,
                    visible.Opacity,
                    visible.IsCollapsed,
                    visible.XDip,
                    visible.YDip,
                    visible.WidthDip,
                    visible.HeightDip,
                    source.IsLocked,
                    itemIds,
                    visible.Items.Count);
            string displayId = byDisplay.ContainsKey(source.Placement.DisplayKey)
                ? source.Placement.DisplayKey
                : primary.StableId;
            byDisplay[displayId].Add(container);
        }

        ProductDesktopHostDisplayProjection[] displays = topology.Displays
            .Select(display => ProductDesktopHostDisplayProjection.Create(
                display.StableId,
                display.WorkArea,
                display.EffectiveDpi,
                byDisplay[display.StableId],
                display.IsPrimary,
                workspaceIsEmpty: false))
            .ToArray();
        return ProductDesktopHostProjectionBatch.Create(
            workspaceRevision,
            topology.Generation,
            DisplayTopologyFingerprint.Compute(topology.Displays),
            displays);
    }
}
