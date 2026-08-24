using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopItemViewportTests
{
    [Fact]
    public void FiveHundredItemViewportMovesByTwelveAndClampsToLastPage()
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

        Assert.Equal(12, firstMove);
        Assert.Equal(488, lastPage);
        Assert.Equal(0, noOverflow);
        Assert.Equal(
            476,
            ProductDesktopItemViewportPolicy.Move(
                lastPage,
                500,
                wheelDelta: 120));
    }
}
