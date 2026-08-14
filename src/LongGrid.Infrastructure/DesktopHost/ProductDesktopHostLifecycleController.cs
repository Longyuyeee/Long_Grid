using System.ComponentModel;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopHostLifecycleStatus
{
    DisabledBySafetyPolicy,
    AwaitingHost,
    AwaitingWorkspace,
    SuspendedUnsafeTopology,
    SuspendedSystemSurface,
    ReadyReadOnly,
    Faulted,
    Completed,
}

public sealed record ProductDesktopHostLifecycleSnapshot(
    ProductDesktopHostLifecycleStatus Status,
    long Generation,
    bool NativeHostConnected,
    int OwnedWindowCount,
    long WorkspaceRevision = 0,
    long TopologyGeneration = 0,
    int RenderedContainerCount = 0,
    bool ReadOnlyAccessibilityAvailable = false,
    bool PassiveWindowContractAttested = false,
    ProductDesktopInteractionSystemSurfaceEventKind? LastSystemSurfaceEvent = null,
    bool ExplicitInteractionActive = false,
    int SelectedItemCount = 0,
    bool FocusedItemAvailable = false,
    long SelectionRevision = 0)
{
    public bool FeatureEnabled =>
        Status is ProductDesktopHostLifecycleStatus.AwaitingHost
            or ProductDesktopHostLifecycleStatus.AwaitingWorkspace
            or ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology
            or ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
            or ProductDesktopHostLifecycleStatus.ReadyReadOnly
            or ProductDesktopHostLifecycleStatus.Faulted;
}

public sealed record ProductDesktopHostReadOnlyProjection
{
    public const int MaximumVisibleItems = 12;
    public const int MaximumVisibleNameLength = 512;

    private ProductDesktopHostReadOnlyProjection(
        string containerId,
        string title,
        IReadOnlyList<string> itemNames,
        string color,
        double opacity,
        bool isCollapsed,
        double xDip,
        double yDip,
        double widthDip,
        double heightDip,
        bool isLocked,
        IReadOnlyList<string> itemIds)
    {
        ContainerId = containerId;
        Title = title;
        ItemNames = itemNames;
        Color = color;
        Opacity = opacity;
        IsCollapsed = isCollapsed;
        XDip = xDip;
        YDip = yDip;
        WidthDip = widthDip;
        HeightDip = heightDip;
        IsLocked = isLocked;
        ItemIds = itemIds;
    }

    public string ContainerId { get; }

    public string Title { get; }

    public IReadOnlyList<string> ItemNames { get; }

    public string Color { get; }

    public double Opacity { get; }

    public bool IsCollapsed { get; }

    public double XDip { get; }

    public double YDip { get; }

    public double WidthDip { get; }

    public double HeightDip { get; }

    public bool IsLocked { get; }

    public IReadOnlyList<string> ItemIds { get; }

    public static ProductDesktopHostReadOnlyProjection Create(
        string containerId,
        string title,
        IEnumerable<string> itemNames,
        string color,
        double opacity,
        bool isCollapsed,
        double xDip,
        double yDip,
        double widthDip,
        double heightDip,
        bool isLocked = false,
        IEnumerable<string>? itemIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(itemNames);
        string[] visibleItems = itemNames
            .Take(MaximumVisibleItems)
            .ToArray();
        string[] visibleItemIds = (itemIds
                ?? Enumerable.Range(1, visibleItems.Length)
                    .Select(ordinal => $"item:{ordinal}"))
            .Take(MaximumVisibleItems)
            .ToArray();
        if (visibleItems.Any(string.IsNullOrWhiteSpace)
            || containerId.Length > ProductConfigurationLimits.MaximumIdLength
            || title.Length > ProductConfigurationLimits.MaximumNameLength
            || visibleItems.Any(item => item.Length > MaximumVisibleNameLength)
            || visibleItemIds.Length != visibleItems.Length
            || visibleItemIds.Any(string.IsNullOrWhiteSpace)
            || visibleItemIds.Any(id => id.Length
                > ProductConfigurationLimits.MaximumIdLength)
            || visibleItemIds.Distinct(StringComparer.Ordinal).Count()
                != visibleItemIds.Length
            || color is null
            || color.Length != 7
            || color[0] != '#'
            || !color[1..].All(Uri.IsHexDigit)
            || !double.IsFinite(opacity)
            || opacity is < 0 or > 1
            || !double.IsFinite(xDip)
            || !double.IsFinite(yDip)
            || !double.IsFinite(widthDip)
            || !double.IsFinite(heightDip)
            || widthDip <= 0
            || heightDip <= 0)
        {
            throw new ArgumentException(
                "DesktopHost projection values must remain finite and bounded.");
        }

        return new(
            containerId,
            title,
            Array.AsReadOnly(visibleItems),
            color,
            opacity,
            isCollapsed,
            xDip,
            yDip,
            widthDip,
            heightDip,
            isLocked,
            Array.AsReadOnly(visibleItemIds));
    }
}

