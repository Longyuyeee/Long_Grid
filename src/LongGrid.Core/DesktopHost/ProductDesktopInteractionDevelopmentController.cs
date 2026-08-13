namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionDevelopmentStatus
{
    DisabledBySafetyPolicy,
    Passive,
    SuspendedFailClosed,
    EmergencyDisabled,
    Completed,
}

public sealed record ProductDesktopInteractionDevelopmentSnapshot(
    ProductDesktopInteractionDevelopmentStatus Status,
    ProductDesktopInteractionSnapshot Admission,
    long Revision,
    bool NativeSurfaceAdapterConnected,
    bool HiddenRequired,
    bool RealFileOperationsAllowed)
{
    public bool IsDevelopmentInteractionAvailable =>
        Status == ProductDesktopInteractionDevelopmentStatus.Passive
        && Admission.Mode == ProductDesktopInteractionMode.Passive
        && !Admission.HasActiveLease
        && !NativeSurfaceAdapterConnected
        && !HiddenRequired
        && !RealFileOperationsAllowed;
}

/// <summary>
/// Owns the fail-closed development interaction policy at the App composition
/// boundary. B6a deliberately exposes no explicit-interaction entry point and
/// creates no native adapter; B6b may attach one only behind this controller.
/// </summary>
public sealed class ProductDesktopInteractionDevelopmentController
{
    private readonly object sync = new();
    private readonly bool enabled;
    private readonly ProductDesktopInteractionAdmissionController admission;
    private readonly ProductDesktopInteractionCancellationAdapter cancellation;
    private ProductDesktopInteractionDevelopmentSnapshot snapshot;

    public ProductDesktopInteractionDevelopmentController(
        ProductDesktopInteractionFeatureDecision featureDecision)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        admission = new(featureDecision);
        cancellation = new(admission);
        ProductDesktopInteractionDevelopmentStatus status =
            featureDecision.Status == ProductDesktopInteractionFeatureStatus
                .DisabledByEmergencyPolicy
                ? ProductDesktopInteractionDevelopmentStatus.EmergencyDisabled
                : enabled
                    ? ProductDesktopInteractionDevelopmentStatus.Passive
                    : ProductDesktopInteractionDevelopmentStatus
                        .DisabledBySafetyPolicy;
        snapshot = CreateSnapshot(
            status,
            revision: 0,
            hiddenRequired: status
                != ProductDesktopInteractionDevelopmentStatus.Passive);
    }

    public ProductDesktopInteractionDevelopmentSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot SuspendFailClosed(
        ProductDesktopInteractionCancellationSignal signal,
        DateTimeOffset nowUtc)
    {
        if (signal is ProductDesktopInteractionCancellationSignal
                .EvidenceChanged
            or ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signal),
                "Fail-closed suspension requires a direct system transition.");
        }

        lock (sync)
        {
            if (!enabled
                || snapshot.Status is ProductDesktopInteractionDevelopmentStatus
                    .EmergencyDisabled
                    or ProductDesktopInteractionDevelopmentStatus.Completed)
            {
                return snapshot;
            }

            _ = cancellation.Handle(signal, nowUtc);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
                hiddenRequired: true);
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot TryResumePassive(
        ProductDesktopInteractionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        lock (sync)
        {
            if (!enabled
                || snapshot.Status
                    != ProductDesktopInteractionDevelopmentStatus
                        .SuspendedFailClosed)
            {
                return snapshot;
            }

            bool passiveAttested = evidence.NativeHostConnected
                && evidence.HostReadyReadOnly
                && evidence.ReadOnlyAccessibilityAttested
                && evidence.PassiveWindowContractAttested
                && evidence.WorkspaceRevision > 0
                && evidence.TopologyGeneration > 0
                && evidence.WindowRegistryGeneration > 0
                && evidence.AvailableContainerIds is not null
                && evidence.LockedContainerIds is not null;
            return passiveAttested
                ? Publish(
                    ProductDesktopInteractionDevelopmentStatus.Passive,
                    hiddenRequired: false)
                : snapshot;
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot EmergencyDisable(
        DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            if (snapshot.Status is ProductDesktopInteractionDevelopmentStatus
                .EmergencyDisabled
                or ProductDesktopInteractionDevelopmentStatus.Completed)
            {
                return snapshot;
            }

            _ = cancellation.Handle(
                ProductDesktopInteractionCancellationSignal
                    .ApplicationShutdown,
                nowUtc);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.EmergencyDisabled,
                hiddenRequired: true);
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot Complete(
        DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            if (snapshot.Status
                == ProductDesktopInteractionDevelopmentStatus.Completed)
            {
                return snapshot;
            }

            _ = cancellation.Handle(
                ProductDesktopInteractionCancellationSignal
                    .ApplicationShutdown,
                nowUtc);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.Completed,
                hiddenRequired: true);
        }
    }

    private ProductDesktopInteractionDevelopmentSnapshot Publish(
        ProductDesktopInteractionDevelopmentStatus status,
        bool hiddenRequired)
    {
        snapshot = CreateSnapshot(
            status,
            checked(snapshot.Revision + 1),
            hiddenRequired);
        return snapshot;
    }

    private ProductDesktopInteractionDevelopmentSnapshot CreateSnapshot(
        ProductDesktopInteractionDevelopmentStatus status,
        long revision,
        bool hiddenRequired) =>
        new(
            status,
            admission.Snapshot,
            revision,
            NativeSurfaceAdapterConnected: false,
            hiddenRequired,
            RealFileOperationsAllowed: false);
}
