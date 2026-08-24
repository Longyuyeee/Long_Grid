using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerMenuNavigationControllerTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task RealStoreNavigationTargetsExactContainerWithoutWriting()
    {
        string sandbox = Sandbox("RealNavigation");
        string storeDirectory = Path.Combine(sandbox, "store");
        try
        {
            var store = new ProductConfigurationStore(storeDirectory);
            ProductWorkspaceState state = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            string configurationPath = store.PrimaryPath;
            byte[] before = await File.ReadAllBytesAsync(configurationPath);
            DateTime beforeWrite = File.GetLastWriteTimeUtc(configurationPath);
            ProductWorkspaceSaveSnapshot clean =
                ProductWorkspaceSaveSnapshot.Initial;

            ProductDesktopContainerMenuAvailability availability =
                ProductDesktopContainerMenuNavigationController
                    .EvaluateAvailability(
                        state,
                        isReadOnly: false,
                        clean,
                        "container-1",
                        "display-1");
            ProductDesktopContainerMenuNavigationResult rename =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenRename),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            ProductDesktopContainerMenuNavigationResult appearance =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenAppearance),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            ProductDesktopContainerMenuNavigationResult sort =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenSort),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            byte[] after = await File.ReadAllBytesAsync(configurationPath);
            DateTime afterWrite = File.GetLastWriteTimeUtc(configurationPath);

            Assert.Equal(new(true, true, true), availability);
            Assert.True(rename.IsAccepted);
            Assert.True(appearance.IsAccepted);
            Assert.True(sort.IsAccepted);
            Assert.All([rename, appearance, sort], result =>
                Assert.Equal(1, result.ContainerOrdinal));
            Assert.Equal(before, after);
            Assert.Equal(beforeWrite, afterWrite);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Rename = "Accepted:Ordinal=1",
                    Appearance = "Accepted:Ordinal=1",
                    Sort = "Accepted:Ordinal=1",
                    ConfigurationBytesChanged = false,
                    ConfigurationWriteTimeChanged = false,
                },
                Actual = new
                {
                    Rename = $"{rename.Status}:Ordinal={rename.ContainerOrdinal}",
                    Appearance =
                        $"{appearance.Status}:Ordinal={appearance.ContainerOrdinal}",
                    Sort = $"{sort.Status}:Ordinal={sort.ContainerOrdinal}",
                    ConfigurationBytesChanged = !before.SequenceEqual(after),
                    ConfigurationWriteTimeChanged = beforeWrite != afterWrite,
                },
                Difference = "None",
            }));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public void LockedReadOnlyAndUnsafeRequestsFailClosedBeforeNavigation()
    {
        ProductWorkspaceState locked = State(isLocked: true);
        ProductWorkspaceSaveSnapshot clean = ProductWorkspaceSaveSnapshot.Initial;

        Assert.Equal(
            new(false, false, true),
            ProductDesktopContainerMenuNavigationController.EvaluateAvailability(
                locked,
                isReadOnly: false,
                clean,
                "container-1",
                "display-1"));
        Assert.Equal(
            new(false, false, true),
            ProductDesktopContainerMenuNavigationController.EvaluateAvailability(
                State(),
                isReadOnly: true,
                clean,
                "container-1",
                "display-1"));
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenRename),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.True(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenSort),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenSort) with
            {
                IsInjected = true,
            },
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request((ProductDesktopContainerMenuAction)99),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
    }

    [Fact]
    public async Task RealWriteFailureDisablesEditingRoutesAndKeepsDiskOriginal()
    {
        string sandbox = Sandbox("RealSaveFailure");
        string storeDirectory = Path.Combine(sandbox, "store");
        try
        {
            var store = new ProductConfigurationStore(
                storeDirectory,
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            ProductWorkspaceState state = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductWorkspaceEditResult edit = ProductWorkspaceReducer.RenameContainer(
                state,
                "container-1",
                "Renamed");
            Assert.True(saves.Submit(edit).IsAccepted);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                revision: 1);

            ProductDesktopContainerMenuAvailability availability =
                ProductDesktopContainerMenuNavigationController
                    .EvaluateAvailability(
                        state,
                        isReadOnly: false,
                        saves.Snapshot,
                        "container-1",
                        "display-1");
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                saves.Snapshot.Failure);
            Assert.Equal(new(false, false, true), availability);
            Assert.Equal("Work", reloaded.Document!.Containers[0].Name);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Failure = "WriteLeaseUnavailable",
                    RenameEnabled = false,
                    AppearanceEnabled = false,
                    SortEnabled = true,
                    PersistedName = "Work",
                },
                Actual = new
                {
                    Failure = saves.Snapshot.Failure.ToString(),
                    RenameEnabled = availability.CanOpenRename,
                    AppearanceEnabled = availability.CanOpenAppearance,
                    SortEnabled = availability.CanOpenSort,
                    PersistedName = reloaded.Document.Containers[0].Name,
                },
                Difference = "None",
            }));
            await lease.DisposeAsync();
            Assert.True(saves.DiscardFailedRetryForExternalBaseline());
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    private static ProductDesktopContainerMenuRequest Request(
        ProductDesktopContainerMenuAction action) => new(
            action,
            "container-1",
            "display-1",
            ExpectedWorkspaceRevision: 1,
            ExpectedTopologyGeneration: 7,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false);

    private static ProductWorkspaceState State(bool isLocked = false) => new()
    {
        ProfileId = "pf004c-menu-navigation",
        Containers =
        [
            new()
            {
                Id = "container-1",
                Name = "Work",
                IsLocked = isLocked,
                Appearance = new()
                {
                    Color = "#2457D6",
                    Opacity = 0.8,
                    Collapsed = false,
                },
                Placement = new()
                {
                    DisplayKey = "display-1",
                    XDip = 100,
                    YDip = 100,
                    WidthDip = 320,
                    HeightDip = 180,
                },
                Items = [],
            },
        ],
    };

    private static ProductDisplayTopologySnapshot Topology() => new(
        ProductDisplayTopologyStatus.Ready,
        Generation: 7,
        Displays:
        [
            new(
                "display-1",
                new(0, 0, 1920, 1080),
                new(0, 0, 1920, 1040),
                96,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ],
        ActivePathCount: 1,
        StableIdentityCount: 1,
        BufferAttempts: 1);

    private static ProductWorkspaceSaveController Saves(
        ProductConfigurationStore store) => new(
            new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store)),
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));

    private static FileStream AcquireLease(string path) => new(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.None);

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus status,
        long revision)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (saves.Snapshot.Status != status
            || saves.Snapshot.CurrentRevision != revision)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string Sandbox(string scenario) => Path.Combine(
        Path.GetTempPath(),
        "LongGrid.PF004C",
        scenario,
        Guid.NewGuid().ToString("N"));

    private static void DeleteSandbox(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
