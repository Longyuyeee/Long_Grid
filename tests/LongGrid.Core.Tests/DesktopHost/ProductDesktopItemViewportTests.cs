using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopItemViewportTests
{
    [Fact]
    public void FiveHundredItemViewportWheelMovesContinuouslyAndClampsToLastPage()
    {
        int firstMove = ProductDesktopItemViewportPolicy.Move(
            0,
            500,
            wheelDelta: -120);
        int lastPage = ProductDesktopItemViewportPolicy.ClampStart(
            500,
            500);
        int noOverflow = ProductDesktopItemViewportPolicy.Move(
            0,
            12,
            wheelDelta: -120);

        Assert.Equal(1, firstMove);
        Assert.Equal(488, lastPage);
        Assert.Equal(0, noOverflow);
        Assert.Equal(
            487,
            ProductDesktopItemViewportPolicy.Move(
                lastPage,
                500,
                wheelDelta: 120));
    }

    [Fact]
    public void CompactViewportShowsEighteenAndKeyboardMovesByOnePage()
    {
        Assert.Equal(
            482,
            ProductDesktopItemViewportPolicy.ClampStart(
                500,
                500,
                ProductContainerContentDensity.Compact));
        Assert.Equal(
            18,
            ProductDesktopItemViewportPolicy.Move(
                0,
                500,
                wheelDelta: -120,
                density: ProductContainerContentDensity.Compact,
                pageNavigation: true));
    }
}
