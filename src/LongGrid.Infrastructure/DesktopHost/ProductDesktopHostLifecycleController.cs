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
    int RenderedContainerCount = 0)
{
    public bool FeatureEnabled =>
        Status is ProductDesktopHostLifecycleStatus.AwaitingHost
            or ProductDesktopHostLifecycleStatus.AwaitingWorkspace
            or ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology
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
        double heightDip)
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
        double heightDip)
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
            heightDip);
    }
}

internal interface IProductDesktopHostReadOnlySurface : IDisposable
{
    nint Handle { get; }

    nint InstanceMarker { get; }

    uint ProcessId { get; }

    uint ThreadId { get; }
}

internal interface IProductDesktopHostReadOnlySurfaceFactory
{
    IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker);
}

public sealed class ProductDesktopHostLifecycleController : IAsyncDisposable
{
    private static long nextInstanceMarker;
    private readonly object gate = new();
    private readonly bool enabled;
    private readonly IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory;
    private readonly ProductDesktopHostWindowBridge? windowBridge;
    private readonly Guid hostInstanceId = Guid.NewGuid();
    private ProductDesktopHostLifecycleSnapshot snapshot;
    private readonly List<IProductDesktopHostReadOnlySurface> surfaces = [];
    private readonly List<(string DisplayId, long WindowGeneration)>
        registrations = [];
    private ProductDesktopHostProjectionBatch? currentBatch;
    private ProductDesktopHostProjectionUpdate? currentUpdate;
    private long lastWorkspaceRevision = -1;
    private long lastTopologyGeneration = -1;
    private long windowGeneration;
    private bool disposed;

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision)
        : this(
            featureDecision,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostReadOnlySurfaceFactory()
                : null,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostWindowInspector()
                : null)
    {
    }

    internal ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory,
        IProductDesktopHostWindowInspector? windowInspector)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        if (enabled && (surfaceFactory is null || windowInspector is null))
        {
            throw new ArgumentException(
                "Enabled DesktopHost lifecycle requires finite native adapters.");
        }

        this.surfaceFactory = surfaceFactory;
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
            ProductDesktopHostIdentity? identity = null;
            ProductDesktopHostWindowRegistrationResult? registration = null;
            foreach (ProductDesktopHostDisplayProjection display in batch.Displays)
            {
                IProductDesktopHostReadOnlySurface created =
                    surfaceFactory!.Create(display, NextInstanceMarker());
                surfaces.Add(created);
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
            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.ReadyReadOnly,
                connected: true,
                ownedWindowCount: registration!.Snapshot.VerifiedWindowCount,
                workspaceRevision: batch.WorkspaceRevision,
                topologyGeneration: batch.TopologyGeneration,
                renderedContainerCount: batch.ContainerCount);
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
        int renderedContainerCount = 0)
    {
        snapshot = new(
            status,
            checked(snapshot.Generation + 1),
            connected,
            ownedWindowCount,
            workspaceRevision,
            topologyGeneration,
            renderedContainerCount);
        return snapshot;
    }

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
