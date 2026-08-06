using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopHostWindowStatus
{
    Disconnected,
    Empty,
    Ready,
    Degraded,
}

public sealed record ProductDesktopHostWindowSnapshot(
    ProductDesktopHostWindowStatus Status,
    long Generation,
    int RegisteredWindowCount,
    int VerifiedWindowCount,
    int RejectedOperationCount)
{
    public static ProductDesktopHostWindowSnapshot Initial { get; } = new(
        ProductDesktopHostWindowStatus.Disconnected,
        0,
        0,
        0,
        0);

    public bool OwnershipAttested =>
        Status is ProductDesktopHostWindowStatus.Empty
            or ProductDesktopHostWindowStatus.Ready
        && RegisteredWindowCount == VerifiedWindowCount;
}

internal sealed record ProductDesktopHostIdentity(
    Guid InstanceId,
    long Generation,
    uint ProcessId,
    uint ThreadId);

internal sealed record ProductDesktopHostWindowClaim(
    string ContainerId,
    ProductDesktopHostIdentity Host,
    long WindowGeneration,
    nint Handle,
    nint InstanceMarker);

internal sealed record ProductDesktopHostWindowObservation(
    bool Exists,
    uint ProcessId,
    uint ThreadId,
    nint InstanceMarker,
    PixelRect Bounds)
{
    public static ProductDesktopHostWindowObservation Missing { get; } = new(
        false,
        0,
        0,
        nint.Zero,
        default);
}

internal interface IProductDesktopHostWindowInspector
{
    ProductDesktopHostWindowObservation Inspect(nint handle);
}

internal enum ProductDesktopHostWindowRegistrationStatus
{
    Registered,
    Disconnected,
    InvalidClaim,
    HostMismatch,
    DuplicateContainer,
    DuplicateHandle,
    WindowUnavailable,
    OwnershipMismatch,
}

internal sealed record ProductDesktopHostWindowRegistrationResult(
    ProductDesktopHostWindowRegistrationStatus Status,
    ProductDesktopHostWindowSnapshot Snapshot)
{
    public bool IsRegistered =>
        Status == ProductDesktopHostWindowRegistrationStatus.Registered;
}

internal sealed record ProductDesktopHostOwnedWindowRecord(
    string ContainerId,
    Guid HostInstanceId,
    long HostGeneration,
    uint HostThreadId,
    long WindowGeneration,
    PixelRect LastObservedBounds,
    bool Verified);

internal sealed record ProductDesktopHostWindowEvidence(
    long Generation,
    IReadOnlyList<ProductDesktopHostOwnedWindowRecord> Windows,
    bool OwnershipAttested)
{
    public IReadOnlyList<string> RegisteredContainerIds =>
        Array.AsReadOnly(Windows.Select(window => window.ContainerId).ToArray());
}

public sealed class ProductDesktopHostWindowBridge
{
    private sealed record Entry(
        ProductDesktopHostWindowClaim Claim,
        PixelRect LastObservedBounds,
        bool Verified);

    private readonly object sync = new();
    private readonly IProductDesktopHostWindowInspector inspector;
    private readonly Dictionary<string, Entry> entries =
        new(StringComparer.Ordinal);
    private ProductDesktopHostIdentity? host;
    private ProductDesktopHostWindowSnapshot snapshot =
        ProductDesktopHostWindowSnapshot.Initial;
    private long generation;
    private int rejectedOperations;

    internal ProductDesktopHostWindowBridge(
        IProductDesktopHostWindowInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        this.inspector = inspector;
    }

    public event EventHandler<ProductDesktopHostWindowSnapshot>? SnapshotChanged;

    public ProductDesktopHostWindowSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    internal void Connect(ProductDesktopHostIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!IsValid(identity))
        {
            throw new ArgumentException(
                "DesktopHost identity must be finite and non-zero.",
                nameof(identity));
        }

        ProductDesktopHostWindowSnapshot published;
        lock (sync)
        {
            if (host == identity)
            {
                return;
            }

            host = identity;
            entries.Clear();
            rejectedOperations = 0;
            published = UpdateSnapshot(ProductDesktopHostWindowStatus.Empty);
        }

