using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.Taskbar;

namespace LongGrid.Infrastructure.Taskbar;

public enum TaskbarAppearanceRecoveryLoadStatus
{
    Missing,
    RecoveryRequired,
    Invalid,
    IoFailure,
}

public sealed record TaskbarAppearanceRecoveryLoadResult(
    TaskbarAppearanceRecoveryLoadStatus Status,
    TaskbarAppearanceRecoveryJournal? Journal,
    string DiagnosticCode);

public sealed class TaskbarAppearanceRecoveryJournalStore
{
    private const string JournalFileName = "taskbar-appearance-recovery.json";
    private readonly string _directoryPath;
    private readonly string _journalPath;
    private readonly string _temporaryPath;

    public TaskbarAppearanceRecoveryJournalStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
        _journalPath = Path.Combine(_directoryPath, JournalFileName);
        _temporaryPath = _journalPath + ".new";
    }

    public async Task<bool> StageAsync(
        TaskbarAppearanceRecoveryJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!TaskbarAppearanceRecoveryJournalPolicy.IsValid(journal))
        {
            return false;
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            journal,
            TaskbarRecoveryJsonContext.Default
                .TaskbarAppearanceRecoveryJournal);
        if (payload.Length > TaskbarAppearanceRecoveryJournalPolicy
                .MaximumJournalBytes)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_directoryPath);
            if (IsReparsePoint(_directoryPath)
                || File.Exists(_journalPath)
                || File.Exists(_temporaryPath))
            {
                return false;
            }

            await using (FileStream stream = new(
                _temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            TaskbarAppearanceRecoveryLoadResult candidate =
                await LoadPathAsync(_temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            if (candidate.Status
                    != TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired
                || !AreEquivalent(candidate.Journal, journal))
            {
                TryDeleteRegularFile(_temporaryPath);
                return false;
            }

            File.Move(_temporaryPath, _journalPath, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            TryDeleteRegularFile(_temporaryPath);
            return false;
        }
    }

    public Task<TaskbarAppearanceRecoveryLoadResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        LoadPathAsync(_journalPath, cancellationToken);

    public async Task<bool> UpdatePhaseAsync(
        string transactionId,
        TaskbarAppearanceRecoveryPhase expectedPhase,
        TaskbarAppearanceRecoveryPhase nextPhase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        bool validTransition =
            expectedPhase == TaskbarAppearanceRecoveryPhase.Staged
                && nextPhase == TaskbarAppearanceRecoveryPhase.Applied
            || expectedPhase == TaskbarAppearanceRecoveryPhase.Applied
                && nextPhase == TaskbarAppearanceRecoveryPhase.Confirmed;
        if (!validTransition || File.Exists(_temporaryPath))
        {
            return false;
        }

        TaskbarAppearanceRecoveryLoadResult current =
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (current.Status != TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired
            || !string.Equals(
                current.Journal!.TransactionId,
                transactionId,
                StringComparison.Ordinal)
            || current.Journal.Phase != expectedPhase)
        {
            return false;
        }

        TaskbarAppearanceRecoveryJournal updated = current.Journal with
        {
            Phase = nextPhase,
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            updated,
            TaskbarRecoveryJsonContext.Default
                .TaskbarAppearanceRecoveryJournal);
        try
        {
            await using (FileStream stream = new(
                _temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            TaskbarAppearanceRecoveryLoadResult candidate =
                await LoadPathAsync(_temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            if (candidate.Status
                    != TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired
                || !AreEquivalent(candidate.Journal, updated))
            {
                TryDeleteRegularFile(_temporaryPath);
                return false;
            }

            File.Replace(
                _temporaryPath,
                _journalPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            TryDeleteRegularFile(_temporaryPath);
            return false;
        }
    }

    public async Task<bool> ClearAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        TaskbarAppearanceRecoveryLoadResult current =
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (current.Status != TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired
            || !string.Equals(
                current.Journal!.TransactionId,
                transactionId,
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            File.Delete(_journalPath);
            return !File.Exists(_journalPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<TaskbarAppearanceRecoveryLoadResult> LoadPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new(
                TaskbarAppearanceRecoveryLoadStatus.Missing,
                null,
                "Missing");
        }

        try
        {
            if (IsReparsePoint(path))
            {
                return new(
                    TaskbarAppearanceRecoveryLoadStatus.Invalid,
                    null,
                    "ReparsePointRejected");
            }

            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0
                or > TaskbarAppearanceRecoveryJournalPolicy.MaximumJournalBytes)
            {
                return new(
                    TaskbarAppearanceRecoveryLoadStatus.Invalid,
                    null,
                    "InvalidSize");
            }

            TaskbarAppearanceRecoveryJournal? journal =
                await JsonSerializer.DeserializeAsync(
                    stream,
                    TaskbarRecoveryJsonContext.Default
                        .TaskbarAppearanceRecoveryJournal,
                    cancellationToken).ConfigureAwait(false);
            return TaskbarAppearanceRecoveryJournalPolicy.IsValid(journal)
                ? new(
                    TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired,
                    journal,
                    "None")
                : new(
                    TaskbarAppearanceRecoveryLoadStatus.Invalid,
                    null,
                    "InvalidContract");
        }
        catch (JsonException)
        {
            return new(
                TaskbarAppearanceRecoveryLoadStatus.Invalid,
                null,
                "MalformedJson");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return new(
                TaskbarAppearanceRecoveryLoadStatus.IoFailure,
                null,
                "IoFailure");
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool AreEquivalent(
        TaskbarAppearanceRecoveryJournal? left,
        TaskbarAppearanceRecoveryJournal right) =>
        left is not null
        && left.SchemaVersion == right.SchemaVersion
        && string.Equals(
            left.TransactionId,
            right.TransactionId,
            StringComparison.Ordinal)
        && left.RequestedPreset == right.RequestedPreset
        && left.Phase == right.Phase
        && left.BaselinePreset == right.BaselinePreset
        && left.WindowsBuild == right.WindowsBuild
        && left.ExplorerProcessId == right.ExplorerProcessId
        && left.CreatedUtc == right.CreatedUtc
        && left.ConfirmationDeadlineUtc == right.ConfirmationDeadlineUtc
        && left.TaskbarWindowClasses.SequenceEqual(
            right.TaskbarWindowClasses,
            StringComparer.Ordinal);

    private static void TryDeleteRegularFile(string path)
    {
        try
        {
            if (File.Exists(path) && !IsReparsePoint(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Preserve the failed staging evidence for the next recovery audit.
        }
    }
}

[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TaskbarAppearanceRecoveryJournal))]
internal sealed partial class TaskbarRecoveryJsonContext : JsonSerializerContext;
