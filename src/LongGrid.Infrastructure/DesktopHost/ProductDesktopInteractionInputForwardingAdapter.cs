using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopInteractionForwardedInputKind
{
    PrimaryPointerPress,
    KeyboardActivation,
    AssistiveTechnologyActivation,
}

public enum ProductDesktopInteractionInputForwardingStatus
{
    DisabledBySafetyPolicy,
    AwaitingPassiveSurface,
    InvalidInput,
    ReplayedInput,
    PreparationRejected,
    Prepared,
    Invalidated,
    Completed,
}

public sealed record ProductDesktopInteractionForwardedInput(
    Guid UserActionId,
    long UserActionSequence,
    DateTimeOffset ObservedAtUtc,
    ProductDesktopInteractionForwardedInputKind Kind,
    string DisplayId,
    int ClientX,
    int ClientY,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

public sealed record ProductDesktopInteractionInputForwardingSnapshot(
    ProductDesktopInteractionInputForwardingStatus Status,
    long ForwardingGeneration,
    long LastUserActionSequence,
    bool PreparedIntentAvailable,
    bool CapturesGlobalInput,
    bool SendsSyntheticInput,
    bool ExplicitInteractionEntered,
    bool RealFileOperationsAllowed);

public sealed record ProductDesktopInteractionInputForwardingResult(
    ProductDesktopInteractionInputForwardingSnapshot Snapshot,
    ProductDesktopInteractionPreparedIntent? PreparedIntent)
{
    public bool IsPrepared =>
        Snapshot.Status == ProductDesktopInteractionInputForwardingStatus.Prepared
        && Snapshot.PreparedIntentAvailable
        && PreparedIntent is not null
        && !Snapshot.CapturesGlobalInput
        && !Snapshot.SendsSyntheticInput
        && !Snapshot.ExplicitInteractionEntered
        && !Snapshot.RealFileOperationsAllowed;
}

/// <summary>
/// Converts one already-isolated, attested input notification into one intent
/// preparation request. It deliberately owns no Windows input source and has no
/// path to admission, Explicit surface mode or file operations.
/// </summary>
public sealed class ProductDesktopInteractionInputForwardingAdapter
{
    private const int RememberedActionCapacity = 64;
    private readonly object gate = new();
    private readonly bool enabled;
    private readonly ProductDesktopInteractionIntentPreparationBridge bridge;
    private readonly Queue<Guid> rememberedActionOrder = new();
    private readonly HashSet<Guid> rememberedActionIds = [];
    private ProductDesktopInteractionInputForwardingSnapshot snapshot;
    private ProductDesktopInteractionPreparedIntent? preparedIntent;
    private bool completed;

    public ProductDesktopInteractionInputForwardingAdapter(
        ProductDesktopInteractionInputForwardingFeatureDecision featureDecision,
        ProductDesktopInteractionIntentPreparationBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        ArgumentNullException.ThrowIfNull(bridge);
        enabled = featureDecision.IsEnabled;
        this.bridge = bridge;
        snapshot = CreateSnapshot(
            enabled
                ? ProductDesktopInteractionInputForwardingStatus
                    .AwaitingPassiveSurface
                : ProductDesktopInteractionInputForwardingStatus
                    .DisabledBySafetyPolicy,
            generation: 0,
            sequence: 0,
            prepared: false);
    }

    public ProductDesktopInteractionInputForwardingSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public bool IsEnabled => enabled;

    public ProductDesktopInteractionInputForwardingResult Forward(
        ProductDesktopInteractionForwardedInput input,
        ProductDesktopHostProjectionBatch batch,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (gate)
        {
            if (completed || !enabled)
            {
                return Result(PreparedIntent: null);
            }

            if (!IsStructurallyValid(input, nowUtc))
            {
                _ = bridge.Invalidate();
                preparedIntent = null;
                return Publish(
                    ProductDesktopInteractionInputForwardingStatus.InvalidInput,
                    prepared: false,
                    incrementGeneration: true,
                    PreparedIntent: null);
            }

            if (input.UserActionSequence <= snapshot.LastUserActionSequence
                || rememberedActionIds.Contains(input.UserActionId))
            {
                return Publish(
                    ProductDesktopInteractionInputForwardingStatus.ReplayedInput,
                    prepared: preparedIntent is not null,
                    incrementGeneration: false,
                    preparedIntent);
            }

            Remember(input.UserActionId);
            snapshot = CreateSnapshot(
                ProductDesktopInteractionInputForwardingStatus
                    .AwaitingPassiveSurface,
                checked(snapshot.ForwardingGeneration + 1),
                input.UserActionSequence,
                prepared: false);
            ProductDesktopInteractionIntentPreparationResult prepared =
                bridge.Prepare(
                    new(
                        input.UserActionId,
                        input.UserActionSequence,
                        input.ObservedAtUtc,
                        ExplicitUserActionConfirmed: true,
                        ToActivation(input.Kind),
                        input.DisplayId,
                        input.ClientX,
                        input.ClientY),
                    batch,
                    evidence,
                    nowUtc);
            if (!prepared.IsPrepared)
            {
                preparedIntent = null;
                return Publish(
                    ProductDesktopInteractionInputForwardingStatus
                        .PreparationRejected,
                    prepared: false,
                    incrementGeneration: false,
                    PreparedIntent: null);
            }

            preparedIntent = prepared.PreparedIntent;
            return Publish(
                ProductDesktopInteractionInputForwardingStatus.Prepared,
                prepared: true,
                incrementGeneration: false,
                preparedIntent);
        }
    }

    public ProductDesktopInteractionInputForwardingSnapshot Invalidate()
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return snapshot;
            }

            _ = bridge.Invalidate();
            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionInputForwardingStatus.Invalidated,
                checked(snapshot.ForwardingGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    public ProductDesktopInteractionInputForwardingSnapshot
        AwaitPassiveSurface()
    {
        lock (gate)
        {
            if (!enabled || completed)
            {
                return snapshot;
            }

            _ = bridge.AwaitPassiveSurface();
            preparedIntent = null;
            snapshot = CreateSnapshot(
                ProductDesktopInteractionInputForwardingStatus
                    .AwaitingPassiveSurface,
                checked(snapshot.ForwardingGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    public ProductDesktopInteractionInputForwardingSnapshot Complete()
    {
        lock (gate)
        {
            if (completed)
            {
                return snapshot;
            }

            completed = true;
            _ = bridge.Complete();
            preparedIntent = null;
            rememberedActionIds.Clear();
            rememberedActionOrder.Clear();
            snapshot = CreateSnapshot(
                ProductDesktopInteractionInputForwardingStatus.Completed,
                checked(snapshot.ForwardingGeneration + 1),
                snapshot.LastUserActionSequence,
                prepared: false);
            return snapshot;
        }
    }

    private ProductDesktopInteractionInputForwardingResult Publish(
        ProductDesktopInteractionInputForwardingStatus status,
        bool prepared,
        bool incrementGeneration,
        ProductDesktopInteractionPreparedIntent? PreparedIntent)
    {
        snapshot = CreateSnapshot(
            status,
            incrementGeneration
                ? checked(snapshot.ForwardingGeneration + 1)
                : snapshot.ForwardingGeneration,
            snapshot.LastUserActionSequence,
            prepared);
        return Result(PreparedIntent);
    }

    private ProductDesktopInteractionInputForwardingResult Result(
        ProductDesktopInteractionPreparedIntent? PreparedIntent) =>
        new(snapshot, PreparedIntent);

    private void Remember(Guid actionId)
    {
        rememberedActionIds.Add(actionId);
        rememberedActionOrder.Enqueue(actionId);
        if (rememberedActionOrder.Count <= RememberedActionCapacity)
        {
            return;
        }

        _ = rememberedActionIds.Remove(rememberedActionOrder.Dequeue());
    }

    private static bool IsStructurallyValid(
        ProductDesktopInteractionForwardedInput input,
        DateTimeOffset nowUtc) =>
        input.UserActionId != Guid.Empty
        && input.UserActionSequence > 0
        && input.ObservedAtUtc != default
        && input.ObservedAtUtc <= nowUtc
        && Enum.IsDefined(input.Kind)
        && !string.IsNullOrWhiteSpace(input.DisplayId)
        && input.ClientX >= 0
        && input.ClientY >= 0
        && input.SourceAttested
        && !input.IsInjected
        && !input.IsAutoRepeat;

    private static ProductDesktopInteractionActivationKind ToActivation(
        ProductDesktopInteractionForwardedInputKind kind) => kind switch
        {
            ProductDesktopInteractionForwardedInputKind.PrimaryPointerPress =>
                ProductDesktopInteractionActivationKind.PrimaryPointerPress,
            ProductDesktopInteractionForwardedInputKind.KeyboardActivation =>
                ProductDesktopInteractionActivationKind.KeyboardActivation,
            ProductDesktopInteractionForwardedInputKind
                .AssistiveTechnologyActivation =>
                ProductDesktopInteractionActivationKind
                    .AssistiveTechnologyActivation,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static ProductDesktopInteractionInputForwardingSnapshot
        CreateSnapshot(
            ProductDesktopInteractionInputForwardingStatus status,
            long generation,
            long sequence,
            bool prepared) =>
        new(
            status,
            generation,
            sequence,
            prepared,
            CapturesGlobalInput: false,
            SendsSyntheticInput: false,
            ExplicitInteractionEntered: false,
            RealFileOperationsAllowed: false);
}
