using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Infrastructure.DesktopHost;

public sealed record ProductResourceStabilityPreflightResult(
    string Outcome,
    int LifecycleIterations,
    int CatalogIterations,
    int ClassifierIterations,
    int SyntheticSurfacesCreated,
    int SyntheticSurfacesReleased,
    int CatalogRefreshes,
    int CatalogNotifications,
    bool SystemEventStateRecoveredEveryIteration,
    bool AllOwnedResourcesReleased,
    bool ThumbnailWorkerIsolationGateRequired,
    bool RealApp24HourSoakRequired,
    bool Real24HourEvidenceCollected,
    bool ReadsRealDesktop,
    bool CreatesNativeWindows,
    bool RealFileOperationsAllowed);

public static class ProductResourceStabilityPreflight
{
    public const int LifecycleIterations = 200;
    public const int CatalogIterations = 200;
    public const int ClassifierIterations = 200;
    public const int CatalogRefreshesPerIteration = 3;

    public static async Task<ProductResourceStabilityPreflightResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        int surfacesCreated = 0;
        int surfacesReleased = 0;
        for (int iteration = 0; iteration < LifecycleIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var factory = new RecordingSurfaceFactory();
            ProductDesktopHostFeatureDecision host =
                ProductDesktopHostFeaturePolicy.Evaluate("1");
            var interaction = new ProductDesktopInteractionDevelopmentController(
                ProductDesktopInteractionFeaturePolicy.Evaluate(host, "1"));
            var controller = new ProductDesktopHostLifecycleController(
                host,
                factory,
                new FactoryBackedInspector(factory),
                interaction);
            try
            {
                ProductDesktopHostLifecycleSnapshot ready =
                    controller.ApplyProjectionUpdate(ReadyUpdate(iteration + 1, 1, 1));
                RequireReady(ready, expectedWindows: 1);
                ProductDesktopHostLifecycleSnapshot suspended =
                    controller.ApplySystemSurfaceEvent(new(
                        ProductDesktopInteractionSystemSurfaceEventKind.ExplorerRestarted,
                        1,
                        FixedTime(iteration)));
                Require(
                    suspended.Status
                        == ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
                        && suspended.OwnedWindowCount == 1
                        && !suspended.PassiveWindowContractAttested,
                    "A lifecycle churn iteration did not hide on Explorer restart.");
                RequireReady(
                    controller.ApplySystemSurfaceEvent(new(
                        ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate,
                        2,
                        FixedTime(iteration).AddSeconds(1))),
                    expectedWindows: 1);

                ProductDesktopHostLifecycleSnapshot topologySuspended =
                    controller.ApplyProjectionUpdate(
                        ProductDesktopHostProjectionUpdate.Create(
                            iteration + 1,
                            2,
                            ProductDesktopHostProjectionDisposition
                                .TopologyRefreshing));
                Require(
                    topologySuspended.Status
                        == ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology
                        && topologySuspended.OwnedWindowCount == 0
                        && factory.Surfaces.Single().IsDisposed,
                    "A lifecycle churn iteration retained its stale topology surface.");
                RequireReady(
                    controller.ApplyProjectionUpdate(
                        ReadyUpdate(iteration + 1, 2, 2)),
                    expectedWindows: 2);
            }
            finally
            {
                await controller.DisposeAsync().ConfigureAwait(false);
            }

            surfacesCreated += factory.Surfaces.Count;
            surfacesReleased += factory.Surfaces.Count(surface => surface.IsDisposed);
            Require(
                factory.Surfaces.Count == 3
                    && factory.Surfaces.All(surface => surface.IsDisposed),
                "A lifecycle churn iteration retained a synthetic surface.");
        }

