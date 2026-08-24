namespace LongGrid.ThumbnailWorker;

public sealed record RestrictedThumbnailWorkerRuntimeSnapshot(
    bool IsStarted,
    int WorkerProcessCount,
    int ActiveOwnedProfileCount,
    bool IsZeroCapabilityAppContainer,
    bool UsesKillOnJobClose)
{
    public static RestrictedThumbnailWorkerRuntimeSnapshot Stopped { get; } =
        new(
            IsStarted: false,
            WorkerProcessCount: 0,
            ActiveOwnedProfileCount: 0,
            IsZeroCapabilityAppContainer: false,
            UsesKillOnJobClose: false);
}

public sealed record RestrictedThumbnailPixelFrame(
    int Width,
    int Height,
    int Stride,
    byte[] Bgra32Pixels);

public sealed record RestrictedThumbnailExtractionResult(
    bool Success,
    bool TimedOut,
    bool WorkerExited,
    bool ProtocolError,
    int HResult,
    RestrictedThumbnailPixelFrame? Frame,
    double RoundTripMilliseconds);

internal enum RestrictedThumbnailEvidenceFault
{
    Hang,
    Exit,
}

public sealed class RestrictedThumbnailWorkerRuntime : IDisposable
{
    private readonly ThumbnailWorkerClient client;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private bool disposed;

    public bool OwnedProfileDeletionConfirmed { get; private set; }

    private RestrictedThumbnailWorkerRuntime(ThumbnailWorkerClient client)
    {
        this.client = client;
    }

    public RestrictedThumbnailWorkerRuntimeSnapshot Snapshot
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool isWorkerRunning = client.IsWorkerRunning;
            return new(
                IsStarted: isWorkerRunning,
                WorkerProcessCount: isWorkerRunning ? 1 : 0,
                ActiveOwnedProfileCount: 1,
                IsZeroCapabilityAppContainer: client.AllWorkersAppContainer,
                UsesKillOnJobClose: client.UsesKillOnJobClose);
        }
    }

    public static RestrictedThumbnailWorkerRuntime Start()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            throw new PlatformNotSupportedException(
                "The restricted thumbnail worker requires Windows 8 or later.");
        }

        var client = new ThumbnailWorkerClient(maximumRequestsPerProcess: 100);
        try
        {
            _ = client.EnsureWorkerStarted();
            bool isWorkerRunning = client.IsWorkerRunning;
            RestrictedThumbnailWorkerRuntimeSnapshot snapshot = new(
                IsStarted: isWorkerRunning,
                WorkerProcessCount: isWorkerRunning ? 1 : 0,
                ActiveOwnedProfileCount: 1,
                IsZeroCapabilityAppContainer: client.AllWorkersAppContainer,
                UsesKillOnJobClose: client.UsesKillOnJobClose);
            if (!snapshot.IsStarted
                || snapshot.WorkerProcessCount != 1
                || snapshot.ActiveOwnedProfileCount != 1
                || !snapshot.IsZeroCapabilityAppContainer
                || !snapshot.UsesKillOnJobClose)
            {
                throw new InvalidOperationException(
                    "The restricted thumbnail worker boundary was not attested.");
            }

            return new(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<RestrictedThumbnailExtractionResult> ExtractAsync(
        string path,
        int pixelSize,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, 256);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ObjectDisposedException.ThrowIf(disposed, this);

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var request = new ThumbnailWorkerRequest(
                ThumbnailWorkerServer.CurrentProtocolVersion,
                Guid.NewGuid().ToString("N"),
                ThumbnailWorkerRequestKind.Extract,
                Path.GetFullPath(path),
                pixelSize,
                ShellItemImageFactoryFlags.BiggerSizeOk,
                IncludePixels: true);
            ThumbnailWorkerCallResult call = await client.ExecuteAsync(
                request,
                timeout).ConfigureAwait(false);
            ThumbnailPixelPayload? pixels = call.Response?.Pixels;
            RestrictedThumbnailPixelFrame? frame = pixels?.Bytes is { } bytes
                ? new(
                    pixels.Width,
                    pixels.Height,
                    pixels.Stride,
                    bytes)
                : null;
            return new(
                call.Completed
                    && call.Response?.Success == true
                    && frame is not null,
                call.TimedOut,
                call.WorkerExited,
                call.ProtocolError,
                call.Response?.HResult ?? 0,
                frame,
                call.RoundTripMilliseconds);
        }
        finally
        {
            requestGate.Release();
        }
    }

    internal async Task<RestrictedThumbnailExtractionResult>
        ExecuteEvidenceFaultAsync(
            RestrictedThumbnailEvidenceFault fault,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThumbnailWorkerRequestKind kind = fault switch
            {
                RestrictedThumbnailEvidenceFault.Hang =>
                    ThumbnailWorkerRequestKind.Hang,
                RestrictedThumbnailEvidenceFault.Exit =>
                    ThumbnailWorkerRequestKind.Exit,
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            };
            ThumbnailWorkerCallResult call = await client.ExecuteAsync(
                new(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    Guid.NewGuid().ToString("N"),
                    kind,
                    Path: null,
                    Size: 1,
                    ShellItemImageFactoryFlags.IconOnly),
                timeout).ConfigureAwait(false);
            return new(
                Success: false,
                call.TimedOut,
                call.WorkerExited,
                call.ProtocolError,
                call.Response?.HResult ?? 0,
                Frame: null,
                call.RoundTripMilliseconds);
        }
        finally
        {
            requestGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        client.Dispose();
        requestGate.Dispose();
        OwnedProfileDeletionConfirmed = client.AppContainerProfileDeleted;
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
