using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerLayoutGestureCommitTests
{
    [Fact]
    public async Task ThousandRealUpdatesWriteNothingAndCompletionSavesExactlyOnce()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.LayoutGesture.Integration",
            Guid.NewGuid().ToString("N"));
        string storeDirectory = Path.Combine(sandbox, "store");
        string sentinelPath = Path.Combine(sandbox, "desktop-sentinel.txt");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(sentinelPath, "must-not-change");

        try
        {
            var store = new ProductConfigurationStore(storeDirectory);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long editRevision = coordinator.AdvanceExternalRevision();
            ProductWorkspaceState state = State();

            ProductWorkspaceContainerLayoutGestureBeginResult begin =
                ProductWorkspaceContainerLayoutGestureSession.Begin(
                    state,
                    editRevision,
                    currentTopologyGeneration: 7,
                    Displays(),
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    "container-1",
                    "display-1");
            Assert.True(begin.IsReady);

            ProductWorkspaceContainerLayoutGestureSnapshot? latest = null;
            for (int index = 1; index <= 1_000; index++)
            {
                latest = begin.Session!.Update(
                    state,
                    editRevision,
                    currentTopologyGeneration: 7,
                    Displays(),
                    cumulativeDeltaXDip: index / 10d,
                    cumulativeDeltaYDip: index / 20d,
                    snapEnabled: false,
                    shiftPressed: false);
            }

            ProductConfigurationLoadResult beforeComplete = await store.LoadAsync();
            Assert.Equal(1_000, latest!.UpdateCount);
            Assert.Equal(200, latest.Placement.XDip);
            Assert.Equal(150, latest.Placement.YDip);
            Assert.Equal(0, saves.Snapshot.CurrentRevision);
            Assert.Equal(ProductConfigurationLoadStatus.Missing, beforeComplete.Status);
            Assert.Equal("must-not-change", await File.ReadAllTextAsync(sentinelPath));

            ProductWorkspaceContainerLayoutGestureCompletionResult completed =
                begin.Session!.Complete(
                    state,
                    editRevision,
                    currentTopologyGeneration: 7,
                    Displays());
            ProductWorkspaceContainerLayoutGestureCommitResult committed =
                coordinator.CommitContainerLayoutGesture(
                    state,
                    currentTopologyGeneration: 7,
                    completed.Completion!);
            ProductWorkspaceContainerLayoutGestureCommitResult duplicate =
                coordinator.CommitContainerLayoutGesture(
                    state,
                    currentTopologyGeneration: 7,
                    completed.Completion!);
            ProductWorkspaceSaveCompletionResult saveCompletion =
                await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(completed.IsReady);
            Assert.True(committed.IsAccepted);
            Assert.Equal(2, committed.EditRevision);
            Assert.Equal(
                ProductWorkspaceContainerLayoutGestureCommitStatus
                    .StaleEditRevision,
                duplicate.Status);
            Assert.Equal(1, saves.Snapshot.CurrentRevision);
            Assert.Equal(
                ProductWorkspaceSaveCompletionStatus.Completed,
                saveCompletion.Status);
            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            Assert.InRange(
                Math.Abs(reloaded.Document!.Containers[0].Placement.XDip - 200),
                0,
                1);
            Assert.InRange(
                Math.Abs(reloaded.Document.Containers[0].Placement.YDip - 150),
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

    [Fact]
    public void StaleUpdateCancelsAndRestoresOriginalPlacement()
    {
        ProductWorkspaceState state = State();
        ProductWorkspaceContainerLayoutGestureSession session =
            ProductWorkspaceContainerLayoutGestureSession.Begin(
                state,
                currentEditRevision: 5,
                currentTopologyGeneration: 7,
                Displays(),
                ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight,
                "container-1",
                "display-1").Session!;

        _ = session.Update(
            state,
            5,
            7,
            Displays(),
            40,
            40,
            snapEnabled: false,
            shiftPressed: false);
        ProductWorkspaceContainerLayoutGestureSnapshot stale = session.Update(
            state,
            currentEditRevision: 6,
            currentTopologyGeneration: 7,
            Displays(),
            50,
            50,
            snapEnabled: false,
            shiftPressed: false);
        ProductWorkspaceContainerLayoutGestureCompletionResult completed =
            session.Complete(state, 6, 7, Displays());

        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled,
            stale.Status);
        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.StaleEditRevision,
            stale.PreviewStatus);
        Assert.Equal(100, stale.Placement.XDip);
        Assert.Equal(100, stale.Placement.YDip);
        Assert.Equal(200, stale.Placement.WidthDip);
        Assert.Equal(160, stale.Placement.HeightDip);
        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureCompletionStatus.Unavailable,
            completed.Status);
    }

    [Fact]
    public void ExplicitCancelAndSecondCompletionAreIdempotent()
    {
        ProductWorkspaceState state = State();
        ProductWorkspaceContainerLayoutGestureSession session =
            ProductWorkspaceContainerLayoutGestureSession.Begin(
                state,
                5,
                7,
                Displays(),
                ProductWorkspaceContainerLayoutGestureKind.Move,
                "container-1",
                "display-1").Session!;
        _ = session.Update(
            state,
            5,
            7,
            Displays(),
            10,
            10,
            snapEnabled: false,
            shiftPressed: false);

        ProductWorkspaceContainerLayoutGestureSnapshot cancelled = session.Cancel();
        ProductWorkspaceContainerLayoutGestureSnapshot cancelledAgain = session.Cancel();
        ProductWorkspaceContainerLayoutGestureCompletionResult complete =
            session.Complete(state, 5, 7, Displays());

        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled,
            cancelled.Status);
        Assert.Equal(cancelled, cancelledAgain);
        Assert.Equal(100, cancelled.Placement.XDip);
        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureCompletionStatus.Unavailable,
            complete.Status);
    }

    [Fact]
    public async Task CommitRejectsPlacementChangedAfterCompletionWithoutSaving()
    {
        ProductWorkspaceState state = State();
        ProductWorkspaceContainerLayoutGestureSession session =
            ProductWorkspaceContainerLayoutGestureSession.Begin(
                state,
                1,
                7,
                Displays(),
                ProductWorkspaceContainerLayoutGestureKind.Move,
                "container-1",
                "display-1").Session!;
        _ = session.Update(
            state,
            1,
            7,
            Displays(),
            20,
            20,
            snapEnabled: false,
            shiftPressed: false);
        ProductWorkspaceContainerLayoutGestureCompletion completion =
            session.Complete(state, 1, 7, Displays()).Completion!;
        ProductWorkspaceState changed = state with
        {
            Containers =
            [
                state.Containers[0] with
                {
                    Placement = state.Containers[0].Placement with { XDip = 101 },
                },
            ],
        };

        await using var saves = new ProductWorkspaceSaveController(
            new SuccessfulWorkflow(),
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        _ = coordinator.AdvanceExternalRevision();
        ProductWorkspaceContainerLayoutGestureCommitResult result =
            coordinator.CommitContainerLayoutGesture(
                changed,
                currentTopologyGeneration: 7,
                completion);

        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureCommitStatus.StateChanged,
            result.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
    }

    [Fact]
    public async Task CommitRejectsTopologyChangedAfterCompletionWithoutSaving()
    {
        ProductWorkspaceState state = State();
        ProductWorkspaceContainerLayoutGestureSession session =
            ProductWorkspaceContainerLayoutGestureSession.Begin(
                state,
                1,
                7,
                Displays(),
                ProductWorkspaceContainerLayoutGestureKind.Move,
                "container-1",
                "display-1").Session!;
        _ = session.Update(
            state,
            1,
            7,
            Displays(),
            20,
            20,
            snapEnabled: false,
            shiftPressed: false);
        ProductWorkspaceContainerLayoutGestureCompletion completion =
            session.Complete(state, 1, 7, Displays()).Completion!;
        await using var saves = new ProductWorkspaceSaveController(
            new SuccessfulWorkflow(),
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));
        var coordinator = new ProductWorkspaceCommitCoordinator(saves);
        _ = coordinator.AdvanceExternalRevision();

        ProductWorkspaceContainerLayoutGestureCommitResult result =
            coordinator.CommitContainerLayoutGesture(
                state,
                currentTopologyGeneration: 8,
                completion);

        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureCommitStatus.StaleTopology,
            result.Status);
        Assert.Equal(0, saves.Snapshot.CurrentRevision);
    }

    private static ProductWorkspaceState State() =>
        new()
        {
            ProfileId = "pf003-gesture-commit",
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

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SuccessfulWorkflow : IProductConfigurationSaveWorkflow
    {
        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.Saved,
                null,
                CanRetry: false));

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                null,
                CanRetry: false));

        public void DiscardRetry()
        {
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