        int catalogRefreshes = 0;
        int catalogNotifications = 0;
        for (int iteration = 0; iteration < CatalogIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var controller = new ProductDesktopCatalogController(
                new ReadyCatalogReader());
            int notifications = 0;
            EventHandler<ProductDesktopCatalogSnapshot> observer =
                (_, _) => notifications++;
            controller.SnapshotChanged += observer;
            for (int refresh = 0; refresh < CatalogRefreshesPerIteration; refresh++)
            {
                ProductDesktopCatalogRefreshResult result =
                    await controller.RefreshAsync(cancellationToken).ConfigureAwait(false);
                Require(
                    result.Status == ProductDesktopCatalogRefreshStatus.Published
                        && result.Snapshot.Status == ProductDesktopCatalogStatus.Ready,
                    "A catalog churn refresh did not publish a finite ready state.");
                catalogRefreshes++;
            }

            controller.SnapshotChanged -= observer;
            await controller.DisposeAsync().ConfigureAwait(false);
            Require(
                controller.Snapshot.Generation == CatalogRefreshesPerIteration
                    && notifications == CatalogRefreshesPerIteration * 2,
                "A catalog churn iteration drifted its generation or notifications.");
            catalogNotifications += notifications;
        }

        bool classifiersRecovered = true;
        for (int iteration = 0; iteration < ClassifierIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var classifier = new ProductDesktopSystemSurfaceEventClassifier();
            ProductDesktopSystemSurfaceSample safe = SafeSystemSample();
            Require(
                classifier.Observe(safe).Count == 0,
                "A new classifier did not begin from a finite safe sample.");
            IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> unsafeEvents =
                classifier.ObserveFocusLost();
            IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> firstSafe =
                classifier.Observe(safe);
            IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> recovered =
                classifier.Observe(safe);
            bool recoveredExactly = unsafeEvents.SequenceEqual(
                    [ProductDesktopInteractionSystemSurfaceEventKind.FocusLost])
                && firstSafe.Count == 0
                && recovered.SequenceEqual(
                    [ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate]);
            classifiersRecovered &= recoveredExactly;
            Require(
                recoveredExactly,
                "A system-surface classifier iteration drifted its recovery state.");
        }

