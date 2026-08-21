using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopContainerLayoutInputPhase
{
    Begin,
    Update,
    Complete,
    Cancel,
}

public enum ProductDesktopContainerLayoutCancellationReason
{
    None,
    CaptureLost,
    CancelMode,
    EscapePressed,
    HostInvalidated,
}

public sealed record ProductDesktopContainerLayoutRequest(
    ProductDesktopContainerLayoutInputPhase Phase,
    ProductWorkspaceContainerLayoutGestureKind Kind,
    string ContainerId,
    string DisplayId,
    long ExpectedWorkspaceRevision,
    long ExpectedTopologyGeneration,
    double CumulativeDeltaXDip,
    double CumulativeDeltaYDip,
    bool SnapEnabled,
    bool ShiftPressed,
    ProductDesktopContainerLayoutCancellationReason CancellationReason);

internal sealed record ProductDesktopContainerLayoutSurfaceInput(
    ProductDesktopContainerLayoutInputPhase Phase,
    ProductWorkspaceContainerLayoutGestureKind Kind,
    string ContainerId,
    double CumulativeDeltaXDip,
    double CumulativeDeltaYDip,
    bool SnapEnabled,
    bool ShiftPressed,
    ProductDesktopContainerLayoutCancellationReason CancellationReason);

public enum ProductDesktopContainerLayoutHitStatus
{
    Hit,
    OutsideSurface,
    NoTarget,
    AmbiguousTarget,
    Locked,
}

public sealed record ProductDesktopContainerLayoutHitResult(
    ProductDesktopContainerLayoutHitStatus Status,
    string? ContainerId,
    ProductWorkspaceContainerLayoutGestureKind? Kind)
{
    public bool IsHit =>
        Status == ProductDesktopContainerLayoutHitStatus.Hit
        && !string.IsNullOrWhiteSpace(ContainerId)
        && Kind is not null;
}

public static class ProductDesktopContainerLayoutHitTestAdapter
{
    public const double ResizeBorderDip = 8;

    public static ProductDesktopContainerLayoutHitResult HitTest(
        ProductDesktopHostDisplayProjection display,
        int clientX,
        int clientY)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (clientX < 0
            || clientY < 0
            || clientX >= display.WorkArea.Width
            || clientY >= display.WorkArea.Height)
        {
            return Miss(ProductDesktopContainerLayoutHitStatus.OutsideSurface);
        }

        var matches = new List<(ProductDesktopHostReadOnlyProjection Container,
            PixelRect Bounds)>();
        foreach (ProductDesktopHostReadOnlyProjection candidate
            in display.Containers)
        {
            PixelRect candidateBounds =
                ProductDesktopHostSurfaceLayout.GetContainerBounds(
                display,
                candidate);
            if (clientX >= candidateBounds.Left
                && clientX < candidateBounds.Right
                && clientY >= candidateBounds.Top
                && clientY < candidateBounds.Bottom)
            {
                matches.Add((candidate, candidateBounds));
            }
        }

        if (matches.Count == 0)
        {
            return Miss(ProductDesktopContainerLayoutHitStatus.NoTarget);
        }
        if (matches.Count != 1)
        {
            return Miss(ProductDesktopContainerLayoutHitStatus.AmbiguousTarget);
        }

        (ProductDesktopHostReadOnlyProjection container, PixelRect bounds) =
            matches[0];
        if (container.IsLocked)
        {
            return new(
                ProductDesktopContainerLayoutHitStatus.Locked,
                container.ContainerId,
                null);
        }

        double scale = display.EffectiveDpi / 96d;
        int border = Math.Max(
            4,
            ProductDesktopHostSurfaceLayout.ToPixels(ResizeBorderDip, scale));
        bool left = clientX < bounds.Left + border;
        bool right = clientX >= bounds.Right - border;
        bool top = clientY < bounds.Top + border;
        bool bottom = clientY >= bounds.Bottom - border;
        ProductWorkspaceContainerLayoutGestureKind? kind = (left, right, top, bottom)
            switch
        {
            (true, false, true, false) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft,
            (false, true, true, false) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight,
            (true, false, false, true) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft,
            (false, true, false, true) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight,
            (true, false, false, false) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeLeft,
            (false, true, false, false) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeRight,
            (false, false, true, false) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeTop,
            (false, false, false, true) =>
                ProductWorkspaceContainerLayoutGestureKind.ResizeBottom,
            _ => null,
        };
        if (kind is null)
        {
            int headerHeight = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.HeaderHeightDip,
                scale);
            if (clientY < bounds.Top + Math.Min(headerHeight, bounds.Height))
            {
                kind = ProductWorkspaceContainerLayoutGestureKind.Move;
            }
        }

        return kind is null
            ? Miss(ProductDesktopContainerLayoutHitStatus.NoTarget)
            : new(
                ProductDesktopContainerLayoutHitStatus.Hit,
                container.ContainerId,
                kind);
    }

    private static ProductDesktopContainerLayoutHitResult Miss(
        ProductDesktopContainerLayoutHitStatus status) => new(status, null, null);
}
