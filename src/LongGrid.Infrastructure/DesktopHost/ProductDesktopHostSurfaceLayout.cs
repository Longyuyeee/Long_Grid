using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal static class ProductDesktopHostSurfaceLayout
{
    internal const int HeaderHeightDip = 54;
    internal const int ItemHeightDip = 28;

    internal static PixelRect GetContainerBounds(
        ProductDesktopHostDisplayProjection display,
        ProductDesktopHostReadOnlyProjection container)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(container);
        double scale = display.EffectiveDpi / 96d;
        int workWidth = display.WorkArea.Width;
        int workHeight = display.WorkArea.Height;
        int width = Math.Clamp(
            ToPixels(container.WidthDip, scale),
            Math.Min(160, workWidth),
            workWidth);
        double requestedHeight = container.IsCollapsed
            ? HeaderHeightDip
            : Math.Max(
                container.HeightDip,
                HeaderHeightDip
                    + (Math.Max(1, container.ItemNames.Count) * ItemHeightDip)
                    + 18);
        int height = Math.Clamp(
            ToPixels(requestedHeight, scale),
            Math.Min(ToPixels(HeaderHeightDip, scale), workHeight),
            workHeight);
        int left = Math.Clamp(
            ToPixels(container.XDip, scale),
            0,
            Math.Max(0, workWidth - width));
        int top = Math.Clamp(
            ToPixels(container.YDip, scale),
            0,
            Math.Max(0, workHeight - height));
        return new(left, top, width, height);
    }

    internal static int ToPixels(double value, double scale) =>
        checked((int)Math.Round(value * scale));
}
