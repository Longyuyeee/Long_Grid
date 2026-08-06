using System.ComponentModel;
using System.Security;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDisplayTopologyReadStatus
{
    Ready,
    Degraded,
    Unavailable,
    UnsupportedPlatform,
    Failed,
}

public sealed record ProductDisplayTopologyReadResult(
    ProductDisplayTopologyReadStatus Status,
    IReadOnlyList<DisplayTopologyNode> Displays,
    int ActivePathCount,
    int StableIdentityCount,
    int BufferAttempts)
{
    public bool IsAuthoritative => Status == ProductDisplayTopologyReadStatus.Ready;
}

public sealed record ProductDisplayTopologySampleMonitor(
    DisplayTopologyNode Display,
    bool HasStableTargetIdentity,
    bool MappedToActivePath,
    bool SourceBoundsMatch,
    bool TargetAvailable);

public sealed record ProductDisplayTopologySample(
    IReadOnlyList<ProductDisplayTopologySampleMonitor> Monitors,
    int ActivePathCount,
    int BufferAttempts);

public interface IProductDisplayTopologySource
{
    ProductDisplayTopologySample Read(CancellationToken cancellationToken = default);
}

public interface IProductDisplayTopologyReader
{
    Task<ProductDisplayTopologyReadResult> ReadAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ProductDisplayTopologyReader : IProductDisplayTopologyReader
{
    private readonly IProductDisplayTopologySource source;

    public ProductDisplayTopologyReader(IProductDisplayTopologySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
    }

    public static ProductDisplayTopologyReader CreateForCurrentSession() =>
        new(new WindowsDisplayTopologySource());

    public Task<ProductDisplayTopologyReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private ProductDisplayTopologyReadResult Read(
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProductDisplayTopologySample sample = source.Read(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (sample.Monitors.Count == 0)
            {
                return Empty(
                    ProductDisplayTopologyReadStatus.Unavailable,
                    sample.ActivePathCount,
                    sample.BufferAttempts);
            }

            DisplayTopologyNode[] displays = sample.Monitors
                .Select(monitor => monitor.Display)
                .ToArray();
            _ = DisplayTopologyFingerprint.Compute(displays);
            int stableIdentityCount = sample.Monitors.Count(monitor =>
                monitor.HasStableTargetIdentity);
            bool complete = sample.ActivePathCount == sample.Monitors.Count
                && sample.BufferAttempts is >= 1 and <= 8
                && sample.Monitors.All(monitor =>
                    monitor.HasStableTargetIdentity
                    && monitor.MappedToActivePath
                    && monitor.SourceBoundsMatch
                    && monitor.TargetAvailable
                    && monitor.Display.Rotation != DisplayRotation.Unknown
                    && WorkAreaIsInsideBounds(monitor.Display));
            return new(
                complete
                    ? ProductDisplayTopologyReadStatus.Ready
                    : ProductDisplayTopologyReadStatus.Degraded,
                Array.AsReadOnly(displays),
                sample.ActivePathCount,
                stableIdentityCount,
                sample.BufferAttempts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlatformNotSupportedException)
        {
            return Empty(ProductDisplayTopologyReadStatus.UnsupportedPlatform);
        }
        catch (Exception exception) when (
            exception is Win32Exception
            or IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return Empty(ProductDisplayTopologyReadStatus.Failed);
        }
    }

    private static bool WorkAreaIsInsideBounds(DisplayTopologyNode display) =>
        display.Bounds.Intersect(display.WorkArea) == display.WorkArea;

    private static ProductDisplayTopologyReadResult Empty(
        ProductDisplayTopologyReadStatus status,
        int activePathCount = 0,
        int bufferAttempts = 0) =>
        new(
            status,
            Array.Empty<DisplayTopologyNode>(),
            activePathCount,
            0,
            bufferAttempts);
}
