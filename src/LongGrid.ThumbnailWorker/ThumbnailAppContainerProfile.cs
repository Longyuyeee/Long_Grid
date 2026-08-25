using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LongGrid.ThumbnailWorker;

internal sealed class ThumbnailAppContainerProfile : IDisposable
{
    internal const long MaximumBrokeredInputBytes = 32L * 1024 * 1024;
    internal const long MaximumBrokeredTotalBytes = 64L * 1024 * 1024;
    private const long MaximumStagedRuntimeBytes = 128L * 1024 * 1024;
    private const string ProfilePrefix = "LongGridThumbnailWorker";
    private const int DisposalDeleteAttempts = 20;
    private static readonly TimeSpan DisposalDeleteRetryDelay =
        TimeSpan.FromMilliseconds(50);

    internal static IReadOnlyList<string> RequiredRuntimeFileNames { get; } =
    [
        "LongGrid.ThumbnailWorker.exe",
        "LongGrid.ThumbnailWorker.dll",
        "LongGrid.ThumbnailWorker.deps.json",
        "LongGrid.ThumbnailWorker.runtimeconfig.json",
    ];

    private readonly Dictionary<string, BrokeredInput> _brokeredInputs =
        new(StringComparer.OrdinalIgnoreCase);
    private nint _appContainerSid;
    private long _brokeredBytes;
    private bool _disposed;

    private ThumbnailAppContainerProfile(
        string profileName,
        nint appContainerSid,
        string storagePath,
        string workerExecutablePath)
    {
        ProfileName = profileName;
        _appContainerSid = appContainerSid;
        StoragePath = storagePath;
        WorkerExecutablePath = workerExecutablePath;
    }

    internal string ProfileName { get; }

