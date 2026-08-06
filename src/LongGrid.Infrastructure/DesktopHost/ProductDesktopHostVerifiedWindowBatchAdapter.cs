using System.Runtime.InteropServices;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopHostWindowMutation(
    string ContainerId,
    nint Handle,
    PixelRect Bounds);

internal interface IProductDesktopHostWindowBatchMutator
{
    bool Apply(IReadOnlyList<ProductDesktopHostWindowMutation> windows);
}

internal interface IWindowsDeferredWindowPositionApi
{
    bool IsSupported { get; }

    nint Begin(int windowCount);

    nint Defer(
        nint deferredWindowPosition,
        nint window,
        PixelRect bounds,
        uint flags);

    bool End(nint deferredWindowPosition);
}

internal sealed class ProductDesktopHostVerifiedWindowBatchAdapter
    : IProductWorkspaceCompositeWindowLayer
{
    private sealed class WindowSnapshot(
        long registryGeneration,
        IReadOnlyDictionary<string, PixelRect> bounds)
        : IProductWorkspaceWindowCompositeSnapshot
    {
        private bool disposed;

        internal long RegistryGeneration { get; } = registryGeneration;

        internal IReadOnlyDictionary<string, PixelRect> Bounds { get; } = bounds;

        internal bool IsDisposed => disposed;

        public void Dispose() => disposed = true;
    }

    private readonly ProductDesktopHostWindowBridge bridge;
    private readonly IProductDesktopHostWindowBatchMutator mutator;

    internal ProductDesktopHostVerifiedWindowBatchAdapter(
        ProductDesktopHostWindowBridge bridge,
        IProductDesktopHostWindowBatchMutator mutator)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(mutator);
        this.bridge = bridge;
        this.mutator = mutator;
    }

    public ProductWorkspaceWindowCompositeCapture Capture(
        IReadOnlyList<string> containerIds,
        long registryGeneration)
    {
        if (!IsValidContainerSet(containerIds))
        {
            return ProductWorkspaceWindowCompositeCapture.Failed;
        }

        IReadOnlyDictionary<string, PixelRect>? captured = null;
        bool succeeded = bridge.TryUseExactVerifiedWindows(
            containerIds,
            registryGeneration,
            windows =>
            {
                captured = windows.ToDictionary(
                    window => window.ContainerId,
                    window => window.Bounds,
                    StringComparer.Ordinal);
                return true;
            });
        return succeeded && captured is not null
            ? new(true, new WindowSnapshot(registryGeneration, captured))
            : ProductWorkspaceWindowCompositeCapture.Failed;
    }

    public bool Apply(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
        long registryGeneration)
    {
        if (!TryNormalizePlacements(placements, out var normalized))
        {
            return false;
        }

        return bridge.TryUseExactVerifiedWindows(
            normalized.Select(placement => placement.ContainerId).ToArray(),
            registryGeneration,
            windows =>
            {
                var handles = windows.ToDictionary(
                    window => window.ContainerId,
                    window => window.Handle,
                    StringComparer.Ordinal);
                ProductDesktopHostWindowMutation[] mutations = normalized
                    .Select(placement => new ProductDesktopHostWindowMutation(
                        placement.ContainerId,
                        handles[placement.ContainerId],
                        placement.Bounds))
                    .ToArray();
                return mutator.Apply(mutations);
            });
    }

    public bool Verify(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
        long registryGeneration)
    {
        if (!TryNormalizePlacements(placements, out var normalized))
        {
            return false;
        }

        return bridge.TryUseExactVerifiedWindows(
            normalized.Select(placement => placement.ContainerId).ToArray(),
            registryGeneration,
            windows => windows.Count == normalized.Length
                && normalized.All(placement => windows.Any(window =>
                    string.Equals(
                        window.ContainerId,
                        placement.ContainerId,
                        StringComparison.Ordinal)
                    && window.Bounds == placement.Bounds)));
    }

    public bool Restore(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        long registryGeneration) =>
        TryReadSnapshot(snapshot, registryGeneration, out var placements)
        && Apply(placements, registryGeneration);

    public bool VerifyRestored(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        long registryGeneration) =>
        TryReadSnapshot(snapshot, registryGeneration, out var placements)
        && Verify(placements, registryGeneration);

    private static bool TryReadSnapshot(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        long registryGeneration,
        out LayoutRecoveryWindowPlacement[] placements)
    {
        placements = Array.Empty<LayoutRecoveryWindowPlacement>();
        if (snapshot is not WindowSnapshot windows
            || windows.IsDisposed
            || registryGeneration <= 0
            || windows.RegistryGeneration != registryGeneration
            || windows.Bounds.Count == 0)
        {
            return false;
        }

        placements = windows.Bounds
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new LayoutRecoveryWindowPlacement(pair.Key, pair.Value))
            .ToArray();
        return TryNormalizePlacements(placements, out placements);
    }

    private static bool IsValidContainerSet(IReadOnlyList<string>? containerIds) =>
        containerIds is not null
        && containerIds.Count > 0
        && containerIds.All(value => !string.IsNullOrWhiteSpace(value))
        && containerIds.Distinct(StringComparer.Ordinal).Count()
            == containerIds.Count;

    private static bool TryNormalizePlacements(
        IReadOnlyList<LayoutRecoveryWindowPlacement>? placements,
        out LayoutRecoveryWindowPlacement[] normalized)
    {
        normalized = Array.Empty<LayoutRecoveryWindowPlacement>();
        if (placements is null
            || placements.Count == 0
            || placements.Any(placement =>
                placement is null
                || string.IsNullOrWhiteSpace(placement.ContainerId)
                || !placement.Bounds.HasArea)
            || placements.Select(placement => placement.ContainerId)
                .Distinct(StringComparer.Ordinal).Count() != placements.Count)
        {
            return false;
        }

        normalized = placements
            .OrderBy(placement => placement.ContainerId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }
}

