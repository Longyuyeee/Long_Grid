using System.Runtime.InteropServices;

namespace LongGrid.Infrastructure.DesktopHost;

internal enum ProductDesktopHostDispatchStatus
{
    Executed,
    QueueRejected,
    QueueTimedOut,
}

internal sealed record ProductDesktopHostDispatchResult(
    ProductDesktopHostDispatchStatus Status,
    bool OperationSucceeded)
{
    internal bool IsSuccess =>
        Status == ProductDesktopHostDispatchStatus.Executed
        && OperationSucceeded;
}

internal interface IProductDesktopHostThreadDispatcher
{
    uint TargetThreadId { get; }

    ProductDesktopHostDispatchResult Invoke(
        Func<bool> operation,
        TimeSpan queueTimeout);
}

internal sealed class SynchronizationContextProductDesktopHostThreadDispatcher
    : IProductDesktopHostThreadDispatcher
{
    private sealed class WorkItem(Func<bool> operation)
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Cancelled = 2;
        private const int Completed = 3;
        private readonly TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int state = Pending;

        internal Task<bool> Completion => completion.Task;

        internal bool TryCancel() =>
            Interlocked.CompareExchange(
                ref state,
                Cancelled,
                Pending) == Pending;

        internal void Run()
        {
            if (Interlocked.CompareExchange(
                ref state,
                Running,
                Pending) != Pending)
            {
                completion.TrySetResult(false);
                return;
            }

            bool result;
            try
            {
                result = operation();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or OverflowException
                    or ExternalException)
            {
                result = false;
            }

            Volatile.Write(ref state, Completed);
            completion.TrySetResult(result);
        }
    }

    private static readonly TimeSpan MaximumQueueTimeout =
        TimeSpan.FromSeconds(5);
    private readonly SynchronizationContext context;
    private readonly Func<uint> currentThreadId;

    internal SynchronizationContextProductDesktopHostThreadDispatcher(
        SynchronizationContext context,
        uint targetThreadId,
        Func<uint>? currentThreadId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfZero(targetThreadId);
        this.context = context;
        TargetThreadId = targetThreadId;
        this.currentThreadId = currentThreadId ?? NativeMethods.GetCurrentThreadId;
    }

    public uint TargetThreadId { get; }

    internal static SynchronizationContextProductDesktopHostThreadDispatcher
        CaptureCurrent(SynchronizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DesktopHost thread dispatch requires Windows.");
        }

        uint threadId = NativeMethods.GetCurrentThreadId();
        if (threadId == 0)
        {
            throw new InvalidOperationException(
                "The current DesktopHost thread could not be identified.");
        }

        return new(context, threadId);
    }

    public ProductDesktopHostDispatchResult Invoke(
        Func<bool> operation,
        TimeSpan queueTimeout)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (queueTimeout < TimeSpan.FromMilliseconds(1)
            || queueTimeout > MaximumQueueTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queueTimeout),
                "The queue timeout must be between 1 millisecond and 5 seconds.");
        }

        if (currentThreadId() == TargetThreadId)
        {
            return new(
                ProductDesktopHostDispatchStatus.Executed,
                TryRun(operation));
        }

        var work = new WorkItem(() =>
            currentThreadId() == TargetThreadId
            && operation());
        try
        {
            context.Post(static state => ((WorkItem)state!).Run(), work);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ObjectDisposedException
                or ExternalException)
        {
            return new(
                ProductDesktopHostDispatchStatus.QueueRejected,
                OperationSucceeded: false);
        }

        if (work.Completion.Wait(queueTimeout))
        {
            return new(
                ProductDesktopHostDispatchStatus.Executed,
                work.Completion.GetAwaiter().GetResult());
        }

        if (work.TryCancel())
        {
            return new(
                ProductDesktopHostDispatchStatus.QueueTimedOut,
                OperationSucceeded: false);
        }

        return new(
            ProductDesktopHostDispatchStatus.Executed,
            work.Completion.GetAwaiter().GetResult());
    }

    private static bool TryRun(Func<bool> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or OverflowException
                or ExternalException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();
    }
}
