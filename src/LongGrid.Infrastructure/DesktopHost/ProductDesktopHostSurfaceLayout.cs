using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal static class ProductDesktopHostSurfaceLayout
{
    internal const int HeaderHeightDip = 54;
    internal const int ItemHeightDip = 28;
    internal const int EmptyCardWidthDip = 360;
    internal const int EmptyCardHeightDip = 184;
    internal const int EmptyCreateButtonWidthDip = 248;
    internal const int EmptyCreateButtonHeightDip = 48;

    internal static PixelRect GetEmptyCardBounds(
        ProductDesktopHostDisplayProjection display)
    {
        ArgumentNullException.ThrowIfNull(display);
        double scale = display.EffectiveDpi / 96d;
        int width = Math.Min(
            ToPixels(EmptyCardWidthDip, scale),
            display.WorkArea.Width);
        int height = Math.Min(
            ToPixels(EmptyCardHeightDip, scale),
            display.WorkArea.Height);
        return new(
            Math.Max(0, (display.WorkArea.Width - width) / 2),
            Math.Max(0, (display.WorkArea.Height - height) / 2),
            width,
            height);
    }

    internal static PixelRect GetEmptyCreateButtonBounds(
        ProductDesktopHostDisplayProjection display)
    {
        PixelRect card = GetEmptyCardBounds(display);
        double scale = display.EffectiveDpi / 96d;
        int width = Math.Min(
            ToPixels(EmptyCreateButtonWidthDip, scale),
            card.Width);
        int height = Math.Min(
            ToPixels(EmptyCreateButtonHeightDip, scale),
            card.Height);
        return new(
            card.Left + ((card.Width - width) / 2),
            card.Bottom - height - ToPixels(20, scale),
            width,
            height);
    }

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