internal interface IProductDesktopHostReadOnlySurface : IDisposable
{
    nint Handle { get; }

    nint InstanceMarker { get; }

    uint ProcessId { get; }

    uint ThreadId { get; }

    bool ReadOnlyAccessibilityAttested { get; }

    bool PassiveWindowContractAttested { get; }

    bool ExplicitWindowContractAttested { get; }

    bool HiddenWindowContractAttested { get; }

    bool ApplyExplicit();

    bool ApplyPassive();

    bool ApplyHidden();

    void BindSelection(
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?> snapshot,
        Func<string, ProductDesktopSelectionRequest, bool> apply)
    {
    }

    void RefreshSelection()
    {
    }
}

internal interface IProductDesktopHostReadOnlySurfaceFactory
{
    IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden);
}

public sealed class ProductDesktopHostLifecycleController : IAsyncDisposable
{
    private static long nextInstanceMarker;
    private readonly object gate = new();
    private readonly bool enabled;
    private readonly IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory;
    private readonly ProductDesktopHostWindowBridge? windowBridge;
    private readonly ProductDesktopInteractionDevelopmentController?
        interactionDevelopment;
    private readonly ProductDesktopInteractionIntentPreparationBridge?
        intentPreparation;
    private readonly ProductDesktopInteractionInputForwardingAdapter?
        inputForwarding;
    private readonly ProductDesktopInteractionIntentConsumptionController?
        intentConsumption;
    private readonly IProductDesktopInteractionActivationSourceFactory?
        activationSourceFactory;
    private readonly Guid hostInstanceId = Guid.NewGuid();
    private ProductDesktopHostLifecycleSnapshot snapshot;
    private readonly List<IProductDesktopHostReadOnlySurface> surfaces = [];
    private readonly List<IProductDesktopInteractionActivationSource>
        activationSources = [];
    private readonly List<(string DisplayId, long WindowGeneration)>
        registrations = [];
    private ProductDesktopHostProjectionBatch? currentBatch;
    private ProductDesktopHostProjectionUpdate? currentUpdate;
    private long lastWorkspaceRevision = -1;
    private long lastTopologyGeneration = -1;
    private long lastSystemSurfaceSequence;
    private long windowGeneration;
    private ProductDesktopHostPassiveSurfaceModeAdapter? interactionSurface;
    private bool disposed;

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision)
        : this(featureDecision, interactionDevelopment: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment)
        : this(featureDecision, interactionDevelopment, intentPreparation: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation)
        : this(
            featureDecision,
            interactionDevelopment,
            intentPreparation,
            inputForwarding: null,
            intentConsumption: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation,
        ProductDesktopInteractionInputForwardingAdapter? inputForwarding)
        : this(
            featureDecision,
            interactionDevelopment,
            intentPreparation,
            inputForwarding,
            intentConsumption: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation,
        ProductDesktopInteractionInputForwardingAdapter? inputForwarding,
        ProductDesktopInteractionIntentConsumptionController? intentConsumption)
        : this(
            featureDecision,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostReadOnlySurfaceFactory()
                : null,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostWindowInspector()
                : null,
            interactionDevelopment,
            intentPreparation,
            inputForwarding,
            intentConsumption,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopInteractionActivationSourceFactory()
                : null)
    {
    }

    internal ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory,
        IProductDesktopHostWindowInspector? windowInspector,
        ProductDesktopInteractionDevelopmentController?
            interactionDevelopment = null,
        ProductDesktopInteractionIntentPreparationBridge?
            intentPreparation = null,
        ProductDesktopInteractionInputForwardingAdapter?
            inputForwarding = null,
        ProductDesktopInteractionIntentConsumptionController?
            intentConsumption = null,
        IProductDesktopInteractionActivationSourceFactory?
            activationSourceFactory = null)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        if (enabled && (surfaceFactory is null || windowInspector is null))
        {
            throw new ArgumentException(
                "Enabled DesktopHost lifecycle requires finite native adapters.");
        }

        this.surfaceFactory = surfaceFactory;
        this.interactionDevelopment = interactionDevelopment;
        this.intentPreparation = intentPreparation;
        this.inputForwarding = inputForwarding;
        this.intentConsumption = intentConsumption;
        this.activationSourceFactory = activationSourceFactory;
        windowBridge = windowInspector is null
            ? null
            : new(windowInspector);
        snapshot = new(
            enabled
                ? ProductDesktopHostLifecycleStatus.AwaitingHost
                : ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            0,
            NativeHostConnected: false,
            OwnedWindowCount: 0);
    }

    public event EventHandler<ProductDesktopHostLifecycleSnapshot>? SnapshotChanged;

    public bool CanRequestKeyboardInteraction
    {
        get
        {
            lock (gate)
            {
                return !disposed
                    && snapshot.Status
                        == ProductDesktopHostLifecycleStatus.ReadyReadOnly
                    && IsPassiveInteractionAvailableUnsafe()
                    && activationSources.Count > 0
                    && activationSources.All(source =>
                        source.CanActivate);
            }
        }
    }

    public bool OwnsForegroundActivationSource
    {
        get
        {
            lock (gate)
            {
                return !disposed
                    && activationSources.Any(source =>
                        source.OwnsForegroundWindow
                        && source.ContractAttested);
            }
        }
    }

    public bool RequestKeyboardInteraction()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshot.Status
                    != ProductDesktopHostLifecycleStatus.ReadyReadOnly
                || activationSources.FirstOrDefault(source =>
                    source.CanActivate) is not { } source)
            {
                return false;
            }

            return source.RequestKeyboardInteraction();
        }
    }

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

    public ProductDesktopHostLifecycleSnapshot ApplyProjectionBatch(
        ProductDesktopHostProjectionBatch? batch)
    {
        ProductDesktopHostLifecycleSnapshot published;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                return snapshot;
            }

            if (batch is not null
                && currentBatch is not null
                && BatchesEqual(currentBatch, batch))
            {
                return snapshot;
            }

            if (batch is null && surfaces.Count == 0)
            {
                return snapshot;
            }

            ReleaseSurfaceUnsafe();
            published = batch is null
                ? UpdateSnapshotUnsafe(
                    ProductDesktopHostLifecycleStatus.AwaitingHost,
                    connected: false,
                    ownedWindowCount: 0)
                : CreateSurfacesUnsafe(batch);
        }

        Publish(published);
        return published;
    }

    public ProductDesktopHostLifecycleSnapshot ApplyProjectionUpdate(
        ProductDesktopHostProjectionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ProductDesktopHostLifecycleSnapshot published;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                return snapshot;
            }

            if (update.WorkspaceRevision < lastWorkspaceRevision
                || update.TopologyGeneration < lastTopologyGeneration)
            {
                return snapshot;
            }

            if (update.WorkspaceRevision == lastWorkspaceRevision
                && update.TopologyGeneration == lastTopologyGeneration)
            {
                if (UpdatesEqual(currentUpdate, update))
                {
                    return snapshot;
                }

                if (currentUpdate?.Disposition !=
                    ProductDesktopHostProjectionDisposition.TopologyRefreshing)
                {
                    ReleaseSurfaceUnsafe();
                    currentUpdate = update;
                    published = UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0,
                        workspaceRevision: update.WorkspaceRevision,
                        topologyGeneration: update.TopologyGeneration);
                }
                else
                {
                    published = ApplyNewUpdateUnsafe(update);
                }
            }
            else
            {
                published = ApplyNewUpdateUnsafe(update);
            }
        }

        Publish(published);
        return published;
    }

    public ProductDesktopHostLifecycleSnapshot ApplySystemSurfaceEvent(
        ProductDesktopInteractionSystemSurfaceEvent systemEvent)
    {
        ArgumentNullException.ThrowIfNull(systemEvent);
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled
                || interactionDevelopment is null
                || !systemEvent.IsValid
                || systemEvent.Sequence <= lastSystemSurfaceSequence)
            {
                return snapshot;
            }

            lastSystemSurfaceSequence = systemEvent.Sequence;
            if (systemEvent.RequiresHiddenSurface)
            {
                InvalidatePreparedInputUnsafe();
                _ = intentConsumption?.Cancel(
                    systemEvent.ToCancellationSignal(),
                    systemEvent.ObservedAtUtc);
                if (!TryApplyActivationSourcesUnsafe(visible: false))
                {
                    ReleaseSurfaceUnsafe();
                    published = UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }
                else if (interactionSurface is null)
                {
                    return snapshot;
                }
                else
                {
                    ProductDesktopInteractionDevelopmentSnapshot suspended =
                        interactionDevelopment.SuspendFailClosed(
                            systemEvent.ToCancellationSignal(),
                            systemEvent.ObservedAtUtc);
                    if (suspended.Surface?.IsHiddenContract != true
                        || !suspended.HiddenRequired)
                    {
                        ReleaseSurfaceUnsafe();
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }
                    else
                    {
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus
                                .SuspendedSystemSurface,
                            connected: true,
                            ownedWindowCount: surfaces.Count,
                            workspaceRevision: currentBatch?.WorkspaceRevision ?? 0,
                            topologyGeneration: currentBatch?.TopologyGeneration ?? 0,
                            renderedContainerCount: currentBatch?.ContainerCount ?? 0,
                            readOnlyAccessibilityAvailable: true,
                            passiveWindowContractAttested: false,
                            lastSystemSurfaceEvent: systemEvent.Kind);
                    }
                }
            }
            else if (snapshot.Status
                    == ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
                && currentBatch is not null
                && interactionSurface is not null
                && interactionDevelopment.Snapshot.Surface is
                { WindowRegistryGeneration: > 0 } hidden)
            {
                ProductDesktopInteractionDevelopmentSnapshot resumed =
                    interactionDevelopment.TryResumePassive(
                        CreateInteractionEvidence(
                            currentBatch,
                            hidden.WindowRegistryGeneration));
                if (resumed.IsDevelopmentInteractionAvailable)
                {
                    if (!TryApplyActivationSourcesUnsafe(visible: true))
                    {
                        ReleaseSurfaceUnsafe();
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }
                    else
                    {
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.ReadyReadOnly,
                            connected: true,
                            ownedWindowCount: surfaces.Count,
                            workspaceRevision: currentBatch.WorkspaceRevision,
                            topologyGeneration: currentBatch.TopologyGeneration,
                            renderedContainerCount: currentBatch.ContainerCount,
                            readOnlyAccessibilityAvailable: true,
                            passiveWindowContractAttested: true,
                            lastSystemSurfaceEvent: systemEvent.Kind);
                    }
                }
            }
        }

        if (published is not null)
        {
            Publish(published);
        }

        return published ?? snapshot;
    }

    public ProductDesktopInteractionIntentPreparationResult
        PrepareInteractionIntent(
            ProductDesktopInteractionIntentPreparationRequest request,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentPreparation is null)
            {
                return new(
                    new(
                        ProductDesktopInteractionIntentPreparationStatus
                            .DisabledBySafetyPolicy,
                        0,
                        0,
                        PreparedIntentAvailable: false,
                        ExplicitInteractionEntered: false,
                        RealFileOperationsAllowed: false),
                    PreparedIntent: null);
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                ProductDesktopInteractionIntentPreparationSnapshot awaiting =
                    intentPreparation.AwaitPassiveSurface();
                return new(awaiting, PreparedIntent: null);
            }

            return intentPreparation.Prepare(
                request,
                currentBatch!,
                evidence,
                nowUtc);
        }
    }

    public ProductDesktopInteractionInputForwardingResult
        ForwardInteractionInput(
            ProductDesktopInteractionForwardedInput input,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inputForwarding is null)
            {
                return new(
                    new(
                        ProductDesktopInteractionInputForwardingStatus
                            .DisabledBySafetyPolicy,
                        0,
                        0,
                        PreparedIntentAvailable: false,
                        CapturesGlobalInput: false,
                        SendsSyntheticInput: false,
                        ExplicitInteractionEntered: false,
                        RealFileOperationsAllowed: false),
                    PreparedIntent: null);
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                ProductDesktopInteractionInputForwardingSnapshot awaiting =
                    inputForwarding.AwaitPassiveSurface();
                return new(awaiting, PreparedIntent: null);
            }

            return inputForwarding.Forward(
                input,
                currentBatch!,
                evidence,
                nowUtc);
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult
        ConsumePreparedInteractionIntent(
            ProductDesktopInteractionPreparedIntent preparedIntent,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(preparedIntent);
        ProductDesktopInteractionIntentConsumptionResult result;
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentConsumption is null
                || currentBatch is null
                || interactionSurface is null
                || snapshot.Status
                    != ProductDesktopHostLifecycleStatus.ReadyReadOnly)
            {
                return ProductDesktopInteractionIntentConsumptionResult
                    .Disabled;
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                result = intentConsumption.AwaitPassiveSurface();
                published = RefreshInteractionObservationUnsafe();
            }
            else
            {
                ProductDesktopHostReadOnlyProjection? target =
                    currentBatch.Displays
                        .SelectMany(display => display.Containers)
                        .SingleOrDefault(container => string.Equals(
                            container.ContainerId,
                            preparedIntent.Intent.TargetContainerId,
                            StringComparison.Ordinal));
                result = target is null
                    ? intentConsumption.RejectUnavailableTarget()
                    : intentConsumption.Consume(
                        preparedIntent,
                        evidence,
                        target.ItemIds,
                        nowUtc);
                published = RefreshInteractionObservationUnsafe();
            }
        }

        if (published is not null)
        {
            Publish(published);
        }
        return result;
    }

    public ProductDesktopInteractionIntentConsumptionResult
        ApplyInteractionSelection(
            ProductDesktopSelectionRequest request,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProductDesktopInteractionIntentConsumptionResult result;
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentConsumption is null || currentBatch is null)
            {
                return ProductDesktopInteractionIntentConsumptionResult
                    .Disabled;
            }

            string? targetId = intentConsumption.Snapshot.Transaction
                ?.Admission.Lease?.TargetContainerId;
            ProductDesktopHostReadOnlyProjection? target = currentBatch.Displays
                .SelectMany(display => display.Containers)
                .SingleOrDefault(container => string.Equals(
                    container.ContainerId,
                    targetId,
                    StringComparison.Ordinal));
            result = target is null
                ? intentConsumption.RejectUnavailableTarget()
                : intentConsumption.ApplySelection(request, target.ItemIds, nowUtc);
            published = RefreshInteractionObservationUnsafe();
        }

        if (published is not null)
        {
            Publish(published);
        }
        return result;
    }

    private ProductDesktopHostLifecycleSnapshot ApplyNewUpdateUnsafe(
        ProductDesktopHostProjectionUpdate update)
    {
        lastWorkspaceRevision = update.WorkspaceRevision;
        lastTopologyGeneration = update.TopologyGeneration;
        ReleaseSurfaceUnsafe();
        currentUpdate = update;
        if (update.Disposition == ProductDesktopHostProjectionDisposition.Ready)
        {
            return CreateSurfacesUnsafe(update.Batch!);
        }

        ProductDesktopHostLifecycleStatus status = update.Disposition ==
            ProductDesktopHostProjectionDisposition.EmptyWorkspace
                ? ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                : ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology;
        return UpdateSnapshotUnsafe(
            status,
            connected: false,
            ownedWindowCount: 0,
            workspaceRevision: update.WorkspaceRevision,
            topologyGeneration: update.TopologyGeneration);
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
            if (inputForwarding?.IsEnabled == true)
            {
                _ = inputForwarding.Complete();
            }
            else
            {
                _ = intentPreparation?.Complete();
            }
            _ = intentConsumption?.Complete(DateTimeOffset.UtcNow);
            ReleaseSurfaceUnsafe();
            published = UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.Completed,
                connected: false,
                ownedWindowCount: 0);
        }

        Publish(published);
        return ValueTask.CompletedTask;
    }

    private ProductDesktopHostLifecycleSnapshot CreateSurfacesUnsafe(
        ProductDesktopHostProjectionBatch batch)
    {
        try
        {
            bool controlledSurfaceLifecycle =
                interactionDevelopment?.CanAttachNativeSurface == true;
            ProductDesktopHostIdentity? identity = null;
            ProductDesktopHostWindowRegistrationResult? registration = null;
            foreach (ProductDesktopHostDisplayProjection display in batch.Displays)
            {
                IProductDesktopHostReadOnlySurface created =
                    surfaceFactory!.Create(
                        display,
                        NextInstanceMarker(),
                        startHidden: controlledSurfaceLifecycle);
                surfaces.Add(created);
                created.BindSelection(
                    CaptureInteractionTransaction,
                    (containerId, request) => ApplySelectionFromSurface(
                        created,
                        display,
                        containerId,
                        request));
                if (!created.ReadOnlyAccessibilityAttested
                    || (controlledSurfaceLifecycle
                        ? !created.HiddenWindowContractAttested
                        : !created.PassiveWindowContractAttested))
                {
                    throw new InvalidOperationException(
                        "Every display surface must attest read-only UIA and passive window behavior.");
                }
                identity ??= new(
                    hostInstanceId,
                    batch.TopologyGeneration,
                    created.ProcessId,
                    created.ThreadId);
                if (created.ProcessId != identity.ProcessId
                    || created.ThreadId != identity.ThreadId)
                {
                    throw new InvalidOperationException(
                        "Every display surface must share one host thread.");
                }

                if (surfaces.Count == 1)
                {
                    windowBridge!.Connect(identity);
                }

                long nextWindowGeneration = checked(windowGeneration + 1);
                registration = windowBridge!.Register(
                    new(
                        DisplayRegistrationId(display.DisplayId),
                        identity,
                        nextWindowGeneration,
                        created.Handle,
                        created.InstanceMarker));
                if (!registration.IsRegistered
                    || !registration.Snapshot.OwnershipAttested)
                {
                    ReleaseSurfaceUnsafe();
                    return UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }

                windowGeneration = nextWindowGeneration;
                registrations.Add((display.DisplayId, nextWindowGeneration));
            }

            currentBatch = batch;
            bool interactionAttached = false;
            if (controlledSurfaceLifecycle)
            {
                interactionSurface = new(
                    surfaces.AsReadOnly(),
                    registration!.Snapshot.Generation);
                ProductDesktopInteractionDevelopmentSnapshot attached =
                    interactionDevelopment!.AttachPassiveSurface(
                        interactionSurface,
                        CreateInteractionEvidence(
                            batch,
                            registration.Snapshot.Generation));
                interactionAttached = attached.IsDevelopmentInteractionAvailable;
                if (interactionAttached && intentConsumption?.IsEnabled == true)
                {
                    interactionAttached = intentConsumption.AttachSurface(
                        interactionSurface);
                }
                if (!interactionAttached)
                {
                    ReleaseSurfaceUnsafe();
                    return UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }

                if (inputForwarding?.IsEnabled == true
                    && intentConsumption?.IsEnabled == true)
                {
                    if (activationSourceFactory is null)
                    {
                        ReleaseSurfaceUnsafe();
                        return UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }

                    foreach (ProductDesktopHostDisplayProjection display
                        in batch.Displays.Where(candidate =>
                            candidate.Containers.Any(container =>
                                !container.IsLocked)))
                    {
                        IProductDesktopInteractionActivationSource source =
                            activationSourceFactory.Create(
                                display,
                                NextInstanceMarker(),
                                HandleActivationInput);
                        activationSources.Add(source);
                        source.BindSelection(
                            CaptureInteractionTransaction,
                            request => ApplySelectionFromActivationSource(
                                source,
                                display,
                                request),
                            () => CancelInteractionFromActivationSource(source));
                        if (!source.IsVisible || !source.ContractAttested)
                        {
                            ReleaseSurfaceUnsafe();
                            return UpdateSnapshotUnsafe(
                                ProductDesktopHostLifecycleStatus.Faulted,
                                connected: false,
                                ownedWindowCount: 0);
                        }
                    }
                }
            }

            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.ReadyReadOnly,
                connected: true,
                ownedWindowCount: registration!.Snapshot.VerifiedWindowCount,
                workspaceRevision: batch.WorkspaceRevision,
                topologyGeneration: batch.TopologyGeneration,
                renderedContainerCount: batch.ContainerCount,
                readOnlyAccessibilityAvailable: true,
                passiveWindowContractAttested: true);
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            ReleaseSurfaceUnsafe();
            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.Faulted,
                connected: false,
                ownedWindowCount: 0);
        }
    }

    private void ReleaseSurfaceUnsafe()
    {
        InvalidatePreparedInputUnsafe();
        foreach (IProductDesktopInteractionActivationSource source
            in activationSources)
        {
            source.Dispose();
        }
        activationSources.Clear();

        if (interactionSurface is not null)
        {
            _ = intentConsumption?.DetachSurface(
                interactionSurface,
                DateTimeOffset.UtcNow);
            _ = interactionDevelopment?.DetachPassiveSurface(
                interactionSurface);
            interactionSurface = null;
        }

        if (windowBridge is not null)
        {
            foreach ((string displayId, long generation) in registrations)
            {
                _ = windowBridge.Unregister(
                    DisplayRegistrationId(displayId),
                    generation);
            }
        }

        foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
        {
            surface.Dispose();
        }

        surfaces.Clear();
        registrations.Clear();
        currentBatch = null;
        windowBridge?.Disconnect(hostInstanceId);
    }

    private bool HandleActivationInput(
        ProductDesktopInteractionForwardedInput input)
    {
        ProductDesktopInteractionInputForwardingResult forwarded =
            ForwardInteractionInput(input, DateTimeOffset.UtcNow);
        bool entered = forwarded.IsPrepared
            && ConsumePreparedInteractionIntent(
                forwarded.PreparedIntent!,
                DateTimeOffset.UtcNow).IsExplicit;
        if (entered)
        {
            lock (gate)
            {
                foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
                {
                    surface.RefreshSelection();
                }
            }
        }
        return entered;
    }

    private ProductDesktopInteractionSurfaceTransactionSnapshot?
        CaptureInteractionTransaction()
    {
        lock (gate)
        {
            return disposed ? null : intentConsumption?.Snapshot.Transaction;
        }
    }

    private bool ApplySelectionFromSurface(
        IProductDesktopHostReadOnlySurface source,
        ProductDesktopHostDisplayProjection display,
        string containerId,
        ProductDesktopSelectionRequest request)
    {
        lock (gate)
        {
            string? targetId = intentConsumption?.Snapshot.Transaction
                ?.Selection?.ContainerId;
            if (disposed
                || !surfaces.Contains(source)
                || !string.Equals(targetId, containerId,
                    StringComparison.Ordinal)
                || !display.Containers.Any(container => string.Equals(
                    container.ContainerId,
                    containerId,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            return ApplySelectionAndRefreshUnsafe(request);
        }
    }

    private bool ApplySelectionFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopSelectionRequest request)
    {
        lock (gate)
        {
            ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
                intentConsumption?.Snapshot.Transaction;
            string? targetId = transaction?.Selection?.ContainerId;
            if (disposed
                || !activationSources.Contains(source)
                || !string.Equals(source.DisplayId, display.DisplayId,
                    StringComparison.Ordinal)
                || !display.Containers.Any(container => string.Equals(
                    container.ContainerId,
                    targetId,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            return ApplySelectionAndRefreshUnsafe(request);
        }
    }

    private bool ApplySelectionAndRefreshUnsafe(
        ProductDesktopSelectionRequest request)
    {
        ProductDesktopInteractionIntentConsumptionResult result =
            ApplyInteractionSelection(request, DateTimeOffset.UtcNow);
        foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
        {
            surface.RefreshSelection();
        }

        return result.IsExplicit
            && result.Snapshot.Transaction?.Selection?.Status
                == ProductDesktopSelectionStatus.Applied;
    }

    private bool CancelInteractionFromActivationSource(
        IProductDesktopInteractionActivationSource source)
    {
        ProductDesktopHostLifecycleSnapshot? published;
        bool succeeded;
        lock (gate)
        {
            if (disposed || !activationSources.Contains(source)
                || intentConsumption is null)
            {
                return false;
            }

            ProductDesktopInteractionIntentConsumptionResult cancelled =
                intentConsumption.Cancel(
                    ProductDesktopInteractionCancellationSignal.EscapePressed,
                    DateTimeOffset.UtcNow);
            bool hidden = TryApplyActivationSourcesUnsafe(visible: false);
            bool visible = hidden
                && TryApplyActivationSourcesUnsafe(visible: true);
            if (!visible)
            {
                _ = TryApplyActivationSourcesUnsafe(visible: false);
            }
            foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
            {
                surface.RefreshSelection();
            }
            published = RefreshInteractionObservationUnsafe();
            succeeded = visible && !cancelled.IsExplicit;
        }

        if (published is not null)
        {
            Publish(published);
        }
        return succeeded;
    }

    private void InvalidatePreparedInputUnsafe()
    {
        if (inputForwarding?.IsEnabled == true)
        {
            _ = inputForwarding.Invalidate();
        }
        else
        {
            _ = intentPreparation?.Invalidate();
        }
    }

    private bool IsPassiveInteractionAvailableUnsafe()
    {
        try
        {
            return interactionSurface?.Capture().Evidence
                ?.IsPassiveContract == true;
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            return false;
        }
    }

    private bool TryApplyActivationSourcesUnsafe(bool visible)
    {
        bool succeeded = true;
        foreach (IProductDesktopInteractionActivationSource source
            in activationSources)
        {
            try
            {
                succeeded &= visible
                    ? source.ApplyVisible()
                    : source.ApplyHidden();
            }
            catch (Exception exception) when (
                exception is Win32Exception
                    or ArgumentException
                    or InvalidOperationException
                    or PlatformNotSupportedException
                    or OverflowException)
            {
                succeeded = false;
            }
        }
        return succeeded;
    }

    private bool TryCreatePassiveInteractionEvidenceUnsafe(
        out ProductDesktopInteractionEvidence evidence)
    {
        evidence = default!;
        if (snapshot.Status != ProductDesktopHostLifecycleStatus.ReadyReadOnly
            || currentBatch is null
            || interactionSurface is null
            || interactionDevelopment?.Snapshot is not
            { IsDevelopmentInteractionAvailable: true, Surface: not null }
                interaction)
        {
            return false;
        }

        ProductDesktopInteractionSurfaceCapture capture;
        try
        {
            capture = interactionSurface.Capture();
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            return false;
        }

        if (!capture.Succeeded
            || capture.Evidence?.IsPassiveContract != true
            || capture.Evidence.WindowRegistryGeneration
                != interaction.Surface.WindowRegistryGeneration)
        {
            return false;
        }

        evidence = CreateInteractionEvidence(
            currentBatch,
            capture.Evidence.WindowRegistryGeneration);
        return true;
    }

    private ProductDesktopHostLifecycleSnapshot UpdateSnapshotUnsafe(
        ProductDesktopHostLifecycleStatus status,
        bool connected,
        int ownedWindowCount,
        long workspaceRevision = 0,
        long topologyGeneration = 0,
        int renderedContainerCount = 0,
        bool readOnlyAccessibilityAvailable = false,
        bool passiveWindowContractAttested = false,
        ProductDesktopInteractionSystemSurfaceEventKind?
            lastSystemSurfaceEvent = null)
    {
        ProductDesktopSelectionSnapshot? selection =
            intentConsumption?.Snapshot.IsExplicit == true
                ? intentConsumption.Snapshot.Transaction?.Selection
                : null;
        snapshot = new(
            status,
            checked(snapshot.Generation + 1),
            connected,
            ownedWindowCount,
            workspaceRevision,
            topologyGeneration,
            renderedContainerCount,
            readOnlyAccessibilityAvailable,
            passiveWindowContractAttested,
            lastSystemSurfaceEvent,
            ExplicitInteractionActive: selection is not null,
            SelectedItemCount: selection?.SelectedItemIds.Count ?? 0,
            FocusedItemAvailable: selection?.FocusedItemId is not null,
            SelectionRevision: selection?.SelectionRevision ?? 0);
        return snapshot;
    }

    private ProductDesktopHostLifecycleSnapshot?
        RefreshInteractionObservationUnsafe()
    {
        ProductDesktopSelectionSnapshot? selection =
            intentConsumption?.Snapshot.IsExplicit == true
                ? intentConsumption.Snapshot.Transaction?.Selection
                : null;
        bool explicitInteractionActive = selection is not null;
        int selectedItemCount = selection?.SelectedItemIds.Count ?? 0;
        bool focusedItemAvailable = selection?.FocusedItemId is not null;
        long selectionRevision = selection?.SelectionRevision ?? 0;
        if (snapshot.ExplicitInteractionActive == explicitInteractionActive
            && snapshot.SelectedItemCount == selectedItemCount
            && snapshot.FocusedItemAvailable == focusedItemAvailable
            && snapshot.SelectionRevision == selectionRevision)
        {
            return null;
        }

        snapshot = snapshot with
        {
            Generation = checked(snapshot.Generation + 1),
            ExplicitInteractionActive = explicitInteractionActive,
            SelectedItemCount = selectedItemCount,
            FocusedItemAvailable = focusedItemAvailable,
            SelectionRevision = selectionRevision,
        };
        return snapshot;
    }

    private static ProductDesktopInteractionEvidence CreateInteractionEvidence(
        ProductDesktopHostProjectionBatch batch,
        long registryGeneration) =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            batch.WorkspaceRevision,
            batch.TopologyGeneration,
            registryGeneration,
            AvailableContainerIds: new HashSet<string>(
                batch.Displays.SelectMany(display => display.Containers)
                    .Select(container => container.ContainerId),
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(
                batch.Displays.SelectMany(display => display.Containers)
                    .Where(container => container.IsLocked)
                    .Select(container => container.ContainerId),
                StringComparer.Ordinal));

    private void Publish(ProductDesktopHostLifecycleSnapshot value) =>
        SnapshotChanged?.Invoke(this, value);

    private static nint NextInstanceMarker()
    {
        long value = Interlocked.Increment(ref nextInstanceMarker);
        if (value == 0)
        {
            value = Interlocked.Increment(ref nextInstanceMarker);
        }

        return checked((nint)value);
    }

    private static bool ProjectionsEqual(
        ProductDesktopHostReadOnlyProjection left,
        ProductDesktopHostReadOnlyProjection right) =>
        left.ContainerId == right.ContainerId
        && left.Title == right.Title
        && left.Color == right.Color
        && left.Opacity.Equals(right.Opacity)
        && left.IsCollapsed == right.IsCollapsed
        && left.XDip.Equals(right.XDip)
        && left.YDip.Equals(right.YDip)
        && left.WidthDip.Equals(right.WidthDip)
        && left.HeightDip.Equals(right.HeightDip)
        && left.ItemIds.SequenceEqual(
            right.ItemIds,
            StringComparer.Ordinal)
        && left.ItemNames.SequenceEqual(
            right.ItemNames,
            StringComparer.Ordinal);

    private static bool BatchesEqual(
        ProductDesktopHostProjectionBatch left,
        ProductDesktopHostProjectionBatch right) =>
        left.WorkspaceRevision == right.WorkspaceRevision
        && left.TopologyGeneration == right.TopologyGeneration
        && left.TopologyFingerprint == right.TopologyFingerprint
        && left.Displays.Count == right.Displays.Count
        && left.Displays.Zip(right.Displays).All(pair =>
            pair.First.DisplayId == pair.Second.DisplayId
            && pair.First.WorkArea == pair.Second.WorkArea
            && pair.First.EffectiveDpi == pair.Second.EffectiveDpi
            && pair.First.Containers.Count == pair.Second.Containers.Count
            && pair.First.Containers.Zip(pair.Second.Containers).All(container =>
                ProjectionsEqual(container.First, container.Second)));

    private static bool UpdatesEqual(
        ProductDesktopHostProjectionUpdate? left,
        ProductDesktopHostProjectionUpdate right) =>
        left is not null
        && left.WorkspaceRevision == right.WorkspaceRevision
        && left.TopologyGeneration == right.TopologyGeneration
        && left.Disposition == right.Disposition
        && (left.Batch is null
            ? right.Batch is null
            : right.Batch is not null && BatchesEqual(left.Batch, right.Batch));

    private static string DisplayRegistrationId(string displayId) =>
        $"display:{displayId}";
}
