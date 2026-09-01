using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductFirstRunJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealStorePersistsEveryJourneyStateAcrossProcessRestarts()
    {
        string directory = CreateTemporaryDirectory();
        string guardPath = Path.Combine(directory, "用户资料-不得修改.txt");
        await File.WriteAllTextAsync(
            guardPath,
            "LongGrid first-run journey must not mutate unrelated user files.",
            Encoding.UTF8);
        string guardHashBefore = HashFile(guardPath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ProductBoxesSettingsLoadResult initial;
            using (var initialStore = new ProductBoxesSettingsStore(directory))
            {
                initial = await initialStore.LoadAsync();
            }
            Assert.Equal(ProductFirstRunJourneyState.NotStarted, initial.Settings.FirstRunJourneyState);

            Assert.Equal(
                ProductBoxesSettingsChangeStatus.Saved,
                await ChangeAfterRestartAsync(directory, ProductFirstRunJourneyState.CustomizeInProgress));
            Assert.Equal(
                ProductFirstRunJourneyState.CustomizeInProgress,
                await LoadStateAfterRestartAsync(directory));

            Assert.Equal(
                ProductBoxesSettingsChangeStatus.Saved,
                await ChangeAfterRestartAsync(directory, ProductFirstRunJourneyState.Skipped));
            Assert.Equal(
                ProductFirstRunJourneyState.Skipped,
                await LoadStateAfterRestartAsync(directory));

            Assert.Equal(
                ProductBoxesSettingsChangeStatus.Saved,
                await ChangeAfterRestartAsync(directory, ProductFirstRunJourneyState.NotStarted));
            Assert.Equal(
                ProductFirstRunJourneyState.NotStarted,
                await LoadStateAfterRestartAsync(directory));

            Assert.Equal(
                ProductBoxesSettingsChangeStatus.Saved,
                await ChangeAfterRestartAsync(directory, ProductFirstRunJourneyState.Completed));
            ProductFirstRunJourneyState actual = await LoadStateAfterRestartAsync(directory);
            Assert.Equal(ProductFirstRunJourneyState.Completed, actual);

            using JsonDocument persisted = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
            Assert.Equal(
                (int)ProductFirstRunJourneyState.Completed,
                persisted.RootElement.GetProperty("firstRunJourneyState").GetInt32());
            Assert.Equal(guardHashBefore, HashFile(guardPath));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));

            output.WriteLine(JsonSerializer.Serialize(new
            {
                Purpose = "Pf011bRealRestartPersistence",
                Expected = new
                {
                    Initial = "NotStarted",
                    Final = "Completed",
                    UnrelatedUnicodeFileChanged = false,
                    BudgetSeconds = 10,
                },
                Actual = new
                {
                    Initial = initial.Settings.FirstRunJourneyState.ToString(),
                    Final = actual.ToString(),
                    UnrelatedUnicodeFileChanged = guardHashBefore != HashFile(guardPath),
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
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
    public async Task DuplicateTransitionWritesOnceAndSaveFailureRollsBack()
    {
        var recordingStore = new RecordingStore();
        using var controller = new ProductBoxesSettingsController(recordingStore);
        controller.Initialize(ProductBoxesSettings.Default);

        ProductBoxesSettingsChangeResult changed =
            await controller.ChangeFirstRunJourneyAsync(ProductFirstRunJourneyState.Skipped);
        ProductBoxesSettingsChangeResult duplicate =
            await controller.ChangeFirstRunJourneyAsync(ProductFirstRunJourneyState.Skipped);

        Assert.Equal(ProductBoxesSettingsChangeStatus.Saved, changed.Status);
        Assert.Equal(ProductBoxesSettingsChangeStatus.Unchanged, duplicate.Status);
        Assert.Equal(1, recordingStore.SaveCount);

        var failingStore = new RecordingStore { ThrowOnSave = true };
        using var failingController = new ProductBoxesSettingsController(failingStore);
        failingController.Initialize(ProductBoxesSettings.Default);
        ProductBoxesSettingsChangeResult rejected =
            await failingController.ChangeFirstRunJourneyAsync(ProductFirstRunJourneyState.Completed);

        Assert.Equal(ProductBoxesSettingsChangeStatus.Failed, rejected.Status);
        Assert.Equal(
            ProductFirstRunJourneyState.NotStarted,
            failingController.Current.FirstRunJourneyState);
        Assert.Equal(1, failingStore.SaveCount);
    }

    [Fact]
    public async Task LegacyV1SettingsDefaultToNotStartedWithoutARewrite()
    {
        string directory = CreateTemporaryDirectory();
        string settingsPath = Path.Combine(directory, "settings.json");
        const string legacy = """
            {
              "schemaVersion": 1,
              "boxesEnabled": true,
              "thumbnailsEnabled": true,
              "openItemsWithSingleClick": false
            }
            """;
        await File.WriteAllTextAsync(settingsPath, legacy);

        try
        {
            using var store = new ProductBoxesSettingsStore(directory);
            ProductBoxesSettingsLoadResult result = await store.LoadAsync();

            Assert.Equal(ProductBoxesSettingsLoadStatus.LoadedPrimary, result.Status);
            Assert.Equal(ProductFirstRunJourneyState.NotStarted, result.Settings.FirstRunJourneyState);
            Assert.Equal(legacy, await File.ReadAllTextAsync(settingsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptSettingsFailClosedWithoutReopeningFirstRun()
    {
        string directory = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{ damaged");

        try
        {
            using var store = new ProductBoxesSettingsStore(directory);
            ProductBoxesSettingsLoadResult result = await store.LoadAsync();

            Assert.Equal(ProductBoxesSettingsLoadStatus.CorruptSafeDisabled, result.Status);
            Assert.Equal(ProductFirstRunJourneyState.Skipped, result.Settings.FirstRunJourneyState);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<ProductBoxesSettingsChangeStatus> ChangeAfterRestartAsync(
        string directory,
        ProductFirstRunJourneyState state)
    {
        using var store = new ProductBoxesSettingsStore(directory);
        ProductBoxesSettingsLoadResult loaded = await store.LoadAsync();
        using var controller = new ProductBoxesSettingsController(store);
        controller.Initialize(loaded.Settings);
        ProductBoxesSettingsChangeResult result = await controller.ChangeFirstRunJourneyAsync(state);
        return result.Status;
    }

    private static async Task<ProductFirstRunJourneyState> LoadStateAfterRestartAsync(string directory)
    {
        using var restarted = new ProductBoxesSettingsStore(directory);
        ProductBoxesSettingsLoadResult loaded = await restarted.LoadAsync();
        return loaded.Settings.FirstRunJourneyState;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.Pf011b.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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
