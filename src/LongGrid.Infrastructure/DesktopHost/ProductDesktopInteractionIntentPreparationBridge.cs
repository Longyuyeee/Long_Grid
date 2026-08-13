using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopInteractionIntentPreparationStatus
{
    DisabledBySafetyPolicy,
    AwaitingPassiveSurface,
    InvalidUserAction,
    ReplayedUserAction,
    StaleUserAction,
    DisplayUnavailable,
    HitRejected,
    IntentRejected,
    Prepared,
    Consumed,
    Invalidated,
    Completed,
}

public sealed record ProductDesktopInteractionIntentPreparationRequest(
    Guid UserActionId,
    long UserActionSequence,
    DateTimeOffset ObservedAtUtc,
    bool ExplicitUserActionConfirmed,
    ProductDesktopInteractionActivationKind Activation,
    string DisplayId,
    int ClientX,
    int ClientY);

public sealed record ProductDesktopInteractionPreparedIntent(
    ProductDesktopInteractionIntent Intent,
    long BridgeGeneration,
    long UserActionSequence);

public sealed record ProductDesktopInteractionIntentPreparationSnapshot(
    ProductDesktopInteractionIntentPreparationStatus Status,
    long BridgeGeneration,
    long LastUserActionSequence,
    bool PreparedIntentAvailable,
    bool ExplicitInteractionEntered,
    bool RealFileOperationsAllowed);

public sealed record ProductDesktopInteractionIntentPreparationResult(
    ProductDesktopInteractionIntentPreparationSnapshot Snapshot,
    ProductDesktopInteractionPreparedIntent? PreparedIntent)
{
    public bool IsPrepared =>
        Snapshot.Status
            == ProductDesktopInteractionIntentPreparationStatus.Prepared
        && PreparedIntent is not null
        && Snapshot.PreparedIntentAvailable
        && !Snapshot.ExplicitInteractionEntered
        && !Snapshot.RealFileOperationsAllowed;
}

public sealed class ProductDesktopInteractionIntentPreparationBridge
{
    public static readonly TimeSpan MaximumUserActionAge =
        TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly bool enabled;
    private ProductDesktopInteractionIntentPreparationSnapshot snapshot;
    private ProductDesktopInteractionPreparedIntent? preparedIntent;
    private bool completed;

