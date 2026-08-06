using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

internal enum ProductConfigurationCompareExchangeStatus
{
    Saved,
    Conflict,
    PrimaryUnavailable,
}

public sealed partial class ProductConfigurationStore
{
    internal async Task<ProductConfigurationCompareExchangeStatus>
        CompareExchangePrimaryAsync(
            ProductConfigurationDocument replacement,
            string expectedFingerprint,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!IsCanonicalFingerprint(expectedFingerprint))
        {
            throw new ArgumentException(
                "The expected fingerprint must be canonical SHA-256 hex.",
                nameof(expectedFingerprint));
        }

        byte[] serialized;
        try
        {
            serialized = ProductConfigurationJson.SerializeToUtf8Bytes(replacement);
        }
        catch (ProductConfigurationContractException)
        {
            throw new ProductConfigurationSaveException(
                ProductConfigurationSaveError.InvalidConfiguration);
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await using FileStream writeLease =
                await AcquireWriteLeaseAsync(cancellationToken)
                    .ConfigureAwait(false);
            ProductConfigurationLoadResult current =
                await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current.Status != ProductConfigurationLoadStatus.LoadedPrimary
                || current.Document is null)
            {
                return ProductConfigurationCompareExchangeStatus
                    .PrimaryUnavailable;
            }

            string currentFingerprint =
                ProductWorkspaceConfigurationFingerprint.Compute(
                    current.Document);
            if (!string.Equals(
                currentFingerprint,
                expectedFingerprint,
                StringComparison.Ordinal))
            {
                return ProductConfigurationCompareExchangeStatus.Conflict;
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
                    await stream.WriteAsync(serialized, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                ReadAttempt staged =
                    await TryReadAsync(TemporaryPath, cancellationToken)
                        .ConfigureAwait(false);
                if (staged.Document is null)
                {
                    throw new ProductConfigurationSaveException(
                        ProductConfigurationSaveError.InvalidConfiguration);
                }

                File.Replace(
                    TemporaryPath,
                    PrimaryPath,
                    BackupPath,
                    ignoreMetadataErrors: false);
            }
            finally
            {
                TryDeleteTemporaryFile();
            }

            return ProductConfigurationCompareExchangeStatus.Saved;
        }
        catch (ProductConfigurationSaveException)
        {
            throw;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
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

    private static bool IsCanonicalFingerprint(string? value) =>
        value is { Length: 64 }
        && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'A' and <= 'F');
}
