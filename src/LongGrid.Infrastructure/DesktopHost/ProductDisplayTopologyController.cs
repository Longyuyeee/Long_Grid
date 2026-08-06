using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDisplayTopologyStatus
{
    Unavailable,
    Refreshing,
    Ready,
    Degraded,
    UnsupportedPlatform,
    Failed,
    Cancelled,
}

public sealed record ProductDisplayTopologySnapshot(
    ProductDisplayTopologyStatus Status,
    long Generation,
    IReadOnlyList<DisplayTopologyNode> Displays,
    int ActivePathCount,
    int StableIdentityCount,
    int BufferAttempts)
{
    public static ProductDisplayTopologySnapshot Initial { get; } = new(
        ProductDisplayTopologyStatus.Unavailable,
        0,
        Array.Empty<DisplayTopologyNode>(),
        0,
        0,
        0);

    public bool IsAuthoritative => Status == ProductDisplayTopologyStatus.Ready;
}

public enum ProductDisplayTopologyRefreshStatus
{
    Published,
    Stale,
    Cancelled,
}

public sealed record ProductDisplayTopologyRefreshResult(
    ProductDisplayTopologyRefreshStatus Status,
    long Generation,
    ProductDisplayTopologySnapshot Snapshot);

public sealed class ProductDisplayTopologyController : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IProductDisplayTopologyReader reader;
    private readonly CancellationTokenSource lifetime = new();
    private ProductDisplayTopologySnapshot snapshot =
        ProductDisplayTopologySnapshot.Initial;
    private TaskCompletionSource refreshesDrained = CompletedSource();
    private long generation;
    private int activeRefreshes;
    private bool disposed;

    public ProductDisplayTopologyController(IProductDisplayTopologyReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
    }

    public event EventHandler<ProductDisplayTopologySnapshot>? SnapshotChanged;

    public ProductDisplayTopologySnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public async Task<ProductDisplayTopologyRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        long refreshGeneration;
        ProductDisplayTopologySnapshot refreshing;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            refreshGeneration = generation = checked(generation + 1);
            refreshing = snapshot = new(
                ProductDisplayTopologyStatus.Refreshing,
                refreshGeneration,
                Array.Empty<DisplayTopologyNode>(),
                0,
                0,
                0);
            if (activeRefreshes++ == 0)
            {
                refreshesDrained = NewSource();
            }
        }

        Publish(refreshing);
        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetime.Token);
            ProductDisplayTopologyReadResult readResult =
                await reader.ReadAsync(linked.Token).ConfigureAwait(false);
            ProductDisplayTopologySnapshot completed = FromReadResult(
                refreshGeneration,
                readResult);
            lock (sync)
            {
                if (refreshGeneration != generation)
                {
                    return new(
                        ProductDisplayTopologyRefreshStatus.Stale,
                        refreshGeneration,
                        snapshot);
                }

                snapshot = completed;
            }

            Publish(completed);
            return new(
                ProductDisplayTopologyRefreshStatus.Published,
                refreshGeneration,
                completed);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || lifetime.IsCancellationRequested)
        {
            ProductDisplayTopologySnapshot cancelled;
            lock (sync)
            {
                if (refreshGeneration != generation)
                {
                    return new(
                        ProductDisplayTopologyRefreshStatus.Stale,
                        refreshGeneration,
                        snapshot);
                }

                cancelled = snapshot = new(
                    ProductDisplayTopologyStatus.Cancelled,
                    refreshGeneration,
                    Array.Empty<DisplayTopologyNode>(),
                    0,
                    0,
                    0);
            }

            Publish(cancelled);
            return new(
                ProductDisplayTopologyRefreshStatus.Cancelled,
                refreshGeneration,
                cancelled);
        }
        finally
        {
            lock (sync)
            {
                activeRefreshes--;
                if (activeRefreshes == 0)
                {
                    refreshesDrained.TrySetResult();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task drained;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            drained = refreshesDrained.Task;
        }

        await drained.ConfigureAwait(false);
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ProductDisplayTopologySnapshot FromReadResult(
        long generation,
        ProductDisplayTopologyReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ProductDisplayTopologyStatus status = result.Status switch
        {
            ProductDisplayTopologyReadStatus.Ready =>
                ProductDisplayTopologyStatus.Ready,
            ProductDisplayTopologyReadStatus.Degraded =>
                ProductDisplayTopologyStatus.Degraded,
            ProductDisplayTopologyReadStatus.Unavailable =>
                ProductDisplayTopologyStatus.Unavailable,
            ProductDisplayTopologyReadStatus.UnsupportedPlatform =>
                ProductDisplayTopologyStatus.UnsupportedPlatform,
            _ => ProductDisplayTopologyStatus.Failed,
        };
        return new(
            status,
            generation,
            Array.AsReadOnly(result.Displays.ToArray()),
            result.ActivePathCount,
            result.StableIdentityCount,
            result.BufferAttempts);
    }

    private void Publish(ProductDisplayTopologySnapshot value) =>
        SnapshotChanged?.Invoke(this, value);

    private static TaskCompletionSource CompletedSource()
    {
        TaskCompletionSource source = NewSource();
        source.SetResult();
        return source;
    }

    private static TaskCompletionSource NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
