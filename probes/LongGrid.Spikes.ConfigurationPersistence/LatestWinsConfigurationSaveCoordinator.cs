namespace LongGrid.Spikes.ConfigurationPersistence;

public sealed class LatestWinsConfigurationSaveCoordinator<T>
    where T : class
{
    private readonly object gate = new();
    private readonly Func<T, CancellationToken, Task> saveAsync;
    private PendingSave? pending;
    private bool workerRunning;

    public LatestWinsConfigurationSaveCoordinator(
        Func<T, CancellationToken, Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        this.saveAsync = saveAsync;
    }

    public Task EnqueueAsync(
        T document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Task completion;

        lock (gate)
        {
            if (pending is null)
            {
                pending = new(document);
            }
            else
            {
                pending.Document = document;
            }

            completion = pending.Completion.Task;

            if (!workerRunning)
            {
                workerRunning = true;
                _ = ProcessAsync();
            }
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

    private sealed class PendingSave(T document)
    {
        public T Document { get; set; } = document;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
