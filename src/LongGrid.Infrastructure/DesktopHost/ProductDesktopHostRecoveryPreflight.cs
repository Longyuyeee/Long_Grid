using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public sealed record ProductDesktopHostRecoveryPreflightResult(
    string Outcome,
    int ScenarioCount,
    bool ExplorerRestartRecovered,
    bool SessionUnavailableRecovered,
    bool TopologyUnavailableRecovered,
    bool DisplayReplacementReleasedOldSurfaces,
    bool HostRestartRejectedStaleIdentity,
    bool AllSyntheticSurfacesReleased,
    bool ReadsRealDesktop,
    bool CreatesNativeWindows,
    bool RealFileOperationsAllowed);

public static class ProductDesktopHostRecoveryPreflight
{
    public const int ScenarioCount = 5;

    public static async Task<ProductDesktopHostRecoveryPreflightResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var systemFactory = new RecordingSurfaceFactory();
        ProductDesktopHostFeatureDecision host =
            ProductDesktopHostFeaturePolicy.Evaluate("1");
        var interaction = new ProductDesktopInteractionDevelopmentController(
            ProductDesktopInteractionFeaturePolicy.Evaluate(host, "1"));
        await using var systemController = new ProductDesktopHostLifecycleController(
            host,
            systemFactory,
            new FactoryBackedInspector(systemFactory),
            interaction);
        ProductDesktopHostLifecycleSnapshot initial =
            systemController.ApplyProjectionUpdate(ReadyUpdate(1, 1, 1));
        Require(
            initial.Status == ProductDesktopHostLifecycleStatus.ReadyReadOnly
                && initial.OwnedWindowCount == 1,
            "The synthetic DesktopHost did not enter its ready baseline.");
        DateTimeOffset observedAt = new(
            2026,
            8,
            14,
            0,
            0,
            0,
            TimeSpan.Zero);
        bool explorerRecovered = SystemSurfaceRoundTrip(
            systemController,
            ProductDesktopInteractionSystemSurfaceEventKind.ExplorerRestarted,
            sequence: 1,
            observedAt);
        bool sessionRecovered = SystemSurfaceRoundTrip(
            systemController,
            ProductDesktopInteractionSystemSurfaceEventKind.SessionUnavailable,
            sequence: 3,
            observedAt.AddSeconds(2));