    internal nint AppContainerSid
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _appContainerSid;
        }
    }

    internal string StoragePath { get; }

    internal string WorkerExecutablePath { get; }

    internal bool WasDeleted { get; private set; }

    internal int DeletionAttempts { get; private set; }

    internal int DeletionHResult { get; private set; }

    internal int BrokeredInputCopiesCreated { get; private set; }

    internal int PathAclLeasesGranted { get; private set; }

    internal int ActivePathAclLeases { get; private set; }

    internal bool AllPathAclLeasesRestored { get; private set; } = true;

    internal static ThumbnailAppContainerProfile Create()
    {
        string profileName = $"{ProfilePrefix}{Guid.NewGuid():N}";
        nint appContainerSid = nint.Zero;
        bool profileCreated = false;
        try
        {
            ThrowIfFailed(CreateAppContainerProfile(
                profileName,
                "Long Grid thumbnail worker",
                "Ephemeral zero-capability thumbnail extraction worker",
                capabilities: nint.Zero,
                capabilityCount: 0,
                out appContainerSid));
            profileCreated = true;

            string sid = new SecurityIdentifier(appContainerSid).Value;
            ThrowIfFailed(GetAppContainerFolderPath(sid, out nint pathPointer));
            string storagePath;
            try
            {
                storagePath = Marshal.PtrToStringUni(pathPointer)
                    ?? throw new InvalidOperationException(
                        "The AppContainer storage path is unavailable.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }

            string runtimePath = Directory.CreateDirectory(
                Path.Combine(storagePath, "worker-runtime")).FullName;
            string brokerPath = Directory.CreateDirectory(
                Path.Combine(storagePath, "broker-input")).FullName;
            _ = brokerPath;
            string executablePath = StageRuntime(runtimePath);
            return new ThumbnailAppContainerProfile(
                profileName,
                appContainerSid,
                storagePath,
                executablePath);
        }
        catch
        {
            if (appContainerSid != nint.Zero)
            {
                _ = FreeSid(appContainerSid);
            }

            if (profileCreated)
            {
                _ = DeleteAppContainerProfile(profileName);
            }

            throw;
        }
    }

    internal string BrokerReadOnlyCopy(string sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullPath = Path.GetFullPath(sourcePath);
        var source = new FileInfo(fullPath);
        source.Refresh();
        if (!source.Exists
            || (source.Attributes & FileAttributes.ReparsePoint) != 0
            || source.Length > MaximumBrokeredInputBytes)
        {
            throw new IOException(
                $"The thumbnail input must be a regular file no larger than "
                + $"{MaximumBrokeredInputBytes} bytes.");
        }

        string cacheKey = $"{fullPath}\0{source.Length}\0{source.LastWriteTimeUtc.Ticks}";
        if (_brokeredInputs.TryGetValue(cacheKey, out BrokeredInput? cached)
            && File.Exists(cached.Path))
        {
            return cached.Path;
        }

        if (source.Length > MaximumBrokeredTotalBytes - _brokeredBytes)
        {
            throw new IOException(
                "The thumbnail input broker exceeded its total byte budget.");
        }

        string extension = Path.GetExtension(source.Name);
        if (extension.Length > 32)
        {
            extension = string.Empty;
        }

        string destination = Path.Combine(
            StoragePath,
            "broker-input",
            $"{Guid.NewGuid():N}{extension}");
        File.Copy(fullPath, destination, overwrite: false);
        File.SetAttributes(destination, FileAttributes.ReadOnly);
        _brokeredInputs.Add(cacheKey, new BrokeredInput(destination));
        _brokeredBytes += source.Length;
        BrokeredInputCopiesCreated++;
        return destination;
    }

    internal ThumbnailPathAclLease GrantMinimumPathAccess(string sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var identity = new SecurityIdentifier(_appContainerSid);
        ThumbnailPathAclLease lease = ThumbnailPathAclLease.Create(
            sourcePath,
            identity,
            restored =>
            {
                ActivePathAclLeases--;
                AllPathAclLeasesRestored &= restored;
            });
        PathAclLeasesGranted++;
        ActivePathAclLeases++;
        return lease;
    }

    internal static bool DeleteByName(string profileName)
    {
        return TryDeleteByName(profileName, out _);
    }

    internal static async Task<ThumbnailAppContainerProfileDeleteResult>
        DeleteByNameWithRetryAsync(
            string profileName,
            int maximumAttempts,
            TimeSpan retryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        if (!IsOwnedProfileName(profileName))
        {
            return new ThumbnailAppContainerProfileDeleteResult(
                Deleted: false,
                Attempts: 0,
                LastHResult: unchecked((int)0x80070057));
        }

        int lastHResult = 0;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            lastHResult = DeleteAppContainerProfile(profileName);
            if (lastHResult >= 0)
            {
                return new ThumbnailAppContainerProfileDeleteResult(
                    Deleted: true,
                    Attempts: attempt,
                    LastHResult: lastHResult);
            }

            if (attempt < maximumAttempts)
            {
                await Task.Delay(retryDelay);
            }
        }

        return new ThumbnailAppContainerProfileDeleteResult(
            Deleted: false,
            Attempts: maximumAttempts,
            LastHResult: lastHResult);
    }

    internal static ThumbnailAppContainerProfileDeleteResult
        DeleteByNameWithRetry(
            string profileName,
            int maximumAttempts,
            TimeSpan retryDelay,
            Func<string, int>? deleteProfile = null,
            Action<TimeSpan>? wait = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        if (!IsOwnedProfileName(profileName))
        {
            return new ThumbnailAppContainerProfileDeleteResult(
                Deleted: false,
                Attempts: 0,
                LastHResult: unchecked((int)0x80070057));
        }

        deleteProfile ??= DeleteAppContainerProfile;
        wait ??= Thread.Sleep;
        int lastHResult = 0;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            lastHResult = deleteProfile(profileName);
            if (lastHResult >= 0)
            {
                return new ThumbnailAppContainerProfileDeleteResult(
                    Deleted: true,
                    Attempts: attempt,
                    LastHResult: lastHResult);
            }

            if (attempt < maximumAttempts && retryDelay > TimeSpan.Zero)
            {
                wait(retryDelay);
            }
        }

        return new ThumbnailAppContainerProfileDeleteResult(
            Deleted: false,
            Attempts: maximumAttempts,
            LastHResult: lastHResult);
    }

    private static bool TryDeleteByName(string profileName, out int hResult)
    {
        if (!IsOwnedProfileName(profileName))
        {
            hResult = unchecked((int)0x80070057);
            return false;
        }

        hResult = DeleteAppContainerProfile(profileName);
        return hResult >= 0;
    }

    private static bool IsOwnedProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)
            || !profileName.StartsWith(ProfilePrefix, StringComparison.Ordinal)
            || profileName.Length != ProfilePrefix.Length + 32)
        {
            return false;
        }

        string suffix = profileName[ProfilePrefix.Length..];
        return Guid.TryParseExact(suffix, "N", out _);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AllPathAclLeasesRestored &= ActivePathAclLeases == 0;
        if (_appContainerSid != nint.Zero)
        {
            _ = FreeSid(_appContainerSid);
            _appContainerSid = nint.Zero;
        }

        ThumbnailAppContainerProfileDeleteResult deletion =
            DeleteByNameWithRetry(
                ProfileName,
                DisposalDeleteAttempts,
                DisposalDeleteRetryDelay);
        WasDeleted = deletion.Deleted;
        DeletionAttempts = deletion.Attempts;
        DeletionHResult = deletion.LastHResult;
        _disposed = true;
    }

    private static string StageRuntime(string destinationDirectory)
    {
        string sourceDirectory = AppContext.BaseDirectory;
        long totalBytes = 0;
        foreach (string fileName in RequiredRuntimeFileNames)
        {
            string sourcePath = Path.Combine(sourceDirectory, fileName);
            var source = new FileInfo(sourcePath);
            source.Refresh();
            if (!source.Exists
                || (source.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new FileNotFoundException(
                    "The allowlisted thumbnail worker runtime file is unavailable.",
                    sourcePath);
            }

            totalBytes = checked(totalBytes + source.Length);
            if (totalBytes > MaximumStagedRuntimeBytes)
            {
                throw new IOException(
                    "The staged thumbnail worker runtime exceeds its byte budget.");
            }

            File.Copy(
                sourcePath,
                Path.Combine(destinationDirectory, fileName),
                overwrite: false);
        }

        string executable = Path.Combine(
            destinationDirectory,
            "LongGrid.ThumbnailWorker.exe");
        return executable;
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            throw new Win32Exception(hresult);
        }
    }

    private sealed record BrokeredInput(string Path);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(
        string appContainerName,
        string displayName,
        string description,
        nint capabilities,
        uint capabilityCount,
        out nint appContainerSid);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeleteAppContainerProfile(string appContainerName);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int GetAppContainerFolderPath(
        string appContainerSid,
        out nint path);

    [DllImport("advapi32.dll")]
    private static extern nint FreeSid(nint sid);
}

internal sealed record ThumbnailAppContainerProfileDeleteResult(
    bool Deleted,
    int Attempts,
    int LastHResult);
