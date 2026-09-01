using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.ThumbnailWorker;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopThumbnailStatus
{
    Disabled,
    LoadingThumbnail,
    ReadyThumbnail,
    FailedFallback,
    Unsupported,
}

internal enum ProductDesktopThumbnailFailureKind
{
    None,
    WorkerRejected,
    TimedOut,
    WorkerExited,
    ProtocolError,
    RuntimeException,
}

internal sealed record ProductDesktopThumbnailExtractionEvidence(
    ProductDesktopThumbnailFailureKind FailureKind,
    int HResult,
    double RoundTripMilliseconds)
{
    internal static ProductDesktopThumbnailExtractionEvidence None { get; } =
        new(ProductDesktopThumbnailFailureKind.None, 0, 0);
}

internal sealed record ProductDesktopThumbnailCandidate(
    string AnonymousItemKey,
    string TargetPath);

public sealed record ProductDesktopThumbnailResult(
    string AnonymousItemKey,
    ProductDesktopThumbnailStatus Status,
    bool CacheHit,
    ProductDesktopThumbnailFrame? Frame);

public sealed record ProductDesktopThumbnailRefreshResult(
    bool Enabled,
    int CandidateCount,
    int WorkerRequestCount,
    int CacheHitCount,
    bool WorkerStarted,
    IReadOnlyList<ProductDesktopThumbnailResult> Results);

public static class ProductDesktopThumbnailItemKey
{
    public static string Create(string containerId, int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{containerId}\n{ordinal}"));
        return $"thumbnail:{Convert.ToHexString(hash)}";
    }
}

internal static class ProductDesktopThumbnailCandidateBuilder
{
    internal static IReadOnlyList<ProductDesktopThumbnailCandidate> Build(
        ProductWorkspaceState? state,
        IReadOnlyDictionary<string, int>? viewportStarts = null)
    {
        if (state is null)
        {
            return Array.Empty<ProductDesktopThumbnailCandidate>();
        }
        var candidates = new List<ProductDesktopThumbnailCandidate>();
        foreach (ProductContainerState container in state.Containers)
        {
            int viewportStart = ProductDesktopItemViewportPolicy.ClampStart(
                viewportStarts is not null
                    && viewportStarts.TryGetValue(container.Id, out int requestedStart)
                        ? requestedStart
                        : 0,
                container.Items.Count,
                container.Appearance.ContentDensity);
            int viewportEnd = Math.Min(
                container.Items.Count,
                viewportStart
                    + ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(
                        container.Appearance.ContentDensity));
            for (int index = viewportStart;
                index < viewportEnd;
                index++)
            {
                ProductItemReferenceState item = container.Items[index];
                DesktopCatalogEntry? entry = item.CatalogEntry;
                if (item.Resolution != ProductItemReferenceResolution.Resolved
                    || entry?.Kind != DesktopItemKind.File
                    || !ProductDesktopThumbnailRequestController
                        .IsSupportedImagePath(entry.Identity.CanonicalTarget))
                {
                    continue;
                }
                candidates.Add(new(
                    ProductDesktopThumbnailItemKey.Create(container.Id, index + 1),
                    entry.Identity.CanonicalTarget));
                if (candidates.Count >=
                    ProductDesktopThumbnailRequestController.MaximumVisibleRequests)
                {
                    return candidates.AsReadOnly();
                }
            }
        }
        return candidates.AsReadOnly();
    }
}

public static class ProductDesktopThumbnailRefreshAdmission
{
    public static bool CanPublish(
        long requestedGeneration,
        long currentGeneration,
        long requestedWorkspaceRevision,
        long currentWorkspaceRevision,
        long requestedTopologyGeneration,
        long currentTopologyGeneration,
        bool requestedEnabled,
        bool currentEnabled) =>
        requestedGeneration == currentGeneration
        && requestedWorkspaceRevision == currentWorkspaceRevision
        && requestedTopologyGeneration == currentTopologyGeneration
        && requestedEnabled == currentEnabled;
}

internal interface IProductRestrictedThumbnailRuntime : IDisposable
{
    RestrictedThumbnailWorkerRuntimeSnapshot Snapshot { get; }

    bool OwnedProfileDeletionConfirmed { get; }

