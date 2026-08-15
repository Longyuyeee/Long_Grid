using LongGrid.ThumbnailWorker;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductThumbnailWorkerLifecycleStatus
{
    DisabledByControlledSessionPolicy,
    ReadyIdleRestricted,
    FailedClosed,
    Disposed,
}

public sealed record ProductThumbnailWorkerLifecycleSnapshot(
    ProductThumbnailWorkerLifecycleStatus Status,
    long Generation,
    bool FormalIntegrationAvailable,
    int WorkerProcessCount,
    int ActiveOwnedProfileCount,
    bool IsZeroCapabilityAppContainer,
    bool UsesKillOnJobClose)
{
    public static ProductThumbnailWorkerLifecycleSnapshot Disabled { get; } =
        new(
            ProductThumbnailWorkerLifecycleStatus
                .DisabledByControlledSessionPolicy,
            Generation: 0,
            FormalIntegrationAvailable: false,
            WorkerProcessCount: 0,
            ActiveOwnedProfileCount: 0,
            IsZeroCapabilityAppContainer: false,
            UsesKillOnJobClose: false);
}

internal interface IProductThumbnailWorkerRuntime : IDisposable
{
    RestrictedThumbnailWorkerRuntimeSnapshot Snapshot { get; }

    bool OwnedProfileDeletionConfirmed { get; }
}

internal sealed class ProductThumbnailWorkerRuntimeAdapter :
    IProductThumbnailWorkerRuntime
{
    private readonly RestrictedThumbnailWorkerRuntime runtime;

    internal ProductThumbnailWorkerRuntimeAdapter()
    {
        runtime = RestrictedThumbnailWorkerRuntime.Start();
    }

    public RestrictedThumbnailWorkerRuntimeSnapshot Snapshot => runtime.Snapshot;

    public bool OwnedProfileDeletionConfirmed =>
        runtime.OwnedProfileDeletionConfirmed;

    public void Dispose() => runtime.Dispose();
}

public sealed class ProductThumbnailWorkerLifecycleController : IDisposable
{
    private readonly object gate = new();
    private IProductThumbnailWorkerRuntime? runtime;
    private ProductThumbnailWorkerLifecycleSnapshot snapshot;
    private bool disposed;
    private bool ownedProfileDeletionConfirmed;

    private ProductThumbnailWorkerLifecycleController(
        ProductResourceTelemetryFeatureDecision telemetryFeature,
        Func<IProductThumbnailWorkerRuntime> runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(telemetryFeature);
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        snapshot = ProductThumbnailWorkerLifecycleSnapshot.Disabled;
        if (!telemetryFeature.IsEnabled)
        {
            return;
        }

        IProductThumbnailWorkerRuntime? candidate = null;
        try
        {
            candidate = runtimeFactory();
            RestrictedThumbnailWorkerRuntimeSnapshot runtimeSnapshot =
                candidate.Snapshot;
            if (!IsAttested(runtimeSnapshot))
            {
                throw new InvalidOperationException(
                    "The formal thumbnail worker runtime failed attestation.");
            }

            runtime = candidate;
            candidate = null;
            snapshot = new(
                ProductThumbnailWorkerLifecycleStatus.ReadyIdleRestricted,
                Generation: 1,
                FormalIntegrationAvailable: true,
                runtimeSnapshot.WorkerProcessCount,
                runtimeSnapshot.ActiveOwnedProfileCount,
                runtimeSnapshot.IsZeroCapabilityAppContainer,
                runtimeSnapshot.UsesKillOnJobClose);
        }
        catch
        {
            candidate?.Dispose();
            ownedProfileDeletionConfirmed =
                candidate?.OwnedProfileDeletionConfirmed ?? false;
            snapshot = new(
                ProductThumbnailWorkerLifecycleStatus.FailedClosed,
                Generation: 1,
                FormalIntegrationAvailable: false,
                WorkerProcessCount: 0,
                ActiveOwnedProfileCount: 0,
                IsZeroCapabilityAppContainer: false,
                UsesKillOnJobClose: false);
        }
    }

    public ProductThumbnailWorkerLifecycleSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                RefreshRuntimeSnapshot();
                return snapshot;
            }
        }
    }

    public static ProductThumbnailWorkerLifecycleController Start(
        ProductResourceTelemetryFeatureDecision telemetryFeature) =>
        new(telemetryFeature, () => new ProductThumbnailWorkerRuntimeAdapter());

    public bool OwnedProfileDeletionConfirmed
    {
        get
        {
            lock (gate)
            {
                return ownedProfileDeletionConfirmed;
            }
        }
    }

    internal static ProductThumbnailWorkerLifecycleController Start(
        ProductResourceTelemetryFeatureDecision telemetryFeature,
        Func<IProductThumbnailWorkerRuntime> runtimeFactory) =>
        new(telemetryFeature, runtimeFactory);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (runtime is not null)
            {
                runtime.Dispose();
                ownedProfileDeletionConfirmed =
                    runtime.OwnedProfileDeletionConfirmed;
            }
            runtime = null;
            snapshot = new(
                ProductThumbnailWorkerLifecycleStatus.Disposed,
                checked(snapshot.Generation + 1),
                FormalIntegrationAvailable: false,
                WorkerProcessCount: 0,
                ActiveOwnedProfileCount: 0,
                IsZeroCapabilityAppContainer: false,
                UsesKillOnJobClose: false);
            disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private static bool IsAttested(
        RestrictedThumbnailWorkerRuntimeSnapshot runtimeSnapshot) =>
        runtimeSnapshot.IsStarted
        && runtimeSnapshot.WorkerProcessCount == 1
        && runtimeSnapshot.ActiveOwnedProfileCount == 1
        && runtimeSnapshot.IsZeroCapabilityAppContainer
        && runtimeSnapshot.UsesKillOnJobClose;

    private void RefreshRuntimeSnapshot()
    {
        if (runtime is null
            || snapshot.Status
                != ProductThumbnailWorkerLifecycleStatus.ReadyIdleRestricted)
        {
            return;
        }

        try
        {
            RestrictedThumbnailWorkerRuntimeSnapshot runtimeSnapshot =
                runtime.Snapshot;
            if (IsAttested(runtimeSnapshot))
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
        }

        runtime.Dispose();
        ownedProfileDeletionConfirmed =
            runtime.OwnedProfileDeletionConfirmed;
        runtime = null;
        snapshot = new(
            ProductThumbnailWorkerLifecycleStatus.FailedClosed,
            checked(snapshot.Generation + 1),
            FormalIntegrationAvailable: false,
            WorkerProcessCount: 0,
            ActiveOwnedProfileCount: 0,
            IsZeroCapabilityAppContainer: false,
            UsesKillOnJobClose: false);
    }
}
