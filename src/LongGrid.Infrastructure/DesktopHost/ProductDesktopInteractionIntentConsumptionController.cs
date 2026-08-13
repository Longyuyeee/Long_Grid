using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopInteractionIntentConsumptionStatus
{
    DisabledBySafetyPolicy,
    AwaitingSurface,
    StalePreparedIntent,
    TargetUnavailable,
    EntryRejected,
    Explicit,
    SelectionApplied,
    Cancelled,
    Completed,
}

public sealed record ProductDesktopInteractionIntentConsumptionSnapshot(
    ProductDesktopInteractionIntentConsumptionStatus Status,
    long Revision,
    bool SurfaceAttached,
    bool PreparedIntentConsumed,
    bool RealFileOperationsAllowed,
    ProductDesktopInteractionSurfaceTransactionSnapshot? Transaction)
{
    public bool IsExplicit =>
        (Status is ProductDesktopInteractionIntentConsumptionStatus.Explicit
            or ProductDesktopInteractionIntentConsumptionStatus.SelectionApplied)
        && SurfaceAttached
        && PreparedIntentConsumed
        && !RealFileOperationsAllowed
        && Transaction?.IsExplicit == true;
}

public sealed record ProductDesktopInteractionIntentConsumptionResult(
    ProductDesktopInteractionIntentConsumptionSnapshot Snapshot)
{
    public static ProductDesktopInteractionIntentConsumptionResult Disabled { get; }
        = new(new ProductDesktopInteractionIntentConsumptionSnapshot(
            ProductDesktopInteractionIntentConsumptionStatus
                .DisabledBySafetyPolicy,
            0,
            SurfaceAttached: false,
            PreparedIntentConsumed: false,
            RealFileOperationsAllowed: false,
            Transaction: null));

    public bool IsExplicit => Snapshot.IsExplicit;
}

/// <summary>
/// Atomically consumes one current prepared intent and gives it to the existing
/// admission/surface/selection transaction. It owns no Windows input source and
/// exposes no file-operation capability.
/// </summary>
public sealed class ProductDesktopInteractionIntentConsumptionController
{
    private readonly object gate = new();
    private readonly ProductDesktopInteractionFeatureDecision featureDecision;
    private readonly ProductDesktopInteractionIntentPreparationBridge bridge;
    private readonly bool enabled;
    private IProductDesktopInteractionSurfaceModeAdapter? surface;
    private ProductDesktopInteractionSurfaceModeTransaction? transaction;
    private ProductDesktopInteractionIntentConsumptionSnapshot snapshot;
    private bool completed;

    public ProductDesktopInteractionIntentConsumptionController(
        ProductDesktopInteractionFeatureDecision featureDecision,
        ProductDesktopInteractionInputForwardingFeatureDecision
            inputForwardingDecision,
        ProductDesktopInteractionIntentPreparationBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        ArgumentNullException.ThrowIfNull(inputForwardingDecision);
        ArgumentNullException.ThrowIfNull(bridge);
        this.featureDecision = featureDecision;
        this.bridge = bridge;
        enabled = featureDecision.IsEnabled && inputForwardingDecision.IsEnabled;
        snapshot = new(
            enabled
                ? ProductDesktopInteractionIntentConsumptionStatus.AwaitingSurface
                : ProductDesktopInteractionIntentConsumptionStatus
                    .DisabledBySafetyPolicy,
            0,
            SurfaceAttached: false,
            PreparedIntentConsumed: false,
            RealFileOperationsAllowed: false,
            Transaction: null);
    }

    public bool IsEnabled => enabled;

