using LongGrid.App;

namespace LongGrid.Core.Tests.Runtime;

public sealed class ProductDesktopPreviewContinuationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("已确认的名称")]
    public async Task CompletedPreviewNeverTouchesDestroyedWindow(string? result)
    {
        var completion = new TaskCompletionSource<string?>();
        completion.SetResult(result);
        await Task.Yield();

        Assert.False(ProductDesktopPreviewContinuation.TryRun(
            completion.Task, () => throw new InvalidOperationException("Destroyed window")));
        Assert.Equal(result, await completion.Task);
    }

    [Fact]
    public void PendingPreviewRunsOnceAndStopsAfterCancellation()
    {
        var completion = new TaskCompletionSource<string?>();
        int calls = 0;
        Assert.True(ProductDesktopPreviewContinuation.TryRun(completion.Task, () => calls++));
        completion.SetResult(null);
        Assert.False(ProductDesktopPreviewContinuation.TryRun(completion.Task, () => calls++));
        Assert.Equal(1, calls);
    }
}
