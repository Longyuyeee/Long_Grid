using LongGrid.App;

namespace LongGrid.Core.Tests.Runtime;

public sealed class ProductDesktopPreviewContinuationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("正常确认")]
    public async Task CleanupRunsOnceWithoutChangingResult(string? result)
    {
        int calls = 0;
        string? actual = await ProductDesktopPreviewContinuation.RunWithCleanupAsync(
            () => Task.FromResult(result), () => calls++);
        Assert.Equal(result, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PresentationFailureCleansUpBeforeFallbackReceivesException()
    {
        var ready = new TaskCompletionSource<string?>();
        int calls = 0;
        Task<string?> operation = ProductDesktopPreviewContinuation.RunWithCleanupAsync(
            () => ready.Task, () => calls++);
        Assert.Equal(0, calls);
        var failure = new InvalidOperationException("Presentation failed");
        ready.SetException(failure);

        Exception actual = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Same(failure, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancelledOperationAlsoCleansUp()
    {
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProductDesktopPreviewContinuation.RunWithCleanupAsync(
                () => Task.FromCanceled<string?>(new CancellationToken(canceled: true)),
                () => calls++));
        Assert.Equal(1, calls);
    }

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
