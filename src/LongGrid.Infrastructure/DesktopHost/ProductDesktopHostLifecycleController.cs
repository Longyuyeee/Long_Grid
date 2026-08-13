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
    ProductDesktopInteractionSystemSurfaceEventKind? LastSystemSurfaceEvent = null)
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
        bool isLocked)
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
        bool isLocked = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(itemNames);
        string[] visibleItems = itemNames
            .Take(MaximumVisibleItems)
            .ToArray();
        if (visibleItems.Any(string.IsNullOrWhiteSpace)
            || containerId.Length > ProductConfigurationLimits.MaximumIdLength
            || title.Length > ProductConfigurationLimits.MaximumNameLength
            || visibleItems.Any(item => item.Length > MaximumVisibleNameLength)
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
            isLocked);
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

    bool HiddenWindowContractAttested { get; }

    bool ApplyPassive();

    bool ApplyHidden();
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
    private readonly Guid hostInstanceId = Guid.NewGuid();
    private ProductDesktopHostLifecycleSnapshot snapshot;
    private readonly List<IProductDesktopHostReadOnlySurface> surfaces = [];
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
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostReadOnlySurfaceFactory()
                : null,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostWindowInspector()
                : null,
            interactionDevelopment,
            intentPreparation)
    {
    }

    internal ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory,
        IProductDesktopHostWindowInspector? windowInspector,
        ProductDesktopInteractionDevelopmentController?
            interactionDevelopment = null,
        ProductDesktopInteractionIntentPreparationBridge?
            intentPreparation = null)
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
                _ = intentPreparation?.Invalidate();
                if (interactionSurface is null)
                {
                    return snapshot;
                }

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

            if (snapshot.Status
                    != ProductDesktopHostLifecycleStatus.ReadyReadOnly
                || currentBatch is null
                || interactionSurface is null
                || interactionDevelopment?.Snapshot is not
                { IsDevelopmentInteractionAvailable: true, Surface: not null }
                    interaction)
            {
                ProductDesktopInteractionIntentPreparationSnapshot awaiting =
                    intentPreparation.AwaitPassiveSurface();
                return new(awaiting, PreparedIntent: null);
            }

            ProductDesktopInteractionEvidence evidence =
                CreateInteractionEvidence(
                    currentBatch,
                    interaction.Surface.WindowRegistryGeneration);
            return intentPreparation.Prepare(
                request,
                currentBatch,
                evidence,
                nowUtc);
        }
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
            _ = intentPreparation?.Complete();
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
                if (!interactionAttached)
                {
                    ReleaseSurfaceUnsafe();
                    return UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
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
        _ = intentPreparation?.Invalidate();
        if (interactionSurface is not null)
        {
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
            lastSystemSurfaceEvent);
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
