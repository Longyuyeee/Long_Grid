using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationRestartRecoveryTests
{
    [Fact]
    public async Task RealStoreRestartRestoresPreviousSaveWithoutChangingUserFiles()
    {
        using var sandbox = new TemporaryDirectory();
        string userDirectory = Path.Combine(sandbox.Path, "真实用户目录");
        string firstPath = Path.Combine(userDirectory, "项目甲.txt");
        string secondPath = Path.Combine(userDirectory, "项目乙.txt");
        Directory.CreateDirectory(userDirectory);
        await File.WriteAllTextAsync(firstPath, "first-real-content");
        await File.WriteAllTextAsync(secondPath, "second-real-content");
        string[] expectedHashes = await HashesAsync(firstPath, secondPath);
        string storeDirectory = Path.Combine(sandbox.Path, "store");
        var original = new ProductConfigurationStore(storeDirectory);
        ProductConfigurationDocument before = Document(
            "恢复前", firstPath, secondPath);
        ProductConfigurationDocument after = Document(
            "恢复后", firstPath, secondPath);
        await original.SaveAsync(before);
        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.Unavailable,
            (await original.GetRestartRecoveryPointAsync()).Availability);
        await original.SaveAsync(after);

        var restarted = new ProductConfigurationStore(storeDirectory);
        ProductConfigurationRestartRecoverySnapshot available =
            await restarted.GetRestartRecoveryPointAsync();
        string metadata = await File.ReadAllTextAsync(
            restarted.RestartRecoveryPointPath);
        ProductConfigurationRestartRecoveryResult notConfirmed =
            await restarted.RestoreRestartRecoveryPointAsync(
                available.Point!, userConfirmed: false);
        ProductConfigurationLoadResult stillAfter = await restarted.LoadAsync();
        ProductConfigurationRestartRecoveryResult restored =
            await restarted.RestoreRestartRecoveryPointAsync(
                available.Point!, userConfirmed: true);

        var verifiedRestart = new ProductConfigurationStore(storeDirectory);
        ProductConfigurationLoadResult reloaded = await verifiedRestart.LoadAsync();
        ProductConfigurationRestartRecoverySnapshot consumed =
            await verifiedRestart.GetRestartRecoveryPointAsync();

        Assert.True(available.IsAvailable);
        Assert.Equal(1, available.Point!.ContainerCount);
        Assert.Equal(2, available.Point.ItemCount);
        Assert.Equal("恢复到上次保存前的配置", available.Point.ActionSummary);
        Assert.DoesNotContain(sandbox.Path, metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("项目甲.txt", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("项目乙.txt", metadata, StringComparison.Ordinal);
        Assert.Equal(
            ProductConfigurationRestartRecoveryStatus.ConfirmationRequired,
            notConfirmed.Status);
        Assert.Equal("恢复后", stillAfter.Document!.Containers[0].Name);
        Assert.True(restored.IsRestored);
        Assert.Equal("恢复前", restored.Document!.Containers[0].Name);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
        Assert.Equal("恢复前", reloaded.Document!.Containers[0].Name);
        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.Unavailable,
            consumed.Availability);
        Assert.Equal(expectedHashes, await HashesAsync(firstPath, secondPath));
    }

    [Fact]
    public async Task FailedSaveNeverPublishesRestartRecoveryPoint()
    {
        using var sandbox = new TemporaryDirectory();
        var store = new ProductConfigurationStore(
            sandbox.Path,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(30),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        await store.SaveAsync(Document("before"));
        await using FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        ProductConfigurationSaveException failure =
            await Assert.ThrowsAsync<ProductConfigurationSaveException>(
                () => store.SaveAsync(Document("failed")));
        var restarted = new ProductConfigurationStore(sandbox.Path);

        Assert.Equal(
            ProductConfigurationSaveError.WriteLeaseUnavailable,
            failure.Error);
        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.Unavailable,
            (await restarted.GetRestartRecoveryPointAsync()).Availability);
        Assert.Equal(
            "before",
            (await restarted.LoadAsync()).Document!.Containers[0].Name);
    }

    [Fact]
    public async Task RealSaveControllerPublishesPointOnlyAfterSavedRevision()
    {
        using var sandbox = new TemporaryDirectory();
        var store = new ProductConfigurationStore(sandbox.Path);
        ProductWorkspaceState before = WorkspaceState("before");
        await store.SaveAsync(
            ProductWorkspaceConfigurationProjector.Project(before).Document!);
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        await using var saves = new ProductWorkspaceSaveController(
            workflow,
            debounceDelay: TimeSpan.FromMilliseconds(1));
        ProductWorkspaceEditResult edit =
            ProductWorkspaceReducer.RenameContainer(
                before, "container-1", "after");

        ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
        Assert.True(submission.IsAccepted);
        await WaitForSavedRevisionAsync(saves, 1);
        _ = await saves.CompleteAsync();

        var restarted = new ProductConfigurationStore(sandbox.Path);
        ProductConfigurationRestartRecoverySnapshot point =
            await restarted.GetRestartRecoveryPointAsync();

        Assert.True(point.IsAvailable);
        ProductConfigurationRestartRecoveryResult restored =
            await restarted.RestoreRestartRecoveryPointAsync(
                point.Point!, userConfirmed: true);
        Assert.True(restored.IsRestored);
        Assert.Equal("before", restored.Document!.Containers[0].Name);
    }

    [Fact]
    public async Task ExternalPrimaryAndBackupChangesHaveFiniteReasons()
    {
        using var sandbox = new TemporaryDirectory();
        string primaryDirectory = Path.Combine(sandbox.Path, "primary-change");
        var primaryStore = await StoreWithRecoveryPointAsync(primaryDirectory);
        ProductConfigurationRestartRecoveryPoint primaryPoint =
            (await primaryStore.GetRestartRecoveryPointAsync()).Point!;
        await File.WriteAllBytesAsync(
            primaryStore.PrimaryPath,
            ProductConfigurationJson.SerializeToUtf8Bytes(Document("external")));

        ProductConfigurationRestartRecoverySnapshot primaryChanged =
            await new ProductConfigurationStore(primaryDirectory)
                .GetRestartRecoveryPointAsync();
        ProductConfigurationRestartRecoveryResult primaryRestore =
            await primaryStore.RestoreRestartRecoveryPointAsync(
                primaryPoint, userConfirmed: true);

        string backupDirectory = Path.Combine(sandbox.Path, "backup-change");
        var backupStore = await StoreWithRecoveryPointAsync(backupDirectory);
        ProductConfigurationRestartRecoveryPoint backupPoint =
            (await backupStore.GetRestartRecoveryPointAsync()).Point!;
        await File.WriteAllBytesAsync(
            backupStore.BackupPath,
            ProductConfigurationJson.SerializeToUtf8Bytes(Document("external-backup")));
        ProductConfigurationRestartRecoverySnapshot backupChanged =
            await new ProductConfigurationStore(backupDirectory)
                .GetRestartRecoveryPointAsync();
        ProductConfigurationRestartRecoveryResult backupRestore =
            await backupStore.RestoreRestartRecoveryPointAsync(
                backupPoint, userConfirmed: true);

        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability
                .CurrentConfigurationChanged,
            primaryChanged.Availability);
        Assert.Equal(
            ProductConfigurationRestartRecoveryStatus.CurrentConfigurationChanged,
            primaryRestore.Status);
        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.RecoveryPointChanged,
            backupChanged.Availability);
        Assert.Equal(
            ProductConfigurationRestartRecoveryStatus.RecoveryPointChanged,
            backupRestore.Status);
    }

    [Fact]
    public async Task DamagedMetadataFailsClosedAndDoesNotExposeConfiguration()
    {
        using var sandbox = new TemporaryDirectory();
        var store = await StoreWithRecoveryPointAsync(sandbox.Path);
        await File.WriteAllTextAsync(
            store.RestartRecoveryPointPath,
            "{ damaged-recovery-metadata");

        ProductConfigurationRestartRecoverySnapshot result =
            await new ProductConfigurationStore(sandbox.Path)
                .GetRestartRecoveryPointAsync();

        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.InvalidRecoveryPoint,
            result.Availability);
        Assert.Null(result.Point);
        Assert.Equal(
            "after",
            (await store.LoadAsync()).Document!.Containers[0].Name);
    }

    [Fact]
    public async Task RestoreLeaseAndStagingFailuresKeepCurrentConfiguration()
    {
        using var sandbox = new TemporaryDirectory();
        string leaseDirectory = Path.Combine(sandbox.Path, "lease");
        var leaseStore = new ProductConfigurationStore(
            leaseDirectory,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(30),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        await leaseStore.SaveAsync(Document("before"));
        await leaseStore.SaveAsync(Document("after"));
        ProductConfigurationRestartRecoveryPoint leasePoint =
            (await leaseStore.GetRestartRecoveryPointAsync()).Point!;
        await using FileStream lease = new(
            leaseStore.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        ProductConfigurationRestartRecoveryResult leaseResult =
            await leaseStore.RestoreRestartRecoveryPointAsync(
                leasePoint, userConfirmed: true);

        string stagingDirectory = Path.Combine(sandbox.Path, "staging");
        var stagingStore = await StoreWithRecoveryPointAsync(stagingDirectory);
        ProductConfigurationRestartRecoveryPoint stagingPoint =
            (await stagingStore.GetRestartRecoveryPointAsync()).Point!;
        Directory.CreateDirectory(stagingStore.RestartRecoveryRestoreTemporaryPath);
        ProductConfigurationRestartRecoveryResult stagingResult =
            await stagingStore.RestoreRestartRecoveryPointAsync(
                stagingPoint, userConfirmed: true);

        Assert.Equal(
            ProductConfigurationRestartRecoveryStatus.WriteLeaseUnavailable,
            leaseResult.Status);
        Assert.Equal(
            "after",
            (await leaseStore.LoadAsync()).Document!.Containers[0].Name);
        Assert.Equal(
            ProductConfigurationRestartRecoveryStatus.IoFailure,
            stagingResult.Status);
        Assert.Equal(
            "after",
            (await stagingStore.LoadAsync()).Document!.Containers[0].Name);
        Assert.True((await stagingStore.GetRestartRecoveryPointAsync()).IsAvailable);
    }

    [Fact]
    public async Task MetadataPublicationFailureNeverFailsPrimarySave()
    {
        using var sandbox = new TemporaryDirectory();
        var store = new ProductConfigurationStore(sandbox.Path);
        await store.SaveAsync(Document("before"));
        Directory.CreateDirectory(store.RestartRecoveryPointPath);

        await store.SaveAsync(Document("after"));

        Assert.Equal(
            "after",
            (await store.LoadAsync()).Document!.Containers[0].Name);
        Assert.Equal(
            ProductConfigurationRestartRecoveryAvailability.Unavailable,
            (await store.GetRestartRecoveryPointAsync()).Availability);
    }

    [Theory]
    [InlineData(ProductWorkspaceSaveStatus.WaitingForDebounce, 1, 0, false)]
    [InlineData(ProductWorkspaceSaveStatus.Saving, 1, 0, false)]
    [InlineData(ProductWorkspaceSaveStatus.Failed, 1, 0, false)]
    [InlineData(ProductWorkspaceSaveStatus.Saved, 2, 1, false)]
    [InlineData(ProductWorkspaceSaveStatus.Clean, 0, 0, true)]
    [InlineData(ProductWorkspaceSaveStatus.Saved, 2, 2, true)]
    public void AppAdmissionRejectsUnsavedOrFailedConfiguration(
        ProductWorkspaceSaveStatus status,
        long currentRevision,
        long savedRevision,
        bool expected)
    {
        ProductWorkspaceSaveSnapshot save = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = status,
            CurrentRevision = currentRevision,
            SavedRevision = savedRevision,
            Failure = status == ProductWorkspaceSaveStatus.Failed
                ? ProductWorkspaceSaveFailure.IoFailure
                : ProductWorkspaceSaveFailure.None,
        };

        Assert.Equal(
            expected,
            ProductConfigurationRestartRecoveryAdmission.CanRestore(save));
    }

    private static async Task<ProductConfigurationStore>
        StoreWithRecoveryPointAsync(string directory)
    {
        var store = new ProductConfigurationStore(directory);
        await store.SaveAsync(Document("before"));
        await store.SaveAsync(Document("after"));
        Assert.True((await store.GetRestartRecoveryPointAsync()).IsAvailable);
        return store;
    }

    private static ProductConfigurationDocument Document(
        string name,
        params string[] itemPaths) => new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "default",
            Containers =
        [
            new ContainerConfiguration
            {
                Id = "container-1",
                Name = name,
                Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                Placement = new()
                {
                    DisplayKey = "display-unassigned",
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = itemPaths.Select((path, index) =>
                    new DesktopItemReferenceConfiguration
                    {
                        Id = $"item-{index + 1}",
                        Kind = ConfigurationItemKind.File,
                        Target = path,
                        Behavior = ConfigurationItemBehavior.Reference,
                    }).ToArray(),
            },
        ],
        };

    private static ProductWorkspaceState WorkspaceState(string name) => new()
    {
        ProfileId = "default",
        Containers =
        [
            new ProductContainerState
            {
                Id = "container-1",
                Name = name,
                Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                Placement = new()
                {
                    DisplayKey = "display-unassigned",
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = Array.Empty<ProductItemReferenceState>(),
            },
        ],
    };

    private static async Task WaitForSavedRevisionAsync(
        ProductWorkspaceSaveController saves,
        long revision)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (saves.Snapshot.Status == ProductWorkspaceSaveStatus.Saved
                && saves.Snapshot.SavedRevision == revision)
            {
                return;
            }
            await Task.Delay(5);
        }
        Assert.Equal(revision, saves.Snapshot.SavedRevision);
    }

    private static async Task<string[]> HashesAsync(params string[] paths)
    {
        var hashes = new List<string>(paths.Length);
        foreach (string path in paths)
        {
            hashes.Add(Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(path))));
        }
        return hashes.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LongGrid.RestartRecovery.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
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
