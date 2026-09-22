using LongGrid.App;

namespace LongGrid.Core.Tests.Runtime;

public sealed class ProductDesktopCreateFallbackTests
{
    [Theory]
    [InlineData(1, "测试盒子")]
    [InlineData(2, null)]
    [InlineData(0, null)]
    public void OnlyExplicitConfirmationReturnsName(int response, string? expected)
    {
        int calls = 0;
        string? actual = ProductDesktopCreateFallback.Confirm("测试盒子", message =>
        {
            calls++;
            Assert.Contains("测试盒子", message);
            Assert.Contains("取消", message);
            Assert.Contains("原始文件不会移动或删除", message);
            return response;
        });
        Assert.Equal(expected, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void InvalidNameDoesNotOpenDialog()
    {
        Assert.Null(ProductDesktopCreateFallback.Confirm(" ", _ => throw new InvalidOperationException()));
    }
}
