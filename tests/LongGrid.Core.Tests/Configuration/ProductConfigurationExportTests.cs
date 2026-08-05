using System.Globalization;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationExportTests
{
    [Fact]
    public async Task MissingStoreCannotPrepareExportAndCreatesNothing()
    {
        using TemporaryDirectory storeDirectory = new(create: false);
        ProductConfigurationStore store = new(storeDirectory.Path);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.PrepareExportAsync());

        Assert.Equal(ProductConfigurationExportError.ExportNotAvailable, exception.Error);
        Assert.False(Directory.Exists(storeDirectory.Path));
    }

    [Fact]
    public async Task ConfirmedExportPublishesValidatedCopyWithoutChangingStore()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        ProductConfigurationDocument document = CreateDocument("export-primary");
        await store.SaveAsync(document);
        byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);

        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();
        ProductConfigurationExportResult result = await store.ExportAsync(
            plan,
            destination.Path,
            LocalDestination(),
            userConfirmed: true);

        string exportedPath = Path.Combine(destination.Path, result.FileName);
        ProductConfigurationDocument exported = ProductConfigurationJson.Deserialize(
            await File.ReadAllBytesAsync(exportedPath));
        Assert.Equal(ProductConfigurationExportSourceState.LoadedPrimary, plan.Preview.SourceState);
        Assert.Equal(document.SchemaVersion, plan.Preview.SchemaVersion);
        Assert.Equal(document.Containers.Count, plan.Preview.ContainerCount);
        Assert.Equal(1, plan.Preview.ItemCount);
        Assert.Equivalent(document, exported, strict: true);
        Assert.Equal(primaryBefore, await File.ReadAllBytesAsync(store.PrimaryPath));
        Assert.DoesNotContain(destination.Path, result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(destination.Path, "*.new"));
    }

    [Fact]
    public async Task RecoveredBackupCanBeExportedWithoutChangingDamagedPrimary()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        ProductConfigurationDocument backupDocument = CreateDocument("backup");
        await store.SaveAsync(backupDocument);
        await store.SaveAsync(CreateDocument("primary"));
        byte[] damaged = "{ damaged"u8.ToArray();
        await File.WriteAllBytesAsync(store.PrimaryPath, damaged);

        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();
        ProductConfigurationExportResult result = await store.ExportAsync(
            plan,
            destination.Path,
            LocalDestination(),
            userConfirmed: true);

        ProductConfigurationDocument exported = ProductConfigurationJson.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(destination.Path, result.FileName)));
        Assert.Equal(
            ProductConfigurationExportSourceState.RecoveredBackupReadOnly,
            plan.Preview.SourceState);
        Assert.Equivalent(backupDocument, exported, strict: true);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(store.PrimaryPath));
    }

    [Fact]
    public async Task UnconfirmedExportCreatesNoDestinationFile()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                destination.Path,
                LocalDestination(),
                userConfirmed: false));

        Assert.Equal(ProductConfigurationExportError.ConfirmationRequired, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task StoreChangeAfterPreviewPreventsExport()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("before"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();
        await store.SaveAsync(CreateDocument("after"));

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.StoreChanged, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Theory]
    [InlineData(false, true, false, ProductConfigurationExportError.DestinationNotUserSelected)]
    [InlineData(true, false, false, ProductConfigurationExportError.NonLocalDestination)]
    [InlineData(true, true, true, ProductConfigurationExportError.ReparsePointNotAllowed)]
    public async Task DestinationPolicyRejectsUnsafeMetadata(
        bool userSelected,
        bool isLocal,
        bool isReparsePoint,
        ProductConfigurationExportError expected)
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                destination.Path,
                new(userSelected, isLocal, isReparsePoint),
                userConfirmed: true));

        Assert.Equal(expected, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task MissingDestinationDirectoryIsRejectedWithoutCreatingIt()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new(create: false);
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(
            ProductConfigurationExportError.DestinationUnavailable,
            exception.Error);
        Assert.False(Directory.Exists(destination.Path));
    }

    [Fact]
    public async Task InvalidDestinationPathIsRejectedAsNonLocal()
    {
        using TemporaryDirectory storeDirectory = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                "\0",
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.NonLocalDestination, exception.Error);
    }

    [Fact]
    public async Task RelativeDestinationPathIsRejectedAsNonLocal()
    {
        using TemporaryDirectory storeDirectory = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                "relative-export",
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.NonLocalDestination, exception.Error);
    }

    [Fact]
    public async Task VirtualDestinationWithoutLocalPathReturnsFiniteError()
    {
        using TemporaryDirectory storeDirectory = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await store.SaveAsync(CreateDocument("profile"));
        ProductConfigurationExportPlan plan = await store.PrepareExportAsync();

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportAsync(
                plan,
                string.Empty,
                new(
                    UserSelected: true,
                    IsLocalFileSystem: false,
                    IsReparsePoint: false),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.NonLocalDestination, exception.Error);
    }

    [Fact]
    public async Task MissingEvidenceInventoryIsEmptyAndDoesNotCreateDirectory()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationStore store = new(directory.Path);

        ProductConfigurationEvidenceInventory inventory =
            await store.GetEvidenceInventoryAsync();

        Assert.Empty(inventory.Items);
        Assert.False(inventory.Truncated);
        Assert.Equal(0, inventory.SkippedUnsafeCount);
        Assert.Equal(0, inventory.ObservedItemCount);
        Assert.Equal(0, inventory.ObservedSizeBytes);
        Assert.Null(inventory.OldestObservedArchivedUtc);
        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task EvidenceInventoryIncludesOnlyExactArchiveNamesWithoutIdentifiers()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string damaged = Path.Combine(
            directory.Path,
            "configuration.json.damaged.0123456789abcdef0123456789abcdef.primary");
        string imported = Path.Combine(
            directory.Path,
            "configuration.json.import.fedcba9876543210fedcba9876543210.backup");
        await File.WriteAllBytesAsync(damaged, new byte[7]);
        await File.WriteAllBytesAsync(imported, new byte[11]);
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "configuration.json.import.next"),
            "ignored");
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "configuration.json.damaged.not-an-id.primary"),
            "ignored");

        ProductConfigurationEvidenceInventory inventory =
            await store.GetEvidenceInventoryAsync();

        Assert.Collection(
            inventory.Items.OrderBy(item => item.SizeBytes),
            item =>
            {
                Assert.Equal(ProductConfigurationEvidenceOrigin.DamagedRecovery, item.Origin);
                Assert.Equal(ProductConfigurationEvidenceRole.Primary, item.Role);
                Assert.Equal(7, item.SizeBytes);
            },
            item =>
            {
                Assert.Equal(ProductConfigurationEvidenceOrigin.ImportPrevious, item.Origin);
                Assert.Equal(ProductConfigurationEvidenceRole.Backup, item.Role);
                Assert.Equal(11, item.SizeBytes);
            });
        Assert.False(inventory.Truncated);
        Assert.Equal(2, inventory.ObservedItemCount);
        Assert.Equal(18, inventory.ObservedSizeBytes);
        Assert.NotNull(inventory.OldestObservedArchivedUtc);
        Assert.DoesNotContain("0123456789abcdef", inventory.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(directory.Path, inventory.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceInventoryIsBoundedAndReportsTruncation()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        for (int index = 0; index < 257; index++)
        {
            string identifier = index.ToString("x32", CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(
                Path.Combine(
                    directory.Path,
                    $"configuration.json.damaged.{identifier}.primary"),
                []);
        }

        ProductConfigurationEvidenceInventory inventory =
            await store.GetEvidenceInventoryAsync();

        Assert.Equal(256, inventory.Items.Count);
        Assert.Equal(257, inventory.ObservedItemCount);
        Assert.Equal(0, inventory.ObservedSizeBytes);
        Assert.True(inventory.Truncated);
    }

    [Fact]
    public async Task EvidenceDirectoryScanIsBoundedAndReportsTruncation()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        for (int index = 0; index < 4097; index++)
        {
            string identifier = index.ToString("x8", CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(
                Path.Combine(directory.Path, $"unrelated-{identifier}.tmp"),
                []);
        }

        ProductConfigurationEvidenceInventory inventory =
            await store.GetEvidenceInventoryAsync();

        Assert.Empty(inventory.Items);
        Assert.True(inventory.Truncated);
    }

    [Fact]
    public async Task ConfirmedEvidenceRemovalDeletesOnlySelectedExactArchive()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string selectedPath = CreateEvidencePath(directory.Path);
        string preservedPath = Path.Combine(
            directory.Path,
            "configuration.json.import.fedcba9876543210fedcba9876543210.backup");
        string unrelatedPath = Path.Combine(directory.Path, "unrelated.txt");
        await File.WriteAllBytesAsync(selectedPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(preservedPath, [4, 5]);
        await File.WriteAllTextAsync(unrelatedPath, "keep");
        ProductConfigurationEvidenceItem selected = (await store.GetEvidenceInventoryAsync())
            .Items
            .Single(item => item.SizeBytes == 3);

        ProductConfigurationEvidenceRemovalResult result = await store.RemoveEvidenceAsync(
            selected,
            userConfirmed: true);

        Assert.Equal(ProductConfigurationEvidenceOrigin.DamagedRecovery, result.Origin);
        Assert.Equal(ProductConfigurationEvidenceRole.Primary, result.Role);
        Assert.Equal(3, result.SizeBytes);
        Assert.False(File.Exists(selectedPath));
        Assert.Equal(new byte[] { 4, 5 }, await File.ReadAllBytesAsync(preservedPath));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelatedPath));
    }

    [Fact]
    public async Task UnconfirmedEvidenceRemovalPreservesArchive()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.RemoveEvidenceAsync(
                item,
                userConfirmed: false));

        Assert.Equal(ProductConfigurationExportError.ConfirmationRequired, exception.Error);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(evidencePath));
    }

    [Fact]
    public async Task ChangedEvidenceRemovalPreservesChangedArchive()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3, 4]);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.RemoveEvidenceAsync(
                item,
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.EvidenceChanged, exception.Error);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(evidencePath));
    }

    [Fact]
    public async Task MissingEvidenceRemovalReturnsFiniteError()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        File.Delete(evidencePath);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.RemoveEvidenceAsync(
                item,
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.EvidenceNotAvailable, exception.Error);
    }

    [Fact]
    public async Task EvidenceRemovalUsesBoundedWriteLease()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(40),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        await using FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.RemoveEvidenceAsync(
                item,
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.WriteLeaseUnavailable, exception.Error);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(evidencePath));
    }

    [Fact]
    public async Task EvidenceRemovalContentionPreservesArchive()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        await using FileStream reader = new(
            evidencePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.RemoveEvidenceAsync(
                item,
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.IoFailure, exception.Error);
        Assert.True(File.Exists(evidencePath));
    }

    [Fact]
    public async Task CancelledEvidenceRemovalPreservesArchive()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        string evidencePath = CreateEvidencePath(directory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RemoveEvidenceAsync(item, true, cancellation.Token));

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(evidencePath));
    }

    [Fact]
    public async Task ConfirmedEvidenceExportCopiesExactBytesWithoutChangingArchive()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        byte[] evidence = "{ damaged configuration with a private target }"u8.ToArray();
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        await File.WriteAllBytesAsync(evidencePath, evidence);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);

        ProductConfigurationExportResult result = await store.ExportEvidenceAsync(
            item,
            destination.Path,
            LocalDestination(),
            userConfirmed: true);

        Assert.Equal(
            evidence,
            await File.ReadAllBytesAsync(Path.Combine(destination.Path, result.FileName)));
        Assert.Equal(evidence, await File.ReadAllBytesAsync(evidencePath));
        Assert.EndsWith(".bin", result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0123456789abcdef", result.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(storeDirectory.Path, result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(destination.Path, "*.new"));
    }

    [Fact]
    public async Task UnconfirmedEvidenceExportCreatesNoFile()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        await File.WriteAllBytesAsync(CreateEvidencePath(storeDirectory.Path), [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: false));

        Assert.Equal(ProductConfigurationExportError.ConfirmationRequired, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task EvidenceChangedAfterInventoryIsNotExported()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3, 4]);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.EvidenceChanged, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task MissingEvidenceAfterInventoryReturnsFiniteError()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        File.Delete(evidencePath);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.EvidenceNotAvailable, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task OversizedEvidenceIsRejectedBeforeDestinationWrite()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        await using (FileStream stream = new(evidencePath, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength((64L * 1024 * 1024) + 1);
        }

        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.EvidenceTooLarge, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task ImportBackupEvidenceUsesFiniteOutputRoleWithoutSourceIdentifier()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = Path.Combine(
            storeDirectory.Path,
            "configuration.json.import.fedcba9876543210fedcba9876543210.backup");
        await File.WriteAllBytesAsync(evidencePath, [4, 5, 6]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);

        ProductConfigurationExportResult result = await store.ExportEvidenceAsync(
            item,
            destination.Path,
            LocalDestination(),
            userConfirmed: true);

        Assert.Contains("-Import-Backup-", result.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain("fedcba9876543210", result.FileName, StringComparison.Ordinal);
        Assert.Equal(
            new byte[] { 4, 5, 6 },
            await File.ReadAllBytesAsync(Path.Combine(destination.Path, result.FileName)));
    }

    [Fact]
    public async Task EvidenceReadContentionReturnsFiniteErrorWithoutTemporaryFile()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        await File.WriteAllBytesAsync(evidencePath, [1, 2, 3]);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        await using FileStream lease = new(
            evidencePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ProductConfigurationExportException exception = await Assert.ThrowsAsync<
            ProductConfigurationExportException>(() => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: true));

        Assert.Equal(ProductConfigurationExportError.IoFailure, exception.Error);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task CancelledEvidenceExportCleansTemporaryFileAndPreservesSource()
    {
        using TemporaryDirectory storeDirectory = new();
        using TemporaryDirectory destination = new();
        ProductConfigurationStore store = new(storeDirectory.Path);
        string evidencePath = CreateEvidencePath(storeDirectory.Path);
        byte[] evidence = new byte[1024 * 1024];
        await File.WriteAllBytesAsync(evidencePath, evidence);
        ProductConfigurationEvidenceItem item = Assert.Single(
            (await store.GetEvidenceInventoryAsync()).Items);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ExportEvidenceAsync(
                item,
                destination.Path,
                LocalDestination(),
                userConfirmed: true,
                cancellation.Token));

        Assert.Empty(Directory.GetFiles(destination.Path));
        Assert.Equal(evidence, await File.ReadAllBytesAsync(evidencePath));
    }

    private static ProductConfigurationExportDestination LocalDestination() =>
        new(UserSelected: true, IsLocalFileSystem: true, IsReparsePoint: false);

    private static string CreateEvidencePath(string directoryPath) => Path.Combine(
        directoryPath,
        "configuration.json.damaged.0123456789abcdef0123456789abcdef.primary");

    private static ProductConfigurationDocument CreateDocument(string profileId) => new()
    {
        SchemaVersion = 1,
        ProfileId = profileId,
        Containers =
        [
            new()
            {
                Id = "container-1",
                Name = "Work",
                IsLocked = false,
                Appearance = new()
                {
                    Color = "#5B5FF5",
                    Opacity = 0.9,
                    Collapsed = false,
                },
                Placement = new()
                {
                    DisplayKey = "primary",
                    XDip = 10,
                    YDip = 20,
                    WidthDip = 300,
                    HeightDip = 240,
                },
                Items =
                [
                    new()
                    {
                        Id = "item-1",
                        Kind = ConfigurationItemKind.File,
                        Target = @"C:\Users\Example\Desktop\notes.txt",
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
                "LongGrid.Tests",
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