        var topologyFactory = new RecordingSurfaceFactory();
        await using var topologyLifecycle = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            topologyFactory,
            new FactoryBackedInspector(topologyFactory));
        var reader = new SequenceTopologyReader(
            TopologyResult(ProductDisplayTopologyReadStatus.Unavailable),
            TopologyResult(
                ProductDisplayTopologyReadStatus.Ready,
                PrimaryDisplay(),
                SecondaryDisplay()));
        await using var topology = new ProductDisplayTopologyController(reader);
        ProductDesktopHostLifecycleSnapshot topologyBaseline =
            topologyLifecycle.ApplyProjectionUpdate(ReadyUpdate(2, 1, 1));
        RecordingSurface topologyOriginal = topologyFactory.Surfaces.Single();
        _ = topologyLifecycle.ApplyProjectionUpdate(
            ProductDesktopHostProjectionUpdate.Create(
                2,
                2,
                ProductDesktopHostProjectionDisposition.TopologyRefreshing));
        ProductDisplayTopologyRefreshResult unavailable =
            await topology.RefreshAsync(cancellationToken).ConfigureAwait(false);
        ProductDesktopHostLifecycleSnapshot unavailableHost =
            topologyLifecycle.ApplyProjectionUpdate(
                ProductDesktopHostProjectionUpdate.Create(
                    2,
                    unavailable.Generation,
                    ProductDesktopHostProjectionDisposition.TopologyUnavailable));
        ProductDisplayTopologyRefreshResult recoveredTopology =
            await topology.RefreshAsync(cancellationToken).ConfigureAwait(false);
        ProductDesktopHostLifecycleSnapshot recoveredHost =
            topologyLifecycle.ApplyProjectionUpdate(
                ReadyUpdate(2, recoveredTopology.Generation, displayCount: 2));
        bool topologyRecovered =
            topologyBaseline.Status == ProductDesktopHostLifecycleStatus.ReadyReadOnly
            && unavailable.Status == ProductDisplayTopologyRefreshStatus.Published
            && unavailable.Snapshot.Status == ProductDisplayTopologyStatus.Unavailable
            && unavailableHost.Status
                == ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology
            && unavailableHost.OwnedWindowCount == 0
            && topologyOriginal.IsDisposed
            && recoveredTopology.Snapshot.Status == ProductDisplayTopologyStatus.Ready
            && recoveredTopology.Snapshot.Displays.Count == 2
            && recoveredHost.Status == ProductDesktopHostLifecycleStatus.ReadyReadOnly
            && recoveredHost.OwnedWindowCount == 2;
        Require(
            topologyRecovered,
            "Topology unavailability did not release and recover the synthetic surfaces.");
        bool displayReplacement = topologyFactory.Surfaces.Count == 3
            && topologyFactory.Surfaces[0].IsDisposed
            && topologyFactory.Surfaces.Skip(1).All(surface => !surface.IsDisposed);
        Require(
            displayReplacement,
            "The two-display replacement did not release the superseded surface exactly once.");

        bool hostRestartRejected = VerifyHostRestartRejectsStaleIdentity();
        await topologyLifecycle.DisposeAsync().ConfigureAwait(false);
        await systemController.DisposeAsync().ConfigureAwait(false);
        bool allReleased = systemFactory.Surfaces.All(surface => surface.IsDisposed)
            && topologyFactory.Surfaces.All(surface => surface.IsDisposed);
        Require(allReleased, "A synthetic DesktopHost surface survived disposal.");

        return new(
            "Passed",
            ScenarioCount,
            explorerRecovered,
            sessionRecovered,
            topologyRecovered,
            displayReplacement,
            hostRestartRejected,
            allReleased,
            ReadsRealDesktop: false,
            CreatesNativeWindows: false,
            RealFileOperationsAllowed: false);
    }

    private static bool SystemSurfaceRoundTrip(
        ProductDesktopHostLifecycleController controller,
        ProductDesktopInteractionSystemSurfaceEventKind kind,
        long sequence,
        DateTimeOffset observedAt)
    {
        ProductDesktopHostLifecycleSnapshot suspended =
            controller.ApplySystemSurfaceEvent(new(kind, sequence, observedAt));
        ProductDesktopHostLifecycleSnapshot recovered =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate,
                sequence + 1,
                observedAt.AddSeconds(1)));
        bool passed = suspended.Status
                == ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
            && suspended.OwnedWindowCount == 1
            && !suspended.PassiveWindowContractAttested
            && suspended.LastSystemSurfaceEvent == kind
            && recovered.Status == ProductDesktopHostLifecycleStatus.ReadyReadOnly
            && recovered.OwnedWindowCount == 1
            && recovered.PassiveWindowContractAttested
            && recovered.LastSystemSurfaceEvent
                == ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate;
        Require(passed, $"The {kind} lifecycle did not fail closed and recover.");
        return true;
    }

    private static bool VerifyHostRestartRejectsStaleIdentity()
    {
        var inspector = new RecordingWindowInspector();
        var bridge = new ProductDesktopHostWindowBridge(inspector);
        var original = new ProductDesktopHostIdentity(
            Guid.Parse("67f459f4-b2ca-4ad0-99c0-7406532cc61c"),
            1,
            100,
            200);
        var replacement = new ProductDesktopHostIdentity(
            Guid.Parse("6d49d80a-b9d2-475f-bca6-f4daeedb1f4e"),
            2,
            100,
            201);
        inspector.Set(10, new(true, 100, 200, 30, new(0, 0, 320, 200)));
        bridge.Connect(original);
        ProductDesktopHostWindowClaim staleClaim = new(
            "container-1",
            original,
            1,
            10,
            30);
        Require(
            bridge.Register(staleClaim).IsRegistered,
            "The original host identity did not establish its baseline.");

        bridge.Connect(replacement);
        ProductDesktopHostWindowRegistrationResult stale = bridge.Register(staleClaim);
        inspector.Set(11, new(true, 100, 201, 31, new(0, 0, 320, 200)));
        ProductDesktopHostWindowRegistrationResult current = bridge.Register(new(
            "container-1",
            replacement,
            2,
            11,
            31));
        bool passed = stale.Status
                == ProductDesktopHostWindowRegistrationStatus.HostMismatch
            && current.IsRegistered
            && bridge.Snapshot.Status == ProductDesktopHostWindowStatus.Ready
            && bridge.Snapshot.RegisteredWindowCount == 1
            && bridge.ReadEvidence().Windows.Single().HostGeneration == 2;
        Require(passed, "A restarted host accepted stale window identity evidence.");
        return true;
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
                new string('A', 64),
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

    private static ProductDisplayTopologyReadResult TopologyResult(
        ProductDisplayTopologyReadStatus status,
        params DisplayTopologyNode[] displays) =>
        new(
            status,
            Array.AsReadOnly(displays),
            displays.Length,
            status == ProductDisplayTopologyReadStatus.Ready ? displays.Length : 0,
            BufferAttempts: 1);

    private static DisplayTopologyNode PrimaryDisplay() => new(
        "display-primary",
        new(0, 0, 1920, 1080),
        new(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        IsPrimary: true);

    private static DisplayTopologyNode SecondaryDisplay() => new(
        "display-secondary-1",
        new(-1280, 0, 1280, 1024),
        new(-1280, 0, 1280, 984),
        120,
        DisplayRotation.Landscape,
        IsPrimary: false);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SequenceTopologyReader(
        params ProductDisplayTopologyReadResult[] results)
        : IProductDisplayTopologyReader
    {
        private readonly Queue<ProductDisplayTopologyReadResult> remaining =
            new(results);

        public Task<ProductDisplayTopologyReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(remaining.Dequeue());
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

    private sealed class RecordingWindowInspector : IProductDesktopHostWindowInspector
    {
        private readonly Dictionary<nint, ProductDesktopHostWindowObservation>
            observations = [];

        internal void Set(
            nint handle,
            ProductDesktopHostWindowObservation observation) =>
            observations[handle] = observation;

        public ProductDesktopHostWindowObservation Inspect(nint handle) =>
            observations.TryGetValue(handle, out ProductDesktopHostWindowObservation? value)
                ? value
                : ProductDesktopHostWindowObservation.Missing;
    }
}
