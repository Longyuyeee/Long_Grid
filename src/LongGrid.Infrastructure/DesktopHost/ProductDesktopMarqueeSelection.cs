using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal readonly record struct ProductDesktopMarqueePoint(int X, int Y);

internal sealed record ProductDesktopMarqueeSelectionSession(
    string DisplayId,
    string ContainerId,
    PixelRect ContainerBounds,
    int ContentTop,
    int ItemHeight,
    Guid LeaseIntentId,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    long SelectionRevision,
    IReadOnlyList<string> VisibleItemIds,
    ProductDesktopMarqueePoint Start,
    ProductDesktopMarqueePoint Current,
    bool ControlPressed);

internal sealed record ProductDesktopMarqueeSelectionCommand(
    string ContainerId,
    ProductDesktopSelectionRequest Request,
    PixelRect Bounds);

internal static class ProductDesktopMarqueeSelectionAdapter
{
    private const int ItemHorizontalInsetDip = 6;

    internal static ProductDesktopMarqueeSelectionSession? TryStart(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        int x,
        int y,
        bool control,
        bool shift)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (shift
            || transaction?.IsExplicit != true
            || transaction.Admission.Lease is not { } lease
            || transaction.Selection is not { } selection)
        {
            return null;
        }

        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                selection.ContainerId,
                StringComparison.Ordinal));
        if (container is null
            || container.IsCollapsed
            || !container.ItemIds.SequenceEqual(
                selection.VisibleItemIds,
                StringComparer.Ordinal))
        {
            return null;
        }

        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            projection,
            container);
        double scale = projection.EffectiveDpi / 96d;
        int border = Math.Max(
            4,
            ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopContainerLayoutHitTestAdapter.ResizeBorderDip,
                scale));
        int headerHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int itemHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.ItemHeightDip,
            scale);
        int contentTop = checked(bounds.Top + headerHeight);
        int itemBottom = checked(contentTop + (container.ItemIds.Count * itemHeight));
        bool safeBlankStart = x >= bounds.Left + border
            && x < bounds.Right - border
            && y >= itemBottom
            && y < bounds.Bottom - border;
        if (!safeBlankStart)
        {
            return null;
        }

        return new(
            projection.DisplayId,
            container.ContainerId,
            bounds,
            contentTop,
            itemHeight,
            lease.IntentId,
            lease.WorkspaceRevision,
            lease.TopologyGeneration,
            lease.WindowRegistryGeneration,
            selection.SelectionRevision,
            Array.AsReadOnly(selection.VisibleItemIds.ToArray()),
            new(x, y),
            new(x, y),
            control);
    }

    internal static ProductDesktopMarqueeSelectionSession? TryUpdate(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopMarqueeSelectionSession session,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(session);
        if (!MatchesFrozenAuthority(projection, transaction, session))
        {
            return null;
        }

        return session with
        {
            Current = new(
                Math.Clamp(x, session.ContainerBounds.Left,
                    session.ContainerBounds.Right),
                Math.Clamp(y, session.ContentTop,
                    session.ContainerBounds.Bottom)),
        };
    }

    internal static ProductDesktopMarqueeSelectionCommand? TryComplete(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopMarqueeSelectionSession session,
        int x,
        int y)
    {
        ProductDesktopMarqueeSelectionSession? updated = TryUpdate(
            projection,
            transaction,
            session,
            x,
            y);
        if (updated is null)
        {
            return null;
        }

        PixelRect marquee = GetBounds(updated);
        int horizontalInset = ProductDesktopHostSurfaceLayout.ToPixels(
            ItemHorizontalInsetDip,
            projection.EffectiveDpi / 96d);
        var selected = new List<string>(updated.VisibleItemIds.Count);
        for (int index = 0; index < updated.VisibleItemIds.Count; index++)
        {
            var itemBounds = new PixelRect(
                updated.ContainerBounds.Left + horizontalInset,
                updated.ContentTop + (index * updated.ItemHeight),
                Math.Max(0,
                    updated.ContainerBounds.Width - (horizontalInset * 2)),
                updated.ItemHeight);
            if (marquee.Intersect(itemBounds).HasArea)
            {
                selected.Add(updated.VisibleItemIds[index]);
            }
        }

        return new(
            updated.ContainerId,
            new(
                ProductDesktopSelectionAction.SelectItems,
                updated.ControlPressed
                    ? ProductDesktopSelectionModifiers.Control
                    : ProductDesktopSelectionModifiers.None,
                ItemIds: Array.AsReadOnly(selected.ToArray())),
            marquee);
    }

    internal static PixelRect GetBounds(
        ProductDesktopMarqueeSelectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int left = Math.Min(session.Start.X, session.Current.X);
        int top = Math.Min(session.Start.Y, session.Current.Y);
        int right = Math.Max(session.Start.X, session.Current.X);
        int bottom = Math.Max(session.Start.Y, session.Current.Y);
        return new(left, top, right - left, bottom - top);
    }

    private static bool MatchesFrozenAuthority(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopMarqueeSelectionSession session)
    {
        if (transaction?.IsExplicit != true
            || transaction.Admission.Lease is not { } lease
            || transaction.Selection is not { } selection
            || !string.Equals(projection.DisplayId, session.DisplayId,
                StringComparison.Ordinal)
            || lease.IntentId != session.LeaseIntentId
            || lease.WorkspaceRevision != session.WorkspaceRevision
            || lease.TopologyGeneration != session.TopologyGeneration
            || lease.WindowRegistryGeneration != session.WindowRegistryGeneration
            || selection.SelectionRevision != session.SelectionRevision
            || !string.Equals(selection.ContainerId, session.ContainerId,
                StringComparison.Ordinal)
            || !selection.VisibleItemIds.SequenceEqual(
                session.VisibleItemIds,
                StringComparer.Ordinal))
        {
            return false;
        }

        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                session.ContainerId,
                StringComparison.Ordinal));
        return container is not null
            && !container.IsCollapsed
            && container.ItemIds.SequenceEqual(
                session.VisibleItemIds,
                StringComparer.Ordinal)
            && ProductDesktopHostSurfaceLayout.GetContainerBounds(
                projection,
                container) == session.ContainerBounds;
    }
}
