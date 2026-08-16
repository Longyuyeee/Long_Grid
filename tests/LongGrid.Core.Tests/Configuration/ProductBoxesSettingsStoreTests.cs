using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductBoxesSettingsStoreTests
{
    [Fact]
    public async Task MissingSettingsDefaultToEnabledWithoutWritingAFile()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new ProductBoxesSettingsStore(directory);

            ProductBoxesSettingsLoadResult result = await store.LoadAsync();

            Assert.Equal(
                ProductBoxesSettingsLoadStatus.MissingDefaulted,
                result.Status);
            Assert.True(result.Settings.BoxesEnabled);
            Assert.False(File.Exists(Path.Combine(directory, "settings.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SavedSettingSurvivesAStoreRestart()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var first = new ProductBoxesSettingsStore(directory);
            await first.SaveAsync(new() { BoxesEnabled = false });

            var restarted = new ProductBoxesSettingsStore(directory);
            ProductBoxesSettingsLoadResult result = await restarted.LoadAsync();

            Assert.Equal(
                ProductBoxesSettingsLoadStatus.LoadedPrimary,
                result.Status);
            Assert.False(result.Settings.BoxesEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DamagedPrimaryRecoversThePreviousAtomicBackup()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new ProductBoxesSettingsStore(directory);
            await store.SaveAsync(new() { BoxesEnabled = true });
            await store.SaveAsync(new() { BoxesEnabled = false });
            await File.WriteAllTextAsync(
                Path.Combine(directory, "settings.json"),
                "{ damaged");

            ProductBoxesSettingsLoadResult result = await store.LoadAsync();

            Assert.Equal(
                ProductBoxesSettingsLoadStatus.RecoveredBackup,
                result.Status);
            Assert.True(result.Settings.BoxesEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DamagedPrimaryAndBackupFailClosed()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "settings.json"),
                "{ damaged");
            await File.WriteAllTextAsync(
                Path.Combine(directory, "settings.backup.json"),
                "{ damaged too");
            var store = new ProductBoxesSettingsStore(directory);

            ProductBoxesSettingsLoadResult result = await store.LoadAsync();

            Assert.Equal(
                ProductBoxesSettingsLoadStatus.CorruptSafeDisabled,
                result.Status);
            Assert.True(result.RequiresAttention);
            Assert.False(result.Settings.BoxesEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ControllerWritesOnceAndRejectsDuplicateClicks()
    {
        var store = new RecordingStore();
        var controller = new ProductBoxesSettingsController(store);
        controller.Initialize(ProductBoxesSettings.Default);

        ProductBoxesSettingsChangeResult changed =
            await controller.ChangeAsync(boxesEnabled: false);
        ProductBoxesSettingsChangeResult duplicate =
            await controller.ChangeAsync(boxesEnabled: false);

        Assert.Equal(ProductBoxesSettingsChangeStatus.Saved, changed.Status);
        Assert.Equal(ProductBoxesSettingsChangeStatus.Unchanged, duplicate.Status);
        Assert.Equal(1, store.SaveCount);
        Assert.False(controller.Current.BoxesEnabled);
    }

    [Fact]
    public async Task FailedSaveRollsBackTheAuthoritativeSetting()
    {
        var store = new RecordingStore { ThrowOnSave = true };
        var controller = new ProductBoxesSettingsController(store);
        controller.Initialize(ProductBoxesSettings.Default);

        ProductBoxesSettingsChangeResult result =
            await controller.ChangeAsync(boxesEnabled: false);

        Assert.Equal(ProductBoxesSettingsChangeStatus.Failed, result.Status);
        Assert.True(result.Settings.BoxesEnabled);
        Assert.True(controller.Current.BoxesEnabled);
        Assert.Equal(1, store.SaveCount);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingStore : IProductBoxesSettingsStore
    {
        internal int SaveCount { get; private set; }

        internal bool ThrowOnSave { get; init; }

        public Task<ProductBoxesSettingsLoadResult> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductBoxesSettingsLoadResult(
                ProductBoxesSettingsLoadStatus.MissingDefaulted,
                ProductBoxesSettings.Default));

        public Task SaveAsync(
            ProductBoxesSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ThrowOnSave
                ? Task.FromException(new IOException("Injected save failure."))
                : Task.CompletedTask;
        }
    }
}
