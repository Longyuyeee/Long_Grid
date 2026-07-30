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

public enum ConfigurationDiskFullCheckpoint
{
    DuringTempWrite,
    BeforeTempFlush,
    BeforeCommit,
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

public sealed class InjectedDiskFullException(ConfigurationDiskFullCheckpoint checkpoint)
    : IOException($"Injected disk-full failure at {checkpoint}.")
{
    public ConfigurationDiskFullCheckpoint Checkpoint { get; } = checkpoint;
}

public sealed class ConfigurationWriteLeaseException(IOException innerException)
    : IOException("The configuration write lease is unavailable.", innerException);

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
        WriteLeasePath = PrimaryPath + ".lock";
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

    public string WriteLeasePath { get; }

    public async Task SaveAsync(
        T document,
        AtomicConfigurationSaveCheckpoint? injectedFailure = null,
        Func<AtomicConfigurationSaveCheckpoint, CancellationToken, Task>? checkpointObserver = null,
        ConfigurationDiskFullCheckpoint? injectedDiskFull = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        ConfigurationValidationFailure validationFailure = validator(document);
        if (validationFailure != ConfigurationValidationFailure.None)
        {
            throw new InvalidDataException($"Configuration validation failed: {validationFailure}.");
        }

        Directory.CreateDirectory(DirectoryPath);
        using FileStream writeLease = AcquireWriteLease();

        ConfigurationLoadResult<T> existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Status is ConfigurationLoadStatus.RecoveredFromBackup
            or ConfigurationLoadStatus.SafeMode)
        {
            throw new InvalidDataException(
                "Normal save refuses to overwrite damaged configuration evidence.");
        }

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

            await using (FileStream fileStream = new(TemporaryPath, streamOptions))
            {
                Stream serializationStream =
                    injectedDiskFull == ConfigurationDiskFullCheckpoint.DuringTempWrite
                        ? new DiskFullWriteStream(fileStream, maximumBytes: 32)
                        : fileStream;

                await JsonSerializer.SerializeAsync(
                    serializationStream,
                    document,
                    serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfDiskFull(
                    injectedDiskFull,
                    ConfigurationDiskFullCheckpoint.BeforeTempFlush);
                await serializationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                fileStream.Flush(flushToDisk: true);
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
                ThrowIfDiskFull(
                    injectedDiskFull,
                    ConfigurationDiskFullCheckpoint.BeforeCommit);
                File.Replace(TemporaryPath, PrimaryPath, BackupPath, ignoreMetadataErrors: false);
            }
            else
            {
                await NotifyCheckpointAsync(
                    checkpointObserver,
                    AtomicConfigurationSaveCheckpoint.BeforeCommit,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfRequested(injectedFailure, AtomicConfigurationSaveCheckpoint.BeforeCommit);
                ThrowIfDiskFull(
                    injectedDiskFull,
                    ConfigurationDiskFullCheckpoint.BeforeCommit);
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
                Share = FileShare.Read | FileShare.Delete,
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

    private static void ThrowIfDiskFull(
        ConfigurationDiskFullCheckpoint? requested,
        ConfigurationDiskFullCheckpoint current)
    {
        if (requested == current)
        {
            throw new InjectedDiskFullException(current);
        }
    }

    private static Task NotifyCheckpointAsync(
        Func<AtomicConfigurationSaveCheckpoint, CancellationToken, Task>? observer,
        AtomicConfigurationSaveCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        observer?.Invoke(checkpoint, cancellationToken) ?? Task.CompletedTask;

    private FileStream AcquireWriteLease()
    {
        try
        {
            FileStreamOptions options = new()
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.OpenOrCreate,
                Options = FileOptions.None,
                Share = FileShare.None,
            };
            return new FileStream(WriteLeasePath, options);
        }
        catch (IOException exception)
        {
            throw new ConfigurationWriteLeaseException(exception);
        }
    }

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

    private sealed class DiskFullWriteStream(Stream inner, long maximumBytes) : Stream
    {
        private long remainingBytes = maximumBytes;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int allowedBytes = GetAllowedByteCount(buffer.Length);
            if (allowedBytes > 0)
            {
                inner.Write(buffer[..allowedBytes]);
                remainingBytes -= allowedBytes;
            }

            ThrowIfIncomplete(allowedBytes, buffer.Length);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int allowedBytes = GetAllowedByteCount(buffer.Length);
            if (allowedBytes > 0)
            {
                await inner.WriteAsync(buffer[..allowedBytes], cancellationToken).ConfigureAwait(false);
                remainingBytes -= allowedBytes;
            }

            ThrowIfIncomplete(allowedBytes, buffer.Length);
        }

        private int GetAllowedByteCount(int requestedBytes) =>
            (int)Math.Min(remainingBytes, requestedBytes);

        private static void ThrowIfIncomplete(int allowedBytes, int requestedBytes)
        {
            if (allowedBytes < requestedBytes)
            {
                throw new InjectedDiskFullException(
                    ConfigurationDiskFullCheckpoint.DuringTempWrite);
            }
        }
    }
}
