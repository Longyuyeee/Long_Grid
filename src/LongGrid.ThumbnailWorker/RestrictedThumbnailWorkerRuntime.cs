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

public sealed class RestrictedThumbnailWorkerRuntime : IDisposable
{
    private readonly ThumbnailWorkerClient client;
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        client.Dispose();
        OwnedProfileDeletionConfirmed = client.AppContainerProfileDeleted;
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
