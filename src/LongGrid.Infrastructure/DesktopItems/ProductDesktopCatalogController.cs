using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.DesktopItems;

public enum ProductDesktopCatalogStatus
{
    Unavailable,
    Refreshing,
    Ready,
    Partial,
    Failed,
    Cancelled,
}

public sealed record ProductDesktopCatalogSnapshot(
    ProductDesktopCatalogStatus Status,
    long Generation,
    IReadOnlyList<DesktopCatalogEntry> Entries,
    IReadOnlyList<ProductDesktopCatalogSourceSnapshot> Sources)
{
    public static ProductDesktopCatalogSnapshot Initial { get; } = new(
        ProductDesktopCatalogStatus.Unavailable,
        0,
        Array.Empty<DesktopCatalogEntry>(),
        Array.Empty<ProductDesktopCatalogSourceSnapshot>());

    public bool IsAuthoritative => Status == ProductDesktopCatalogStatus.Ready;
}

public enum ProductDesktopCatalogRefreshStatus
{
    Published,
    Stale,
    Cancelled,
}

public sealed record ProductDesktopCatalogRefreshResult(
    ProductDesktopCatalogRefreshStatus Status,
    long Generation,
    ProductDesktopCatalogSnapshot Snapshot);

public sealed class ProductDesktopCatalogController : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IProductDesktopCatalogReader reader;
    private readonly CancellationTokenSource lifetime = new();
    private ProductDesktopCatalogSnapshot snapshot = ProductDesktopCatalogSnapshot.Initial;
    private TaskCompletionSource refreshesDrained = CompletedSource();
    private long generation;
    private int activeRefreshes;
    private bool disposed;

    public ProductDesktopCatalogController(IProductDesktopCatalogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
    }

    public event EventHandler<ProductDesktopCatalogSnapshot>? SnapshotChanged;

    public ProductDesktopCatalogSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public async Task<ProductDesktopCatalogRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        long refreshGeneration;
        ProductDesktopCatalogSnapshot refreshing;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            refreshGeneration = generation = checked(generation + 1);
            refreshing = snapshot = new(
                ProductDesktopCatalogStatus.Refreshing,
                refreshGeneration,
                Array.Empty<DesktopCatalogEntry>(),
                Array.Empty<ProductDesktopCatalogSourceSnapshot>());
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
            ProductDesktopCatalogReadResult readResult =
                await reader.ReadAsync(linked.Token).ConfigureAwait(false);
            ProductDesktopCatalogSnapshot completed = FromReadResult(
                refreshGeneration,
                readResult);
            lock (sync)
            {
                if (refreshGeneration != generation)
                {
                    return new(
                        ProductDesktopCatalogRefreshStatus.Stale,
                        refreshGeneration,
                        snapshot);
                }

                snapshot = completed;
            }

            Publish(completed);
            return new(
                ProductDesktopCatalogRefreshStatus.Published,
                refreshGeneration,
                completed);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || lifetime.IsCancellationRequested)
        {
            ProductDesktopCatalogSnapshot cancelled;
            lock (sync)
            {
                if (refreshGeneration != generation)
                {
                    return new(
                        ProductDesktopCatalogRefreshStatus.Stale,
                        refreshGeneration,
                        snapshot);
                }

                cancelled = snapshot = new(
                    ProductDesktopCatalogStatus.Cancelled,
                    refreshGeneration,
                    Array.Empty<DesktopCatalogEntry>(),
                    Array.Empty<ProductDesktopCatalogSourceSnapshot>());
            }

            Publish(cancelled);
            return new(
                ProductDesktopCatalogRefreshStatus.Cancelled,
                refreshGeneration,
                cancelled);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            ProductDesktopCatalogSnapshot failed;
            lock (sync)
            {
                if (refreshGeneration != generation)
                {
                    return new(
                        ProductDesktopCatalogRefreshStatus.Stale,
                        refreshGeneration,
                        snapshot);
                }

                failed = snapshot = new(
                    ProductDesktopCatalogStatus.Failed,
                    refreshGeneration,
                    Array.Empty<DesktopCatalogEntry>(),
                    Array.Empty<ProductDesktopCatalogSourceSnapshot>());
            }

            Publish(failed);
            return new(
                ProductDesktopCatalogRefreshStatus.Published,
                refreshGeneration,
                failed);
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

    private static ProductDesktopCatalogSnapshot FromReadResult(
        long generation,
        ProductDesktopCatalogReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ProductDesktopCatalogStatus status = result.Status switch
        {
            ProductDesktopCatalogReadStatus.Ready => ProductDesktopCatalogStatus.Ready,
            ProductDesktopCatalogReadStatus.Partial => ProductDesktopCatalogStatus.Partial,
            ProductDesktopCatalogReadStatus.Unavailable =>
                ProductDesktopCatalogStatus.Unavailable,
            ProductDesktopCatalogReadStatus.Failed => ProductDesktopCatalogStatus.Failed,
            _ => ProductDesktopCatalogStatus.Failed,
        };
        return new(
            status,
            generation,
            Array.AsReadOnly(result.Entries.ToArray()),
            Array.AsReadOnly(result.Sources.ToArray()));
    }

    private void Publish(ProductDesktopCatalogSnapshot value) =>
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
