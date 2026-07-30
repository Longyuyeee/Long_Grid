using System.Text.Json;

namespace LongGrid.Spikes.ConfigurationPersistence;

public enum ConfigurationLoadStatus
{
    Missing,
    LoadedPrimary,
    RecoveredFromBackup,
    SafeMode,
}

public enum ConfigurationValidationFailure
{
    None,
    Missing,
    Empty,
    TooLarge,
    InvalidJson,
    UnsupportedSchema,
    InvalidDocument,
    IoFailure,
}

public enum AtomicConfigurationSaveCheckpoint
{
    AfterTempFlush,
    AfterTempValidation,
    BeforeCommit,
    AfterCommit,
}

public sealed record ConfigurationLoadResult<T>(
    ConfigurationLoadStatus Status,
    T? Document,
    ConfigurationValidationFailure PrimaryFailure,
    ConfigurationValidationFailure BackupFailure);

public sealed class InjectedSaveFailureException(AtomicConfigurationSaveCheckpoint checkpoint)
    : IOException($"Injected configuration save failure at {checkpoint}.")
{
    public AtomicConfigurationSaveCheckpoint Checkpoint { get; } = checkpoint;
}

public sealed class AtomicJsonConfigurationStore<T>
    where T : class
{
    private readonly Func<T, ConfigurationValidationFailure> validator;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly long maximumDocumentBytes;

    public AtomicJsonConfigurationStore(
        string directoryPath,
        string fileName,
        Func<T, ConfigurationValidationFailure> validator,
        long maximumDocumentBytes = 4 * 1024 * 1024,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(validator);

        if (Path.IsPathRooted(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("The configuration file name must be a simple file name.", nameof(fileName));
        }

        if (maximumDocumentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDocumentBytes),
                maximumDocumentBytes,
                "The maximum document size must be positive.");
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        PrimaryPath = Path.Combine(DirectoryPath, fileName);
        BackupPath = PrimaryPath + ".bak";
        TemporaryPath = PrimaryPath + ".new";
        this.validator = validator;
        this.maximumDocumentBytes = maximumDocumentBytes;
        this.serializerOptions = serializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            MaxDepth = 64,
        };
    }

    public string DirectoryPath { get; }

    public string PrimaryPath { get; }

    public string BackupPath { get; }

    public string TemporaryPath { get; }

    public async Task SaveAsync(
        T document,
        AtomicConfigurationSaveCheckpoint? injectedFailure = null,
        Func<AtomicConfigurationSaveCheckpoint, CancellationToken, Task>? checkpointObserver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        ConfigurationValidationFailure validationFailure = validator(document);
        if (validationFailure != ConfigurationValidationFailure.None)
        {
            throw new InvalidDataException($"Configuration validation failed: {validationFailure}.");
        }

        ConfigurationLoadResult<T> existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Status is ConfigurationLoadStatus.RecoveredFromBackup
            or ConfigurationLoadStatus.SafeMode)
        {
            throw new InvalidDataException(
                "Normal save refuses to overwrite damaged configuration evidence.");
        }

        Directory.CreateDirectory(DirectoryPath);
        TryDeleteTemporaryFile();

        try
        {
            FileStreamOptions streamOptions = new()
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                Share = FileShare.None,
            };

            await using (FileStream stream = new(TemporaryPath, streamOptions))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            await NotifyCheckpointAsync(
                checkpointObserver,
                AtomicConfigurationSaveCheckpoint.AfterTempFlush,
                cancellationToken).ConfigureAwait(false);
            ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.AfterTempFlush);

            ReadAttempt<T> stagedDocument = await TryReadAsync(
                TemporaryPath,
                cancellationToken).ConfigureAwait(false);

            if (!stagedDocument.IsValid)
            {
                throw new InvalidDataException(
                    $"Staged configuration validation failed: {stagedDocument.Failure}.");
            }

            await NotifyCheckpointAsync(
                checkpointObserver,
                AtomicConfigurationSaveCheckpoint.AfterTempValidation,
                cancellationToken).ConfigureAwait(false);
            ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.AfterTempValidation);

            if (File.Exists(PrimaryPath))
            {
                await NotifyCheckpointAsync(
                    checkpointObserver,
                    AtomicConfigurationSaveCheckpoint.BeforeCommit,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.BeforeCommit);
                File.Replace(TemporaryPath, PrimaryPath, BackupPath, ignoreMetadataErrors: false);
            }
            else
            {
                await NotifyCheckpointAsync(
                    checkpointObserver,
                    AtomicConfigurationSaveCheckpoint.BeforeCommit,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.BeforeCommit);
                File.Move(TemporaryPath, PrimaryPath);
            }

            await NotifyCheckpointAsync(
                checkpointObserver,
                AtomicConfigurationSaveCheckpoint.AfterCommit,
                cancellationToken).ConfigureAwait(false);
            ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.AfterCommit);
        }
        finally
        {
            TryDeleteTemporaryFile();
        }
    }

    public async Task<ConfigurationLoadResult<T>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ReadAttempt<T> primary = await TryReadAsync(PrimaryPath, cancellationToken).ConfigureAwait(false);
        if (primary.IsValid)
        {
            return new(
                ConfigurationLoadStatus.LoadedPrimary,
                primary.Document,
                ConfigurationValidationFailure.None,
                ConfigurationValidationFailure.None);
        }

        ReadAttempt<T> backup = await TryReadAsync(BackupPath, cancellationToken).ConfigureAwait(false);
        if (backup.IsValid)
        {
            return new(
                ConfigurationLoadStatus.RecoveredFromBackup,
                backup.Document,
                primary.Failure,
                ConfigurationValidationFailure.None);
        }

        bool filesAreMissing = primary.Failure == ConfigurationValidationFailure.Missing
            && backup.Failure == ConfigurationValidationFailure.Missing;

        return new(
            filesAreMissing ? ConfigurationLoadStatus.Missing : ConfigurationLoadStatus.SafeMode,
            null,
            primary.Failure,
            backup.Failure);
    }

    private async Task<ReadAttempt<T>> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return ReadAttempt<T>.Failed(ConfigurationValidationFailure.Missing);
            }

            FileStreamOptions streamOptions = new()
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read,
            };

            await using FileStream stream = new(path, streamOptions);
            if (stream.Length == 0)
            {
                return ReadAttempt<T>.Failed(ConfigurationValidationFailure.Empty);
            }

            if (stream.Length > maximumDocumentBytes)
            {
                return ReadAttempt<T>.Failed(ConfigurationValidationFailure.TooLarge);
            }

            T? document = await JsonSerializer.DeserializeAsync<T>(
                stream,
                serializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (document is null)
            {
                return ReadAttempt<T>.Failed(ConfigurationValidationFailure.InvalidJson);
            }

            ConfigurationValidationFailure failure = validator(document);
            return failure == ConfigurationValidationFailure.None
                ? ReadAttempt<T>.Succeeded(document)
                : ReadAttempt<T>.Failed(failure);
        }
        catch (JsonException)
        {
            return ReadAttempt<T>.Failed(ConfigurationValidationFailure.InvalidJson);
        }
        catch (IOException)
        {
            return ReadAttempt<T>.Failed(ConfigurationValidationFailure.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return ReadAttempt<T>.Failed(ConfigurationValidationFailure.IoFailure);
        }
    }

    private static void ThrowIfRequested(
        AtomicConfigurationSaveCheckpoint? requested,
        AtomicConfigurationSaveCheckpoint current)
    {
        if (requested == current)
        {
            throw new InjectedSaveFailureException(current);
        }
    }

    private static Task NotifyCheckpointAsync(
        Func<AtomicConfigurationSaveCheckpoint, CancellationToken, Task>? observer,
        AtomicConfigurationSaveCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        observer?.Invoke(checkpoint, cancellationToken) ?? Task.CompletedTask;

    private void TryDeleteTemporaryFile()
    {
        try
        {
            File.Delete(TemporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ReadAttempt<TDocument>(
        bool IsValid,
        TDocument? Document,
        ConfigurationValidationFailure Failure)
        where TDocument : class
    {
        public static ReadAttempt<TDocument> Succeeded(TDocument document) =>
            new(true, document, ConfigurationValidationFailure.None);

        public static ReadAttempt<TDocument> Failed(ConfigurationValidationFailure failure) =>
            new(false, null, failure);
    }
}
