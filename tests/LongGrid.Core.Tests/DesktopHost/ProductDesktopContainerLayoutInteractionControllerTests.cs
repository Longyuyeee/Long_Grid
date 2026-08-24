using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerLayoutInteractionControllerTests
{
    [Fact]
    public async Task RealStoreCommitPublishesOneFinalCandidate()
    {
        string sandbox = Sandbox("RealCommit");
        string storeDirectory = Path.Combine(sandbox, "store");
        string sentinelPath = Path.Combine(sandbox, "desktop-sentinel.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(sentinelPath, "must-not-change");

        try
        {
            var store = new ProductConfigurationStore(storeDirectory);
            ProductWorkspaceState original = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(original).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = commits.AdvanceExternalRevision();
            var controller =
                new ProductDesktopContainerLayoutInteractionController(commits);

            Assert.True(controller.Handle(
                Request(ProductDesktopContainerLayoutInputPhase.Begin),
                original,
                isReadOnly: false,
                editRevision,
                Topology()).IsAccepted);
            ProductDesktopContainerLayoutInteractionResult preview =
                controller.Handle(
                    Request(
                        ProductDesktopContainerLayoutInputPhase.Update,
                        deltaX: 100,
                        deltaY: 50),
                    original,
                    isReadOnly: false,
                    editRevision,
                    Topology());

            Assert.Equal(
                ProductDesktopContainerLayoutInteractionStatus.PreviewUpdated,
                preview.Status);
            Assert.Equal(200, preview.PreviewPlacement!.XDip);
            Assert.Equal(150, preview.PreviewPlacement.YDip);
            Assert.Equal(0, saves.Snapshot.CurrentRevision);

            ProductDesktopContainerLayoutInteractionResult completed =
                controller.Handle(
                    Request(
                        ProductDesktopContainerLayoutInputPhase.Complete,
                        deltaX: 100,
                        deltaY: 50),
                    original,
                    isReadOnly: false,
                    editRevision,
                    Topology());
            long saveRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(saves, ProductWorkspaceSaveStatus.Saved, saveRevision);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();
            ProductDesktopContainerLayoutPublicationResult publication =
                controller.ObserveSave(
                    completed.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);

            Assert.Equal(
                ProductDesktopContainerLayoutInteractionStatus.Committed,
                completed.Status);
            Assert.True(completed.ClearPreview);
            Assert.Equal(
                ProductDesktopContainerLayoutPublicationStatus.Published,
                publication.Status);
            Assert.Equal(1, saveRevision);
            Assert.Equal(200, reloaded.Document!.Containers[0].Placement.XDip);
            Assert.Equal(150, reloaded.Document.Containers[0].Placement.YDip);
            Assert.Equal("must-not-change", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public async Task CancelRestoresPreviewWithoutSubmittingSave()
    {
        string sandbox = Sandbox("Cancel");
        try
        {
            var store = new ProductConfigurationStore(Path.Combine(sandbox, "store"));
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = commits.AdvanceExternalRevision();
            var controller =
                new ProductDesktopContainerLayoutInteractionController(commits);
            ProductWorkspaceState original = State();

            _ = controller.Handle(
                Request(ProductDesktopContainerLayoutInputPhase.Begin),
                original,
                false,
                editRevision,
                Topology());
            _ = controller.Handle(
                Request(
                    ProductDesktopContainerLayoutInputPhase.Update,
                    deltaX: 40,
                    deltaY: 24),
                original,
                false,
                editRevision,
                Topology());
            ProductDesktopContainerLayoutInteractionResult cancelled =
                controller.Handle(
                    Request(
                        ProductDesktopContainerLayoutInputPhase.Cancel,
                        cancellation:
                            ProductDesktopContainerLayoutCancellationReason
                                .EscapePressed),
                    original,
                    false,
                    editRevision,
                    Topology());

            Assert.Equal(
                ProductDesktopContainerLayoutInteractionStatus.Cancelled,
                cancelled.Status);
            Assert.True(cancelled.ClearPreview);
            Assert.Equal(0, saves.Snapshot.CurrentRevision);
            Assert.Equal(ProductWorkspaceSaveStatus.Clean, saves.Snapshot.Status);
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public async Task RealLeaseFailureCompensatesThroughInteractionController()
    {
        string sandbox = Sandbox("RealCompensation");
        string storeDirectory = Path.Combine(sandbox, "store");
        string sentinelPath = Path.Combine(sandbox, "desktop-sentinel.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(sentinelPath, "must-not-change");

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
            var controller =
                new ProductDesktopContainerLayoutInteractionController(commits);
            _ = controller.Handle(
                Request(ProductDesktopContainerLayoutInputPhase.Begin),
                original,
                false,
                editRevision,
                Topology());
            _ = controller.Handle(
                Request(
                    ProductDesktopContainerLayoutInputPhase.Update,
                    deltaX: 100,
                    deltaY: 50),
                original,
                false,
                editRevision,
                Topology());

            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductDesktopContainerLayoutInteractionResult committed =
                controller.Handle(
                    Request(
                        ProductDesktopContainerLayoutInputPhase.Complete,
                        deltaX: 100,
                        deltaY: 50),
                    original,
                    false,
                    editRevision,
                    Topology());
            long failedRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                failedRevision);
            ProductDesktopContainerLayoutPublicationResult compensation =
                controller.ObserveSave(
                    committed.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);
            long compensationRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                compensationRevision);

            Assert.True(compensation.IsCompensated);
            Assert.Equal(100, compensation.State!.Containers[0].Placement.XDip);
            Assert.Equal(100, compensation.State.Containers[0].Placement.YDip);
            ProductConfigurationLoadResult blockedReload = await store.LoadAsync();
            Assert.Equal(100, blockedReload.Document!.Containers[0].Placement.XDip);

            await lease.DisposeAsync();
            Assert.Equal(
                ProductWorkspaceSaveRetryStatus.Accepted,
                saves.Retry().Status);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                compensationRevision);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(100, reloaded.Document!.Containers[0].Placement.XDip);
            Assert.Equal(100, reloaded.Document.Containers[0].Placement.YDip);
            Assert.Equal("must-not-change", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    private static ProductDesktopContainerLayoutRequest Request(
        ProductDesktopContainerLayoutInputPhase phase,
        double deltaX = 0,
        double deltaY = 0,
        ProductDesktopContainerLayoutCancellationReason cancellation =
            ProductDesktopContainerLayoutCancellationReason.None) =>
        new(
            phase,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            "container-1",
            "display-1",
            ExpectedWorkspaceRevision: 1,
            ExpectedTopologyGeneration: 7,
            deltaX,
            deltaY,
            SnapEnabled: false,
            ShiftPressed: false,
            cancellation);

    private static ProductWorkspaceState State() => new()
    {
        ProfileId = "pf003d2-app-layout",
        Containers =
        [
            new()
            {
                Id = "container-1",
                Name = "Container 1",
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
                    WidthDip = 200,
                    HeightDip = 160,
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
        ProductConfigurationStore store) =>
        new(
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
        "LongGrid.PF003D2",
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
