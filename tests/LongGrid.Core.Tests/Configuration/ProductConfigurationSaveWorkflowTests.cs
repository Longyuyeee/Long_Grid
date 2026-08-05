using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationSaveWorkflowTests
{
    [Fact]
    public async Task SaveReturnsFiniteSuccessAndPersistsDocument()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(store);

        ProductConfigurationSaveAttemptResult result = await workflow.SaveAsync(
            CreateDocument("profile-saved"));
        await workflow.CompleteAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Saved, result.Status);
        Assert.Null(result.Error);
        Assert.False(result.CanRetry);
        Assert.Equal(
            "profile-saved",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task RetryReplaysCapturedSnapshotAfterRepair()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await File.WriteAllTextAsync(store.PrimaryPath, "{ damaged");
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(store);
        Dictionary<string, System.Text.Json.JsonElement> extensionData = new()
        {
            ["future"] = System.Text.Json.JsonSerializer.SerializeToElement(1),
        };
        ProductConfigurationDocument source = CreateDocument("profile-retry") with
        {
            ExtensionData = extensionData,
        };

        ProductConfigurationSaveAttemptResult failed =
            await workflow.SaveAsync(source);
        extensionData["future"] =
            System.Text.Json.JsonSerializer.SerializeToElement(2);
        File.Delete(store.PrimaryPath);
        ProductConfigurationSaveAttemptResult retried =
            await workflow.RetryAsync();
        await workflow.CompleteAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Failed, failed.Status);
        Assert.Equal(ProductConfigurationSaveError.DamagedEvidence, failed.Error);
        Assert.True(failed.CanRetry);
        Assert.Equal(ProductConfigurationSaveAttemptStatus.Saved, retried.Status);
        ProductConfigurationDocument persisted =
            (await store.LoadAsync()).Document!;
        Assert.Equal(1, persisted.ExtensionData!["future"].GetInt32());
    }

    [Fact]
    public async Task InvalidConfigurationIsNotRetainedForRetry()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(
            new ProductConfigurationStore(directory.Path));

        ProductConfigurationSaveAttemptResult failed = await workflow.SaveAsync(
            CreateDocument(string.Empty));
        ProductConfigurationSaveAttemptResult retry =
            await workflow.RetryAsync();
        await workflow.CompleteAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Failed, failed.Status);
        Assert.Equal(ProductConfigurationSaveError.InvalidConfiguration, failed.Error);
        Assert.False(failed.CanRetry);
        Assert.Equal(
            ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
            retry.Status);
    }

    [Fact]
    public async Task NewSaveSupersedesOlderRetryIntent()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        await File.WriteAllTextAsync(store.PrimaryPath, "{ damaged");
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(store);

        ProductConfigurationSaveAttemptResult failed = await workflow.SaveAsync(
            CreateDocument("profile-old"));
        File.Delete(store.PrimaryPath);
        ProductConfigurationSaveAttemptResult saved = await workflow.SaveAsync(
            CreateDocument("profile-new"));
        ProductConfigurationSaveAttemptResult retry =
            await workflow.RetryAsync();
        await workflow.CompleteAsync();

        Assert.True(failed.CanRetry);
        Assert.Equal(ProductConfigurationSaveAttemptStatus.Saved, saved.Status);
        Assert.Equal(
            ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
            retry.Status);
        Assert.Equal(
            "profile-new",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task CompletedWorkflowRejectsSaveAndRetryWithoutWriting()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(
            new ProductConfigurationStore(directory.Path));
        await workflow.CompleteAsync();

        ProductConfigurationSaveAttemptResult save = await workflow.SaveAsync(
            CreateDocument("profile-rejected"));
        ProductConfigurationSaveAttemptResult retry =
            await workflow.RetryAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Completed, save.Status);
        Assert.Equal(ProductConfigurationSaveAttemptStatus.Completed, retry.Status);
        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task CallerCancellationDoesNotCreateAmbiguousRetry()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromSeconds(5),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        await using FileStream lease = new(
            store.WriteLeasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(store);
        using CancellationTokenSource cancellation = new();

        Task<ProductConfigurationSaveAttemptResult> save = workflow.SaveAsync(
            CreateDocument("profile-accepted"),
            cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        ProductConfigurationSaveAttemptResult retry =
            await workflow.RetryAsync();

        Assert.Equal(
            ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
            retry.Status);
        await lease.DisposeAsync();
        await workflow.CompleteAsync();
        Assert.Equal(
            "profile-accepted",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task WorkspaceStateProjectsBeforeEnteringSaveQueue()
    {
        using TemporaryDirectory directory = new();
        ProductConfigurationStore store = new(directory.Path);
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(store);
        string target = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.SaveWorkflow.Tests",
            "Project");
        ProductWorkspaceState state = CreateWorkspaceState(target);

        ProductConfigurationSaveAttemptResult result =
            await workflow.SaveAsync(state);
        await workflow.CompleteAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Saved, result.Status);
        ProductConfigurationDocument persisted =
            (await store.LoadAsync()).Document!;
        Assert.Equal(Path.GetFullPath(target), persisted.Containers[0].Items[0].Target);
    }

    [Fact]
    public async Task InvalidWorkspaceStateReturnsFiniteFailureWithoutWriting()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationSaveWorkflow workflow = CreateWorkflow(
            new ProductConfigurationStore(directory.Path));
        ProductWorkspaceState state = CreateWorkspaceState("display text only");

        ProductConfigurationSaveAttemptResult result =
            await workflow.SaveAsync(state);
        ProductConfigurationSaveAttemptResult retry =
            await workflow.RetryAsync();
        await workflow.CompleteAsync();

        Assert.Equal(ProductConfigurationSaveAttemptStatus.Failed, result.Status);
        Assert.Equal(ProductConfigurationSaveError.InvalidConfiguration, result.Error);
        Assert.False(result.CanRetry);
        Assert.Equal(
            ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
            retry.Status);
        Assert.False(Directory.Exists(directory.Path));
    }

    private static ProductConfigurationSaveWorkflow CreateWorkflow(
        ProductConfigurationStore store) =>
        new(new ProductConfigurationSaveCoordinator(store));

    private static ProductConfigurationDocument CreateDocument(string profileId) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = profileId,
            Containers = [],
        };

    private static ProductWorkspaceState CreateWorkspaceState(string target) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        ProductItemReferenceState.CreateResolved(
                            "item-1",
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity("filesystem", target),
                                "user-desktop",
                                "Project",
                                DesktopItemKind.Directory)),
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
                "LongGrid.SaveWorkflow.Tests",
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
