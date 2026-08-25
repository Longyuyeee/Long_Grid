using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductDesktopWorkspaceCreatePreviewPlacementTests
{
    [Fact]
    public void CandidateDipBoundsMapIntoAbsoluteDisplayPixels()
    {
        PixelRect? bounds =
            ProductDesktopWorkspaceCreatePreviewPlacement.ResolveWindowBounds(
                Placement(40, 60, 360, 240),
                new(-1920, 80, 1920, 1040),
                144);

        Assert.Equal(new PixelRect(-1860, 170, 540, 360), bounds);
    }

    [Fact]
    public void OversizedAndOffscreenCandidateIsClampedToWorkArea()
    {
        PixelRect? bounds =
            ProductDesktopWorkspaceCreatePreviewPlacement.ResolveWindowBounds(
                Placement(900, 700, 900, 900),
                new(100, 200, 800, 600),
                96);

        Assert.Equal(new PixelRect(100, 200, 800, 600), bounds);
    }

    [Theory]
    [InlineData(0, 240, 96)]
    [InlineData(360, double.NaN, 96)]
    [InlineData(360, 240, 47)]
    [InlineData(360, 240, 769)]
    public void InvalidCandidateOrDpiFailsClosed(
        double width,
        double height,
        uint dpi)
    {
        Assert.Null(
            ProductDesktopWorkspaceCreatePreviewPlacement.ResolveWindowBounds(
                Placement(0, 0, width, height),
                new(0, 0, 1920, 1080),
                dpi));
    }

    private static ProductContainerPlacementState Placement(
        double x,
        double y,
        double width,
        double height) => new()
        {
            DisplayKey = "display-primary",
            XDip = x,
            YDip = y,
            WidthDip = width,
            HeightDip = height,
        };
}