    Task<RestrictedThumbnailExtractionResult> ExtractAsync(
        string path,
        int pixelSize,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class ProductRestrictedThumbnailRuntimeAdapter :
    IProductRestrictedThumbnailRuntime
{
    private readonly RestrictedThumbnailWorkerRuntime runtime =
        RestrictedThumbnailWorkerRuntime.Start();

    public RestrictedThumbnailWorkerRuntimeSnapshot Snapshot => runtime.Snapshot;

    public bool OwnedProfileDeletionConfirmed =>
        runtime.OwnedProfileDeletionConfirmed;

    public Task<RestrictedThumbnailExtractionResult> ExtractAsync(
        string path,
        int pixelSize,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        runtime.ExtractAsync(path, pixelSize, timeout, cancellationToken);

    public void Dispose() => runtime.Dispose();
}

internal sealed class ProductDesktopThumbnailRequestController : IDisposable
{
    internal const int MaximumVisibleRequests = 12;
    internal const int MaximumCacheEntries = 64;
    internal static readonly TimeSpan RequestTimeout =
        TimeSpan.FromMilliseconds(1500);

    private static readonly HashSet<string> ImageExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".gif", ".heic", ".heif", ".jpeg", ".jpg",
        ".png", ".tif", ".tiff", ".webp",
    };

    private readonly Func<IProductRestrictedThumbnailRuntime> runtimeFactory;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Dictionary<CacheKey, ProductDesktopThumbnailFrame> cache = [];
    private readonly Queue<CacheKey> cacheOrder = [];
    private IProductRestrictedThumbnailRuntime? runtime;
    private bool disposed;
    private bool ownedProfileDeletionConfirmed;
    private ProductDesktopThumbnailExtractionEvidence lastExtractionEvidence =
        ProductDesktopThumbnailExtractionEvidence.None;

    internal ProductDesktopThumbnailRequestController(
        Func<IProductRestrictedThumbnailRuntime>? runtimeFactory = null)
    {
        this.runtimeFactory = runtimeFactory
            ?? (() => new ProductRestrictedThumbnailRuntimeAdapter());
    }

    internal bool OwnedProfileDeletionConfirmed =>
        ownedProfileDeletionConfirmed;

    internal ProductDesktopThumbnailExtractionEvidence LastExtractionEvidence =>
        lastExtractionEvidence;

    internal async Task<ProductDesktopThumbnailRefreshResult> RefreshAsync(
        bool enabled,
        IEnumerable<ProductDesktopThumbnailCandidate> candidates,
        int pixelSize,
        string themeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, 256);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeKey);
        ObjectDisposedException.ThrowIf(disposed, this);

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                StopRuntime();
                return new(
                    Enabled: false,
                    CandidateCount: 0,
                    WorkerRequestCount: 0,
                    CacheHitCount: 0,
                    WorkerStarted: false,
                    Results: Array.Empty<ProductDesktopThumbnailResult>());
            }

            ProductDesktopThumbnailCandidate[] bounded = candidates
                .Take(MaximumVisibleRequests)
                .ToArray();
            var results = new List<ProductDesktopThumbnailResult>(bounded.Length);
            int workerRequests = 0;
            int cacheHits = 0;
            bool workerStarted = false;
            bool workerUnavailable = false;
            foreach (ProductDesktopThumbnailCandidate candidate in bounded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workerUnavailable)
                {
                    results.Add(Fallback(candidate.AnonymousItemKey));
                    continue;
                }
                if (!TryCreateCacheKey(
                    candidate,
                    pixelSize,
                    themeKey,
                    out CacheKey cacheKey,
                    out string authorizedPath))
                {
                    results.Add(new(
                        candidate.AnonymousItemKey,
                        ProductDesktopThumbnailStatus.Unsupported,
                        CacheHit: false,
                        Frame: null));
                    continue;
                }

                if (cache.TryGetValue(cacheKey, out ProductDesktopThumbnailFrame? cached))
                {
                    cacheHits++;
                    results.Add(new(
                        candidate.AnonymousItemKey,
                        ProductDesktopThumbnailStatus.ReadyThumbnail,
                        CacheHit: true,
                        cached));
                    continue;
                }

