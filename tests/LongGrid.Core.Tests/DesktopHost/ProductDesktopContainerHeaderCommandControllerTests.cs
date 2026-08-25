using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerHeaderCommandControllerTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task RealStorePublishesCollapsedStateWithExpectedActualEvidence()
    {
        string sandbox = Sandbox("RealPublish");
        string storeDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(sandbox);
        try
        {
            var store = new ProductConfigurationStore(storeDirectory);
            ProductWorkspaceState original = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(original).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = commits.AdvanceExternalRevision();
            var controller = new ProductDesktopContainerHeaderCommandController(
                commits,
                saves);

            ProductDesktopContainerHeaderCommandResult accepted = controller.Handle(
                Request(ProductDesktopContainerHeaderCommandKind.ToggleCollapsed),
                original,
                isReadOnly: false,
                editRevision,
                Topology());
            long saveRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                saveRevision);
            ProductDesktopContainerHeaderCommandResult published =
                controller.ObserveSave(
                    accepted.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(accepted.IsAccepted);
            Assert.True(accepted.State!.Containers[0].Appearance.Collapsed);
            Assert.Equal(
                ProductDesktopContainerHeaderCommandStatus.Published,
                published.Status);
            Assert.True(reloaded.Document!.Containers[0].Appearance.Collapsed);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Status = "Published",
                    InMemoryCollapsed = true,
                    PersistedCollapsed = true,
                    SaveRevision = 1,
                },
                Actual = new
                {
                    Status = published.Status.ToString(),
                    InMemoryCollapsed = accepted.State.Containers[0]
                        .Appearance.Collapsed,
                    PersistedCollapsed = reloaded.Document.Containers[0]
                        .Appearance.Collapsed,
                    SaveRevision = saveRevision,
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
    public async Task RealLeaseFailureCompensatesMemoryAndPersistsOriginalState()
    {
        string sandbox = Sandbox("RealCompensation");
        string storeDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(sandbox);
        try
        {
            var store = new ProductConfigurationStore(
                storeDirectory,
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            ProductWorkspaceState original = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(original).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = commits.AdvanceExternalRevision();
            var controller = new ProductDesktopContainerHeaderCommandController(
                commits,
                saves);

            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductDesktopContainerHeaderCommandResult accepted = controller.Handle(
                Request(ProductDesktopContainerHeaderCommandKind.ToggleLocked),
                original,
                isReadOnly: false,
                editRevision,
                Topology());
            long failedRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                failedRevision);
            ProductDesktopContainerHeaderCommandResult compensation =
                controller.ObserveSave(
                    accepted.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);
            long compensationRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                compensationRevision);
            ProductConfigurationLoadResult blockedReload = await store.LoadAsync();

            Assert.True(compensation.IsCompensated);
            Assert.False(compensation.State!.Containers[0].IsLocked);
            Assert.False(blockedReload.Document!.Containers[0].IsLocked);
            await lease.DisposeAsync();
            Assert.Equal(
                ProductWorkspaceSaveRetryStatus.Accepted,
                saves.Retry().Status);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                compensationRevision);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();
            Assert.False(reloaded.Document!.Containers[0].IsLocked);

            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Failure = "WriteLeaseUnavailable",
                    Status = "Compensated",
                    InMemoryLocked = false,
                    PersistedLocked = false,
                },
                Actual = new
                {
                    Failure = compensation.SourceFailure.ToString(),
                    Status = compensation.Status.ToString(),
                    InMemoryLocked = compensation.State.Containers[0].IsLocked,
                    PersistedLocked = reloaded.Document.Containers[0].IsLocked,
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
    public async Task RejectsUnsafeStaleAndLockedCollapseButAllowsUnlock()
    {
        string sandbox = Sandbox("Validation");
        try
        {
            var store = new ProductConfigurationStore(Path.Combine(sandbox, "store"));
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long revision = commits.AdvanceExternalRevision();
            var controller = new ProductDesktopContainerHeaderCommandController(
                commits,
                saves);
            ProductWorkspaceState locked = State(isLocked: true);

            Assert.Equal(
                ProductWorkspaceEditError.ContainerLocked,
                controller.Handle(
                    Request(ProductDesktopContainerHeaderCommandKind.ToggleCollapsed),
                    locked,
                    false,
                    revision,
                    Topology()).EditError);
            Assert.Equal(
                ProductDesktopContainerHeaderCommandStatus.Rejected,
                controller.Handle(
                    Request((ProductDesktopContainerHeaderCommandKind)99),
                    locked,
                    false,
                    revision,
                    Topology()).Status);
            Assert.Equal(
                ProductDesktopContainerHeaderCommandStatus.Rejected,
                controller.Handle(
                    Request(ProductDesktopContainerHeaderCommandKind.ToggleLocked)
                        with
                    { IsInjected = true },
                    locked,
                    false,
                    revision,
                    Topology()).Status);
            Assert.Equal(
                ProductDesktopContainerHeaderCommandStatus.Rejected,
                controller.Handle(
                    Request(ProductDesktopContainerHeaderCommandKind.ToggleLocked)
                        with
                    { ExpectedWorkspaceRevision = revision - 1 },
                    locked,
                    false,
                    revision,
                    Topology()).Status);
            ProductDesktopContainerHeaderCommandResult unlock = controller.Handle(
                Request(ProductDesktopContainerHeaderCommandKind.ToggleLocked),
                locked,
                false,
                revision,
                Topology());

            Assert.True(unlock.IsAccepted);
            Assert.False(unlock.State!.Containers[0].IsLocked);
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    private static ProductDesktopContainerHeaderCommandRequest Request(
        ProductDesktopContainerHeaderCommandKind kind) => new(
            kind,
            "container-1",
            "display-1",
            ExpectedWorkspaceRevision: 1,
            ExpectedTopologyGeneration: 7,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false);

    private static ProductWorkspaceState State(bool isLocked = false) => new()
    {
        ProfileId = "pf004b-header-command",
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
        "LongGrid.PF004B",
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
