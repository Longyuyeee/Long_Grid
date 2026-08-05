using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationLoadStatus
{
    Missing,
    LoadedPrimary,
    RecoveredFromBackup,
    SafeMode,
}

public enum ProductConfigurationStorageFailure
{
    None,
    Missing,
    Empty,
    TooLarge,
    InvalidConfiguration,
    IoFailure,
}

public enum ProductConfigurationSaveError
{
    InvalidConfiguration,
    DamagedEvidence,
    WriteLeaseUnavailable,
    IoFailure,
}

public enum ProductConfigurationRecoveryAction
{
    AcceptValidatedBackup,
    ResetSafeMode,
}

public enum ProductConfigurationRecoveryError
{
    ConfirmationRequired,
    RecoveryNotAvailable,
    WriteLeaseUnavailable,
    IoFailure,
}

public sealed record ProductConfigurationRecoveryRequest(
    ProductConfigurationRecoveryAction Action,
    bool UserConfirmed);

public sealed record ProductConfigurationRecoveryResult(
    ProductConfigurationRecoveryAction Action,
    bool DamagedPrimaryArchived,
    bool DamagedBackupArchived);

public sealed record ProductConfigurationLoadResult(
    ProductConfigurationLoadStatus Status,
    ProductConfigurationDocument? Document,
    ProductConfigurationStorageFailure PrimaryFailure,
    ProductConfigurationStorageFailure BackupFailure,
    ProductConfigurationError PrimaryContractError,
    ProductConfigurationError BackupContractError);

public sealed class ProductConfigurationSaveException(
    ProductConfigurationSaveError error)
    : IOException($"Product configuration save failed: {error}.")
{
    public ProductConfigurationSaveError Error { get; } = error;
}

public sealed class ProductConfigurationRecoveryException(
    ProductConfigurationRecoveryError error)
    : IOException($"Product configuration recovery failed: {error}.")
{
    public ProductConfigurationRecoveryError Error { get; } = error;
}

