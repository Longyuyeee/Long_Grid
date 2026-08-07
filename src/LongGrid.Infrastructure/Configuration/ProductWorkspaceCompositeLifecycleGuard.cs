using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Infrastructure.Configuration;

internal enum ProductWorkspaceCompositeLifecycleStatus
{
    Ready,
    TopologyChanged,
    DesktopHostChanged,
    ShuttingDown,
    Disposed,
}

internal sealed class ProductWorkspaceCompositeLifecycleGuard
    : IProductWorkspaceCompositeBindingExchange, IDisposable
{
    private readonly object sync = new();
    private readonly ProductDisplayTopologyController topology;
    private readonly ProductDesktopHostWindowBridge windows;
    private ProductWorkspaceWindowCompositeBinding current;
    private ProductWorkspaceCompositeLifecycleStatus status =
        ProductWorkspaceCompositeLifecycleStatus.Ready;

    internal ProductWorkspaceCompositeLifecycleGuard(
        ProductWorkspaceWindowCompositeBinding initial,
        ProductDisplayTopologyController topology,
        ProductDesktopHostWindowBridge windows)
    {
        if (!ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(initial))
        {
            throw new ArgumentException(
                "The initial composite binding is invalid.",
                nameof(initial));
        }

        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(windows);
        current = initial;
        this.topology = topology;
        this.windows = windows;
        topology.SnapshotChanged += OnTopologyChanged;
        windows.SnapshotChanged += OnWindowsChanged;
        ObserveTopology(topology.Snapshot);
        ObserveWindows(windows.Snapshot);
        if (Status != ProductWorkspaceCompositeLifecycleStatus.Ready)
        {
            Dispose();
            throw new ArgumentException(
                "The initial binding does not match authoritative lifecycle evidence.",
                nameof(initial));
        }
    }

    internal ProductWorkspaceCompositeLifecycleStatus Status
    {
        get
        {
            lock (sync)
            {
                return status;
            }
        }
    }

    internal ProductWorkspaceWindowCompositeBinding Current
    {
        get
        {
            lock (sync)
            {
                if (status != ProductWorkspaceCompositeLifecycleStatus.Ready)
                {
                    throw new InvalidOperationException(
                        "The composite lifecycle binding is no longer current.");
                }

                return current;
            }
        }
    }

    public bool Matches(ProductWorkspaceWindowCompositeBinding expected)
    {
        lock (sync)
        {
            return status == ProductWorkspaceCompositeLifecycleStatus.Ready
                && current == expected;
        }
    }

    public bool TryExchange(
        ProductWorkspaceWindowCompositeBinding expected,
        ProductWorkspaceWindowCompositeBinding replacement)
    {
        if (!ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(expected)
            || !ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(
                replacement)
            || !HasSameLifecycleIdentity(expected, replacement))
        {
            return false;
        }

        lock (sync)
        {
            if (status != ProductWorkspaceCompositeLifecycleStatus.Ready
                || current != expected)
            {
                return false;
            }

            current = replacement;
            return true;
        }
    }

    internal void BeginShutdown()
    {
        lock (sync)
        {
            if (status != ProductWorkspaceCompositeLifecycleStatus.Disposed)
            {
                status = ProductWorkspaceCompositeLifecycleStatus.ShuttingDown;
            }
        }
    }

    public void Dispose()
    {
        topology.SnapshotChanged -= OnTopologyChanged;
        windows.SnapshotChanged -= OnWindowsChanged;
        lock (sync)
        {
            status = ProductWorkspaceCompositeLifecycleStatus.Disposed;
        }

        GC.SuppressFinalize(this);
    }

    private void OnTopologyChanged(
        object? sender,
        ProductDisplayTopologySnapshot snapshot) =>
        ObserveTopology(snapshot);

    private void OnWindowsChanged(
        object? sender,
        ProductDesktopHostWindowSnapshot snapshot) =>
        ObserveWindows(snapshot);

    private void ObserveTopology(ProductDisplayTopologySnapshot snapshot)
    {
        lock (sync)
        {
            if (status == ProductWorkspaceCompositeLifecycleStatus.Ready
                && (!snapshot.IsAuthoritative
                    || snapshot.Generation != current.TopologyGeneration))
            {
                status = ProductWorkspaceCompositeLifecycleStatus.TopologyChanged;
            }
        }
    }

    private void ObserveWindows(ProductDesktopHostWindowSnapshot snapshot)
    {
        lock (sync)
        {
            if (status == ProductWorkspaceCompositeLifecycleStatus.Ready
                && (!snapshot.OwnershipAttested
                    || snapshot.Generation != current.WindowRegistryGeneration))
            {
                status = ProductWorkspaceCompositeLifecycleStatus
                    .DesktopHostChanged;
            }
        }
    }

    private static bool HasSameLifecycleIdentity(
        ProductWorkspaceWindowCompositeBinding expected,
        ProductWorkspaceWindowCompositeBinding replacement) =>
        expected.TopologyGeneration == replacement.TopologyGeneration
        && expected.WindowRegistryGeneration
            == replacement.WindowRegistryGeneration
        && expected.DesktopHostInstanceId == replacement.DesktopHostInstanceId
        && expected.DesktopHostGeneration == replacement.DesktopHostGeneration;
}
