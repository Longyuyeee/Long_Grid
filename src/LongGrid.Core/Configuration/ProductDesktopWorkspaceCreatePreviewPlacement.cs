using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public static class ProductDesktopWorkspaceCreatePreviewPlacement
{
    public static PixelRect? ResolveWindowBounds(
        ProductContainerPlacementState? placement,
        PixelRect workArea,
        uint effectiveDpi)
    {
        if (placement is null
            || !workArea.HasArea
            || effectiveDpi is < 48 or > 768
            || !IsFinitePositive(placement.WidthDip)
            || !IsFinitePositive(placement.HeightDip)
            || !double.IsFinite(placement.XDip)
            || !double.IsFinite(placement.YDip))
        {
            return null;
        }

        double scale = effectiveDpi / 96d;
        int width = Math.Clamp(
            ToPixels(placement.WidthDip, scale),
            Math.Min(280, workArea.Width),
            workArea.Width);
        int height = Math.Clamp(
            ToPixels(placement.HeightDip, scale),
            Math.Min(220, workArea.Height),
            workArea.Height);
        int relativeLeft = Math.Clamp(
            ToPixels(placement.XDip, scale),
            0,
            Math.Max(0, workArea.Width - width));
        int relativeTop = Math.Clamp(
            ToPixels(placement.YDip, scale),
            0,
            Math.Max(0, workArea.Height - height));
        return new(
            checked(workArea.Left + relativeLeft),
            checked(workArea.Top + relativeTop),
            width,
            height);
    }

    private static bool IsFinitePositive(double value) =>
        double.IsFinite(value) && value > 0;

    private static int ToPixels(double value, double scale) =>
        checked((int)Math.Round(value * scale));
}
