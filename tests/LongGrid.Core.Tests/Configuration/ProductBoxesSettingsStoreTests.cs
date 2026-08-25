using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductBoxesSettingsStoreTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealStorePersistsSingleClickOpenOnlyAfterExplicitOptIn()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"LongGrid.SingleClick.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new ProductBoxesSettingsStore(directory);
            using var controller = new ProductBoxesSettingsController(store);
            ProductBoxesSettingsLoadResult initial = await store.LoadAsync();
            controller.Initialize(initial.Settings);

            Assert.False(initial.Settings.OpenItemsWithSingleClick);
            ProductBoxesSettingsChangeResult saved =
                await controller.ChangeSingleClickOpenAsync(true);
            ProductBoxesSettingsLoadResult restarted =
                await new ProductBoxesSettingsStore(directory).LoadAsync();

            Assert.Equal(ProductBoxesSettingsChangeStatus.Saved, saved.Status);
            Assert.True(restarted.Settings.OpenItemsWithSingleClick);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Purpose = "Pf006b2b1RealStoreSingleClickPolicy",
                Expected = new
                {
                    DefaultEnabled = false,
                    RestartedEnabled = true,
                    Status = "Saved",
                },
                Actual = new
                {
                    DefaultEnabled = initial.Settings.OpenItemsWithSingleClick,
                    RestartedEnabled =
                        restarted.Settings.OpenItemsWithSingleClick,
                    Status = saved.Status.ToString(),
                },
                Difference = "None",
                Outcome = "Pass",
            }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
            Assert.True(result.Settings.ThumbnailsEnabled);
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
            await first.SaveAsync(new()
            {
                BoxesEnabled = false,
                ThumbnailsEnabled = false,
            });

            var restarted = new ProductBoxesSettingsStore(directory);
            ProductBoxesSettingsLoadResult result = await restarted.LoadAsync();

            Assert.Equal(
                ProductBoxesSettingsLoadStatus.LoadedPrimary,
                result.Status);
            Assert.False(result.Settings.BoxesEnabled);
            Assert.False(result.Settings.ThumbnailsEnabled);
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
            Assert.False(result.Settings.ThumbnailsEnabled);
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

    [Fact]
    public async Task ThumbnailSwitchPersistsOnceAndRollsBackOnRealSaveFailure()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var realStore = new ProductBoxesSettingsStore(directory);
            using var controller = new ProductBoxesSettingsController(realStore);
            controller.Initialize(ProductBoxesSettings.Default);

            ProductBoxesSettingsChangeResult saved =
                await controller.ChangeThumbnailsAsync(false);
            ProductBoxesSettingsChangeResult duplicate =
                await controller.ChangeThumbnailsAsync(false);
            ProductBoxesSettingsLoadResult restarted =
                await new ProductBoxesSettingsStore(directory).LoadAsync();

            Assert.Equal(ProductBoxesSettingsChangeStatus.Saved, saved.Status);
            Assert.Equal(
                ProductBoxesSettingsChangeStatus.Unchanged,
                duplicate.Status);
            Assert.False(restarted.Settings.ThumbnailsEnabled);

            var failedStore = new RecordingStore { ThrowOnSave = true };
            using var failed = new ProductBoxesSettingsController(failedStore);
            failed.Initialize(ProductBoxesSettings.Default);
            ProductBoxesSettingsChangeResult rejected =
                await failed.ChangeThumbnailsAsync(false);

            Assert.Equal(ProductBoxesSettingsChangeStatus.Failed, rejected.Status);
            Assert.True(failed.Current.ThumbnailsEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