    public ProductDesktopInteractionIntentPreparationBridge(
        ProductDesktopInteractionIntentBridgeFeatureDecision featureDecision)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        snapshot = CreateSnapshot(
            enabled
                ? ProductDesktopInteractionIntentPreparationStatus
                    .AwaitingPassiveSurface
                : ProductDesktopInteractionIntentPreparationStatus
                    .DisabledBySafetyPolicy,
            generation: 0,
            lastUserActionSequence: 0,
            prepared: false);
    }

    public ProductDesktopInteractionIntentPreparationSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public ProductDesktopInteractionIntentPreparationResult Prepare(
        ProductDesktopInteractionIntentPreparationRequest request,
        ProductDesktopHostProjectionBatch batch,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            if (completed)
            {
                return Result();
            }

            if (!enabled)
            {
                return Result();
            }

            if (!IsStructurallyValid(request, nowUtc))
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .InvalidUserAction,
                    invalidateCurrent: true);
            }

            if (request.UserActionSequence <= snapshot.LastUserActionSequence)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .ReplayedUserAction,
                    invalidateCurrent: false);
            }

            long nextGeneration = checked(snapshot.BridgeGeneration + 1);
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus
                    .AwaitingPassiveSurface,
                nextGeneration,
                request.UserActionSequence,
                prepared: false);
            preparedIntent = null;
            if (nowUtc - request.ObservedAtUtc > MaximumUserActionAge)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .StaleUserAction,
                    invalidateCurrent: false);
            }

            if (!IsCompletePassiveEvidence(batch, evidence))
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .AwaitingPassiveSurface,
                    invalidateCurrent: false);
            }

            ProductDesktopHostDisplayProjection? display = batch.Displays
                .SingleOrDefault(candidate => string.Equals(
                    candidate.DisplayId,
                    request.DisplayId,
                    StringComparison.Ordinal));
            if (display is null)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .DisplayUnavailable,
                    invalidateCurrent: false);
            }

            ProductDesktopInteractionHitTestResult hit =
                ProductDesktopInteractionHitTestAdapter.HitTest(
                    display,
                    request.ClientX,
                    request.ClientY);
            if (!hit.IsHit)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus.HitRejected,
                    invalidateCurrent: false);
            }

            if (evidence.AvailableContainerIds?.Contains(hit.ContainerId!)
                    != true
                || evidence.LockedContainerIds?.Contains(hit.ContainerId!)
                    == true)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .IntentRejected,
                    invalidateCurrent: false);
            }

            ProductDesktopInteractionIntentCreationResult created =
                ProductDesktopInteractionIntentFactory.Create(
                    request.Activation,
                    hit,
                    evidence,
                    request.UserActionId,
                    request.ObservedAtUtc);
            if (!created.IsCreated)
            {
                return Publish(
                    ProductDesktopInteractionIntentPreparationStatus
                        .IntentRejected,
                    invalidateCurrent: false);
            }

            preparedIntent = new(
                created.Intent!,
                snapshot.BridgeGeneration,
                request.UserActionSequence);
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus.Prepared,
                snapshot.BridgeGeneration,
                snapshot.LastUserActionSequence,
                prepared: true);
            return Result();
        }
    }

    public bool IsCurrent(
        ProductDesktopInteractionPreparedIntent candidate,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            return enabled
                && !completed
                && preparedIntent == candidate
                && snapshot.PreparedIntentAvailable
                && candidate.BridgeGeneration == snapshot.BridgeGeneration
                && candidate.UserActionSequence
                    == snapshot.LastUserActionSequence
                && candidate.Intent.ExpiresAtUtc > nowUtc
                && EvidenceMatches(candidate.Intent, evidence);
        }
    }

    internal bool TryConsume(
        ProductDesktopInteractionPreparedIntent candidate,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc,
        out ProductDesktopInteractionIntent? intent)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            bool current = enabled
                && !completed
                && preparedIntent == candidate
                && snapshot.PreparedIntentAvailable
                && candidate.BridgeGeneration == snapshot.BridgeGeneration
                && candidate.UserActionSequence
                    == snapshot.LastUserActionSequence
                && candidate.Intent.ExpiresAtUtc > nowUtc
                && EvidenceMatches(candidate.Intent, evidence);
            if (!current)
            {
                intent = null;
                return false;
            }

            intent = candidate.Intent;
            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus.Consumed,
                checked(snapshot.BridgeGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return true;
        }
    }

    public ProductDesktopInteractionIntentPreparationSnapshot Invalidate()
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return snapshot;
            }

            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus.Invalidated,
                checked(snapshot.BridgeGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    public ProductDesktopInteractionIntentPreparationSnapshot
        AwaitPassiveSurface()
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return snapshot;
            }

            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus
                    .AwaitingPassiveSurface,
                checked(snapshot.BridgeGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    public ProductDesktopInteractionIntentPreparationSnapshot Complete()
    {
        lock (gate)
        {
            if (completed)
            {
                return snapshot;
            }

            completed = true;
            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionIntentPreparationStatus.Completed,
                checked(snapshot.BridgeGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    private ProductDesktopInteractionIntentPreparationResult Publish(
        ProductDesktopInteractionIntentPreparationStatus status,
        bool invalidateCurrent)
    {
        long generation = snapshot.BridgeGeneration;
        if (invalidateCurrent)
        {
            preparedIntent = null;
            generation = checked(generation + 1);
        }

        snapshot = CreateSnapshot(
            status,
            generation,
            snapshot.LastUserActionSequence,
            prepared: preparedIntent is not null);
        return Result();
    }

    private ProductDesktopInteractionIntentPreparationResult Result() =>
        new(snapshot, preparedIntent);

    private static ProductDesktopInteractionIntentPreparationSnapshot
        CreateSnapshot(
            ProductDesktopInteractionIntentPreparationStatus status,
            long generation,
            long lastUserActionSequence,
            bool prepared) =>
        new(
            status,
            generation,
            lastUserActionSequence,
            prepared,
            ExplicitInteractionEntered: false,
            RealFileOperationsAllowed: false);

    private static bool IsStructurallyValid(
        ProductDesktopInteractionIntentPreparationRequest request,
        DateTimeOffset nowUtc) =>
        request.UserActionId != Guid.Empty
        && request.UserActionSequence > 0
        && request.ObservedAtUtc != default
        && request.ObservedAtUtc <= nowUtc
        && request.ExplicitUserActionConfirmed
        && Enum.IsDefined(request.Activation)
        && !string.IsNullOrWhiteSpace(request.DisplayId)
        && request.ClientX >= 0
        && request.ClientY >= 0;

    private static bool IsCompletePassiveEvidence(
        ProductDesktopHostProjectionBatch batch,
        ProductDesktopInteractionEvidence evidence) =>
        evidence.NativeHostConnected
        && evidence.HostReadyReadOnly
        && evidence.ReadOnlyAccessibilityAttested
        && evidence.PassiveWindowContractAttested
        && evidence.WorkspaceRevision == batch.WorkspaceRevision
        && evidence.TopologyGeneration == batch.TopologyGeneration
        && evidence.WindowRegistryGeneration > 0
        && evidence.AvailableContainerIds is not null
        && evidence.LockedContainerIds is not null;

    private static bool EvidenceMatches(
        ProductDesktopInteractionIntent intent,
        ProductDesktopInteractionEvidence evidence) =>
        evidence.NativeHostConnected
        && evidence.HostReadyReadOnly
        && evidence.ReadOnlyAccessibilityAttested
        && evidence.PassiveWindowContractAttested
        && intent.WorkspaceRevision == evidence.WorkspaceRevision
        && intent.TopologyGeneration == evidence.TopologyGeneration
        && intent.WindowRegistryGeneration
            == evidence.WindowRegistryGeneration
        && evidence.AvailableContainerIds?.Contains(intent.TargetContainerId)
            == true
        && evidence.LockedContainerIds?.Contains(intent.TargetContainerId)
            == false;
}
