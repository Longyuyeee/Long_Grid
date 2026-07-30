namespace LongGrid.Spikes.ConfigurationPersistence;

public sealed class LatestWinsConfigurationSaveCoordinator<T>
    where T : class
{
    private readonly object gate = new();
    private readonly Func<T, CancellationToken, Task> saveAsync;
    private readonly Func<T, T> snapshot;
    private TaskCompletionSource idle = CreateCompletedSource();
    private PendingSave? pending;
    private bool accepting = true;
    private bool workerRunning;

    public LatestWinsConfigurationSaveCoordinator(
        Func<T, CancellationToken, Task> saveAsync,
        Func<T, T> snapshot)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        ArgumentNullException.ThrowIfNull(snapshot);
        this.saveAsync = saveAsync;
        this.snapshot = snapshot;
    }

    public Task EnqueueAsync(
        T document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (gate)
        {
            ThrowIfComplete();
        }

        T captured = snapshot(document)
            ?? throw new InvalidOperationException(
                "The configuration snapshot delegate returned null.");
        Task completion;

        lock (gate)
        {
            ThrowIfComplete();

            if (pending is null)
            {
                pending = new(captured);
            }
            else
            {
                pending.Document = captured;
            }

            completion = pending.Completion.Task;

            if (!workerRunning)
            {
                idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
                workerRunning = true;
                _ = ProcessAsync();
            }
        }

        return cancellationToken.CanBeCanceled
            ? completion.WaitAsync(cancellationToken)
            : completion;
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Task completion;

        lock (gate)
        {
            accepting = false;
            completion = idle.Task;
        }

        return cancellationToken.CanBeCanceled
            ? completion.WaitAsync(cancellationToken)
            : completion;
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            PendingSave current;

            lock (gate)
            {
                if (pending is null)
                {
                    workerRunning = false;
                    idle.TrySetResult();
                    return;
                }

                current = pending;
                pending = null;
            }

            try
            {
                await saveAsync(current.Document, CancellationToken.None).ConfigureAwait(false);
                current.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                current.Completion.TrySetException(exception);
            }
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }

    private void ThrowIfComplete()
    {
        if (!accepting)
        {
            throw new InvalidOperationException(
                "The configuration save coordinator is complete.");
        }
    }

    private sealed class PendingSave(T document)
    {
        public T Document { get; set; } = document;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
