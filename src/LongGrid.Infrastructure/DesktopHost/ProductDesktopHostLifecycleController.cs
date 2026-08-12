using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopHostLifecycleStatus
{
    DisabledBySafetyPolicy,
    AwaitingHost,
    Completed,
}

public sealed record ProductDesktopHostLifecycleSnapshot(
    ProductDesktopHostLifecycleStatus Status,
    long Generation,
    bool NativeHostConnected,
    int OwnedWindowCount)
{
    public bool FeatureEnabled =>
        Status == ProductDesktopHostLifecycleStatus.AwaitingHost;
}

public sealed class ProductDesktopHostLifecycleController : IAsyncDisposable
{
    private readonly object gate = new();
    private ProductDesktopHostLifecycleSnapshot snapshot;
    private bool disposed;

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        snapshot = new(
            featureDecision.IsEnabled
                ? ProductDesktopHostLifecycleStatus.AwaitingHost
                : ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            0,
            NativeHostConnected: false,
            OwnedWindowCount: 0);
    }

    public event EventHandler<ProductDesktopHostLifecycleSnapshot>? SnapshotChanged;

    public ProductDesktopHostLifecycleSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            snapshot = new(
                ProductDesktopHostLifecycleStatus.Completed,
                checked(snapshot.Generation + 1),
                NativeHostConnected: false,
                OwnedWindowCount: 0);
            published = snapshot;
        }

        SnapshotChanged?.Invoke(this, published);
        return ValueTask.CompletedTask;
    }
}
