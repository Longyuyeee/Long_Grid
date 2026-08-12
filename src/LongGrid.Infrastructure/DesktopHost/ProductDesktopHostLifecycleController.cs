using System.ComponentModel;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopHostLifecycleStatus
{
    DisabledBySafetyPolicy,
    AwaitingHost,
    ReadyReadOnly,
    Faulted,
    Completed,
}

public sealed record ProductDesktopHostLifecycleSnapshot(
    ProductDesktopHostLifecycleStatus Status,
    long Generation,
    bool NativeHostConnected,
    int OwnedWindowCount)
{
    public bool FeatureEnabled =>
        Status is ProductDesktopHostLifecycleStatus.AwaitingHost
            or ProductDesktopHostLifecycleStatus.ReadyReadOnly
            or ProductDesktopHostLifecycleStatus.Faulted;
}

public sealed record ProductDesktopHostReadOnlyProjection
{
    public const int MaximumVisibleItems = 12;

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
        ProductDesktopHostReadOnlyProjection projection,
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
    private IProductDesktopHostReadOnlySurface? surface;
    private ProductDesktopHostReadOnlyProjection? currentProjection;
    private string? registeredContainerId;
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

    public ProductDesktopHostLifecycleSnapshot ApplyProjection(
        ProductDesktopHostReadOnlyProjection? projection)
    {
        ProductDesktopHostLifecycleSnapshot published;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                return snapshot;
            }

            if (projection is not null
                && currentProjection is not null
                && ProjectionsEqual(currentProjection, projection))
            {
                return snapshot;
            }

            if (projection is null && surface is null)
            {
                return snapshot;
            }

            ReleaseSurfaceUnsafe();
            if (projection is null)
            {
                published = UpdateSnapshotUnsafe(
                    ProductDesktopHostLifecycleStatus.AwaitingHost,
                    connected: false,
                    ownedWindowCount: 0);
            }
            else
            {
                published = CreateSurfaceUnsafe(projection);
            }
        }

        Publish(published);
        return published;
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

    private ProductDesktopHostLifecycleSnapshot CreateSurfaceUnsafe(
        ProductDesktopHostReadOnlyProjection projection)
    {
        try
        {
            nint marker = NextInstanceMarker();
            IProductDesktopHostReadOnlySurface created =
                surfaceFactory!.Create(projection, marker);
            surface = created;
            var identity = new ProductDesktopHostIdentity(
                hostInstanceId,
                1,
                created.ProcessId,
                created.ThreadId);
            windowBridge!.Connect(identity);
            long nextWindowGeneration = checked(windowGeneration + 1);
            ProductDesktopHostWindowRegistrationResult registration =
                windowBridge.Register(
                    new(
                        projection.ContainerId,
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

            currentProjection = projection;
            registeredContainerId = projection.ContainerId;
            windowGeneration = nextWindowGeneration;
            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.ReadyReadOnly,
                connected: true,
                ownedWindowCount: registration.Snapshot.VerifiedWindowCount);
        }
        catch (Exception exception) when (
            exception is Win32Exception
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
        if (registeredContainerId is not null && windowBridge is not null)
        {
            _ = windowBridge.Unregister(
                registeredContainerId,
                windowGeneration);
        }

        surface?.Dispose();
        surface = null;
        currentProjection = null;
        registeredContainerId = null;
        windowBridge?.Disconnect(hostInstanceId);
    }

    private ProductDesktopHostLifecycleSnapshot UpdateSnapshotUnsafe(
        ProductDesktopHostLifecycleStatus status,
        bool connected,
        int ownedWindowCount)
    {
        snapshot = new(
            status,
            checked(snapshot.Generation + 1),
            connected,
            ownedWindowCount);
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
}
