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
    bool RealFileOperationsAllowed,
    ProductDesktopInteractionSurfaceEvidence? Surface)
{
    public bool IsDevelopmentInteractionAvailable =>
        Status == ProductDesktopInteractionDevelopmentStatus.Passive
        && Admission.Mode == ProductDesktopInteractionMode.Passive
        && !Admission.HasActiveLease
        && NativeSurfaceAdapterConnected
        && !HiddenRequired
        && !RealFileOperationsAllowed
        && Surface?.IsPassiveContract == true;
}

/// <summary>
/// Owns the fail-closed development interaction policy at the App composition
/// boundary. B6b may attach only a Hidden/Passive native adapter; this
/// controller deliberately exposes no explicit-interaction entry point.
/// </summary>
public sealed class ProductDesktopInteractionDevelopmentController
{
    private readonly object sync = new();
    private readonly bool enabled;
    private readonly ProductDesktopInteractionAdmissionController admission;
    private readonly ProductDesktopInteractionCancellationAdapter cancellation;
    private IProductDesktopInteractionSurfaceModeAdapter? surface;
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

    public bool CanAttachNativeSurface
    {
        get
        {
            lock (sync)
            {
                return enabled
                    && snapshot.Status
                        == ProductDesktopInteractionDevelopmentStatus.Passive
                    && surface is null;
            }
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot AttachPassiveSurface(
        IProductDesktopInteractionSurfaceModeAdapter adapter,
        ProductDesktopInteractionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(evidence);
        lock (sync)
        {
            if (!CanAttachNativeSurfaceUnsafe()
                || !IsCompletePassiveEvidence(evidence))
            {
                return snapshot;
            }

            ProductDesktopInteractionSurfaceEvidence? initial =
                CaptureEvidence(adapter);
            if (initial?.IsHiddenContract != true
                || initial.WindowRegistryGeneration
                    != evidence.WindowRegistryGeneration)
            {
                _ = TryCall(() => adapter.Hide(
                    evidence.WindowRegistryGeneration));
                return Publish(
                    ProductDesktopInteractionDevelopmentStatus
                        .SuspendedFailClosed,
                    hiddenRequired: true,
                    currentSurface: CaptureEvidence(adapter));
            }

            surface = adapter;
            bool applied = TryCall(() => adapter.ApplyPassive(
                evidence.WindowRegistryGeneration));
            ProductDesktopInteractionSurfaceEvidence? passive =
                applied ? CaptureEvidence(adapter) : null;
            if (passive?.IsPassiveContract == true
                && passive.WindowRegistryGeneration
                    == evidence.WindowRegistryGeneration)
            {
                return Publish(
                    ProductDesktopInteractionDevelopmentStatus.Passive,
                    hiddenRequired: false,
                    currentSurface: passive);
            }

            HideAttachedSurfaceUnsafe(evidence.WindowRegistryGeneration);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
                hiddenRequired: true,
                currentSurface: CaptureEvidence(adapter));
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
            long generation = snapshot.Surface?.WindowRegistryGeneration ?? 0;
            HideAttachedSurfaceUnsafe(generation);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
                hiddenRequired: true,
                currentSurface: CaptureEvidence(surface));
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

            if (!IsCompletePassiveEvidence(evidence) || surface is null)
            {
                return snapshot;
            }

            bool applied = TryCall(() => surface.ApplyPassive(
                evidence.WindowRegistryGeneration));
            ProductDesktopInteractionSurfaceEvidence? passive =
                applied ? CaptureEvidence(surface) : null;
            if (passive?.IsPassiveContract == true
                && passive.WindowRegistryGeneration
                    == evidence.WindowRegistryGeneration)
            {
                return Publish(
                    ProductDesktopInteractionDevelopmentStatus.Passive,
                    hiddenRequired: false,
                    currentSurface: passive);
            }

            HideAttachedSurfaceUnsafe(evidence.WindowRegistryGeneration);
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
                hiddenRequired: true,
                currentSurface: CaptureEvidence(surface));
        }
    }

    public ProductDesktopInteractionDevelopmentSnapshot DetachPassiveSurface(
        IProductDesktopInteractionSurfaceModeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (sync)
        {
            if (!ReferenceEquals(surface, adapter))
            {
                return snapshot;
            }

            long generation = snapshot.Surface?.WindowRegistryGeneration ?? 0;
            HideAttachedSurfaceUnsafe(generation);
            surface = null;
            ProductDesktopInteractionDevelopmentStatus status =
                snapshot.Status is ProductDesktopInteractionDevelopmentStatus
                    .EmergencyDisabled
                    or ProductDesktopInteractionDevelopmentStatus.Completed
                    ? snapshot.Status
                    : ProductDesktopInteractionDevelopmentStatus.Passive;
            return Publish(
                status,
                hiddenRequired: status
                    != ProductDesktopInteractionDevelopmentStatus.Passive,
                currentSurface: null);
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
            long generation = snapshot.Surface?.WindowRegistryGeneration ?? 0;
            HideAttachedSurfaceUnsafe(generation);
            surface = null;
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.EmergencyDisabled,
                hiddenRequired: true,
                currentSurface: null);
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
            long generation = snapshot.Surface?.WindowRegistryGeneration ?? 0;
            HideAttachedSurfaceUnsafe(generation);
            surface = null;
            return Publish(
                ProductDesktopInteractionDevelopmentStatus.Completed,
                hiddenRequired: true,
                currentSurface: null);
        }
    }

    private ProductDesktopInteractionDevelopmentSnapshot Publish(
        ProductDesktopInteractionDevelopmentStatus status,
        bool hiddenRequired,
        ProductDesktopInteractionSurfaceEvidence? currentSurface)
    {
        snapshot = CreateSnapshot(
            status,
            checked(snapshot.Revision + 1),
            hiddenRequired,
            currentSurface);
        return snapshot;
    }

    private ProductDesktopInteractionDevelopmentSnapshot CreateSnapshot(
        ProductDesktopInteractionDevelopmentStatus status,
        long revision,
        bool hiddenRequired,
        ProductDesktopInteractionSurfaceEvidence? currentSurface = null) =>
        new(
            status,
            admission.Snapshot,
            revision,
            NativeSurfaceAdapterConnected: surface is not null,
            hiddenRequired,
            RealFileOperationsAllowed: false,
            currentSurface);

    private bool CanAttachNativeSurfaceUnsafe() =>
        enabled
        && snapshot.Status == ProductDesktopInteractionDevelopmentStatus.Passive
        && surface is null;

    private static bool IsCompletePassiveEvidence(
        ProductDesktopInteractionEvidence evidence) =>
        evidence.NativeHostConnected
        && evidence.HostReadyReadOnly
        && evidence.ReadOnlyAccessibilityAttested
        && evidence.PassiveWindowContractAttested
        && evidence.WorkspaceRevision > 0
        && evidence.TopologyGeneration > 0
        && evidence.WindowRegistryGeneration > 0
        && evidence.AvailableContainerIds is not null
        && evidence.LockedContainerIds is not null;

    private void HideAttachedSurfaceUnsafe(long generation)
    {
        if (surface is null || generation <= 0)
        {
            return;
        }

        _ = TryCall(() => surface.Hide(generation));
    }

    private static ProductDesktopInteractionSurfaceEvidence? CaptureEvidence(
        IProductDesktopInteractionSurfaceModeAdapter? adapter)
    {
        if (adapter is null)
        {
            return null;
        }

        try
        {
            ProductDesktopInteractionSurfaceCapture capture = adapter.Capture();
            return capture.Succeeded ? capture.Evidence : null;
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return null;
        }
    }

    private static bool TryCall(Func<bool> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return false;
        }
    }
}
