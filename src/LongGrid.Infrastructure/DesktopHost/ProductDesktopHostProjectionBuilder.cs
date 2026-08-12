using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public static class ProductDesktopHostProjectionBuilder
{
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
                    visible.HeightDip);
            string displayId = byDisplay.ContainsKey(source.Placement.DisplayKey)
                ? source.Placement.DisplayKey
                : primary.StableId;
            byDisplay[displayId].Add(container);
        }

        ProductDesktopHostDisplayProjection[] displays = topology.Displays
            .Where(display => byDisplay[display.StableId].Count > 0)
            .Select(display => ProductDesktopHostDisplayProjection.Create(
                display.StableId,
                display.WorkArea,
                display.EffectiveDpi,
                byDisplay[display.StableId]))
            .ToArray();
        return ProductDesktopHostProjectionBatch.Create(
            workspaceRevision,
            topology.Generation,
            DisplayTopologyFingerprint.Compute(topology.Displays),
            displays);
    }
}
