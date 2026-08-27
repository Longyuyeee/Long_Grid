namespace LongGrid.Core.Taskbar;

public enum TaskbarAppearanceRecoveryLeaseStatus
{
    Acquired,
    Contended,
    UnsafePath,
    IoFailure,
}

public sealed record TaskbarAppearanceRecoveryLeaseResult(
    TaskbarAppearanceRecoveryLeaseStatus Status,
    TaskbarAppearanceRecoveryLease? Lease,
    string DiagnosticCode)
{
    public bool IsAcquired =>
        Status == TaskbarAppearanceRecoveryLeaseStatus.Acquired
        && Lease is not null;
}

public sealed class TaskbarAppearanceRecoveryLease : IDisposable
{
    public const string LeaseFileName = "taskbar-appearance-recovery.lock";

    private readonly string _directoryPath;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private FileStream? _stream;

    private TaskbarAppearanceRecoveryLease(
        string directoryPath,
        FileStream stream)
    {
        _directoryPath = directoryPath;
        _stream = stream;
    }

    public string DirectoryPath => _directoryPath;

    public bool IsHeld => Volatile.Read(ref _stream) is not null;

    public TaskbarAppearanceRecoveryLeaseOperation? TryBeginOperation()
    {
        _operationGate.Wait();
        if (_stream is null)
        {
            _operationGate.Release();
            return null;
        }

        return new TaskbarAppearanceRecoveryLeaseOperation(_operationGate);
    }

    public static TaskbarAppearanceRecoveryLeaseResult TryAcquire(
        string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        string fullDirectoryPath;
        try
        {
            fullDirectoryPath = Path.GetFullPath(directoryPath);
            Directory.CreateDirectory(fullDirectoryPath);
            if (IsReparsePoint(fullDirectoryPath))
            {
                return Failure(
                    TaskbarAppearanceRecoveryLeaseStatus.UnsafePath,
                    "RecoveryDirectoryIsReparsePoint");
            }

            string leasePath = Path.Combine(
                fullDirectoryPath,
                LeaseFileName);
            if (File.Exists(leasePath) && IsReparsePoint(leasePath))
            {
                return Failure(
                    TaskbarAppearanceRecoveryLeaseStatus.UnsafePath,
                    "RecoveryLeaseIsReparsePoint");
            }

            FileStream stream = new(
                leasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);
            try
            {
                if (IsReparsePoint(leasePath)
                    || IsReparsePoint(fullDirectoryPath))
                {
                    stream.Dispose();
                    return Failure(
                        TaskbarAppearanceRecoveryLeaseStatus.UnsafePath,
                        "RecoveryPathChangedToReparsePoint");
                }

                return new(
                    TaskbarAppearanceRecoveryLeaseStatus.Acquired,
                    new TaskbarAppearanceRecoveryLease(
                        fullDirectoryPath,
                        stream),
                    "None");
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return Failure(
                TaskbarAppearanceRecoveryLeaseStatus.Contended,
                "RecoveryLeaseContended");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return Failure(
                TaskbarAppearanceRecoveryLeaseStatus.IoFailure,
                "RecoveryLeaseIoFailure");
        }
    }

    public void Dispose()
    {
        _operationGate.Wait();
        try
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            stream?.Dispose();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static TaskbarAppearanceRecoveryLeaseResult Failure(
        TaskbarAppearanceRecoveryLeaseStatus status,
        string diagnosticCode) =>
        new(status, null, diagnosticCode);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSharingViolation(IOException exception)
    {
        int errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }
}

public sealed class TaskbarAppearanceRecoveryLeaseOperation : IDisposable
{
    private SemaphoreSlim? _gate;

    internal TaskbarAppearanceRecoveryLeaseOperation(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        SemaphoreSlim? gate = Interlocked.Exchange(ref _gate, null);
        gate?.Release();
    }
}
