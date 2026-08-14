using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public sealed partial class ProductConfigurationStore
{
    private const int MaximumEvidenceItems = 256;
    private const int MaximumEvidenceScanEntries = 4096;
    private const long MaximumEvidenceExportBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions AnonymousEvidenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
    public async Task<ProductConfigurationExportPlan> PrepareExportAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            string revisionBefore = await ComputeStoreRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            ProductConfigurationLoadResult loaded = await LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            string revisionAfter = await ComputeStoreRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(revisionBefore, revisionAfter, StringComparison.Ordinal))
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.StoreChanged);
            }

            if (loaded.Document is null
                || loaded.Status is ProductConfigurationLoadStatus.Missing
                    or ProductConfigurationLoadStatus.SafeMode)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.ExportNotAvailable);
            }

            byte[] payload = ProductConfigurationJson.SerializeToUtf8Bytes(loaded.Document);
            ProductConfigurationExportSourceState sourceState =
                loaded.Status is ProductConfigurationLoadStatus.LoadedPrimary
                    ? ProductConfigurationExportSourceState.LoadedPrimary
                    : ProductConfigurationExportSourceState.RecoveredBackupReadOnly;
            int itemCount = loaded.Document.Containers.Sum(container => container.Items.Count);
            return new(
                new(
                    loaded.Document.SchemaVersion,
                    loaded.Document.Containers.Count,
                    itemCount,
                    sourceState),
                payload,
                revisionAfter);
        }
        catch (ProductConfigurationExportException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    public async Task<ProductConfigurationExportResult> ExportAsync(
        ProductConfigurationExportPlan plan,
        string destinationDirectory,
        ProductConfigurationExportDestination destination,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destination);
        if (!userConfirmed)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.ConfirmationRequired);
        }

        string validatedDirectory = ValidateExportDestination(
            destinationDirectory,
            destination);
        string fileName = $"LongGrid-Configuration-v{plan.Preview.SchemaVersion}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json";
        string finalPath = Path.Combine(validatedDirectory, fileName);
        string temporaryPath = finalPath + ".new";
        try
        {
            await RequireUnchangedExportStoreAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(plan.Payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            byte[] staged = await File.ReadAllBytesAsync(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            _ = ProductConfigurationJson.Deserialize(staged);
            await RequireUnchangedExportStoreAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, finalPath);
            return new(fileName);
        }
        catch (ProductConfigurationExportException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw;
        }
        catch (ProductConfigurationContractException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
        catch (IOException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.DestinationUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.DestinationUnavailable);
        }
    }

    public async Task<ProductAnonymousInteractionEvidenceCaptureResult>
        CaptureAnonymousInteractionEvidenceAsync(
            ProductAnonymousInteractionEvidence evidence,
            bool userConfirmed,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!userConfirmed)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.ConfirmationRequired);
        }

        ValidateAnonymousInteractionEvidence(evidence);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            evidence,
            AnonymousEvidenceJsonOptions);
        string identifier = Guid.NewGuid().ToString("N");
        string finalPath = Path.Combine(
            DirectoryPath,
            $"interaction-evidence.{identifier}.snapshot.json");
        string temporaryPath = finalPath + ".new";
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await using FileStream writeLease =
                await AcquireWriteLeaseAsync(cancellationToken).ConfigureAwait(false);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            byte[] staged = await File.ReadAllBytesAsync(
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(payload),
                    SHA256.HashData(staged)))
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceVerificationFailed);
            }

            File.Move(temporaryPath, finalPath);
            FileInfo published = new(finalPath);
            return new(published.Length, published.LastWriteTimeUtc);
        }
        catch (ProductConfigurationExportException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw;
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error is ProductConfigurationSaveError.WriteLeaseUnavailable)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.WriteLeaseUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    public Task<ProductConfigurationEvidenceInventory> GetEvidenceInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(DirectoryPath))
        {
            return Task.FromResult(new ProductConfigurationEvidenceInventory(
                [],
                false,
                0,
                0,
                0,
                null));
        }

        try
        {
            List<ProductConfigurationEvidenceItem> items = [];
            int skippedUnsafeCount = 0;
            int scannedEntries = 0;
            int observedItemCount = 0;
            long observedSizeBytes = 0;
            DateTimeOffset? oldestObservedArchivedUtc = null;
            bool truncated = false;
            foreach (string path in Directory.EnumerateFiles(DirectoryPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scannedEntries == MaximumEvidenceScanEntries)
                {
                    truncated = true;
                    break;
                }

                scannedEntries++;
                if (!TryDescribeEvidenceFile(path, out ProductConfigurationEvidenceOrigin origin,
                        out ProductConfigurationEvidenceRole role))
                {
                    continue;
                }

                FileInfo info = new(path);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skippedUnsafeCount++;
                    continue;
                }

                observedItemCount++;
                observedSizeBytes = info.Length > long.MaxValue - observedSizeBytes
                    ? long.MaxValue
                    : observedSizeBytes + info.Length;
                DateTimeOffset archivedUtc = info.LastWriteTimeUtc;
                if (oldestObservedArchivedUtc is null
                    || archivedUtc < oldestObservedArchivedUtc.Value)
                {
                    oldestObservedArchivedUtc = archivedUtc;
                }

                if (items.Count == MaximumEvidenceItems)
                {
                    truncated = true;
                    continue;
                }

                items.Add(new(origin, role, info.Length, archivedUtc, path));
            }

            return Task.FromResult(new ProductConfigurationEvidenceInventory(
                items.OrderByDescending(item => item.ArchivedUtc).ToArray(),
                truncated,
                skippedUnsafeCount,
                observedItemCount,
                observedSizeBytes,
                oldestObservedArchivedUtc));
        }
        catch (IOException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    public async Task<ProductConfigurationExportResult> ExportEvidenceAsync(
        ProductConfigurationEvidenceItem item,
        string destinationDirectory,
        ProductConfigurationExportDestination destination,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(destination);
        if (!userConfirmed)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.ConfirmationRequired);
        }

        string validatedDirectory = ValidateExportDestination(
            destinationDirectory,
            destination);
        RequireUnchangedEvidence(item);
        string origin = item.Origin switch
        {
            ProductConfigurationEvidenceOrigin.DamagedRecovery => "Recovery",
            ProductConfigurationEvidenceOrigin.ImportPrevious => "Import",
            ProductConfigurationEvidenceOrigin.AnonymousInteraction => "Interaction",
            _ => throw new ProductConfigurationExportException(
                ProductConfigurationExportError.EvidenceNotAvailable),
        };
        string role = item.Role switch
        {
            ProductConfigurationEvidenceRole.Primary => "Primary",
            ProductConfigurationEvidenceRole.Backup => "Backup",
            ProductConfigurationEvidenceRole.Snapshot => "Snapshot",
            _ => throw new ProductConfigurationExportException(
                ProductConfigurationExportError.EvidenceNotAvailable),
        };
        string fileName = item.Origin ==
            ProductConfigurationEvidenceOrigin.AnonymousInteraction
                ? $"LongGrid-Interaction-Evidence-{role}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json"
                : $"LongGrid-Configuration-Evidence-{origin}-{role}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.bin";
        string finalPath = Path.Combine(validatedDirectory, fileName);
        string temporaryPath = finalPath + ".new";
        try
        {
            await using FileStream source = new(
                item.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != item.SizeBytes
                || File.GetLastWriteTimeUtc(item.SourcePath) != item.ArchivedUtc.UtcDateTime)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceChanged);
            }

            if (source.Length > MaximumEvidenceExportBytes)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceTooLarge);
            }

            byte[] sourceHash;
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using FileStream target = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
                sourceHash = hash.GetHashAndReset();
            }

            byte[] stagedHash = await ComputeFileHashAsync(
                    temporaryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, stagedHash))
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceVerificationFailed);
            }

            File.Move(temporaryPath, finalPath);
            return new(fileName);
        }
        catch (ProductConfigurationExportException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteExportTemporaryFile(temporaryPath);
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    public async Task<ProductConfigurationEvidenceRemovalResult> RemoveEvidenceAsync(
        ProductConfigurationEvidenceItem item,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!userConfirmed)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.ConfirmationRequired);
        }

        try
        {
            RequireUnchangedEvidence(item);
            await using FileStream writeLease = await AcquireWriteLeaseAsync(cancellationToken)
                .ConfigureAwait(false);
            RequireUnchangedEvidence(item);
            await using FileStream evidence = new(
                item.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (evidence.Length != item.SizeBytes
                || File.GetLastWriteTimeUtc(item.SourcePath) != item.ArchivedUtc.UtcDateTime)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(item.SourcePath);
            return new(item.Origin, item.Role, item.SizeBytes);
        }
        catch (ProductConfigurationExportException)
        {
            throw;
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error is ProductConfigurationSaveError.WriteLeaseUnavailable)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.WriteLeaseUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    private void ValidateEvidenceSelection(ProductConfigurationEvidenceItem item)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(item.SourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.EvidenceNotAvailable);
        }

        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                DirectoryPath,
                StringComparison.OrdinalIgnoreCase)
            || !TryDescribeEvidenceFile(fullPath, out _, out _))
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.EvidenceNotAvailable);
        }

        try
        {
            FileInfo info = new(fullPath);
            if (!info.Exists)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceNotAvailable);
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.ReparsePointNotAllowed);
            }
        }
        catch (ProductConfigurationExportException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.EvidenceNotAvailable);
        }
    }

    private void RequireUnchangedEvidence(ProductConfigurationEvidenceItem item)
    {
        ValidateEvidenceSelection(item);
        try
        {
            FileInfo info = new(item.SourcePath);
            if (info.Length != item.SizeBytes
                || info.LastWriteTimeUtc != item.ArchivedUtc.UtcDateTime)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.EvidenceChanged);
            }
        }
        catch (ProductConfigurationExportException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.IoFailure);
        }
    }

    private bool TryDescribeEvidenceFile(
        string path,
        out ProductConfigurationEvidenceOrigin origin,
        out ProductConfigurationEvidenceRole role)
    {
        string fileName = Path.GetFileName(path);
        Match configuration = Regex.Match(
            fileName,
            "^" + Regex.Escape(Path.GetFileName(PrimaryPath))
                + "\\.(damaged|import)\\.[0-9a-f]{32}\\.(primary|backup)$",
            RegexOptions.CultureInvariant);
        if (configuration.Success)
        {
            origin = configuration.Groups[1].Value == "damaged"
                ? ProductConfigurationEvidenceOrigin.DamagedRecovery
                : ProductConfigurationEvidenceOrigin.ImportPrevious;
            role = configuration.Groups[2].Value == "primary"
                ? ProductConfigurationEvidenceRole.Primary
                : ProductConfigurationEvidenceRole.Backup;
            return true;
        }

        if (Regex.IsMatch(
                fileName,
                "^interaction-evidence\\.[0-9a-f]{32}\\.snapshot\\.json$",
                RegexOptions.CultureInvariant))
        {
            origin = ProductConfigurationEvidenceOrigin.AnonymousInteraction;
            role = ProductConfigurationEvidenceRole.Snapshot;
            return true;
        }

        origin = default;
        role = default;
        return false;
    }

    private static void ValidateAnonymousInteractionEvidence(
        ProductAnonymousInteractionEvidence evidence)
    {
        if (evidence.SchemaVersion !=
                ProductAnonymousInteractionEvidence.CurrentSchemaVersion
            || !evidence.Anonymous
            || evidence.RealFileOperationsAllowed
            || !Enum.IsDefined(evidence.HostStatus)
            || evidence.LifecycleGeneration < 0
            || evidence.WorkspaceRevision < 0
            || evidence.TopologyGeneration < 0
            || evidence.SelectionRevision < 0
            || evidence.SelectedItemCount < 0
            || evidence.SelectedItemCount >
                ProductAnonymousInteractionEvidence.MaximumSelectedItemCount
            || (evidence.SelectedItemCount > 0
                && !evidence.ExplicitInteractionActive)
            || (evidence.FocusedItemAvailable
                && !evidence.ExplicitInteractionActive))
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.AnonymousEvidenceInvalid);
        }
    }

    private static async Task<byte[]> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false)) != 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return hash.GetHashAndReset();
    }

    private static string ValidateExportDestination(
        string destinationDirectory,
        ProductConfigurationExportDestination destination)
    {
        if (!destination.UserSelected)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.DestinationNotUserSelected);
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.NonLocalDestination);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(destinationDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.NonLocalDestination);
        }

        if (!destination.IsLocalFileSystem
            || !Path.IsPathFullyQualified(destinationDirectory)
            || !Path.IsPathFullyQualified(fullPath)
            || fullPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.NonLocalDestination);
        }

        if (destination.IsReparsePoint)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.ReparsePointNotAllowed);
        }

        try
        {
            DirectoryInfo directory = new(fullPath);
            if (!directory.Exists)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.DestinationUnavailable);
            }

            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.ReparsePointNotAllowed);
            }

            return fullPath;
        }
        catch (ProductConfigurationExportException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.DestinationUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.DestinationUnavailable);
        }
    }

    private async Task RequireUnchangedExportStoreAsync(
        ProductConfigurationExportPlan plan,
        CancellationToken cancellationToken)
    {
        string currentRevision = await ComputeStoreRevisionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
            plan.ExpectedStoreRevision,
            currentRevision,
            StringComparison.Ordinal))
        {
            throw new ProductConfigurationExportException(
                ProductConfigurationExportError.StoreChanged);
        }
    }

    private static void TryDeleteExportTemporaryFile(string path)
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
}
