namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionFeatureStatus
{
    DisabledByDesktopHostSafetyPolicy,
    DisabledByInteractionSafetyPolicy,
    DisabledByEmergencyPolicy,
    EnabledForDevelopment,
}

public sealed record ProductDesktopInteractionFeatureDecision(
    ProductDesktopInteractionFeatureStatus Status)
{
    public bool IsEnabled =>
        Status == ProductDesktopInteractionFeatureStatus.EnabledForDevelopment;
}

public static class ProductDesktopInteractionFeaturePolicy
{
    public const string EnvironmentVariableName =
        "LONGGRID_ENABLE_DESKTOP_INTERACTION";
    public const string EmergencyDisableEnvironmentVariableName =
        "LONGGRID_DISABLE_DESKTOP_INTERACTION";

    public static ProductDesktopInteractionFeatureDecision Evaluate(
        ProductDesktopHostFeatureDecision desktopHost,
        string? value,
        string? emergencyDisableValue = null)
    {
        ArgumentNullException.ThrowIfNull(desktopHost);
        return new(
            string.Equals(
                emergencyDisableValue,
                "1",
                StringComparison.Ordinal)
                ? ProductDesktopInteractionFeatureStatus
                    .DisabledByEmergencyPolicy
                : !desktopHost.IsEnabled
                ? ProductDesktopInteractionFeatureStatus
                    .DisabledByDesktopHostSafetyPolicy
                : string.Equals(value, "1", StringComparison.Ordinal)
                    ? ProductDesktopInteractionFeatureStatus
                        .EnabledForDevelopment
                    : ProductDesktopInteractionFeatureStatus
                        .DisabledByInteractionSafetyPolicy);
    }
}

public enum ProductDesktopInteractionMode
{
    DisabledBySafetyPolicy,
    Passive,
    ExplicitInteraction,
}

public enum ProductDesktopInteractionAdmissionStatus
{
    NotAttempted,
    Admitted,
    DisabledBySafetyPolicy,
    InvalidIntent,
    IntentExpired,
    HostNotReady,
    HostAttestationMissing,
    StaleWorkspace,
    StaleTopology,
    StaleWindowRegistry,
    TargetUnavailable,
    TargetLocked,
    AlreadyActive,
}

public enum ProductDesktopInteractionCancellationReason
{
    None,
    ExplicitCancel,
    EscapePressed,
    FocusLost,
    DesktopRevealRequested,
    FullScreenTransition,
    SessionUnavailable,
    RemoteSessionTransition,
    ExplorerRestarted,
    ApplicationShutdown,
    IntentExpired,
    HostUnavailable,
    HostAttestationLost,
    WorkspaceChanged,
    TopologyChanged,
    WindowRegistryChanged,
    TargetUnavailable,
    TargetLocked,
}

public sealed record ProductDesktopInteractionEvidence(
    bool NativeHostConnected,
    bool HostReadyReadOnly,
    bool ReadOnlyAccessibilityAttested,
    bool PassiveWindowContractAttested,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    IReadOnlySet<string> AvailableContainerIds,
    IReadOnlySet<string> LockedContainerIds);