internal sealed class WindowsProductDesktopHostWindowBatchMutator
    : IProductDesktopHostWindowBatchMutator
{
    private const uint NoActivate = 0x0010;
    private const uint NoZOrder = 0x0004;
    private const uint NoOwnerZOrder = 0x0200;
    private const uint NoSendChanging = 0x0400;
    private readonly IWindowsDeferredWindowPositionApi api;

    internal WindowsProductDesktopHostWindowBatchMutator()
        : this(new WindowsDeferredWindowPositionApi())
    {
    }

    internal WindowsProductDesktopHostWindowBatchMutator(
        IWindowsDeferredWindowPositionApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    public bool Apply(IReadOnlyList<ProductDesktopHostWindowMutation> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (!api.IsSupported
            || windows.Count == 0
            || windows.Any(window =>
                window.Handle == nint.Zero || !window.Bounds.HasArea))
        {
            return false;
        }

        nint deferred = api.Begin(windows.Count);
        if (deferred == nint.Zero)
        {
            return false;
        }

        foreach (ProductDesktopHostWindowMutation window in windows)
        {
            deferred = api.Defer(
                deferred,
                window.Handle,
                window.Bounds,
                NoActivate | NoZOrder | NoOwnerZOrder | NoSendChanging);
            if (deferred == nint.Zero)
            {
                return false;
            }
        }

        return api.End(deferred);
    }
}

internal sealed class WindowsDeferredWindowPositionApi
    : IWindowsDeferredWindowPositionApi
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public nint Begin(int windowCount) =>
        NativeMethods.BeginDeferWindowPos(windowCount);

    public nint Defer(
        nint deferredWindowPosition,
        nint window,
        PixelRect bounds,
        uint flags) =>
        NativeMethods.DeferWindowPos(
            deferredWindowPosition,
            window,
            nint.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            flags);

    public bool End(nint deferredWindowPosition) =>
        NativeMethods.EndDeferWindowPos(deferredWindowPosition);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint BeginDeferWindowPos(int windowCount);

        [DllImport("user32.dll")]
        internal static extern nint DeferWindowPos(
            nint deferredWindowPosition,
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndDeferWindowPos(
            nint deferredWindowPosition);
    }
}