public sealed class ProductConfigurationStore
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromMilliseconds(1);
    private readonly TimeSpan writeLeaseTimeout;
    private readonly TimeSpan writeLeaseRetryDelay;

    public ProductConfigurationStore(
        string directoryPath,
        string fileName = "configuration.json",
        TimeSpan? writeLeaseTimeout = null,
        TimeSpan? writeLeaseRetryDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Path.IsPathRooted(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The configuration file name must be a simple file name.",
                nameof(fileName));
        }

        this.writeLeaseTimeout = writeLeaseTimeout ?? TimeSpan.FromSeconds(2);
        this.writeLeaseRetryDelay = writeLeaseRetryDelay ?? TimeSpan.FromMilliseconds(20);
        if (this.writeLeaseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writeLeaseTimeout),
                "The write lease timeout must be positive.");
        }

        if (this.writeLeaseRetryDelay < MinimumRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writeLeaseRetryDelay),
                "The write lease retry delay must be at least one millisecond.");
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        PrimaryPath = Path.Combine(DirectoryPath, fileName);
        BackupPath = PrimaryPath + ".bak";
        TemporaryPath = PrimaryPath + ".new";
        WriteLeasePath = PrimaryPath + ".lock";
    }

    public string DirectoryPath { get; }

    public string PrimaryPath { get; }

    public string BackupPath { get; }

    public string TemporaryPath { get; }

    public string WriteLeasePath { get; }

    internal string RecoveryTemporaryPath => PrimaryPath + ".recovery.new";

    internal string ImportTemporaryPath => PrimaryPath + ".import.new";

    internal string AlternateImportTemporaryPath => PrimaryPath + ".import.next";

    public async Task<ProductConfigurationLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ReadAttempt primary = await TryReadAsync(PrimaryPath, cancellationToken)
            .ConfigureAwait(false);
        if (primary.Document is not null)
        {
            return new(
                ProductConfigurationLoadStatus.LoadedPrimary,
                primary.Document,
                ProductConfigurationStorageFailure.None,
                ProductConfigurationStorageFailure.None,
                ProductConfigurationError.None,
                ProductConfigurationError.None);
        }

        ReadAttempt backup = await TryReadAsync(BackupPath, cancellationToken)
            .ConfigureAwait(false);
        if (backup.Document is not null)
        {
            return new(
                ProductConfigurationLoadStatus.RecoveredFromBackup,
                backup.Document,
                primary.Failure,
                ProductConfigurationStorageFailure.None,
                primary.ContractError,
                ProductConfigurationError.None);
        }

        bool missing = primary.Failure == ProductConfigurationStorageFailure.Missing
            && backup.Failure == ProductConfigurationStorageFailure.Missing
            && !File.Exists(RecoveryTemporaryPath)
            && !File.Exists(ImportTemporaryPath)
            && !File.Exists(AlternateImportTemporaryPath);
        return new(
            missing
                ? ProductConfigurationLoadStatus.Missing
                : ProductConfigurationLoadStatus.SafeMode,
            null,
            primary.Failure,
            backup.Failure,
            primary.ContractError,
            backup.ContractError);
    }

    public async Task SaveAsync(
        ProductConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        byte[] serialized;
        try
        {
            serialized = ProductConfigurationJson.SerializeToUtf8Bytes(document);
        }
        catch (ProductConfigurationContractException)
        {
            throw new ProductConfigurationSaveException(
                ProductConfigurationSaveError.InvalidConfiguration);
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await using FileStream writeLease = await AcquireWriteLeaseAsync(cancellationToken)
                .ConfigureAwait(false);

            ProductConfigurationLoadResult existing = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing.Status is ProductConfigurationLoadStatus.RecoveredFromBackup
                or ProductConfigurationLoadStatus.SafeMode)
            {
                throw new ProductConfigurationSaveException(
                    ProductConfigurationSaveError.DamagedEvidence);
            }

            TryDeleteTemporaryFile();
            try
            {
                FileStreamOptions options = new()
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                    Share = FileShare.None,
                };

                await using (FileStream stream = new(TemporaryPath, options))
                {
                    await stream.WriteAsync(serialized, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                ReadAttempt staged = await TryReadAsync(TemporaryPath, cancellationToken)
                    .ConfigureAwait(false);
                if (staged.Document is null)
                {
                    throw new ProductConfigurationSaveException(
                        ProductConfigurationSaveError.InvalidConfiguration);
                }

                if (File.Exists(PrimaryPath))
                {
                    File.Replace(
                        TemporaryPath,
                        PrimaryPath,
                        BackupPath,
                        ignoreMetadataErrors: false);
                }
                else
                {
                    File.Move(TemporaryPath, PrimaryPath);
                }
            }
            finally
            {
                TryDeleteTemporaryFile();
            }
        }
        catch (ProductConfigurationSaveException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationSaveException(
                ProductConfigurationSaveError.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationSaveException(
                ProductConfigurationSaveError.IoFailure);
        }
    }

    public async Task<ProductConfigurationRecoveryResult> RecoverAsync(
        ProductConfigurationRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.UserConfirmed)
        {
            throw new ProductConfigurationRecoveryException(
                ProductConfigurationRecoveryError.ConfirmationRequired);
        }

        if (request.Action is not (
            ProductConfigurationRecoveryAction.AcceptValidatedBackup or
            ProductConfigurationRecoveryAction.ResetSafeMode))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        ProductConfigurationLoadStatus requiredStatus = request.Action switch
        {
            ProductConfigurationRecoveryAction.AcceptValidatedBackup =>
                ProductConfigurationLoadStatus.RecoveredFromBackup,
            ProductConfigurationRecoveryAction.ResetSafeMode =>
                ProductConfigurationLoadStatus.SafeMode,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        try
        {
            ProductConfigurationLoadResult preflight = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (preflight.Status != requiredStatus)
            {
                throw new ProductConfigurationRecoveryException(
                    ProductConfigurationRecoveryError.RecoveryNotAvailable);
            }

            await using FileStream writeLease = await AcquireWriteLeaseAsync(cancellationToken)
                .ConfigureAwait(false);
            ProductConfigurationLoadResult current = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current.Status != requiredStatus)
            {
                throw new ProductConfigurationRecoveryException(
                    ProductConfigurationRecoveryError.RecoveryNotAvailable);
            }

            return request.Action switch
            {
                ProductConfigurationRecoveryAction.AcceptValidatedBackup =>
                    await AcceptValidatedBackupAsync(cancellationToken).ConfigureAwait(false),
                ProductConfigurationRecoveryAction.ResetSafeMode =>
                    await ResetSafeModeAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
        }
        catch (ProductConfigurationRecoveryException)
        {
            throw;
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error is ProductConfigurationSaveError.WriteLeaseUnavailable)
        {
            throw new ProductConfigurationRecoveryException(
                ProductConfigurationRecoveryError.WriteLeaseUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationRecoveryException(
                ProductConfigurationRecoveryError.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationRecoveryException(
                ProductConfigurationRecoveryError.IoFailure);
        }
    }

    public async Task<ProductConfigurationImportPlan> PrepareImportAsync(
        Stream sourceStream,
        ProductConfigurationImportSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentNullException.ThrowIfNull(source);
        ValidateImportSource(source);

        if (!sourceStream.CanRead)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.SourceUnavailable);
        }

        bool sourceReadCompleted = false;
        try
        {
            byte[] payload = await ReadBoundedImportPayloadAsync(
                    sourceStream,
                    cancellationToken)
                .ConfigureAwait(false);
            sourceReadCompleted = true;
            ProductConfigurationDocument document;
            try
            {
                document = ProductConfigurationJson.Deserialize(payload);
            }
            catch (ProductConfigurationContractException exception)
            {
                ProductConfigurationImportError error = exception.Error is
                    ProductConfigurationError.DocumentTooLarge
                        ? ProductConfigurationImportError.DocumentTooLarge
                        : ProductConfigurationImportError.InvalidConfiguration;
                throw new ProductConfigurationImportException(error);
            }

            string revisionBefore = await ComputeStoreRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            ProductConfigurationLoadResult current = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            string revisionAfter = await ComputeStoreRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(revisionBefore, revisionAfter, StringComparison.Ordinal))
            {
                throw new ProductConfigurationImportException(
                    ProductConfigurationImportError.StoreChanged);
            }

            int itemCount = document.Containers.Sum(container => container.Items.Count);
            ProductConfigurationImportPreview preview = new(
                document.SchemaVersion,
                document.Containers.Count,
                itemCount,
                MapImportExistingState(current.Status));
            return new(preview, payload, revisionAfter);
        }
        catch (ProductConfigurationImportException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationImportException(
                sourceReadCompleted
                    ? ProductConfigurationImportError.IoFailure
                    : ProductConfigurationImportError.SourceUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationImportException(
                sourceReadCompleted
                    ? ProductConfigurationImportError.IoFailure
                    : ProductConfigurationImportError.SourceUnavailable);
        }
    }

    public async Task<ProductConfigurationImportResult> ImportAsync(
        ProductConfigurationImportPlan plan,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!userConfirmed)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.ConfirmationRequired);
        }

        try
        {
            RequireValidImportPayload(plan.Payload.Span);
            await RequireUnchangedStoreAsync(plan, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(DirectoryPath);
            await using FileStream writeLease = await AcquireWriteLeaseAsync(cancellationToken)
                .ConfigureAwait(false);
            await RequireUnchangedStoreAsync(plan, cancellationToken).ConfigureAwait(false);
            ProductConfigurationLoadResult current = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            string importStagingPath = File.Exists(ImportTemporaryPath)
                ? AlternateImportTemporaryPath
                : ImportTemporaryPath;
            TryDeleteImportTemporaryFile(importStagingPath);
            string? backupArchivePath = null;
            bool backupMoved = false;
            bool importPublished = false;
            bool preserveImportMarker = false;
            try
            {
                await using (FileStream destination = CreateImportStagingStream(
                    importStagingPath))
                {
                    await destination.WriteAsync(plan.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    await FlushToDiskAsync(destination, cancellationToken).ConfigureAwait(false);
                }

                await RequireValidImportStagingAsync(
                        importStagingPath,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (current.Status is ProductConfigurationLoadStatus.SafeMode
                    && File.Exists(BackupPath))
                {
                    backupArchivePath = CreateImportArchivePath("backup");
                    File.Move(BackupPath, backupArchivePath);
                    backupMoved = true;
                }

                bool primaryExists = File.Exists(PrimaryPath);
                if (primaryExists)
                {
                    File.Replace(
                        importStagingPath,
                        PrimaryPath,
                        CreateImportArchivePath("primary"),
                        ignoreMetadataErrors: false);
                }
                else
                {
                    File.Move(importStagingPath, PrimaryPath);
                }

                importPublished = true;
                TryDeleteRecoveryTemporaryFile();
                TryDeleteImportTemporaryFile(ImportTemporaryPath);
                TryDeleteImportTemporaryFile(AlternateImportTemporaryPath);
                return new(
                    PrimaryArchived: primaryExists,
                    BackupArchived: backupMoved);
            }
            catch
            {
                if (backupMoved && !importPublished && backupArchivePath is not null)
                {
                    try
                    {
                        File.Move(backupArchivePath, BackupPath);
                        backupMoved = false;
                    }
                    catch (IOException)
                    {
                        preserveImportMarker = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        preserveImportMarker = true;
                    }
                }

                throw;
            }
            finally
            {
                if (!preserveImportMarker)
                {
                    TryDeleteImportTemporaryFile(importStagingPath);
                }
            }
        }
        catch (ProductConfigurationImportException)
        {
            throw;
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error is ProductConfigurationSaveError.WriteLeaseUnavailable)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.WriteLeaseUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.IoFailure);
        }
    }

    private async Task<ProductConfigurationRecoveryResult> AcceptValidatedBackupAsync(
        CancellationToken cancellationToken)
    {
        TryDeleteRecoveryTemporaryFile();
        try
        {
            await using (FileStream source = new(
                BackupPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destination = CreateRecoveryStagingStream())
            {
                await source.CopyToAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
                await FlushToDiskAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            await RequireValidRecoveryStagingAsync(cancellationToken).ConfigureAwait(false);
            string damagedArchivePath = CreateDamageArchivePath("primary");
            File.Replace(
                RecoveryTemporaryPath,
                PrimaryPath,
                damagedArchivePath,
                ignoreMetadataErrors: false);

            return new(
                ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                DamagedPrimaryArchived: true,
                DamagedBackupArchived: false);
        }
        finally
        {
            TryDeleteRecoveryTemporaryFile();
        }
    }

    private async Task<ProductConfigurationRecoveryResult> ResetSafeModeAsync(
        CancellationToken cancellationToken)
    {
        byte[] resetDocument = ProductConfigurationJson.SerializeToUtf8Bytes(
            ProductConfigurationDefaults.CreateEmpty());
        TryDeleteRecoveryTemporaryFile();
        string? backupArchivePath = null;
        bool backupMoved = false;
        bool resetPublished = false;
        bool preserveRecoveryMarker = false;

        try
        {
            await using (FileStream destination = CreateRecoveryStagingStream())
            {
                await destination.WriteAsync(resetDocument, cancellationToken)
                    .ConfigureAwait(false);
                await FlushToDiskAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            await RequireValidRecoveryStagingAsync(cancellationToken).ConfigureAwait(false);

            if (File.Exists(BackupPath))
            {
                backupArchivePath = CreateDamageArchivePath("backup");
                File.Move(BackupPath, backupArchivePath);
                backupMoved = true;
            }

            bool primaryExists = File.Exists(PrimaryPath);
            if (primaryExists)
            {
                File.Replace(
                    RecoveryTemporaryPath,
                    PrimaryPath,
                    CreateDamageArchivePath("primary"),
                    ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(RecoveryTemporaryPath, PrimaryPath);
            }

            resetPublished = true;
            return new(
                ProductConfigurationRecoveryAction.ResetSafeMode,
                DamagedPrimaryArchived: primaryExists,
                DamagedBackupArchived: backupMoved);
        }
        catch
        {
            if (backupMoved && !resetPublished && backupArchivePath is not null)
            {
                try
                {
                    File.Move(backupArchivePath, BackupPath);
                    backupMoved = false;
                }
                catch (IOException)
                {
                    preserveRecoveryMarker = true;
                }
                catch (UnauthorizedAccessException)
                {
                    preserveRecoveryMarker = true;
                }
            }

            throw;
        }
        finally
        {
            if (!preserveRecoveryMarker)
            {
                TryDeleteRecoveryTemporaryFile();
            }
        }
    }

    private FileStream CreateRecoveryStagingStream() =>
        new(
            RecoveryTemporaryPath,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                Share = FileShare.None,
            });

    private static FileStream CreateImportStagingStream(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                Share = FileShare.None,
            });

    private async Task RequireValidRecoveryStagingAsync(
        CancellationToken cancellationToken)
    {
        ReadAttempt staged = await TryReadAsync(RecoveryTemporaryPath, cancellationToken)
            .ConfigureAwait(false);
        if (staged.Document is null)
        {
            throw new ProductConfigurationRecoveryException(
                ProductConfigurationRecoveryError.RecoveryNotAvailable);
        }
    }

    private static async Task RequireValidImportStagingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ReadAttempt staged = await TryReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (staged.Document is null)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.InvalidConfiguration);
        }
    }

    private static async Task FlushToDiskAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private string CreateDamageArchivePath(string kind) =>
        PrimaryPath + ".damaged." + Guid.NewGuid().ToString("N") + "." + kind;

    private string CreateImportArchivePath(string kind) =>
        PrimaryPath + ".import." + Guid.NewGuid().ToString("N") + "." + kind;

    private static void ValidateImportSource(ProductConfigurationImportSource source)
    {
        if (!source.UserSelected)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.SourceNotUserSelected);
        }

        if (!string.Equals(source.FileExtension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.UnsupportedFileType);
        }

        if (!source.IsLocalFileSystem)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.NonLocalSource);
        }

        if (source.IsReparsePoint)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.ReparsePointNotAllowed);
        }
    }

    private static async Task<byte[]> ReadBoundedImportPayloadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        int maximum = ProductConfigurationLimits.MaximumSerializedBytes;
        using MemoryStream buffer = new(capacity: Math.Min(maximum, 64 * 1024));
        byte[] chunk = new byte[16 * 1024];
        while (buffer.Length <= maximum)
        {
            int remaining = maximum + 1 - checked((int)buffer.Length);
            int read = await source.ReadAsync(
                    chunk.AsMemory(0, Math.Min(chunk.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.EmptyDocument);
        }

        if (buffer.Length > maximum)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.DocumentTooLarge);
        }

        return buffer.ToArray();
    }

    private static void RequireValidImportPayload(ReadOnlySpan<byte> payload)
    {
        try
        {
            _ = ProductConfigurationJson.Deserialize(payload);
        }
        catch (ProductConfigurationContractException)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.InvalidConfiguration);
        }
    }

    private async Task RequireUnchangedStoreAsync(
        ProductConfigurationImportPlan plan,
        CancellationToken cancellationToken)
    {
        string currentRevision = await ComputeStoreRevisionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
            plan.ExpectedStoreRevision,
            currentRevision,
            StringComparison.Ordinal))
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.StoreChanged);
        }
    }

    private async Task<string> ComputeStoreRevisionAsync(
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await AppendRevisionFileAsync(hash, "primary", PrimaryPath, cancellationToken)
            .ConfigureAwait(false);
        await AppendRevisionFileAsync(hash, "backup", BackupPath, cancellationToken)
            .ConfigureAwait(false);
        await AppendRevisionFileAsync(
                hash,
                "recovery-marker",
                RecoveryTemporaryPath,
                cancellationToken)
            .ConfigureAwait(false);
        await AppendRevisionFileAsync(
                hash,
                "import-marker",
                ImportTemporaryPath,
                cancellationToken)
            .ConfigureAwait(false);
        await AppendRevisionFileAsync(
                hash,
                "alternate-import-marker",
                AlternateImportTemporaryPath,
                cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task AppendRevisionFileAsync(
        IncrementalHash hash,
        string label,
        string path,
        CancellationToken cancellationToken)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(label));
        if (!File.Exists(path))
        {
            hash.AppendData([0]);
            return;
        }

        hash.AppendData([1]);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        hash.AppendData(BitConverter.GetBytes(stream.Length));
        int remaining = ProductConfigurationLimits.MaximumSerializedBytes + 1;
        byte[] buffer = new byte[16 * 1024];
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static ProductConfigurationImportExistingState MapImportExistingState(
        ProductConfigurationLoadStatus status) => status switch
        {
            ProductConfigurationLoadStatus.Missing =>
                ProductConfigurationImportExistingState.NoSavedConfiguration,
            ProductConfigurationLoadStatus.LoadedPrimary =>
                ProductConfigurationImportExistingState.LoadedPrimary,
            ProductConfigurationLoadStatus.RecoveredFromBackup =>
                ProductConfigurationImportExistingState.RecoveredBackupReadOnly,
            ProductConfigurationLoadStatus.SafeMode =>
                ProductConfigurationImportExistingState.SafeMode,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private async Task<FileStream> AcquireWriteLeaseAsync(
        CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        bool retrying = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retrying && elapsed.Elapsed >= writeLeaseTimeout)
            {
                throw new ProductConfigurationSaveException(
                    ProductConfigurationSaveError.WriteLeaseUnavailable);
            }

            try
            {
                return new FileStream(
                    WriteLeasePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                retrying = true;
                TimeSpan remaining = writeLeaseTimeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new ProductConfigurationSaveException(
                        ProductConfigurationSaveError.WriteLeaseUnavailable);
                }

                await Task.Delay(
                        remaining < writeLeaseRetryDelay ? remaining : writeLeaseRetryDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task<ReadAttempt> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return ReadAttempt.Failed(ProductConfigurationStorageFailure.Missing);
            }

            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length == 0)
            {
                return ReadAttempt.Failed(ProductConfigurationStorageFailure.Empty);
            }

            if (stream.Length > ProductConfigurationLimits.MaximumSerializedBytes)
            {
                return ReadAttempt.Failed(ProductConfigurationStorageFailure.TooLarge);
            }

            byte[] bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            try
            {
                return ReadAttempt.Succeeded(ProductConfigurationJson.Deserialize(bytes));
            }
            catch (ProductConfigurationContractException exception)
            {
                return ReadAttempt.Failed(
                    ProductConfigurationStorageFailure.InvalidConfiguration,
                    exception.Error);
            }
        }
        catch (IOException)
        {
            return ReadAttempt.Failed(ProductConfigurationStorageFailure.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return ReadAttempt.Failed(ProductConfigurationStorageFailure.IoFailure);
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

    private void TryDeleteRecoveryTemporaryFile()
    {
        try
        {
            File.Delete(RecoveryTemporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteImportTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ReadAttempt(
        ProductConfigurationDocument? Document,
        ProductConfigurationStorageFailure Failure,
        ProductConfigurationError ContractError)
    {
        public static ReadAttempt Succeeded(ProductConfigurationDocument document) =>
            new(
                document,
                ProductConfigurationStorageFailure.None,
                ProductConfigurationError.None);

        public static ReadAttempt Failed(
            ProductConfigurationStorageFailure failure,
            ProductConfigurationError contractError = ProductConfigurationError.None) =>
            new(null, failure, contractError);
    }
}
