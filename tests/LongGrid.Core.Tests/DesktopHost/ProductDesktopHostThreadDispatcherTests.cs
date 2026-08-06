using System.Collections.Concurrent;
using System.ComponentModel;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostThreadDispatcherTests
{
    [Fact]
    public void InvokeRunsInlineOnTargetThread()
    {
        var context = new QueuedSynchronizationContext();
        var dispatcher = new SynchronizationContextProductDesktopHostThreadDispatcher(
            context,
            targetThreadId: 42,
            currentThreadId: () => 42);
        int calls = 0;

        ProductDesktopHostDispatchResult result = dispatcher.Invoke(
            () =>
            {
                calls++;
                return true;
            },
            TimeSpan.FromMilliseconds(20));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls);
        Assert.Equal(0, context.Count);
    }

    [Fact]
    public async Task InvokeQueuesAndCompletesOnTargetContext()
    {
        var context = new QueuedSynchronizationContext();
        var dispatcher = Dispatcher(context);
        uint observedThread = 0;

        Task<ProductDesktopHostDispatchResult> pending = Task.Run(() =>
            dispatcher.Invoke(
                () =>
                {
                    observedThread = 42;
                    return true;
                },
                TimeSpan.FromSeconds(1)));
        await context.WaitForPostAsync();
        context.RunOne();
        ProductDesktopHostDispatchResult result = await pending;

        Assert.True(result.IsSuccess);
        Assert.Equal((uint)42, observedThread);
    }

    [Fact]
    public async Task QueueTimeoutCancelsPendingWorkWithoutLateExecution()
    {
        var context = new QueuedSynchronizationContext();
        var dispatcher = Dispatcher(context);
        int calls = 0;

        ProductDesktopHostDispatchResult result = await Task.Run(() =>
            dispatcher.Invoke(
                () =>
                {
                    calls++;
                    return true;
                },
                TimeSpan.FromMilliseconds(20)));
        await context.WaitForPostAsync();
        context.RunOne();

        Assert.Equal(
            ProductDesktopHostDispatchStatus.QueueTimedOut,
            result.Status);
        Assert.False(result.OperationSucceeded);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task StartedOperationIsAwaitedInsteadOfReportedAsTimeout()
    {
        var context = new QueuedSynchronizationContext();
        var dispatcher = Dispatcher(context);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ProductDesktopHostDispatchResult> pending = Task.Run(() =>
            dispatcher.Invoke(
                () =>
                {
                    entered.SetResult();
                    release.Task.GetAwaiter().GetResult();
                    return true;
                },
                TimeSpan.FromMilliseconds(20)));
        await context.WaitForPostAsync();
        Task target = Task.Run(context.RunOne);
        await entered.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(40));
        Assert.False(pending.IsCompleted);
        release.SetResult();

        Assert.True((await pending).IsSuccess);
        await target;
    }

    [Fact]
    public void QueueRejectionAndOperationExceptionAreFinite()
    {
        var rejecting = new SynchronizationContextProductDesktopHostThreadDispatcher(
            new RejectingSynchronizationContext(),
            targetThreadId: 42,
            currentThreadId: () => 7);
        ProductDesktopHostDispatchResult rejected = rejecting.Invoke(
            () => true,
            TimeSpan.FromMilliseconds(20));
        Assert.Equal(
            ProductDesktopHostDispatchStatus.QueueRejected,
            rejected.Status);

        var inline = new SynchronizationContextProductDesktopHostThreadDispatcher(
            new SynchronizationContext(),
            targetThreadId: 42,
            currentThreadId: () => 42);
        ProductDesktopHostDispatchResult faulted = inline.Invoke(
            () => throw new Win32Exception(),
            TimeSpan.FromMilliseconds(20));
        Assert.Equal(ProductDesktopHostDispatchStatus.Executed, faulted.Status);
        Assert.False(faulted.OperationSucceeded);
    }

    [Fact]
    public void PostedCallbackRefusesToRunOnTheWrongNativeThread()
    {
        var dispatcher = new SynchronizationContextProductDesktopHostThreadDispatcher(
            new InlinePostSynchronizationContext(),
            targetThreadId: 42,
            currentThreadId: () => 7);
        int calls = 0;

        ProductDesktopHostDispatchResult result = dispatcher.Invoke(
            () =>
            {
                calls++;
                return true;
            },
            TimeSpan.FromMilliseconds(20));

        Assert.Equal(ProductDesktopHostDispatchStatus.Executed, result.Status);
        Assert.False(result.OperationSucceeded);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void InvokeRejectsUnsafeTimeout(int milliseconds)
    {
        var dispatcher = new SynchronizationContextProductDesktopHostThreadDispatcher(
            new SynchronizationContext(),
            targetThreadId: 42,
            currentThreadId: () => 42);

        Assert.Throws<ArgumentOutOfRangeException>(() => dispatcher.Invoke(
            () => true,
            TimeSpan.FromMilliseconds(milliseconds)));
    }

    private static SynchronizationContextProductDesktopHostThreadDispatcher
        Dispatcher(QueuedSynchronizationContext context) =>
        new(
            context,
            targetThreadId: 42,
            currentThreadId: () => context.IsRunning ? 42u : 7u);

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)>
            queue = new();
        private readonly TaskCompletionSource posted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool IsRunning { get; private set; }

        internal int Count => queue.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            queue.Enqueue((d, state));
            posted.TrySetResult();
        }

        internal Task WaitForPostAsync() => posted.Task;

        internal void RunOne()
        {
            Assert.True(queue.TryDequeue(out var item));
            IsRunning = true;
            try
            {
                item.Callback(item.State);
            }
            finally
            {
                IsRunning = false;
            }
        }
    }

    private sealed class InlinePostSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            d(state);
    }

    private sealed class RejectingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("The host is shutting down.");
    }
}
