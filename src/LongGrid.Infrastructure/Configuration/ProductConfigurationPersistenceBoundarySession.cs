using System.Security.Cryptography;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationPersistenceBoundaryPhase
{
    PrepareBaseline,
    AttemptFailure,
    RecoverAndRetry,
}

public enum ProductConfigurationPersistenceBoundaryOutcome
{
    BaselinePrepared,
    ExpectedFailureObserved,
    UnexpectedSaveSuccess,
    RecoverySucceeded,
}

public sealed record ProductConfigurationPersistenceBoundaryResult(
    ProductConfigurationPersistenceBoundaryPhase Phase,
    ProductConfigurationPersistenceBoundaryOutcome Outcome,
    ProductConfigurationLoadStatus LoadStatus,
    ProductConfigurationSaveError? SaveError,
    string PrimarySha256,
    string? BackupSha256,
    bool TemporaryFilePresent);

public sealed class ProductConfigurationPersistenceBoundarySession(string directoryPath)
{
    private readonly ProductConfigurationStore store = new(directoryPath);

    public async Task<ProductConfigurationPersistenceBoundaryResult> ExecuteAsync(
        ProductConfigurationPersistenceBoundaryPhase phase,
        CancellationToken cancellationToken = default) =>
        phase switch
        {
            ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline =>
                await PrepareBaselineAsync(cancellationToken).ConfigureAwait(false),
            ProductConfigurationPersistenceBoundaryPhase.AttemptFailure =>
                await AttemptFailureAsync(cancellationToken).ConfigureAwait(false),
            ProductConfigurationPersistenceBoundaryPhase.RecoverAndRetry =>
                await RecoverAndRetryAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

    private async Task<ProductConfigurationPersistenceBoundaryResult> PrepareBaselineAsync(
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(store.DirectoryPath)
            && Directory.EnumerateFileSystemEntries(store.DirectoryPath).Any())
        {
            throw new InvalidOperationException(
                "The persistence-boundary session directory must be empty.");
        }

        await store.SaveAsync(CreateBaselineA(), cancellationToken).ConfigureAwait(false);
        await store.SaveAsync(CreateBaselineB(), cancellationToken).ConfigureAwait(false);
        return await RequireStateAsync(
                ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline,
                ProductConfigurationPersistenceBoundaryOutcome.BaselinePrepared,
                CreateBaselineB(),
                CreateBaselineA(),
                saveError: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProductConfigurationPersistenceBoundaryResult> AttemptFailureAsync(
        CancellationToken cancellationToken)
    {
        await RequireStateAsync(
                ProductConfigurationPersistenceBoundaryPhase.AttemptFailure,
                ProductConfigurationPersistenceBoundaryOutcome.ExpectedFailureObserved,
                CreateBaselineB(),
                CreateBaselineA(),
                saveError: null,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await store.SaveAsync(CreateLargeCandidate(), cancellationToken).ConfigureAwait(false);
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error is ProductConfigurationSaveError.IoFailure)
        {
            return await RequireStateAsync(
                    ProductConfigurationPersistenceBoundaryPhase.AttemptFailure,
                    ProductConfigurationPersistenceBoundaryOutcome.ExpectedFailureObserved,
                    CreateBaselineB(),
                    CreateBaselineA(),
                    exception.Error,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await RequireStateAsync(
                ProductConfigurationPersistenceBoundaryPhase.AttemptFailure,
                ProductConfigurationPersistenceBoundaryOutcome.UnexpectedSaveSuccess,
                CreateLargeCandidate(),
                CreateBaselineB(),
                saveError: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProductConfigurationPersistenceBoundaryResult> RecoverAndRetryAsync(
        CancellationToken cancellationToken)
    {
        await RequireStateAsync(
                ProductConfigurationPersistenceBoundaryPhase.RecoverAndRetry,
                ProductConfigurationPersistenceBoundaryOutcome.RecoverySucceeded,
                CreateBaselineB(),
                CreateBaselineA(),
                saveError: null,
                cancellationToken)
            .ConfigureAwait(false);

        await store.SaveAsync(CreateLargeCandidate(), cancellationToken).ConfigureAwait(false);
        return await RequireStateAsync(
                ProductConfigurationPersistenceBoundaryPhase.RecoverAndRetry,
                ProductConfigurationPersistenceBoundaryOutcome.RecoverySucceeded,
                CreateLargeCandidate(),
                CreateBaselineB(),
                saveError: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProductConfigurationPersistenceBoundaryResult> RequireStateAsync(
        ProductConfigurationPersistenceBoundaryPhase phase,
        ProductConfigurationPersistenceBoundaryOutcome outcome,
        ProductConfigurationDocument expectedPrimary,
        ProductConfigurationDocument? expectedBackup,
        ProductConfigurationSaveError? saveError,
        CancellationToken cancellationToken)
    {
        ProductConfigurationLoadResult loaded = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (loaded.Status != ProductConfigurationLoadStatus.LoadedPrimary
            || loaded.Document is null
            || !string.Equals(
                HashDocument(loaded.Document),
                HashDocument(expectedPrimary),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The product configuration store is not in the required session state.");
        }

        string primaryHash = await RequireHashAsync(store.PrimaryPath, cancellationToken)
            .ConfigureAwait(false);
        string expectedPrimaryHash = HashDocument(expectedPrimary);
        if (!string.Equals(primaryHash, expectedPrimaryHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The primary configuration fingerprint does not match the required state.");
        }

        string? backupHash = null;
        if (expectedBackup is not null)
        {
            backupHash = await RequireHashAsync(store.BackupPath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    backupHash,
                    HashDocument(expectedBackup),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The backup configuration fingerprint does not match the required state.");
            }
        }

        return new(
            phase,
            outcome,
            loaded.Status,
            saveError,
            primaryHash,
            backupHash,
            File.Exists(store.TemporaryPath));
    }

    private static async Task<string> RequireHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string HashDocument(ProductConfigurationDocument document) =>
        Convert.ToHexString(SHA256.HashData(
            ProductConfigurationJson.SerializeToUtf8Bytes(document)));

    private static ProductConfigurationDocument CreateBaselineA() =>
        ProductConfigurationDefaults.CreateEmpty() with
        {
            ProfileId = "issue24-baseline-a",
        };

    private static ProductConfigurationDocument CreateBaselineB() =>
        ProductConfigurationDefaults.CreateEmpty() with
        {
            ProfileId = "issue24-baseline-b",
        };

    private static ProductConfigurationDocument CreateLargeCandidate()
    {
        const int itemCount = ProductConfigurationLimits.MaximumItems;
        string targetPadding = new('x', 6_000);
        DesktopItemReferenceConfiguration[] items = Enumerable.Range(0, itemCount)
            .Select(index => new DesktopItemReferenceConfiguration
            {
                Id = $"candidate-item-{index:D3}",
                Kind = ConfigurationItemKind.File,
                Target = $"C:\\Issue24\\anonymous-{index:D3}-{targetPadding}",
                Behavior = ConfigurationItemBehavior.Reference,
            })
            .ToArray();

        return new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "issue24-large-candidate",
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "candidate-container",
                    Name = "Anonymous capacity candidate",
                    IsLocked = false,
                    Appearance = new()
                    {
                        Color = "#336699",
                        Opacity = 1,
                        Collapsed = false,
                    },
                    Placement = new()
                    {
                        DisplayKey = "anonymous-display",
                        XDip = 0,
                        YDip = 0,
                        WidthDip = 320,
                        HeightDip = 240,
                    },
                    Items = items,
                },
            ],
        };
    }
}