        Publish(published);
    }

    internal void Disconnect(Guid instanceId)
    {
        ProductDesktopHostWindowSnapshot? published = null;
        lock (sync)
        {
            if (host?.InstanceId != instanceId)
            {
                return;
            }

            host = null;
            entries.Clear();
            rejectedOperations = 0;
            published = UpdateSnapshot(
                ProductDesktopHostWindowStatus.Disconnected);
        }

        Publish(published);
    }

    internal ProductDesktopHostWindowRegistrationResult Register(
        ProductDesktopHostWindowClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ProductDesktopHostWindowRegistrationResult result;
        lock (sync)
        {
            ProductDesktopHostWindowRegistrationStatus rejection =
                ValidateClaim(claim);
            if (rejection != ProductDesktopHostWindowRegistrationStatus.Registered)
            {
                result = Reject(rejection);
            }
            else
            {
                ProductDesktopHostWindowObservation observation =
                    SafeInspect(claim.Handle);
                ProductDesktopHostWindowRegistrationStatus observed =
                    ValidateObservation(claim, observation);
                if (observed != ProductDesktopHostWindowRegistrationStatus.Registered)
                {
                    result = Reject(observed);
                }
                else
                {
                    entries.Add(
                        claim.ContainerId,
                        new(claim, observation.Bounds, true));
                    ProductDesktopHostWindowSnapshot next = UpdateSnapshot(
                        ProductDesktopHostWindowStatus.Ready);
                    result = new(
                        ProductDesktopHostWindowRegistrationStatus.Registered,
                        next);
                }
            }
        }

        Publish(result.Snapshot);
        return result;
    }

    internal ProductDesktopHostWindowSnapshot Refresh()
    {
        ProductDesktopHostWindowSnapshot published;
        lock (sync)
        {
            if (host is null)
            {
                return snapshot;
            }

            foreach ((string containerId, Entry entry) in entries.ToArray())
            {
                ProductDesktopHostWindowObservation observation =
                    SafeInspect(entry.Claim.Handle);
                bool verified = ValidateObservation(entry.Claim, observation)
                    == ProductDesktopHostWindowRegistrationStatus.Registered;
                entries[containerId] = entry with
                {
                    LastObservedBounds = verified
                        ? observation.Bounds
                        : entry.LastObservedBounds,
                    Verified = verified,
                };
            }

            ProductDesktopHostWindowStatus status = entries.Count == 0
                ? ProductDesktopHostWindowStatus.Empty
                : entries.Values.All(entry => entry.Verified)
                    ? ProductDesktopHostWindowStatus.Ready
                    : ProductDesktopHostWindowStatus.Degraded;
            published = UpdateSnapshot(status);
        }

        Publish(published);
        return published;
    }

    internal bool Unregister(
        string containerId,
        long windowGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ProductDesktopHostWindowSnapshot? published = null;
        lock (sync)
        {
            if (!entries.TryGetValue(containerId, out Entry? entry)
                || entry.Claim.WindowGeneration != windowGeneration)
            {
                return false;
            }

            entries.Remove(containerId);
            published = UpdateSnapshot(
                entries.Count == 0
                    ? ProductDesktopHostWindowStatus.Empty
                    : entries.Values.All(value => value.Verified)
                        ? ProductDesktopHostWindowStatus.Ready
                        : ProductDesktopHostWindowStatus.Degraded);
        }

        Publish(published);
        return true;
    }

    internal ProductDesktopHostWindowEvidence ReadEvidence()
    {
        lock (sync)
        {
            ProductDesktopHostOwnedWindowRecord[] windows = entries.Values
                .OrderBy(entry => entry.Claim.ContainerId, StringComparer.Ordinal)
                .Select(entry => new ProductDesktopHostOwnedWindowRecord(
                    entry.Claim.ContainerId,
                    entry.Claim.Host.InstanceId,
                    entry.Claim.Host.Generation,
                    entry.Claim.Host.ThreadId,
                    entry.Claim.WindowGeneration,
                    entry.LastObservedBounds,
                    entry.Verified))
                .ToArray();
            return new(
                snapshot.Generation,
                Array.AsReadOnly(windows),
                snapshot.OwnershipAttested);
        }
    }

    private ProductDesktopHostWindowRegistrationStatus ValidateClaim(
        ProductDesktopHostWindowClaim claim)
    {
        if (host is null)
        {
            return ProductDesktopHostWindowRegistrationStatus.Disconnected;
        }

        if (string.IsNullOrWhiteSpace(claim.ContainerId)
            || claim.WindowGeneration <= 0
            || claim.Handle == nint.Zero
            || claim.InstanceMarker == nint.Zero
            || !IsValid(claim.Host))
        {
            return ProductDesktopHostWindowRegistrationStatus.InvalidClaim;
        }

        if (claim.Host != host)
        {
            return ProductDesktopHostWindowRegistrationStatus.HostMismatch;
        }

        if (entries.ContainsKey(claim.ContainerId))
        {
            return ProductDesktopHostWindowRegistrationStatus.DuplicateContainer;
        }

        return entries.Values.Any(entry => entry.Claim.Handle == claim.Handle)
            ? ProductDesktopHostWindowRegistrationStatus.DuplicateHandle
            : ProductDesktopHostWindowRegistrationStatus.Registered;
    }

    private static ProductDesktopHostWindowRegistrationStatus ValidateObservation(
        ProductDesktopHostWindowClaim claim,
        ProductDesktopHostWindowObservation observation)
    {
        if (!observation.Exists || !observation.Bounds.HasArea)
        {
            return ProductDesktopHostWindowRegistrationStatus.WindowUnavailable;
        }

        return observation.ProcessId == claim.Host.ProcessId
            && observation.ThreadId == claim.Host.ThreadId
            && observation.InstanceMarker == claim.InstanceMarker
            ? ProductDesktopHostWindowRegistrationStatus.Registered
            : ProductDesktopHostWindowRegistrationStatus.OwnershipMismatch;
    }

    private ProductDesktopHostWindowObservation SafeInspect(nint handle)
    {
        try
        {
            return inspector.Inspect(handle)
                ?? ProductDesktopHostWindowObservation.Missing;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or OverflowException)
        {
            return ProductDesktopHostWindowObservation.Missing;
        }
    }

    private ProductDesktopHostWindowRegistrationResult Reject(
        ProductDesktopHostWindowRegistrationStatus status)
    {
        rejectedOperations = checked(rejectedOperations + 1);
        return new(
            status,
            UpdateSnapshot(ProductDesktopHostWindowStatus.Degraded));
    }

    private ProductDesktopHostWindowSnapshot UpdateSnapshot(
        ProductDesktopHostWindowStatus status)
    {
        generation = checked(generation + 1);
        snapshot = new(
            status,
            generation,
            entries.Count,
            entries.Values.Count(entry => entry.Verified),
            rejectedOperations);
        return snapshot;
    }

    private static bool IsValid(ProductDesktopHostIdentity identity) =>
        identity.InstanceId != Guid.Empty
        && identity.Generation > 0
        && identity.ProcessId > 0
        && identity.ThreadId > 0;

    private void Publish(ProductDesktopHostWindowSnapshot value) =>
        SnapshotChanged?.Invoke(this, value);
}
