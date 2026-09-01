using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public static class ProductDesktopHostProjectionBuilder
{
    public static ProductDesktopHostProjectionUpdate BuildUpdate(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision,
        IReadOnlyDictionary<string, ProductDesktopThumbnailResult>?
            thumbnailResults = null,
        IReadOnlyDictionary<string, int>? viewportStarts = null,
        long presentationGeneration = 0,
        ProductDesktopSearchNavigationTarget? searchTarget = null)
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
                unavailableDisposition,
                presentationGeneration: presentationGeneration);
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
                ProductDesktopHostProjectionDisposition.Invalid,
                presentationGeneration: presentationGeneration);
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
                    [emptyDisplay],
                    presentationGeneration);
            return ProductDesktopHostProjectionUpdate.Create(
                workspaceRevision,
                topology.Generation,
                ProductDesktopHostProjectionDisposition.EmptyWorkspace,
                emptyBatch,
                presentationGeneration);
        }

        ProductDesktopHostProjectionBatch? batch = Build(
            state,
            readSnapshot,
            topology,
            workspaceRevision,
            thumbnailResults,
            viewportStarts,
            presentationGeneration,
            searchTarget);
        return ProductDesktopHostProjectionUpdate.Create(
            workspaceRevision,
            topology.Generation,
            batch is null
                ? ProductDesktopHostProjectionDisposition.Invalid
                : ProductDesktopHostProjectionDisposition.Ready,
            batch,
            presentationGeneration);
    }

    public static ProductDesktopHostProjectionBatch? Build(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision,
        IReadOnlyDictionary<string, ProductDesktopThumbnailResult>?
            thumbnailResults = null,
        IReadOnlyDictionary<string, int>? viewportStarts = null,
        long presentationGeneration = 0,
        ProductDesktopSearchNavigationTarget? searchTarget = null)
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
            ProductContainerContentDensity contentDensity =
                source.Appearance.ContentDensity;
            bool isSearchTarget = searchTarget?.IsApplied == true
                && searchTarget.WorkspaceRevision == workspaceRevision
                && searchTarget.TopologyGeneration == topology.Generation
                && string.Equals(
                    searchTarget.ContainerId,
                    source.Id,
                    StringComparison.Ordinal);
            int viewportStart = ProductDesktopItemViewportPolicy.ClampStart(
                isSearchTarget && searchTarget!.ItemId is not null
                    ? searchTarget.ViewportStart
                    : viewportStarts is not null
                    && viewportStarts.TryGetValue(source.Id, out int requestedStart)
                        ? requestedStart
                        : 0,
                visible.Items.Count,
                contentDensity);
            ProductWorkspaceReadItem[] viewportItems = visible.Items
                .Skip(viewportStart)
                .Take(ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(
                    contentDensity))
                .ToArray();
            IEnumerable<string> itemNames = viewportItems.Select(item =>
            {
                string name = item.UserVisibleName
                    ?? $"待审查项目 {item.Ordinal}";
                return name.Length <=
                    ProductDesktopHostReadOnlyProjection.MaximumVisibleNameLength
                    ? name
                    : name[..ProductDesktopHostReadOnlyProjection.MaximumVisibleNameLength];
            });
            IEnumerable<string> itemIds = viewportItems.Select(item =>
                item.ItemId ?? $"item:{item.Ordinal}");
            IEnumerable<ProductDesktopItemVisualPresentation> itemVisuals =
                viewportItems.Select(item => ApplyThumbnailResult(
                    source.Id,
                    item,
                    thumbnailResults));
            ProductDesktopHostReadOnlyProjection container =
                ProductDesktopHostReadOnlyProjection.Create(
                    source.Id,
                    visible.UserVisibleName,
                    itemNames,
                    visible.Color,
                    visible.Opacity,
                    visible.IsCollapsed && !isSearchTarget,
                    visible.XDip,
                    visible.YDip,
                    visible.WidthDip,
                    visible.HeightDip,
                    source.IsLocked,
                    itemIds,
                    visible.Items.Count,
                    itemVisuals,
                    source.Appearance.TitleVisibility,
                    source.Appearance.TitleDoubleClickAction,
                    visible.Items.Count == 0 ? 0 : viewportStart + 1,
                    contentDensity,
                    searchHighlighted: isSearchTarget,
                    searchHighlightedItemId: isSearchTarget
                        ? searchTarget!.ItemId
                        : null);
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
            displays,
            presentationGeneration);
    }

    private static ProductDesktopItemVisualPresentation ApplyThumbnailResult(
        string containerId,
        ProductWorkspaceReadItem item,
        IReadOnlyDictionary<string, ProductDesktopThumbnailResult>?
            thumbnailResults)
    {
        ProductDesktopItemVisualPresentation fallback =
            ProductDesktopItemVisualPresentation.Create(
                item.Kind,
                item.Resolution);
        if (item.Source != ProductWorkspaceReadItemSource.Reference
            || thumbnailResults is null
            || !thumbnailResults.TryGetValue(
                ProductDesktopThumbnailItemKey.Create(containerId, item.Ordinal),
                out ProductDesktopThumbnailResult? thumbnail))
        {
            return fallback;
        }
        return thumbnail.Status switch
        {
            ProductDesktopThumbnailStatus.LoadingThumbnail => fallback with
            {
                Status = ProductDesktopItemVisualStatus.LoadingThumbnail,
            },
            ProductDesktopThumbnailStatus.ReadyThumbnail
                when thumbnail.Frame is not null => fallback with
                {
                    Status = ProductDesktopItemVisualStatus.ReadyThumbnail,
                    Thumbnail = thumbnail.Frame,
                },
            ProductDesktopThumbnailStatus.FailedFallback
                or ProductDesktopThumbnailStatus.Unsupported => fallback with
                {
                    Status = ProductDesktopItemVisualStatus.FailedFallback,
                },
            _ => fallback,
        };
    }
}
