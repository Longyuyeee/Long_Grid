using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Infrastructure.Configuration;

internal enum ProductWorkspaceCompositeInputShutdownStatus
{
    Hidden,
    AlreadyHidden,
    DrainTimedOut,
    HideFailed,
}

internal sealed record ProductWorkspaceCompositeInputShutdownResult(
    ProductWorkspaceCompositeInputShutdownStatus Status,
    bool InputOperationDrained,
    bool HostsHidden)
{
    internal bool IsComplete =>
        Status is ProductWorkspaceCompositeInputShutdownStatus.Hidden
            or ProductWorkspaceCompositeInputShutdownStatus.AlreadyHidden;
}

internal sealed class ProductWorkspaceCompositeDesktopHostInputGate
    : IProductWorkspaceCompositeInputGate, IDisposable
{
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(5);
    private readonly object sync = new();
    private readonly ManualResetEventSlim idle = new(initialState: true);
    private readonly ProductDesktopHostWindowBridge bridge;
    private readonly IProductDesktopHostInputController controller;
    private readonly IProductDesktopHostThreadDispatcher dispatcher;
    private readonly ProductWorkspaceCompositeLifecycleGuard lifecycle;
    private readonly string[] containerIds;
    private readonly long registryGeneration;
    private readonly TimeSpan queueTimeout;
    private bool accepting = true;
    private bool operationActive;
    private bool inputClosed;
    private bool hostsHidden;
    private bool disposed;

    internal ProductWorkspaceCompositeDesktopHostInputGate(
        ProductDesktopHostWindowBridge bridge,
        IProductDesktopHostInputController controller,
        IProductDesktopHostThreadDispatcher dispatcher,
        ProductWorkspaceCompositeLifecycleGuard lifecycle,
        IReadOnlyList<string> containerIds,
        long registryGeneration,
        TimeSpan? queueTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(containerIds);
        this.queueTimeout = queueTimeout ?? TimeSpan.FromMilliseconds(500);
        if (this.queueTimeout < TimeSpan.FromMilliseconds(1)
            || this.queueTimeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queueTimeout),
                "The queue timeout must be between 1 millisecond and 5 seconds.");
        }

        string[] normalized = containerIds
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (registryGeneration <= 0
            || normalized.Length == 0
            || normalized.Any(string.IsNullOrWhiteSpace)
            || normalized.Distinct(StringComparer.Ordinal).Count()
                != normalized.Length
            || lifecycle.Status
                != ProductWorkspaceCompositeLifecycleStatus.Ready
            || !bridge.TryPrepareExactVerifiedWindows(
                normalized,
                registryGeneration,
                out ProductDesktopHostPreparedWindowBatch? prepared)
            || prepared!.HostThreadId != dispatcher.TargetThreadId)
        {
            throw new ArgumentException(
                "The input gate requires the exact current product-owned DesktopHost registry.",
                nameof(containerIds));
        }

        this.bridge = bridge;
        this.controller = controller;
        this.dispatcher = dispatcher;
        this.lifecycle = lifecycle;
        this.containerIds = normalized;
        this.registryGeneration = registryGeneration;
    }

    internal bool InputClosed
    {
        get
        {
            lock (sync)
            {
                return inputClosed;
            }
        }
    }

    internal bool HostsHidden
    {
        get
        {
            lock (sync)
            {
                return hostsHidden;
            }
        }
    }

    public bool Close() => RunNormal(
        () => Execute(windows => controller.SetEnabled(windows, enabled: false)),
        canStart: () => !inputClosed && !hostsHidden,
        onSuccess: () => inputClosed = true);

    public bool Reopen() => RunNormal(
        () => lifecycle.Status == ProductWorkspaceCompositeLifecycleStatus.Ready
            && Execute(windows =>
                lifecycle.Status
                    == ProductWorkspaceCompositeLifecycleStatus.Ready
                && controller.SetEnabled(windows, enabled: true)),
        canStart: () => inputClosed
            && !hostsHidden
            && lifecycle.Status == ProductWorkspaceCompositeLifecycleStatus.Ready,
        onSuccess: () => inputClosed = false);

    public bool HideAffectedHosts() => RunNormal(
        () => Execute(controller.Hide),
        canStart: () => inputClosed && !hostsHidden,
        onSuccess: () => hostsHidden = true);

    internal ProductWorkspaceCompositeInputShutdownResult ShutdownAndHide(
        TimeSpan drainTimeout)
    {
        if (drainTimeout < TimeSpan.FromMilliseconds(1)
            || drainTimeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainTimeout),
                "The drain timeout must be between 1 millisecond and 5 seconds.");
        }

        lock (sync)
        {
            ThrowIfDisposed();
            lifecycle.BeginShutdown();
            accepting = false;
            if (hostsHidden)
            {
                return new(
                    ProductWorkspaceCompositeInputShutdownStatus.AlreadyHidden,
                    InputOperationDrained: !operationActive,
                    HostsHidden: true);
            }
        }

        if (!idle.Wait(drainTimeout))
        {
            return new(
                ProductWorkspaceCompositeInputShutdownStatus.DrainTimedOut,
                InputOperationDrained: false,
                HostsHidden: false);
        }

        lock (sync)
        {
            if (hostsHidden)
            {
                return new(
                    ProductWorkspaceCompositeInputShutdownStatus.AlreadyHidden,
                    InputOperationDrained: true,
                    HostsHidden: true);
            }

            if (operationActive)
            {
                return new(
                    ProductWorkspaceCompositeInputShutdownStatus.DrainTimedOut,
                    InputOperationDrained: false,
                    HostsHidden: false);
            }

            operationActive = true;
            idle.Reset();
        }

        bool hidden = false;
        try
        {
            hidden = Execute(controller.Hide);
            return new(
                hidden
                    ? ProductWorkspaceCompositeInputShutdownStatus.Hidden
                    : ProductWorkspaceCompositeInputShutdownStatus.HideFailed,
                InputOperationDrained: true,
                HostsHidden: hidden);
        }
        finally
        {
            lock (sync)
            {
                if (hidden)
                {
                    hostsHidden = true;
                }

                operationActive = false;
                idle.Set();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            if (operationActive
                || (!accepting && !hostsHidden))
            {
                throw new InvalidOperationException(
                    "The input gate cannot be disposed before shutdown operations drain and affected hosts are hidden.");
            }

            disposed = true;
            accepting = false;
        }

        idle.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool RunNormal(
        Func<bool> operation,
        Func<bool> canStart,
        Action onSuccess)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (!accepting || operationActive || !canStart())
            {
                return false;
            }

            operationActive = true;
            idle.Reset();
        }

        bool succeeded = false;
        try
        {
            succeeded = operation();
            return succeeded;
        }
        finally
        {
            lock (sync)
            {
                if (succeeded)
                {
                    onSuccess();
                }

                operationActive = false;
                idle.Set();
            }
        }
    }

    private bool Execute(Func<IReadOnlyList<nint>, bool> operation)
    {
        if (!bridge.TryPrepareExactVerifiedWindows(
                containerIds,
                registryGeneration,
                out ProductDesktopHostPreparedWindowBatch? prepared)
            || prepared!.HostThreadId != dispatcher.TargetThreadId)
        {
            return false;
        }

        ProductDesktopHostDispatchResult result = dispatcher.Invoke(
            () => bridge.TryUsePreparedVerifiedWindows(
                prepared,
                windows => operation(windows
                    .Select(window => window.Handle)
                    .ToArray())),
            queueTimeout);
        return result.IsSuccess;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
