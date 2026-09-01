using System.Text;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

public sealed record ProductWorkspaceRecoveryPreflightResult(
    string Outcome,
    int ScenarioCount,
    bool BackupAcceptedAfterRestart,
    bool SafeModeResetAfterRestart,
    bool RestartSafePointRecovered,
    bool CatalogRecovered,
    bool ExplicitRetrySucceeded,
    bool CancellationLeftNoRetry,
    bool TemporarySandboxCleaned,
    bool ReadsRealDesktop,
    bool RealFileOperationsAllowed);

public static class ProductWorkspaceRecoveryPreflight
{
    public const int ScenarioCount = 6;

    public static async Task<ProductWorkspaceRecoveryPreflightResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ProductRecoveryPreflight",
            Guid.NewGuid().ToString("N"));
        bool cleaned = false;
        try
        {
            Directory.CreateDirectory(sandbox);
            bool backupAccepted = await VerifyBackupAcceptanceAfterRestartAsync(
                Path.Combine(sandbox, "backup"),
                cancellationToken).ConfigureAwait(false);
            bool safeModeReset = await VerifySafeModeResetAfterRestartAsync(
                Path.Combine(sandbox, "safe-mode"),
                cancellationToken).ConfigureAwait(false);
            bool restartSafePoint = await VerifyRestartSafePointAsync(
                Path.Combine(sandbox, "restart-safe-point"),
                cancellationToken).ConfigureAwait(false);
            bool catalogRecovered = await VerifyCatalogRecoveryAsync(
                Path.Combine(sandbox, "catalog"),
                cancellationToken).ConfigureAwait(false);
            bool explicitRetry = await VerifyExplicitRetryAsync(
                Path.Combine(sandbox, "retry"),
                cancellationToken).ConfigureAwait(false);
            bool cancellation = await VerifyCancellationAsync(
                Path.Combine(sandbox, "cancellation"),
                cancellationToken).ConfigureAwait(false);

            DeleteSandbox(sandbox);
            cleaned = !Directory.Exists(sandbox);
            Require(cleaned, "The temporary recovery sandbox was not removed.");
            return new(
                "Passed",
                ScenarioCount,
                backupAccepted,
                safeModeReset,
                restartSafePoint,
                catalogRecovered,
                explicitRetry,
                cancellation,
                cleaned,
                ReadsRealDesktop: false,
                RealFileOperationsAllowed: false);
        }
        finally
        {
            if (!cleaned)
            {
                DeleteSandbox(sandbox);
            }
        }
    }

    private static async Task<bool> VerifyRestartSafePointAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var original = new ProductConfigurationStore(directory);
        await original.SaveAsync(
            CreateDocument("restart-before"),
            cancellationToken).ConfigureAwait(false);
        await original.SaveAsync(
            CreateDocument("restart-after"),
            cancellationToken).ConfigureAwait(false);

        var restarted = new ProductConfigurationStore(directory);
        ProductConfigurationRestartRecoverySnapshot point =
            await restarted.GetRestartRecoveryPointAsync(cancellationToken)
                .ConfigureAwait(false);
        Require(point.IsAvailable, "A restarted store did not expose its safe point.");
        ProductConfigurationRestartRecoveryResult restored =
            await restarted.RestoreRestartRecoveryPointAsync(
                    point.Point!,
                    userConfirmed: true,
                    cancellationToken)
                .ConfigureAwait(false);
        var verifiedRestart = new ProductConfigurationStore(directory);
        ProductConfigurationLoadResult loaded =
            await verifiedRestart.LoadAsync(cancellationToken).ConfigureAwait(false);
        ProductConfigurationRestartRecoverySnapshot consumed =
            await verifiedRestart.GetRestartRecoveryPointAsync(cancellationToken)
                .ConfigureAwait(false);
        Require(
            restored.IsRestored
                && loaded.Status == ProductConfigurationLoadStatus.LoadedPrimary
                && loaded.Document?.ProfileId == "restart-before"
                && consumed.Availability ==
                    ProductConfigurationRestartRecoveryAvailability.Unavailable,
            "The restart safe point did not restore once and remain consumed.");
        return true;
    }

    private static async Task<bool> VerifyBackupAcceptanceAfterRestartAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var original = new ProductConfigurationStore(directory);
        await original.SaveAsync(CreateDocument("backup-baseline"), cancellationToken)
            .ConfigureAwait(false);
        await original.SaveAsync(CreateDocument("newer-primary"), cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            original.PrimaryPath,
            Encoding.UTF8.GetBytes("{ damaged-primary"),
            cancellationToken).ConfigureAwait(false);

        var restarted = new ProductConfigurationStore(directory);
        ProductConfigurationLoadResult before = await restarted.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Require(
            before.Status == ProductConfigurationLoadStatus.RecoveredFromBackup
                && before.Document?.ProfileId == "backup-baseline",
            "A restarted store did not expose the validated backup read-only.");
        ProductConfigurationRecoveryResult recovery = await restarted.RecoverAsync(
            new(ProductConfigurationRecoveryAction.AcceptValidatedBackup, UserConfirmed: true),
            cancellationToken).ConfigureAwait(false);
        ProductConfigurationLoadResult after = await restarted.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Require(
            recovery.DamagedPrimaryArchived
                && !recovery.DamagedBackupArchived
                && after.Status == ProductConfigurationLoadStatus.LoadedPrimary
                && after.Document?.ProfileId == "backup-baseline",
            "Confirmed backup acceptance did not publish a writable primary.");
        return true;
    }

    private static async Task<bool> VerifySafeModeResetAfterRestartAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var paths = new ProductConfigurationStore(directory);
        await File.WriteAllTextAsync(
            paths.PrimaryPath,
            "{ damaged-primary",
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            paths.BackupPath,
            "{ damaged-backup",
            cancellationToken).ConfigureAwait(false);

        var restarted = new ProductConfigurationStore(directory);
        ProductConfigurationLoadResult before = await restarted.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Require(
            before.Status == ProductConfigurationLoadStatus.SafeMode,
            "A restarted store did not fail closed for two damaged documents.");
        ProductConfigurationRecoveryResult recovery = await restarted.RecoverAsync(
            new(ProductConfigurationRecoveryAction.ResetSafeMode, UserConfirmed: true),
            cancellationToken).ConfigureAwait(false);
        ProductConfigurationLoadResult after = await restarted.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Require(
            recovery.DamagedPrimaryArchived
                && recovery.DamagedBackupArchived
                && after.Status == ProductConfigurationLoadStatus.LoadedPrimary
                && after.Document?.SchemaVersion
                    == ProductConfigurationLimits.CurrentSchemaVersion
                && after.Document.ProfileId == "default"
                && after.Document.Containers.Count == 0,
            "Confirmed safe-mode reset did not archive both documents and publish defaults.");
        return true;
    }

    private static async Task<bool> VerifyCatalogRecoveryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string target = Path.Combine(directory, "synthetic-item.txt");
        var store = new ProductConfigurationStore(directory);
        await store.SaveAsync(CreateDocument("catalog-profile", target), cancellationToken)
            .ConfigureAwait(false);
        ProductConfigurationLoadResult load = await store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ProductWorkspaceSessionSnapshot unavailable = ProductWorkspaceSessionLoader.Load(
            load,
            ProductWorkspaceCatalogSnapshot.Unavailable);
        ProductWorkspaceSessionSnapshot available = ProductWorkspaceSessionLoader.Load(
            load,
            ProductWorkspaceCatalogSnapshot.Available(
            [
                new DesktopCatalogEntry(
                    new DesktopItemIdentity("filesystem", target),
                    "synthetic-desktop",
                    "Anonymous item",
                    DesktopItemKind.File),
            ]));
        Require(
            unavailable.Status == ProductWorkspaceSessionStatus.AwaitingCatalog
                && !unavailable.HasResolvedState
                && available.Status == ProductWorkspaceSessionStatus.Ready
                && available.Summary.Resolved == 1,
            "Catalog unavailability did not remain finite or recover to one resolved item.");
        return true;
    }

    private static async Task<bool> VerifyExplicitRetryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var store = new ProductConfigurationStore(directory);
        await File.WriteAllTextAsync(
            store.PrimaryPath,
            "{ damaged-primary",
            cancellationToken).ConfigureAwait(false);
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        ProductConfigurationSaveAttemptResult failed = await workflow.SaveAsync(
            CreateDocument("retry-profile"),
            cancellationToken).ConfigureAwait(false);
        File.Delete(store.PrimaryPath);
        ProductConfigurationSaveAttemptResult retried = await workflow.RetryAsync(
            cancellationToken).ConfigureAwait(false);
        await workflow.CompleteAsync(cancellationToken).ConfigureAwait(false);
        Require(
            failed.Status == ProductConfigurationSaveAttemptStatus.Failed
                && failed.Error == ProductConfigurationSaveError.DamagedEvidence
                && failed.CanRetry
                && retried.Status == ProductConfigurationSaveAttemptStatus.Saved
                && (await store.LoadAsync(cancellationToken).ConfigureAwait(false))
                    .Document?.ProfileId == "retry-profile",
            "A retryable save failure did not recover through explicit retry.");
        return true;
    }

    private static async Task<bool> VerifyCancellationAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var store = new ProductConfigurationStore(
            directory,
            writeLeaseTimeout: TimeSpan.FromSeconds(2),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        await using FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        Task<ProductConfigurationSaveAttemptResult> pending = workflow.SaveAsync(
            CreateDocument("cancelled-profile"),
            cancellation.Token);
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        ProductConfigurationSaveAttemptResult retry = await workflow.RetryAsync(
            cancellationToken).ConfigureAwait(false);
        Require(
            cancelled
                && retry.Status == ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
            "Caller cancellation created an ambiguous retry intent.");
        await lease.DisposeAsync().ConfigureAwait(false);
        await workflow.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static ProductConfigurationDocument CreateDocument(
        string profileId,
        string? target = null) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = profileId,
            Containers = target is null
                ? []
                :
                [
                    new ContainerConfiguration
                    {
                        Id = "container-1",
                        Name = "Recovery workspace",
                        Appearance = new()
                        {
                            Color = "#334155",
                            Opacity = 0.8,
                        },
                        Placement = new()
                        {
                            DisplayKey = "display-1",
                            WidthDip = 420,
                            HeightDip = 300,
                        },
                        Items =
                        [
                            new DesktopItemReferenceConfiguration
                            {
                                Id = "item-1",
                                Kind = ConfigurationItemKind.File,
                                Target = target,
                                Behavior = ConfigurationItemBehavior.Reference,
                            },
                        ],
                    },
                ],
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DeleteSandbox(string sandbox)
    {
        if (Directory.Exists(sandbox))
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }
}
