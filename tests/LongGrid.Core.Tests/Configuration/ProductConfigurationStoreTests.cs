using System.Text;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationStoreTests
{
    [Fact]
    public async Task MissingStoreReportsFiniteMissingState()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.Missing, result.Status);
        Assert.Null(result.Document);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.PrimaryFailure);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.BackupFailure);
        Assert.Equal(ProductConfigurationError.None, result.PrimaryContractError);
        Assert.Equal(ProductConfigurationError.None, result.BackupContractError);
    }

    [Fact]
    public async Task MissingStoreLoadDoesNotCreateStorageDirectory()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.Missing, result.Status);
        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task FirstSavePublishesValidatedPrimaryWithoutTemporaryFile()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationDocument source = CreateDocument("profile-a");

        await store.SaveAsync(source);
        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, result.Status);
        Assert.Equivalent(source, result.Document, strict: true);
        Assert.True(File.Exists(store.PrimaryPath));
        Assert.True(File.Exists(store.WriteLeasePath));
        Assert.False(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.TemporaryPath));
    }

    [Fact]
    public async Task ReplacementKeepsPreviousValidatedDocumentAsBackup()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationDocument previous = CreateDocument("profile-previous");
        ProductConfigurationDocument current = CreateDocument("profile-current");

        await store.SaveAsync(previous);
        await store.SaveAsync(current);

        ProductConfigurationLoadResult result = await store.LoadAsync();
        ProductConfigurationDocument backup = ProductConfigurationJson.Deserialize(
            await File.ReadAllBytesAsync(store.BackupPath));
        Assert.Equivalent(current, result.Document, strict: true);
        Assert.Equivalent(previous, backup, strict: true);
        Assert.False(File.Exists(store.TemporaryPath));
    }

    [Fact]
    public async Task CorruptPrimaryRecoversBackupAndPreservesEvidence()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationDocument previous = CreateDocument("profile-previous");

        await store.SaveAsync(previous);
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);

        ProductConfigurationLoadResult result = await store.LoadAsync();
        ProductConfigurationSaveException exception = await Assert.ThrowsAsync<
            ProductConfigurationSaveException>(
            () => store.SaveAsync(CreateDocument("profile-replacement")));

        Assert.Equal(ProductConfigurationLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equivalent(previous, result.Document, strict: true);
        Assert.Equal(
            ProductConfigurationStorageFailure.InvalidConfiguration,
            result.PrimaryFailure);
        Assert.Equal(ProductConfigurationError.MalformedJson, result.PrimaryContractError);
        Assert.Equal(ProductConfigurationSaveError.DamagedEvidence, exception.Error);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.False(File.Exists(store.TemporaryPath));
    }

    [Fact]
    public async Task ConfirmedBackupAcceptanceArchivesDamageAndKeepsBackup()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationDocument previous = CreateDocument("profile-previous");
        await store.SaveAsync(previous);
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] backup = await File.ReadAllBytesAsync(store.BackupPath);
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);

        ProductConfigurationRecoveryResult result = await store.RecoverAsync(
            new(
                ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                UserConfirmed: true));

        ProductConfigurationLoadResult loaded = await store.LoadAsync();
        string archive = Assert.Single(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
        Assert.Equal(
            ProductConfigurationRecoveryAction.AcceptValidatedBackup,
            result.Action);
        Assert.True(result.DamagedPrimaryArchived);
        Assert.False(result.DamagedBackupArchived);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, loaded.Status);
        Assert.Equivalent(previous, loaded.Document, strict: true);
        Assert.Equal(backup, await File.ReadAllBytesAsync(store.BackupPath));
        Assert.Equal(damaged, await File.ReadAllBytesAsync(archive));
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
    }

    [Fact]
    public async Task BackupAcceptanceWithoutConfirmationMakesNoChanges()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await store.SaveAsync(CreateDocument("profile-previous"));
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] backup = await File.ReadAllBytesAsync(store.BackupPath);
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);
        File.Delete(store.WriteLeasePath);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                    UserConfirmed: false)));

        Assert.Equal(
            ProductConfigurationRecoveryError.ConfirmationRequired,
            exception.Error);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.Equal(backup, await File.ReadAllBytesAsync(store.BackupPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
        Assert.False(File.Exists(store.WriteLeasePath));
    }

    [Fact]
    public async Task BackupAcceptanceOutsideRecoveryStateCreatesNoStorage()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                    UserConfirmed: true)));

        Assert.Equal(
            ProductConfigurationRecoveryError.RecoveryNotAvailable,
            exception.Error);
        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task BackupAcceptanceUsesBoundedWriteLeaseAndPreservesDamageOnTimeout()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(40),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        await store.SaveAsync(CreateDocument("profile-previous"));
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                    UserConfirmed: true)));

        Assert.Equal(
            ProductConfigurationRecoveryError.WriteLeaseUnavailable,
            exception.Error);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
    }

    [Fact]
    public async Task BackupAcceptanceLeaseWaitHonorsCallerCancellation()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromSeconds(2),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(10));
        await store.SaveAsync(CreateDocument("profile-previous"));
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                    UserConfirmed: true),
                cancellation.Token));

        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
    }

    [Fact]
    public async Task BackupAcceptanceIoFailureIsFiniteAndPreservesDamage()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await store.SaveAsync(CreateDocument("profile-previous"));
        await store.SaveAsync(CreateDocument("profile-current"));
        byte[] damaged = Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);
        Directory.CreateDirectory(store.PrimaryPath + ".recovery.new");

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                    UserConfirmed: true)));

        Assert.Equal(ProductConfigurationRecoveryError.IoFailure, exception.Error);
        Assert.DoesNotContain(directory.Path, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
    }

    [Fact]
    public async Task BackupAcceptanceRejectsNullAndUnknownRequestsBeforeStorage()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.RecoverAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.RecoverAsync(
                new((ProductConfigurationRecoveryAction)999, UserConfirmed: true)));

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task ConfirmedSafeModeResetArchivesBothDamagedFilesAndPublishesEmptyDefault()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        byte[] damagedPrimary = Encoding.UTF8.GetBytes("{ damaged-primary");
        byte[] damagedBackup = Encoding.UTF8.GetBytes("{ damaged-backup");
        await File.WriteAllBytesAsync(store.PrimaryPath, damagedPrimary);
        await File.WriteAllBytesAsync(store.BackupPath, damagedBackup);

        ProductConfigurationRecoveryResult result = await store.RecoverAsync(
            new(
                ProductConfigurationRecoveryAction.ResetSafeMode,
                UserConfirmed: true));

        ProductConfigurationLoadResult loaded = await store.LoadAsync();
        string primaryArchive = Assert.Single(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.primary"));
        string backupArchive = Assert.Single(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.backup"));
        Assert.Equal(ProductConfigurationRecoveryAction.ResetSafeMode, result.Action);
        Assert.True(result.DamagedPrimaryArchived);
        Assert.True(result.DamagedBackupArchived);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, loaded.Status);
        Assert.Equivalent(
            ProductConfigurationDefaults.CreateEmpty(),
            loaded.Document,
            strict: true);
        Assert.Equal(damagedPrimary, await File.ReadAllBytesAsync(primaryArchive));
        Assert.Equal(damagedBackup, await File.ReadAllBytesAsync(backupArchive));
        Assert.False(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
    }

    [Fact]
    public async Task SafeModeResetArchivesOnlyExistingPrimaryEvidence()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        byte[] damagedPrimary = Encoding.UTF8.GetBytes("{ damaged-primary");
        await File.WriteAllBytesAsync(store.PrimaryPath, damagedPrimary);

        ProductConfigurationRecoveryResult result = await store.RecoverAsync(
            new(
                ProductConfigurationRecoveryAction.ResetSafeMode,
                UserConfirmed: true));

        Assert.True(result.DamagedPrimaryArchived);
        Assert.False(result.DamagedBackupArchived);
        Assert.Single(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.primary"));
        Assert.Empty(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.backup"));
        Assert.Equal(
            ProductConfigurationLoadStatus.LoadedPrimary,
            (await store.LoadAsync()).Status);
    }

    [Fact]
    public async Task SafeModeResetArchivesOnlyExistingBackupEvidence()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        byte[] damagedBackup = Encoding.UTF8.GetBytes("{ damaged-backup");
        await File.WriteAllBytesAsync(store.BackupPath, damagedBackup);

        ProductConfigurationRecoveryResult result = await store.RecoverAsync(
            new(
                ProductConfigurationRecoveryAction.ResetSafeMode,
                UserConfirmed: true));

        Assert.False(result.DamagedPrimaryArchived);
        Assert.True(result.DamagedBackupArchived);
        Assert.Empty(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.primary"));
        string backupArchive = Assert.Single(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.backup"));
        Assert.Equal(damagedBackup, await File.ReadAllBytesAsync(backupArchive));
        Assert.Equal(
            ProductConfigurationLoadStatus.LoadedPrimary,
            (await store.LoadAsync()).Status);
    }

    [Fact]
    public async Task SafeModeResetWithoutConfirmationMakesNoChanges()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        byte[] damagedPrimary = Encoding.UTF8.GetBytes("{ damaged-primary");
        await File.WriteAllBytesAsync(store.PrimaryPath, damagedPrimary);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.ResetSafeMode,
                    UserConfirmed: false)));

        Assert.Equal(
            ProductConfigurationRecoveryError.ConfirmationRequired,
            exception.Error);
        Assert.Equal(damagedPrimary, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "configuration.json.damaged.*"));
        Assert.False(File.Exists(store.WriteLeasePath));
    }

    [Fact]
    public async Task SafeModeResetIsRejectedOutsideSafeMode()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.ResetSafeMode,
                    UserConfirmed: true)));

        Assert.Equal(
            ProductConfigurationRecoveryError.RecoveryNotAvailable,
            exception.Error);
        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task InterruptedSafeModeResetMarkerPreventsMissingStateAndCanResume()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await File.WriteAllBytesAsync(
            store.PrimaryPath + ".recovery.new",
            ProductConfigurationJson.SerializeToUtf8Bytes(
                ProductConfigurationDefaults.CreateEmpty()));

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.SafeMode, result.Status);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.PrimaryFailure);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.BackupFailure);

        ProductConfigurationRecoveryResult resumed = await store.RecoverAsync(
            new(
                ProductConfigurationRecoveryAction.ResetSafeMode,
                UserConfirmed: true));

        Assert.False(resumed.DamagedPrimaryArchived);
        Assert.False(resumed.DamagedBackupArchived);
        Assert.Equal(
            ProductConfigurationLoadStatus.LoadedPrimary,
            (await store.LoadAsync()).Status);
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
    }

    [Fact]
    public async Task SafeModeResetRollsBackBackupMoveWhenPrimaryPublishFails()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        Directory.CreateDirectory(store.PrimaryPath);
        byte[] damagedBackup = Encoding.UTF8.GetBytes("{ damaged-backup");
        await File.WriteAllBytesAsync(store.BackupPath, damagedBackup);

        ProductConfigurationRecoveryException exception = await Assert.ThrowsAsync<
            ProductConfigurationRecoveryException>(
            () => store.RecoverAsync(
                new(
                    ProductConfigurationRecoveryAction.ResetSafeMode,
                    UserConfirmed: true)));

        Assert.Equal(ProductConfigurationRecoveryError.IoFailure, exception.Error);
        Assert.Equal(damagedBackup, await File.ReadAllBytesAsync(store.BackupPath));
        Assert.Empty(
            Directory.GetFiles(directory.Path, "configuration.json.damaged.*.backup"));
        Assert.False(File.Exists(store.PrimaryPath + ".recovery.new"));
        Assert.Equal(
            ProductConfigurationLoadStatus.SafeMode,
            (await store.LoadAsync()).Status);
    }

    [Fact]
    public async Task InvalidPrimaryWithoutBackupEntersSafeMode()
    {
        using TemporaryDirectory directory = new();
        Directory.CreateDirectory(directory.Path);
        ProductConfigurationStore store = new(directory.Path);
        await File.WriteAllTextAsync(store.PrimaryPath, string.Empty);

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.SafeMode, result.Status);
        Assert.Null(result.Document);
        Assert.Equal(ProductConfigurationStorageFailure.Empty, result.PrimaryFailure);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.BackupFailure);
    }

    [Fact]
    public async Task InvalidDocumentIsRejectedBeforeStorageIsCreated()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationDocument invalid = CreateDocument("profile") with
        {
            SchemaVersion = 99,
        };

        ProductConfigurationSaveException exception = await Assert.ThrowsAsync<
            ProductConfigurationSaveException>(() => store.SaveAsync(invalid));

        Assert.Equal(ProductConfigurationSaveError.InvalidConfiguration, exception.Error);
        Assert.False(Directory.Exists(directory.Path));
        Assert.DoesNotContain("profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedPrimaryEntersSafeModeBeforeAllocation()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await using (FileStream stream = new(store.PrimaryPath, FileMode.CreateNew))
        {
            stream.SetLength(ProductConfigurationLimits.MaximumSerializedBytes + 1L);
        }

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.SafeMode, result.Status);
        Assert.Equal(ProductConfigurationStorageFailure.TooLarge, result.PrimaryFailure);
    }

    [Fact]
    public async Task LockedWriteLeaseTimesOutWithFiniteFailureThenRecovers()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(40),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        Directory.CreateDirectory(directory.Path);

        await using (FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            ProductConfigurationSaveException exception = await Assert.ThrowsAsync<
                ProductConfigurationSaveException>(
                () => store.SaveAsync(CreateDocument("profile-blocked")));
            Assert.Equal(
                ProductConfigurationSaveError.WriteLeaseUnavailable,
                exception.Error);
        }

        await store.SaveAsync(CreateDocument("profile-recovered"));
        Assert.Equal(
            "profile-recovered",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task WriteLeaseWaitHonorsCallerCancellation()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromSeconds(2),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(10));
        Directory.CreateDirectory(directory.Path);
        await using FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(CreateDocument("profile-cancelled"), cancellation.Token));

        Assert.False(File.Exists(store.PrimaryPath));
        Assert.False(File.Exists(store.TemporaryPath));
    }

    [Fact]
    public async Task InaccessiblePrimaryProducesIoSafeModeWithoutPathDisclosure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await File.WriteAllBytesAsync(
            store.PrimaryPath,
            ProductConfigurationJson.SerializeToUtf8Bytes(CreateDocument("profile")));
        await using FileStream exclusive = new(
            store.PrimaryPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ProductConfigurationLoadResult result = await store.LoadAsync();

        Assert.Equal(ProductConfigurationLoadStatus.SafeMode, result.Status);
        Assert.Equal(ProductConfigurationStorageFailure.IoFailure, result.PrimaryFailure);
        Assert.Equal(ProductConfigurationStorageFailure.Missing, result.BackupFailure);
    }

    [Fact]
    public async Task IoFailureDuringDirectoryCreationUsesFiniteSaveError()
    {
        using TemporaryDirectory directory = new();
        string occupiedPath = Path.Combine(directory.Path, "occupied");
        await File.WriteAllTextAsync(occupiedPath, "not a directory");
        ProductConfigurationStore store = new(Path.Combine(occupiedPath, "child"));

        ProductConfigurationSaveException exception = await Assert.ThrowsAsync<
            ProductConfigurationSaveException>(
            () => store.SaveAsync(CreateDocument("profile")));

        Assert.Equal(ProductConfigurationSaveError.IoFailure, exception.Error);
        Assert.DoesNotContain(occupiedPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../configuration.json")]
    [InlineData("folder/configuration.json")]
    public void FileNameMustBeSimple(string fileName)
    {
        using TemporaryDirectory directory = new();

        Assert.Throws<ArgumentException>(
            () => new ProductConfigurationStore(directory.Path, fileName));
    }

    [Fact]
    public void RetryPolicyMustBeBoundedAndPositive()
    {
        using TemporaryDirectory directory = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProductConfigurationStore(
                directory.Path,
                writeLeaseTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProductConfigurationStore(
                directory.Path,
                writeLeaseRetryDelay: TimeSpan.Zero));
    }

    [Fact]
    public async Task SaveCoordinatorCoalescesWaitingDocumentsToLatestSnapshot()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = CreatePatientStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);
        ProductConfigurationSaveCoordinator coordinator = new(store);

        Task first = coordinator.EnqueueAsync(CreateDocument("profile-first"));
        Task[] merged = Enumerable.Range(1, 100)
            .Select(index => coordinator.EnqueueAsync(CreateDocument($"profile-{index}")))
            .ToArray();

        await lease.DisposeAsync();
        await Task.WhenAll(merged.Prepend(first));
        await coordinator.CompleteAsync();

        ProductConfigurationLoadResult result = await store.LoadAsync();
        ProductConfigurationDocument backup = ProductConfigurationJson.Deserialize(
            await File.ReadAllBytesAsync(store.BackupPath));
        Assert.Equal("profile-100", result.Document?.ProfileId);
        Assert.Equal("profile-first", backup.ProfileId);
    }

    [Fact]
    public async Task SaveCoordinatorSnapshotsMutableExtensionDataAtEnqueue()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = CreatePatientStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);
        ProductConfigurationSaveCoordinator coordinator = new(store);
        Dictionary<string, System.Text.Json.JsonElement> extensionData = new()
        {
            ["future"] = System.Text.Json.JsonSerializer.SerializeToElement(1),
        };
        ProductConfigurationDocument source = CreateDocument("profile") with
        {
            ExtensionData = extensionData,
        };

        Task save = coordinator.EnqueueAsync(source);
        extensionData["future"] = System.Text.Json.JsonSerializer.SerializeToElement(2);
        await lease.DisposeAsync();
        await save;
        await coordinator.CompleteAsync();

        ProductConfigurationDocument persisted = (await store.LoadAsync()).Document!;
        Assert.Equal(1, persisted.ExtensionData!["future"].GetInt32());
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelAcceptedSave()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = CreatePatientStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);
        ProductConfigurationSaveCoordinator coordinator = new(store);
        using CancellationTokenSource cancellation = new();

        Task save = coordinator.EnqueueAsync(
            CreateDocument("profile-accepted"),
            cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);

        await lease.DisposeAsync();
        await coordinator.CompleteAsync();
        Assert.Equal(
            "profile-accepted",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task CompleteTimeoutKeepsAcceptedSaveAndRejectsNewWork()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = CreatePatientStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await using FileStream lease = AcquireLease(store.WriteLeasePath);
        ProductConfigurationSaveCoordinator coordinator = new(store);
        Task accepted = coordinator.EnqueueAsync(CreateDocument("profile-accepted"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.CompleteAsync(timeout.Token));
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = coordinator.EnqueueAsync(CreateDocument("profile-rejected"));
            });

        await lease.DisposeAsync();
        await accepted;
        await coordinator.CompleteAsync();
        Assert.Equal(
            "profile-accepted",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task FailedBatchDoesNotStopLaterRecoveryBatch()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(store.PrimaryPath, "{ damaged");
        ProductConfigurationSaveCoordinator coordinator = new(store);

        ProductConfigurationSaveException exception = await Assert.ThrowsAsync<
            ProductConfigurationSaveException>(
            () => coordinator.EnqueueAsync(CreateDocument("profile-failed")));
        Assert.Equal(ProductConfigurationSaveError.DamagedEvidence, exception.Error);

        File.Delete(store.PrimaryPath);
        await coordinator.EnqueueAsync(CreateDocument("profile-recovered"));
        await coordinator.CompleteAsync();
        Assert.Equal(
            "profile-recovered",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task EmptyCoordinatorCompletesAndRejectsNewWork()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationSaveCoordinator coordinator = new(
            new ProductConfigurationStore(directory.Path));

        await coordinator.CompleteAsync();

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = coordinator.EnqueueAsync(CreateDocument("profile"));
            });
        Assert.False(Directory.Exists(directory.Path));
    }

    private static ProductConfigurationStore CreatePatientStore(string directoryPath) =>
        new(
            directoryPath,
            writeLeaseTimeout: TimeSpan.FromSeconds(5),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));

    private static FileStream AcquireLease(string path) =>
        new(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private static ProductConfigurationDocument CreateDocument(string profileId) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = profileId,
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = false,
                    Appearance = new ContainerAppearanceConfiguration
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = false,
                    },
                    Placement = new ContainerPlacementConfiguration
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        new DesktopItemReferenceConfiguration
                        {
                            Id = "item-1",
                            Kind = ConfigurationItemKind.Folder,
                            Target = "%USERPROFILE%\\Documents\\Project",
                            Behavior = ConfigurationItemBehavior.Reference,
                        },
                    ],
                },
            ],
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LongGrid.Infrastructure.Tests",
                Guid.NewGuid().ToString("N"));
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