    public ProductDesktopInteractionIntentConsumptionSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public bool AttachSurface(IProductDesktopInteractionSurfaceModeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (gate)
        {
            if (!enabled || completed || surface is not null)
            {
                return false;
            }

            ProductDesktopInteractionSurfaceCapture capture = adapter.Capture();
            if (!capture.Succeeded || capture.Evidence?.IsPassiveContract != true)
            {
                return false;
            }

            surface = adapter;
            transaction = new(
                new ProductDesktopInteractionAdmissionController(featureDecision),
                adapter);
            Publish(
                ProductDesktopInteractionIntentConsumptionStatus.AwaitingSurface,
                consumed: false,
                transaction.Snapshot);
            return true;
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult Consume(
        ProductDesktopInteractionPreparedIntent candidate,
        ProductDesktopInteractionEvidence evidence,
        IReadOnlyList<string> visibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(visibleItemIds);
        lock (gate)
        {
            if (!enabled || completed || transaction is null)
            {
                return Result();
            }

            if (!bridge.TryConsume(candidate, evidence, nowUtc, out var intent))
            {
                return PublishResult(
                    ProductDesktopInteractionIntentConsumptionStatus
                        .StalePreparedIntent,
                    consumed: false,
                    transaction.Snapshot);
            }

            ProductDesktopInteractionSurfaceTransactionSnapshot entered =
                transaction.TryEnter(intent!, evidence, visibleItemIds, nowUtc);
            return PublishResult(
                entered.IsExplicit
                    ? ProductDesktopInteractionIntentConsumptionStatus.Explicit
                    : ProductDesktopInteractionIntentConsumptionStatus.EntryRejected,
                consumed: true,
                entered);
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult ApplySelection(
        ProductDesktopSelectionRequest request,
        IReadOnlyList<string> currentVisibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentVisibleItemIds);
        lock (gate)
        {
            if (!enabled
                || completed
                || transaction?.Snapshot.Admission.Lease is not { } lease)
            {
                return Result();
            }

            ProductDesktopInteractionSurfaceTransactionSnapshot applied =
                transaction.ApplySelection(
                    request,
                    lease,
                    currentVisibleItemIds,
                    nowUtc);
            return PublishResult(
                applied.IsExplicit
                    ? ProductDesktopInteractionIntentConsumptionStatus
                        .SelectionApplied
                    : ProductDesktopInteractionIntentConsumptionStatus.EntryRejected,
                consumed: snapshot.PreparedIntentConsumed,
                applied);
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult Cancel(
        ProductDesktopInteractionCancellationSignal signal,
        DateTimeOffset nowUtc)
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return Result();
            }

            ProductDesktopInteractionSurfaceTransactionSnapshot? cancelled =
                transaction?.Cancel(signal, nowUtc);
            return PublishResult(
                ProductDesktopInteractionIntentConsumptionStatus.Cancelled,
                consumed: false,
                cancelled);
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult AwaitPassiveSurface() =>
        PublishExternal(
            ProductDesktopInteractionIntentConsumptionStatus.AwaitingSurface);

    public ProductDesktopInteractionIntentConsumptionResult
        RejectUnavailableTarget() => PublishExternal(
            ProductDesktopInteractionIntentConsumptionStatus.TargetUnavailable);

    public bool DetachSurface(
        IProductDesktopInteractionSurfaceModeAdapter adapter,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (gate)
        {
            if (!enabled || !ReferenceEquals(surface, adapter))
            {
                return false;
            }

            _ = transaction?.Cancel(
                ProductDesktopInteractionCancellationSignal.ApplicationShutdown,
                nowUtc);
            surface = null;
            transaction = null;
            Publish(
                ProductDesktopInteractionIntentConsumptionStatus.AwaitingSurface,
                consumed: false,
                currentTransaction: null);
            return true;
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult Complete(
        DateTimeOffset nowUtc)
    {
        lock (gate)
        {
            if (completed || !enabled)
            {
                return Result();
            }

            _ = transaction?.Cancel(
                ProductDesktopInteractionCancellationSignal.ApplicationShutdown,
                nowUtc);
            completed = true;
            surface = null;
            transaction = null;
            return PublishResult(
                ProductDesktopInteractionIntentConsumptionStatus.Completed,
                consumed: false,
                currentTransaction: null);
        }
    }

    private ProductDesktopInteractionIntentConsumptionResult PublishExternal(
        ProductDesktopInteractionIntentConsumptionStatus status)
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return Result();
            }

            return PublishResult(
                status,
                snapshot.PreparedIntentConsumed,
                transaction?.Snapshot);
        }
    }

    private ProductDesktopInteractionIntentConsumptionResult PublishResult(
        ProductDesktopInteractionIntentConsumptionStatus status,
        bool consumed,
        ProductDesktopInteractionSurfaceTransactionSnapshot? currentTransaction)
    {
        Publish(status, consumed, currentTransaction);
        return Result();
    }

    private void Publish(
        ProductDesktopInteractionIntentConsumptionStatus status,
        bool consumed,
        ProductDesktopInteractionSurfaceTransactionSnapshot? currentTransaction)
    {
        snapshot = new(
            status,
            checked(snapshot.Revision + 1),
            SurfaceAttached: surface is not null,
            PreparedIntentConsumed: consumed,
            RealFileOperationsAllowed: false,
            currentTransaction);
    }

    private ProductDesktopInteractionIntentConsumptionResult Result() =>
        new(snapshot);
}
