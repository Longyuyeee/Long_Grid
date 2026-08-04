using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public sealed class ProductConfigurationSaveCoordinator
{
    private readonly object gate = new();
    private readonly ProductConfigurationStore store;
    private TaskCompletionSource idle = CreateCompletedSource();
    private PendingSave? pending;
    private bool accepting = true;
    private bool workerRunning;

    public ProductConfigurationSaveCoordinator(ProductConfigurationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    public Task EnqueueAsync(
        ProductConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (gate)
        {
            ThrowIfComplete();
        }

        ProductConfigurationDocument captured = Snapshot(document);
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
                await store.SaveAsync(current.Document, CancellationToken.None)
                    .ConfigureAwait(false);
                current.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                current.Completion.TrySetException(exception);
            }
        }
    }

    private static ProductConfigurationDocument Snapshot(
        ProductConfigurationDocument document) =>
        ProductConfigurationJson.Deserialize(
            ProductConfigurationJson.SerializeToUtf8Bytes(document));

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
                "The product configuration save coordinator is complete.");
        }
    }

    private sealed class PendingSave(ProductConfigurationDocument document)
    {
        public ProductConfigurationDocument Document { get; set; } = document;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
