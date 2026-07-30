namespace LongGrid.Core.Concurrency;

/// <summary>
/// Runs asynchronous operations with a fixed concurrency ceiling.
/// Cancellation is cooperative: it prevents queued work from starting, but it
/// cannot forcibly interrupt an operation that has already entered native code.
/// </summary>
public sealed class BoundedAsyncExecutor : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private int _activeOperations;
    private int _maxObservedConcurrency;
    private bool _disposed;

    public BoundedAsyncExecutor(int maximumConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        MaximumConcurrency = maximumConcurrency;
        _slots = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public int MaximumConcurrency { get; }

    public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var enteredOperation = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            enteredOperation = true;
            var activeOperations = Interlocked.Increment(ref _activeOperations);
            RecordMaximum(activeOperations);
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (enteredOperation)
            {
                Interlocked.Decrement(ref _activeOperations);
            }

            _slots.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _slots.Dispose();
        _disposed = true;
    }

    private void RecordMaximum(int candidate)
    {
        var observed = Volatile.Read(ref _maxObservedConcurrency);
        while (candidate > observed)
        {
            var prior = Interlocked.CompareExchange(
                ref _maxObservedConcurrency,
                candidate,
                observed);
            if (prior == observed)
            {
                return;
            }

            observed = prior;
        }
    }
}
