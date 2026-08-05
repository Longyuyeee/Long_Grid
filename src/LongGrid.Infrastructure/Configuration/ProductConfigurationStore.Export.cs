using System.Text.RegularExpressions;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public sealed partial class ProductConfigurationStore
{
    private const int MaximumEvidenceItems = 256;
    private const int MaximumEvidenceScanEntries = 4096;
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

    public Task<ProductConfigurationEvidenceInventory> GetEvidenceInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(DirectoryPath))
        {
            return Task.FromResult(new ProductConfigurationEvidenceInventory([], false, 0));
        }

        try
        {
            List<ProductConfigurationEvidenceItem> items = [];
            int skippedUnsafeCount = 0;
            int scannedEntries = 0;
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
                Match match = Regex.Match(
                    Path.GetFileName(path),
                    "^" + Regex.Escape(Path.GetFileName(PrimaryPath))
                        + "\\.(damaged|import)\\.[0-9a-f]{32}\\.(primary|backup)$",
                    RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                FileInfo info = new(path);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skippedUnsafeCount++;
                    continue;
                }

                if (items.Count == MaximumEvidenceItems)
                {
                    truncated = true;
                    continue;
                }

                items.Add(new(
                    match.Groups[1].Value == "damaged"
                        ? ProductConfigurationEvidenceOrigin.DamagedRecovery
                        : ProductConfigurationEvidenceOrigin.ImportPrevious,
                    match.Groups[2].Value == "primary"
                        ? ProductConfigurationEvidenceRole.Primary
                        : ProductConfigurationEvidenceRole.Backup,
                    info.Length,
                    info.LastWriteTimeUtc));
            }

            return Task.FromResult(new ProductConfigurationEvidenceInventory(
                items.OrderByDescending(item => item.ArchivedUtc).ToArray(),
                truncated,
                skippedUnsafeCount));
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
