using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using LongGrid.ThumbnailWorker;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductThumbnailWorkerLifecycleTests
{
    [Fact]
    public void OwnedProfileDeletionRetriesBoundedlyAndReportsFinalEvidence()
    {
        string profileName =
            $"LongGridThumbnailWorker{Guid.NewGuid():N}";
        int calls = 0;
        var waits = new List<TimeSpan>();

        ThumbnailAppContainerProfileDeleteResult recovered =
            ThumbnailAppContainerProfile.DeleteByNameWithRetry(
                profileName,
                maximumAttempts: 4,
                retryDelay: TimeSpan.FromMilliseconds(50),
                _ => ++calls < 3
                    ? unchecked((int)0x80070020)
                    : 0,
                waits.Add);

        Assert.True(recovered.Deleted);
        Assert.Equal(3, recovered.Attempts);
        Assert.Equal(0, recovered.LastHResult);
        Assert.Equal(3, calls);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)],
            waits);

        ThumbnailAppContainerProfileDeleteResult exhausted =
            ThumbnailAppContainerProfile.DeleteByNameWithRetry(
                profileName,
                maximumAttempts: 2,
                retryDelay: TimeSpan.Zero,
                _ => unchecked((int)0x80070020),
                _ => throw new InvalidOperationException());
        Assert.False(exhausted.Deleted);
        Assert.Equal(2, exhausted.Attempts);
        Assert.Equal(unchecked((int)0x80070020), exhausted.LastHResult);
    }

    [Fact]
    public void ProfileDeletionRetryRejectsUnownedNamesWithoutCallingWindows()
    {
        bool called = false;

        ThumbnailAppContainerProfileDeleteResult result =
            ThumbnailAppContainerProfile.DeleteByNameWithRetry(
                "not-owned",
                maximumAttempts: 2,
                retryDelay: TimeSpan.Zero,
                _ =>
                {
                    called = true;
                    return 0;
                });

        Assert.False(result.Deleted);
        Assert.Equal(0, result.Attempts);
        Assert.Equal(unchecked((int)0x80070057), result.LastHResult);
        Assert.False(called);
    }

    [Fact]
    public void ProductRuntimeDeploymentSetIsFixedAndAvailable()
    {
        string[] required =
        [
            "LongGrid.ThumbnailWorker.exe",
            "LongGrid.ThumbnailWorker.dll",
            "LongGrid.ThumbnailWorker.deps.json",
            "LongGrid.ThumbnailWorker.runtimeconfig.json",
        ];

        Assert.Equal(required, ThumbnailAppContainerProfile.RequiredRuntimeFileNames);
        Assert.All(required, fileName => Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, fileName)),
            $"Required formal worker runtime file was missing: {fileName}"));
        Assert.DoesNotContain(
            ThumbnailAppContainerProfile.RequiredRuntimeFileNames,
            fileName => fileName.Contains(
                "Spikes",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DisabledSessionDoesNotCreateWorkerRuntime()
    {
        bool factoryCalled = false;
        using ProductThumbnailWorkerLifecycleController controller =
            ProductThumbnailWorkerLifecycleController.Start(
                DisabledTelemetryFeature(),
                () =>
                {
                    factoryCalled = true;
                    return new FakeRuntime(AttestedRuntimeSnapshot());
                });

        Assert.False(factoryCalled);
        Assert.Equal(
            ProductThumbnailWorkerLifecycleSnapshot.Disabled,
            controller.Snapshot);
    }

    [Fact]
    public void ControlledSessionPublishesOnlyAttestedBoundedCounts()
    {
        var runtime = new FakeRuntime(AttestedRuntimeSnapshot());
        using ProductThumbnailWorkerLifecycleController controller =
            ProductThumbnailWorkerLifecycleController.Start(
                EnabledTelemetryFeature(),
                () => runtime);

        ProductThumbnailWorkerLifecycleSnapshot snapshot = controller.Snapshot;
        Assert.Equal(
            ProductThumbnailWorkerLifecycleStatus.ReadyIdleRestricted,
            snapshot.Status);
        Assert.Equal(1, snapshot.Generation);
        Assert.True(snapshot.FormalIntegrationAvailable);
        Assert.Equal(1, snapshot.WorkerProcessCount);
        Assert.Equal(1, snapshot.ActiveOwnedProfileCount);
        Assert.True(snapshot.IsZeroCapabilityAppContainer);
        Assert.True(snapshot.UsesKillOnJobClose);
        Assert.False(runtime.Disposed);
    }

    [Fact]
    public void UnattestedRuntimeFailsClosedAndIsDisposed()
    {
        var runtime = new FakeRuntime(
            AttestedRuntimeSnapshot() with
            {
                IsZeroCapabilityAppContainer = false,
            });
        using ProductThumbnailWorkerLifecycleController controller =
            ProductThumbnailWorkerLifecycleController.Start(
                EnabledTelemetryFeature(),
                () => runtime);

        Assert.True(runtime.Disposed);
        Assert.True(controller.OwnedProfileDeletionConfirmed);
        Assert.Equal(
            new ProductThumbnailWorkerLifecycleSnapshot(
                ProductThumbnailWorkerLifecycleStatus.FailedClosed,
                Generation: 1,
                FormalIntegrationAvailable: false,
                WorkerProcessCount: 0,
                ActiveOwnedProfileCount: 0,
                IsZeroCapabilityAppContainer: false,
                UsesKillOnJobClose: false),
            controller.Snapshot);
    }

    [Fact]
    public void DisposeReleasesWorkerAndProfileCounts()
    {
        var runtime = new FakeRuntime(AttestedRuntimeSnapshot());
        ProductThumbnailWorkerLifecycleController controller =
            ProductThumbnailWorkerLifecycleController.Start(
                EnabledTelemetryFeature(),
                () => runtime);

        controller.Dispose();

        Assert.True(runtime.Disposed);
        Assert.Equal(
            new ProductThumbnailWorkerLifecycleSnapshot(
                ProductThumbnailWorkerLifecycleStatus.Disposed,
                Generation: 2,
                FormalIntegrationAvailable: false,
                WorkerProcessCount: 0,
                ActiveOwnedProfileCount: 0,
                IsZeroCapabilityAppContainer: false,
                UsesKillOnJobClose: false),
            controller.Snapshot);
    }

    [Fact]
    public void UnexpectedWorkerExitTransitionsTelemetryToFailedClosed()
    {
        var runtime = new FakeRuntime(AttestedRuntimeSnapshot());
        using ProductThumbnailWorkerLifecycleController controller =
            ProductThumbnailWorkerLifecycleController.Start(
                EnabledTelemetryFeature(),
                () => runtime);
        runtime.CurrentSnapshot = runtime.CurrentSnapshot with
        {
            IsStarted = false,
            WorkerProcessCount = 0,
        };

        ProductThumbnailWorkerLifecycleSnapshot snapshot = controller.Snapshot;

        Assert.True(runtime.Disposed);
        Assert.Equal(
            ProductThumbnailWorkerLifecycleStatus.FailedClosed,
            snapshot.Status);
        Assert.Equal(2, snapshot.Generation);
        Assert.False(snapshot.FormalIntegrationAvailable);
        Assert.Equal(0, snapshot.WorkerProcessCount);
        Assert.Equal(0, snapshot.ActiveOwnedProfileCount);
    }

    private static ProductResourceTelemetryFeatureDecision
        EnabledTelemetryFeature() => new(
            ProductResourceTelemetryFeatureStatus.EnabledForControlledSession,
            "LongGrid.ResourceTelemetry.0123456789abcdef0123456789abcdef");

    private static ProductResourceTelemetryFeatureDecision
        DisabledTelemetryFeature() => new(
            ProductResourceTelemetryFeatureStatus.DisabledBySessionPolicy,
            PipeName: null);

    private static RestrictedThumbnailWorkerRuntimeSnapshot
        AttestedRuntimeSnapshot() => new(
            IsStarted: true,
            WorkerProcessCount: 1,
            ActiveOwnedProfileCount: 1,
            IsZeroCapabilityAppContainer: true,
            UsesKillOnJobClose: true);

    private sealed class FakeRuntime(
        RestrictedThumbnailWorkerRuntimeSnapshot snapshot) :
        IProductThumbnailWorkerRuntime
    {
        public bool Disposed { get; private set; }

        public RestrictedThumbnailWorkerRuntimeSnapshot CurrentSnapshot { get; set; }
            = snapshot;

        public RestrictedThumbnailWorkerRuntimeSnapshot Snapshot => CurrentSnapshot;

        public bool OwnedProfileDeletionConfirmed => Disposed;

        public void Dispose() => Disposed = true;
    }
}
