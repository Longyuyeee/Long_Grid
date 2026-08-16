using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostSurfaceLayoutTests
{
    [Fact]
    public void ContinuedCreateEntryUsesFirstNonOverlappingFiniteSlot()
    {
        ProductDesktopHostDisplayProjection display = Display(
            [Container("container-1", 340, 16, 160, 80)]);

        PixelRect button = Assert.IsType<PixelRect>(
            ProductDesktopHostSurfaceLayout
                .GetContinuedCreateButtonBounds(display));
        PixelRect occupied = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            display,
            display.Containers[0]);

        Assert.Equal(new PixelRect(188, 16, 144, 40), button);
        Assert.False(button.Intersect(occupied).HasArea);
    }

    [Fact]
    public void ContinuedCreateEntryFailsClosedWhenNoSlotIsFree()
    {
        ProductDesktopHostDisplayProjection display = Display(
            [Container("container-1", 0, 0, 500, 300)]);

        Assert.Null(ProductDesktopHostSurfaceLayout
            .GetContinuedCreateButtonBounds(display));
    }

    private static ProductDesktopHostDisplayProjection Display(
        IReadOnlyList<ProductDesktopHostReadOnlyProjection> containers) =>
        ProductDesktopHostDisplayProjection.Create(
            "display-primary",
            new PixelRect(0, 0, 500, 300),
            96,
            containers,
            isPrimary: true,
            workspaceIsEmpty: false);

    private static ProductDesktopHostReadOnlyProjection Container(
        string id,
        double x,
        double y,
        double width,
        double height) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            "方格",
            Array.Empty<string>(),
            "#2457D6",
            0.82,
            false,
            x,
            y,
            width,
            height);
}
