using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerLayoutCompensationTests
{
    [Fact]
    public void MatchingFailureRequiresCompensation()
    {
        ProductWorkspaceContainerLayoutPublicationToken token = Token();
        ProductWorkspaceSaveSnapshot failed = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = ProductWorkspaceSaveStatus.Failed,
            CurrentRevision = 3,
            Failure = ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
            CanRetry = true,
        };

        ProductWorkspaceContainerLayoutPublicationDecision decision =
            ProductWorkspaceContainerLayoutPublication.Evaluate(
                token,
                failed,
                currentWorkspaceRevision: 7,
                token.CommittedPlacement);

        Assert.Equal(
            ProductWorkspaceContainerLayoutPublicationDecision
                .CompensationRequired,
            decision);
    }

    [Theory]
    [InlineData(8, 3, 200)]
    [InlineData(7, 4, 200)]
    [InlineData(7, 3, 201)]
    public void LaterWorkspaceSaveOrPlacementNeverCompensates(
        long workspaceRevision,
        long saveRevision,
        double currentX)
    {
        ProductWorkspaceContainerLayoutPublicationToken token = Token();
        ProductWorkspaceSaveSnapshot failed = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = ProductWorkspaceSaveStatus.Failed,
            CurrentRevision = saveRevision,
            Failure = ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
            CanRetry = true,
        };

        ProductWorkspaceContainerLayoutPublicationDecision decision =
            ProductWorkspaceContainerLayoutPublication.Evaluate(
                token,
                failed,
                workspaceRevision,
                token.CommittedPlacement with { XDip = currentX });

        Assert.Equal(
            ProductWorkspaceContainerLayoutPublicationDecision.Superseded,
            decision);
    }

    [Fact]
    public void WaitingAndSavedSnapshotsHaveFiniteDecisions()
    {
        ProductWorkspaceContainerLayoutPublicationToken token = Token();
        ProductWorkspaceSaveSnapshot waiting = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = ProductWorkspaceSaveStatus.WaitingForDebounce,
            CurrentRevision = 3,
        };
        ProductWorkspaceSaveSnapshot saved = waiting with
        {
            Status = ProductWorkspaceSaveStatus.Saved,
            SavedRevision = 3,
        };

        Assert.Equal(
            ProductWorkspaceContainerLayoutPublicationDecision.AwaitingSave,
            ProductWorkspaceContainerLayoutPublication.Evaluate(
                token,
                waiting,
                7,
                token.CommittedPlacement));
        Assert.Equal(
            ProductWorkspaceContainerLayoutPublicationDecision.Published,
            ProductWorkspaceContainerLayoutPublication.Evaluate(
                token,
                saved,
                7,
                token.CommittedPlacement));
    }

    [Fact]
    public async Task RealLeaseFailureRestoresMemoryAndDiskWithoutTouchingSentinel()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.LayoutCompensation.RealLeaseFailure",
            Guid.NewGuid().ToString("N"));
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
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = coordinator.AdvanceExternalRevision();
            ProductWorkspaceContainerLayoutGestureSession session =
                ProductWorkspaceContainerLayoutGestureSession.Begin(
                    original,
                    editRevision,
                    currentTopologyGeneration: 7,
                    Displays(),
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    "container-1",
                    "display-1").Session!;
            _ = session.Update(
                original,
                editRevision,
                7,
                Displays(),
                cumulativeDeltaXDip: 100,
                cumulativeDeltaYDip: 50,
                snapEnabled: false,
                shiftPressed: false);
            ProductWorkspaceContainerLayoutGestureCompletion completion =
                session.Complete(original, editRevision, 7, Displays()).Completion!;

            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductWorkspaceContainerLayoutGestureCommitResult committed =
                coordinator.CommitContainerLayoutGesture(
                    original,
                    currentTopologyGeneration: 7,
                    completion);
            long failedSaveRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                failedSaveRevision);
            ProductConfigurationLoadResult diskAfterFailure = await store.LoadAsync();

            Assert.True(committed.IsAccepted);
            Assert.NotNull(committed.PublicationToken);
            Assert.Equal(
                "拖动或缩放方格",
                Assert.Single(coordinator
                    .GetSessionHistorySnapshot(committed.State)
                    .Items).ActionText);
            Assert.Equal(200, committed.State!.Containers[0].Placement.XDip);
            Assert.Equal(150, committed.State.Containers[0].Placement.YDip);
            Assert.Equal(
                ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                saves.Snapshot.Failure);
            Assert.Equal(100, diskAfterFailure.Document!.Containers[0].Placement.XDip);
            Assert.Equal(100, diskAfterFailure.Document.Containers[0].Placement.YDip);

            ProductWorkspaceContainerLayoutPublicationToken forged =
                committed.PublicationToken! with { };
            ProductWorkspaceContainerLayoutCompensationCommitResult forgedResult =
                coordinator.CompensateContainerLayoutGesture(
                    committed.State,
                    forged);
            committed.PublicationToken!.OriginalPlacement.ExtensionData!["marker"] =
                Json("tampered");
            ProductWorkspaceContainerLayoutCompensationCommitResult compensation =
                coordinator.CompensateContainerLayoutGesture(
                    committed.State,
                    committed.PublicationToken!);
            ProductWorkspaceContainerLayoutCompensationCommitResult duplicate =
                coordinator.CompensateContainerLayoutGesture(
                    compensation.State!,
                    committed.PublicationToken!);
            long compensationSaveRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                compensationSaveRevision);
            ProductConfigurationLoadResult diskWhileBlocked = await store.LoadAsync();

            Assert.True(compensation.IsAccepted);
            Assert.Empty(coordinator
                .GetSessionHistorySnapshot(compensation.State)
                .Items);
            Assert.Equal(
                ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                compensation.SourceFailure);
            Assert.Equal(
                ProductWorkspaceContainerLayoutCompensationCommitStatus
                    .InvalidRequest,
                forgedResult.Status);
            Assert.Equal(100, compensation.State!.Containers[0].Placement.XDip);
            Assert.Equal(100, compensation.State.Containers[0].Placement.YDip);
            Assert.Equal(
                "\"original\"",
                compensation.State.Containers[0].Placement.ExtensionData!["marker"]
                    .GetRawText());
            Assert.Equal(
                ProductWorkspaceContainerLayoutCompensationCommitStatus.Superseded,
                duplicate.Status);
            Assert.Equal(100, diskWhileBlocked.Document!.Containers[0].Placement.XDip);
            Assert.Equal(100, diskWhileBlocked.Document.Containers[0].Placement.YDip);

            await lease.DisposeAsync();
            Assert.Equal(
                ProductWorkspaceSaveRetryStatus.Accepted,
                saves.Retry().Status);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                compensationSaveRevision);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            Assert.InRange(
                Math.Abs(reloaded.Document!.Containers[0].Placement.XDip - 100),
                0,
                1);
            Assert.InRange(
                Math.Abs(reloaded.Document.Containers[0].Placement.YDip - 100),
                0,
                1);
            Assert.Equal("must-not-change", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceContainerLayoutPublicationToken Token() =>
        new(
            Guid.Parse("239985d8-0914-470d-b6f7-88e77c986f12"),
            "container-1",
            WorkspaceRevision: 7,
            SaveRevision: 3,
            TopologyGeneration: 5,
            Placement(100, 100),
            Placement(200, 150));

    private static ProductWorkspaceState State() =>
        new()
        {
            ProfileId = "pf003-layout-compensation",
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
                    Placement = Placement(100, 100),
                    Items = [],
                },
            ],
        };

    private static ProductContainerPlacementState Placement(double x, double y) =>
        new()
        {
            DisplayKey = "display-1",
            XDip = x,
            YDip = y,
            WidthDip = 200,
            HeightDip = 160,
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["marker"] = Json("original"),
            },
        };

    private static JsonElement Json(string value)
    {
        using JsonDocument document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }

    private static DisplayTopologyNode[] Displays() =>
    [
        new(
            "display-1",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            IsPrimary: true),
    ];

    private static FileStream AcquireLease(string path) =>
        new(
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

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
