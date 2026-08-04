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