        bool allReleased = surfacesCreated == LifecycleIterations * 3
            && surfacesReleased == surfacesCreated
            && catalogRefreshes
                == CatalogIterations * CatalogRefreshesPerIteration
            && catalogNotifications == catalogRefreshes * 2;
        Require(allReleased, "The accelerated resource ownership totals did not close.");
        return new(
            "AcceleratedPass",
            LifecycleIterations,
            CatalogIterations,
            ClassifierIterations,
            surfacesCreated,
            surfacesReleased,
            catalogRefreshes,
            catalogNotifications,
            classifiersRecovered,
            allReleased,
            ThumbnailWorkerIsolationGateRequired: true,
            RealApp24HourSoakRequired: true,
            Real24HourEvidenceCollected: false,
            ReadsRealDesktop: false,
            CreatesNativeWindows: false,
            RealFileOperationsAllowed: false);
    }

    private static ProductDesktopHostProjectionUpdate ReadyUpdate(
        long workspaceRevision,
        long topologyGeneration,
        int displayCount)
    {
        ProductDesktopHostDisplayProjection[] displays = Enumerable.Range(
                0,
                displayCount)
            .Select(index => ProductDesktopHostDisplayProjection.Create(
                index == 0 ? "display-primary" : $"display-secondary-{index}",
                index == 0
                    ? new PixelRect(0, 0, 1920, 1040)
                    : new PixelRect(-1280 * index, 0, 1280, 984),
                index == 0 ? 96u : 120u,
                [Projection($"container-{index + 1}")],
                isPrimary: index == 0,
                workspaceIsEmpty: false))
            .ToArray();
        ProductDesktopHostProjectionBatch batch =
            ProductDesktopHostProjectionBatch.Create(
                workspaceRevision,
                topologyGeneration,
                new string('C', 64),
                displays);
        return ProductDesktopHostProjectionUpdate.Create(
            workspaceRevision,
            topologyGeneration,
            ProductDesktopHostProjectionDisposition.Ready,
            batch);
    }

    private static ProductDesktopHostReadOnlyProjection Projection(string id) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            "Anonymous workspace",
            ["Anonymous item"],
            "#2457D6",
            0.82,
            isCollapsed: false,
            24,
            36,
            360,
            240,
            itemIds: [$"{id}-item"]);

    private static void RequireReady(
        ProductDesktopHostLifecycleSnapshot snapshot,
        int expectedWindows) =>
        Require(
            snapshot.Status == ProductDesktopHostLifecycleStatus.ReadyReadOnly
                && snapshot.OwnedWindowCount == expectedWindows
                && snapshot.ReadOnlyAccessibilityAvailable
                && snapshot.PassiveWindowContractAttested,
            "A lifecycle churn iteration did not publish an attested ready state.");

    private static DateTimeOffset FixedTime(int iteration) => new DateTimeOffset(
        2026,
        8,
        14,
        0,
        0,
        0,
        TimeSpan.Zero).AddMinutes(iteration);

    private static ProductDesktopSystemSurfaceSample SafeSystemSample() => new(
        ShellWindow: new nint(10),
        ForegroundWindow: new nint(20),
        FullScreenStateKnown: true,
        FullScreenActive: false,
        RemoteSession: false);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ReadyCatalogReader : IProductDesktopCatalogReader
    {
        public Task<ProductDesktopCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductDesktopCatalogReadResult(
                ProductDesktopCatalogReadStatus.Ready,
                Array.Empty<DesktopCatalogEntry>(),
                Array.Empty<ProductDesktopCatalogSourceSnapshot>()));
        }
    }

    private sealed class RecordingSurfaceFactory
        : IProductDesktopHostReadOnlySurfaceFactory
    {
        private nint nextHandle = 100;

        internal List<RecordingSurface> Surfaces { get; } = [];

        public IProductDesktopHostReadOnlySurface Create(
            ProductDesktopHostDisplayProjection projection,
            nint instanceMarker,
            bool startHidden)
        {
            var surface = new RecordingSurface(
                nextHandle++,
                instanceMarker,
                (uint)Environment.ProcessId,
                42,
                startHidden);
            Surfaces.Add(surface);
            return surface;
        }
    }

    private sealed class RecordingSurface(
        nint handle,
        nint instanceMarker,
        uint processId,
        uint threadId,
        bool startHidden) : IProductDesktopHostReadOnlySurface
    {
        private ProductDesktopInteractionSurfaceMode mode = startHidden
            ? ProductDesktopInteractionSurfaceMode.Hidden
            : ProductDesktopInteractionSurfaceMode.Passive;

        public nint Handle { get; } = handle;

        public nint InstanceMarker { get; } = instanceMarker;

        public uint ProcessId { get; } = processId;

        public uint ThreadId { get; } = threadId;

        public bool ReadOnlyAccessibilityAttested => true;

        public bool PassiveWindowContractAttested =>
            mode == ProductDesktopInteractionSurfaceMode.Passive;

        public bool ExplicitWindowContractAttested =>
            mode == ProductDesktopInteractionSurfaceMode.Explicit;

        public bool HiddenWindowContractAttested =>
            mode == ProductDesktopInteractionSurfaceMode.Hidden;

        internal bool IsDisposed { get; private set; }

        public bool ApplyExplicit()
        {
            mode = ProductDesktopInteractionSurfaceMode.Explicit;
            return true;
        }

        public bool ApplyPassive()
        {
            mode = ProductDesktopInteractionSurfaceMode.Passive;
            return true;
        }

        public bool ApplyHidden()
        {
            mode = ProductDesktopInteractionSurfaceMode.Hidden;
            return true;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FactoryBackedInspector(RecordingSurfaceFactory factory)
        : IProductDesktopHostWindowInspector
    {
        public ProductDesktopHostWindowObservation Inspect(nint handle)
        {
            RecordingSurface? surface = factory.Surfaces.LastOrDefault(candidate =>
                candidate.Handle == handle);
            return surface is null || surface.IsDisposed
                ? ProductDesktopHostWindowObservation.Missing
                : new(
                    true,
                    surface.ProcessId,
                    surface.ThreadId,
                    surface.InstanceMarker,
                    new(24, 36, 360, 240));
        }
    }
}
