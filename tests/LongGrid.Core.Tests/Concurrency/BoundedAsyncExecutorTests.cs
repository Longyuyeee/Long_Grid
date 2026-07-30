using LongGrid.Core.Concurrency;

namespace LongGrid.Core.Tests.Concurrency;

public sealed class BoundedAsyncExecutorTests
{
    [Fact]
    public async Task RunAsyncDoesNotExceedConfiguredConcurrency()
    {
        using var executor = new BoundedAsyncExecutor(2);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => executor.RunAsync(
                async cancellationToken =>
                {
                    await Task.Delay(20, cancellationToken);
                    return true;
                }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(2, executor.MaxObservedConcurrency);
    }

    [Fact]
    public async Task RunAsyncCanceledQueuedWorkNeverStarts()
    {
        using var executor = new BoundedAsyncExecutor(1);
        var firstOperationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstOperation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = executor.RunAsync(
            async _ =>
            {
                firstOperationEntered.SetResult();
                await releaseFirstOperation.Task;
                return true;
            });
        await firstOperationEntered.Task;

        using var cancellation = new CancellationTokenSource();
        var queuedOperationStarted = false;
        var queuedTask = executor.RunAsync(
            _ =>
            {
                queuedOperationStarted = true;
                return Task.FromResult(true);
            },
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);
        Assert.False(queuedOperationStarted);

        releaseFirstOperation.SetResult();
        await firstTask;
    }

    [Fact]
    public async Task RunAsyncReleasesSlotAfterOperationFailure()
    {
        using var executor = new BoundedAsyncExecutor(1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.RunAsync<bool>(
                _ => throw new InvalidOperationException("Expected test failure.")));

        var result = await executor.RunAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }
}
