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

internal sealed record ProductDesktopContainerLayoutKeyboardCommand(
    string ContainerId,
    ProductWorkspaceContainerLayoutGestureKind Kind,
    double DeltaXDip,
    double DeltaYDip,
    bool ShiftPressed);

internal sealed record ProductDesktopContainerLayoutKeyboardDecision(
    bool Handled,
    bool? TitleFocused,
    ProductWorkspaceContainerLayoutGestureKind? Kind,
    double DeltaXDip,
    double DeltaYDip,
    bool ShiftPressed)
{
    internal bool HasLayoutCommand =>
        Handled && TitleFocused is null && Kind is not null;
}

internal static class ProductDesktopContainerLayoutKeyboardAdapter
{
    internal const double FineStepDip = 1;
    internal const double LargeStepDip = 8;

    internal static ProductDesktopContainerLayoutKeyboardDecision Map(
        bool titleFocused,
        int virtualKey,
        bool alt,
        bool control,
        bool shift)
    {
        if (virtualKey == 0x09 && !alt && !control)
        {
            return new(
                Handled: true,
                TitleFocused: !titleFocused,
                Kind: null,
                DeltaXDip: 0,
                DeltaYDip: 0,
                ShiftPressed: shift);
        }

        if (!titleFocused || virtualKey is < 0x25 or > 0x28)
        {
            return Ignored;
        }

        double step = shift ? LargeStepDip : FineStepDip;
        if (!alt)
        {
            (double x, double y) = virtualKey switch
            {
                0x25 => (-step, 0d),
                0x26 => (0d, -step),
                0x27 => (step, 0d),
                0x28 => (0d, step),
                _ => (0d, 0d),
            };
            return new(
                Handled: true,
                TitleFocused: null,
                ProductWorkspaceContainerLayoutGestureKind.Move,
                x,
                y,
                shift);
        }

        ProductWorkspaceContainerLayoutGestureKind kind = virtualKey is
            0x25 or 0x27
                ? ProductWorkspaceContainerLayoutGestureKind.ResizeRight
                : ProductWorkspaceContainerLayoutGestureKind.ResizeBottom;
        (double resizeX, double resizeY) = virtualKey switch
        {
            0x25 => (-step, 0d),
            0x26 => (0d, -step),
            0x27 => (step, 0d),
            0x28 => (0d, step),
            _ => (0d, 0d),
        };
        return new(
            Handled: true,
            TitleFocused: null,
            kind,
            resizeX,
            resizeY,
            shift);
    }

    private static ProductDesktopContainerLayoutKeyboardDecision Ignored =>
        new(
            Handled: false,
            TitleFocused: null,
            Kind: null,
            DeltaXDip: 0,
            DeltaYDip: 0,
            ShiftPressed: false);
}

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