                try
                {
                    runtime ??= StartAttestedRuntime();
                    workerStarted = true;
                    workerRequests++;
                    RestrictedThumbnailExtractionResult extracted =
                        await runtime.ExtractAsync(
                            authorizedPath,
                            pixelSize,
                            RequestTimeout,
                            cancellationToken).ConfigureAwait(false);
                    lastExtractionEvidence = new(
                        extracted.Success
                            ? ProductDesktopThumbnailFailureKind.None
                            : extracted.TimedOut
                                ? ProductDesktopThumbnailFailureKind.TimedOut
                                : extracted.ProtocolError
                                    ? ProductDesktopThumbnailFailureKind.ProtocolError
                                    : extracted.WorkerExited
                                        ? ProductDesktopThumbnailFailureKind.WorkerExited
                                        : ProductDesktopThumbnailFailureKind.WorkerRejected,
                        extracted.HResult,
                        extracted.RoundTripMilliseconds);
                    if (extracted.Success && extracted.Frame is { } frame)
                    {
                        ProductDesktopThumbnailFrame productFrame =
                            ProductDesktopThumbnailFrame.Create(
                            frame.Width,
                            frame.Height,
                            frame.Stride,
                            frame.Bgra32Pixels);
                        AddCache(cacheKey, productFrame);
                        results.Add(new(
                            candidate.AnonymousItemKey,
                            ProductDesktopThumbnailStatus.ReadyThumbnail,
                            CacheHit: false,
                            productFrame));
                    }
                    else
                    {
                        results.Add(Fallback(candidate.AnonymousItemKey));
                        if (extracted.TimedOut
                            || extracted.WorkerExited
                            || extracted.ProtocolError)
                        {
                            workerUnavailable = true;
                            StopRuntime();
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or PlatformNotSupportedException)
                {
                    lastExtractionEvidence = new(
                        ProductDesktopThumbnailFailureKind.RuntimeException,
                        exception.HResult,
                        0);
                    results.Add(Fallback(candidate.AnonymousItemKey));
                    workerUnavailable = true;
                    StopRuntime();
                }
            }

            return new(
                Enabled: true,
                bounded.Length,
                workerRequests,
                cacheHits,
                workerStarted,
                results.AsReadOnly());
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        refreshGate.Wait();
        try
        {
            if (disposed)
            {
                return;
            }
            StopRuntime();
            cache.Clear();
            cacheOrder.Clear();
            disposed = true;
        }
        finally
        {
            refreshGate.Release();
            refreshGate.Dispose();
        }
    }

    private static ProductDesktopThumbnailResult Fallback(string key) => new(
        key,
        ProductDesktopThumbnailStatus.FailedFallback,
        CacheHit: false,
        Frame: null);

    internal static bool IsSupportedImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && ImageExtensions.Contains(Path.GetExtension(path));

    private static bool TryCreateCacheKey(
        ProductDesktopThumbnailCandidate candidate,
        int pixelSize,
        string themeKey,
        out CacheKey cacheKey,
        out string authorizedPath)
    {
        cacheKey = default;
        authorizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate.AnonymousItemKey)
            || string.IsNullOrWhiteSpace(candidate.TargetPath)
            || !ImageExtensions.Contains(Path.GetExtension(candidate.TargetPath)))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(candidate.TargetPath);
            var file = new FileInfo(fullPath);
            file.Refresh();
            if (!file.Exists
                || (file.Attributes & FileAttributes.ReparsePoint) != 0
                || file.Length is < 1 or > 33_554_432)
            {
                return false;
            }
            cacheKey = new(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    fullPath.ToUpperInvariant()))),
                file.Length,
                file.LastWriteTimeUtc.Ticks,
                pixelSize,
                themeKey);
            authorizedPath = fullPath;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            return false;
        }
    }

    private void AddCache(
        CacheKey key,
        ProductDesktopThumbnailFrame frame)
    {
        if (cache.TryAdd(key, frame))
        {
            cacheOrder.Enqueue(key);
        }
        while (cache.Count > MaximumCacheEntries && cacheOrder.TryDequeue(out CacheKey oldest))
        {
            _ = cache.Remove(oldest);
        }
    }

    private void StopRuntime()
    {
        if (runtime is null)
        {
            return;
        }
        runtime.Dispose();
        ownedProfileDeletionConfirmed = runtime.OwnedProfileDeletionConfirmed;
        runtime = null;
    }

    private IProductRestrictedThumbnailRuntime StartAttestedRuntime()
    {
        IProductRestrictedThumbnailRuntime candidate = runtimeFactory();
        RestrictedThumbnailWorkerRuntimeSnapshot snapshot;
        try
        {
            snapshot = candidate.Snapshot;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
        if (!snapshot.IsStarted
            || snapshot.WorkerProcessCount != 1
            || snapshot.ActiveOwnedProfileCount != 1
            || !snapshot.IsZeroCapabilityAppContainer
            || !snapshot.UsesKillOnJobClose)
        {
            candidate.Dispose();
            ownedProfileDeletionConfirmed =
                candidate.OwnedProfileDeletionConfirmed;
            throw new InvalidOperationException(
                "The thumbnail request runtime failed isolation attestation.");
        }
        return candidate;
    }

    private readonly record struct CacheKey(
        string SafeIdentity,
        long Length,
        long LastWriteTicks,
        int PixelSize,
        string ThemeKey);
}