public sealed record ProductDesktopInteractionIntent(
    Guid IntentId,
    string TargetContainerId,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record ProductDesktopInteractionLease(
    Guid IntentId,
    string TargetContainerId,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    DateTimeOffset ExpiresAtUtc);

public sealed record ProductDesktopInteractionSnapshot(
    ProductDesktopInteractionMode Mode,
    ProductDesktopInteractionAdmissionStatus LastAdmissionStatus,
    ProductDesktopInteractionCancellationReason LastCancellationReason,
    ProductDesktopInteractionLease? Lease)
{
    public bool HasActiveLease =>
        Mode == ProductDesktopInteractionMode.ExplicitInteraction
        && Lease is not null;
}

public sealed class ProductDesktopInteractionAdmissionController
{
    public static readonly TimeSpan MaximumIntentLifetime =
        TimeSpan.FromSeconds(5);

    private readonly object sync = new();
    private readonly bool enabled;
    private ProductDesktopInteractionSnapshot snapshot;

    public ProductDesktopInteractionAdmissionController(
        ProductDesktopInteractionFeatureDecision featureDecision)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        snapshot = new(
            enabled
                ? ProductDesktopInteractionMode.Passive
                : ProductDesktopInteractionMode.DisabledBySafetyPolicy,
            enabled
                ? ProductDesktopInteractionAdmissionStatus.NotAttempted
                : ProductDesktopInteractionAdmissionStatus
                    .DisabledBySafetyPolicy,
            ProductDesktopInteractionCancellationReason.None,
            Lease: null);
    }

    public ProductDesktopInteractionSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public ProductDesktopInteractionSnapshot TryEnterExplicitInteraction(
        ProductDesktopInteractionIntent intent,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(evidence);

        lock (sync)
        {
            if (!enabled)
            {
                return snapshot;
            }

            if (snapshot.HasActiveLease)
            {
                return snapshot = snapshot with
                {
                    LastAdmissionStatus =
                        ProductDesktopInteractionAdmissionStatus.AlreadyActive,
                };
            }

            ProductDesktopInteractionAdmissionStatus status =
                Evaluate(intent, evidence, nowUtc);
            if (status != ProductDesktopInteractionAdmissionStatus.Admitted)
            {
                return snapshot = new(
                    ProductDesktopInteractionMode.Passive,
                    status,
                    ProductDesktopInteractionCancellationReason.None,
                    Lease: null);
            }

            return snapshot = new(
                ProductDesktopInteractionMode.ExplicitInteraction,
                ProductDesktopInteractionAdmissionStatus.Admitted,
                ProductDesktopInteractionCancellationReason.None,
                new(
                    intent.IntentId,
                    intent.TargetContainerId,
                    intent.WorkspaceRevision,
                    intent.TopologyGeneration,
                    intent.WindowRegistryGeneration,
                    intent.ExpiresAtUtc));
        }
    }

    public ProductDesktopInteractionSnapshot Revalidate(
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        lock (sync)
        {
            if (!snapshot.HasActiveLease)
            {
                return snapshot;
            }

            ProductDesktopInteractionCancellationReason reason =
                EvaluateActiveLease(snapshot.Lease!, evidence, nowUtc);
            return reason == ProductDesktopInteractionCancellationReason.None
                ? snapshot
                : CancelUnchecked(reason);
        }
    }

    public ProductDesktopInteractionSnapshot Cancel()
    {
        lock (sync)
        {
            return snapshot.HasActiveLease
                ? CancelUnchecked(
                    ProductDesktopInteractionCancellationReason.ExplicitCancel)
                : snapshot;
        }
    }

    internal ProductDesktopInteractionSnapshot CancelForSystemTransition(
        ProductDesktopInteractionCancellationReason reason)
    {
        if (reason is not (ProductDesktopInteractionCancellationReason
                .EscapePressed
            or ProductDesktopInteractionCancellationReason.FocusLost
            or ProductDesktopInteractionCancellationReason
                .DesktopRevealRequested
            or ProductDesktopInteractionCancellationReason
                .FullScreenTransition
            or ProductDesktopInteractionCancellationReason.SessionUnavailable
            or ProductDesktopInteractionCancellationReason
                .RemoteSessionTransition
            or ProductDesktopInteractionCancellationReason.ExplorerRestarted
            or ProductDesktopInteractionCancellationReason
                .ApplicationShutdown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                "System transitions require an explicit cancellation reason.");
        }

        lock (sync)
        {
            return snapshot.HasActiveLease
                ? CancelUnchecked(reason)
                : snapshot;
        }
    }

    private static ProductDesktopInteractionAdmissionStatus Evaluate(
        ProductDesktopInteractionIntent intent,
        ProductDesktopInteractionEvidence evidence,
        DateTimeOffset nowUtc)
    {
        if (intent.IntentId == Guid.Empty
            || string.IsNullOrWhiteSpace(intent.TargetContainerId)
            || intent.WorkspaceRevision <= 0
            || intent.TopologyGeneration <= 0
            || intent.WindowRegistryGeneration <= 0
            || intent.IssuedAtUtc > nowUtc
            || intent.ExpiresAtUtc <= intent.IssuedAtUtc
            || intent.ExpiresAtUtc - intent.IssuedAtUtc > MaximumIntentLifetime)
        {
            return ProductDesktopInteractionAdmissionStatus.InvalidIntent;
        }

        if (intent.ExpiresAtUtc <= nowUtc)
        {
            return ProductDesktopInteractionAdmissionStatus.IntentExpired;
        }

        if (!evidence.NativeHostConnected || !evidence.HostReadyReadOnly)
        {
            return ProductDesktopInteractionAdmissionStatus.HostNotReady;
        }

        if (!evidence.ReadOnlyAccessibilityAttested
            || !evidence.PassiveWindowContractAttested)
        {
            return ProductDesktopInteractionAdmissionStatus
                .HostAttestationMissing;
        }

        if (evidence.AvailableContainerIds is null
            || evidence.LockedContainerIds is null)
        {
            return ProductDesktopInteractionAdmissionStatus.TargetUnavailable;
        }

        if (intent.WorkspaceRevision != evidence.WorkspaceRevision)
        {
            return ProductDesktopInteractionAdmissionStatus.StaleWorkspace;
        }

        if (intent.TopologyGeneration != evidence.TopologyGeneration)
        {
            return ProductDesktopInteractionAdmissionStatus.StaleTopology;
        }

        if (intent.WindowRegistryGeneration
            != evidence.WindowRegistryGeneration)
        {
            return ProductDesktopInteractionAdmissionStatus
                .StaleWindowRegistry;
        }

        if (!ContainsOrdinal(
                evidence.AvailableContainerIds,
                intent.TargetContainerId))
        {
            return ProductDesktopInteractionAdmissionStatus.TargetUnavailable;
        }

        return ContainsOrdinal(
            evidence.LockedContainerIds,
            intent.TargetContainerId)
            ? ProductDesktopInteractionAdmissionStatus.TargetLocked
            : ProductDesktopInteractionAdmissionStatus.Admitted;
    }

    private static ProductDesktopInteractionCancellationReason
        EvaluateActiveLease(
            ProductDesktopInteractionLease lease,
            ProductDesktopInteractionEvidence evidence,
            DateTimeOffset nowUtc)
    {
        if (lease.ExpiresAtUtc <= nowUtc)
        {
            return ProductDesktopInteractionCancellationReason.IntentExpired;
        }

        if (!evidence.NativeHostConnected || !evidence.HostReadyReadOnly)
        {
            return ProductDesktopInteractionCancellationReason.HostUnavailable;
        }

        if (!evidence.ReadOnlyAccessibilityAttested
            || !evidence.PassiveWindowContractAttested)
        {
            return ProductDesktopInteractionCancellationReason
                .HostAttestationLost;
        }

        if (evidence.AvailableContainerIds is null
            || evidence.LockedContainerIds is null)
        {
            return ProductDesktopInteractionCancellationReason.TargetUnavailable;
        }

        if (lease.WorkspaceRevision != evidence.WorkspaceRevision)
        {
            return ProductDesktopInteractionCancellationReason.WorkspaceChanged;
        }

        if (lease.TopologyGeneration != evidence.TopologyGeneration)
        {
            return ProductDesktopInteractionCancellationReason.TopologyChanged;
        }

        if (lease.WindowRegistryGeneration
            != evidence.WindowRegistryGeneration)
        {
            return ProductDesktopInteractionCancellationReason
                .WindowRegistryChanged;
        }

        if (!ContainsOrdinal(
                evidence.AvailableContainerIds,
                lease.TargetContainerId))
        {
            return ProductDesktopInteractionCancellationReason.TargetUnavailable;
        }

        return ContainsOrdinal(
            evidence.LockedContainerIds,
            lease.TargetContainerId)
            ? ProductDesktopInteractionCancellationReason.TargetLocked
            : ProductDesktopInteractionCancellationReason.None;
    }

    private ProductDesktopInteractionSnapshot CancelUnchecked(
        ProductDesktopInteractionCancellationReason reason) =>
        snapshot = new(
            ProductDesktopInteractionMode.Passive,
            snapshot.LastAdmissionStatus,
            reason,
            Lease: null);

    private static bool ContainsOrdinal(
        IEnumerable<string> containerIds,
        string targetContainerId) =>
        containerIds.Any(containerId =>
            string.Equals(
                containerId,
                targetContainerId,
                StringComparison.Ordinal));
}
