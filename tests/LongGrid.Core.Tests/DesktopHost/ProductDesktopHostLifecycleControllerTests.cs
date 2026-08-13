using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class ProductDesktopHostLifecycleControllerTests
{
    private static ProductDesktopHostReadOnlyProjection CreateProjection(
        string title = "工作",
        string containerId = "container-1") =>
        ProductDesktopHostReadOnlyProjection.Create(
            containerId,
            title,
            ["需求.docx", "设计稿.fig"],
            "#2457D6",
            0.82,
            isCollapsed: false,
            24,
            36,
            360,
            240);

    private static ProductDesktopHostProjectionBatch CreateBatch(
        params ProductDesktopHostDisplayProjection[]? displays) =>
        ProductDesktopHostProjectionBatch.Create(
            7,
            11,
            new string('A', 64),
            displays is { Length: > 0 } ? displays :
            [
                ProductDesktopHostDisplayProjection.Create(
                    "display-primary",
                    new(0, 0, 1920, 1040),
                    96,
                    [CreateProjection()]),
            ]);

    private static ProductDesktopHostProjectionBatch CreateVersionedBatch(
        long workspaceRevision,
        long topologyGeneration,
        string title = "方格") =>
        ProductDesktopHostProjectionBatch.Create(
            workspaceRevision,
            topologyGeneration,
            new string('B', 64),
            [
                ProductDesktopHostDisplayProjection.Create(
                    "display-primary",
                    new(0, 0, 1920, 1040),
                    96,
                    [CreateProjection(title)]),
            ]);

    private static ProductDesktopHostProjectionUpdate ReadyUpdate(
        long workspaceRevision,
        long topologyGeneration,
        string title = "方格")
    {
        ProductDesktopHostProjectionBatch batch = CreateVersionedBatch(
            workspaceRevision,
            topologyGeneration,
            title);
        return ProductDesktopHostProjectionUpdate.Create(
            workspaceRevision,
            topologyGeneration,
            ProductDesktopHostProjectionDisposition.Ready,
            batch);
    }

    [Fact]
    public void DefaultPolicyCreatesNoNativeHostOrOwnedWindows()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate(null));

        ProductDesktopHostLifecycleSnapshot snapshot = controller.Snapshot;

        Assert.Equal(
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            snapshot.Status);
        Assert.False(snapshot.FeatureEnabled);
        Assert.False(snapshot.NativeHostConnected);
        Assert.Equal(0, snapshot.OwnedWindowCount);
        Assert.Equal(0, snapshot.Generation);
    }

    [Fact]
    public void ExplicitOptInOnlyWaitsForFutureHost()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"));

        ProductDesktopHostLifecycleSnapshot snapshot = controller.Snapshot;

        Assert.Equal(ProductDesktopHostLifecycleStatus.AwaitingHost, snapshot.Status);
        Assert.True(snapshot.FeatureEnabled);
        Assert.False(snapshot.NativeHostConnected);
        Assert.Equal(0, snapshot.OwnedWindowCount);
    }

    [Fact]
    public void ProjectionCopiesAndCapsVisibleNames()
    {
        string[] names = Enumerable.Range(1, 20)
            .Select(index => $"项目 {index}")
            .ToArray();

        ProductDesktopHostReadOnlyProjection projection =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-1",
                "工作",
                names,
                "#123ABC",
                1,
                false,
                0,
                0,
                320,
                200);
        names[0] = "已篡改";

        Assert.Equal(
            ProductDesktopHostReadOnlyProjection.MaximumVisibleItems,
            projection.ItemNames.Count);
        Assert.Equal("项目 1", projection.ItemNames[0]);
    }

    [Theory]
    [InlineData("invalid", 0.5, 320, 200)]
    [InlineData("#123ABC", -0.1, 320, 200)]
    [InlineData("#123ABC", 1.1, 320, 200)]
    [InlineData("#123ABC", 0.5, 0, 200)]
    [InlineData("#123ABC", 0.5, 320, 0)]
    public void ProjectionRejectsUnsafeValues(
        string color,
        double opacity,
        double width,
        double height)
    {
        Assert.Throws<ArgumentException>(() =>
            ProductDesktopHostReadOnlyProjection.Create(
                "container-1",
                "工作",
                ["项目"],
                color,
                opacity,
                false,
                0,
                0,
                width,
                height));
    }

    [Fact]
    public void DisabledPolicyIgnoresProjectionWithoutCreatingSurface()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate(null),
            factory,
            new FactoryBackedInspector(factory));

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            snapshot.Status);
        Assert.Empty(factory.Surfaces);
    }

    [Fact]
    public async Task VerifiedProjectionOwnsOneReadOnlySurfaceUntilRemoved()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));

        ProductDesktopHostLifecycleSnapshot ready =
            controller.ApplyProjectionBatch(CreateBatch());
        ProductDesktopHostLifecycleSnapshot unchanged =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, ready.Status);
        Assert.True(ready.NativeHostConnected);
        Assert.Equal(1, ready.OwnedWindowCount);
        Assert.Equal(ready, unchanged);
        RecordingSurface onlySurface = Assert.Single(factory.Surfaces);
        Assert.False(onlySurface.IsDisposed);

        ProductDesktopHostLifecycleSnapshot waiting =
            controller.ApplyProjectionBatch(null);

        Assert.Equal(ProductDesktopHostLifecycleStatus.AwaitingHost, waiting.Status);
        Assert.False(waiting.NativeHostConnected);
        Assert.Equal(0, waiting.OwnedWindowCount);
        Assert.True(onlySurface.IsDisposed);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task ChangedProjectionReplacesPreviouslyVerifiedSurface()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));
        _ = controller.ApplyProjectionBatch(CreateBatch());
        RecordingSurface first = Assert.Single(factory.Surfaces);

        ProductDesktopHostLifecycleSnapshot replacement =
            controller.ApplyProjectionBatch(CreateBatch(
                ProductDesktopHostDisplayProjection.Create(
                    "display-primary",
                    new(0, 0, 1920, 1040),
                    96,
                    [CreateProjection("项目")])));

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, replacement.Status);
        Assert.True(first.IsDisposed);
        Assert.Equal(2, factory.Surfaces.Count);
        Assert.False(factory.Surfaces[1].IsDisposed);
        await controller.DisposeAsync();
        Assert.True(factory.Surfaces[1].IsDisposed);
    }

    [Fact]
    public async Task OwnershipMismatchDestroysSurfaceAndPublishesFault()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory, returnWrongMarker: true));

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(ProductDesktopHostLifecycleStatus.Faulted, snapshot.Status);
        Assert.False(snapshot.NativeHostConnected);
        Assert.Equal(0, snapshot.OwnedWindowCount);
        Assert.True(Assert.Single(factory.Surfaces).IsDisposed);
        await controller.DisposeAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MissingSurfaceAttestationFailsClosed(
        bool accessibilityAttested,
        bool passiveWindowAttested)
    {
        var factory = new RecordingSurfaceFactory
        {
            AccessibilityAttested = accessibilityAttested,
            PassiveWindowAttested = passiveWindowAttested,
        };
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(ProductDesktopHostLifecycleStatus.Faulted, snapshot.Status);
        Assert.False(snapshot.ReadOnlyAccessibilityAvailable);
        Assert.False(snapshot.PassiveWindowContractAttested);
        Assert.True(Assert.Single(factory.Surfaces).IsDisposed);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task MultiDisplayBatchOwnsOneSurfacePerDisplay()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));
        ProductDesktopHostDisplayProjection primary =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1920, 1040),
                96,
                [CreateProjection(), CreateProjection("第二方格", "container-3")]);
        ProductDesktopHostDisplayProjection secondary =
            ProductDesktopHostDisplayProjection.Create(
                "display-secondary",
                new(-1280, 0, 1280, 984),
                120,
                [ProductDesktopHostReadOnlyProjection.Create(
                    "container-2",
                    "副屏方格",
                    ["项目"],
                    "#123ABC",
                    0.7,
                    false,
                    20,
                    20,
                    300,
                    220)]);

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch(primary, secondary));

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, snapshot.Status);
        Assert.Equal(2, snapshot.OwnedWindowCount);
        Assert.Equal(3, snapshot.RenderedContainerCount);
        Assert.True(snapshot.ReadOnlyAccessibilityAvailable);
        Assert.True(snapshot.PassiveWindowContractAttested);
        Assert.Equal(2, factory.Surfaces.Count);
        await controller.DisposeAsync();
        Assert.All(factory.Surfaces, surface => Assert.True(surface.IsDisposed));
    }

    [Fact]
    public async Task SecondDisplayOwnershipFailureClosesEntireBatch()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory, mismatchSurfaceOrdinal: 2));
        ProductDesktopHostDisplayProjection first =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1920, 1040),
                96,
                [CreateProjection()]);
        ProductDesktopHostDisplayProjection second =
            ProductDesktopHostDisplayProjection.Create(
                "display-secondary",
                new(-1280, 0, 1280, 984),
                120,
                [CreateProjection("副屏", "container-2")]);

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch(first, second));

        Assert.Equal(ProductDesktopHostLifecycleStatus.Faulted, snapshot.Status);
        Assert.Equal(0, snapshot.OwnedWindowCount);
        Assert.Equal(2, factory.Surfaces.Count);
        Assert.All(factory.Surfaces, surface => Assert.True(surface.IsDisposed));
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task WindowsFactoryCreatesAndAttestsRealReadOnlyWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"));

        ProductDesktopHostLifecycleSnapshot snapshot =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, snapshot.Status);
        Assert.True(snapshot.NativeHostConnected);
        Assert.Equal(1, snapshot.OwnedWindowCount);
        Assert.Equal(7, snapshot.WorkspaceRevision);
        Assert.Equal(11, snapshot.TopologyGeneration);
        Assert.Equal(1, snapshot.RenderedContainerCount);
        Assert.True(snapshot.ReadOnlyAccessibilityAvailable);
        Assert.True(snapshot.PassiveWindowContractAttested);
        await controller.DisposeAsync();
        Assert.Equal(ProductDesktopHostLifecycleStatus.Completed, controller.Snapshot.Status);
    }

    [Fact]
    public async Task DoubleOptInCreatesHiddenThenPublishesAttestedPassiveSurface()
    {
        var factory = new RecordingSurfaceFactory();
        var interaction = new ProductDesktopInteractionDevelopmentController(
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("1"),
                "1"));
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory),
            interaction);

        ProductDesktopHostLifecycleSnapshot ready =
            controller.ApplyProjectionBatch(CreateBatch());
        RecordingSurface surface = Assert.Single(factory.Surfaces);

        Assert.True(surface.WasCreatedHidden);
        Assert.True(surface.ApplyPassiveCalls > 0);
        Assert.True(interaction.Snapshot.IsDevelopmentInteractionAvailable);
        Assert.True(interaction.Snapshot.Surface!.IsPassiveContract);

        await controller.DisposeAsync();

        Assert.True(surface.ApplyHiddenCalls > 0);
        Assert.True(surface.IsDisposed);
        Assert.False(interaction.Snapshot.NativeSurfaceAdapterConnected);
    }

    [Fact]
    public async Task WindowsDoubleOptInAttestsHiddenRegionBeforePassivePublish()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostFeatureDecision host =
            ProductDesktopHostFeaturePolicy.Evaluate("1");
        var interaction = new ProductDesktopInteractionDevelopmentController(
            ProductDesktopInteractionFeaturePolicy.Evaluate(host, "1"));
        var controller = new ProductDesktopHostLifecycleController(
            host,
            interaction);

        ProductDesktopHostLifecycleSnapshot ready =
            controller.ApplyProjectionBatch(CreateBatch());

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, ready.Status);
        Assert.True(interaction.Snapshot.Surface!.IsPassiveContract);

        ProductDesktopInteractionDevelopmentSnapshot suspended =
            interaction.SuspendFailClosed(
                ProductDesktopInteractionCancellationSignal.FocusLost,
                DateTimeOffset.UtcNow);
        Assert.True(suspended.Surface!.IsHiddenContract);

        await controller.DisposeAsync();
        Assert.False(interaction.Snapshot.NativeSurfaceAdapterConnected);
    }

    [Fact]
    public async Task SystemSurfaceEventHidesThenStableRecoveryRestoresPassive()
    {
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
        _ = controller.ApplyProjectionBatch(CreateBatch());
        RecordingSurface surface = Assert.Single(factory.Surfaces);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ProductDesktopHostLifecycleSnapshot suspended =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind
                    .DesktopRevealRequested,
                1,
                now));

        Assert.Equal(
            ProductDesktopHostLifecycleStatus.SuspendedSystemSurface,
            suspended.Status);
        Assert.True(suspended.NativeHostConnected);
        Assert.False(suspended.PassiveWindowContractAttested);
        Assert.True(surface.HiddenWindowContractAttested);
        Assert.Equal(
            ProductDesktopInteractionSystemSurfaceEventKind
                .DesktopRevealRequested,
            suspended.LastSystemSurfaceEvent);

        ProductDesktopHostLifecycleSnapshot resumed =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RecoveryCandidate,
                2,
                now.AddSeconds(2)));

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, resumed.Status);
        Assert.Equal(
            ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate,
            resumed.LastSystemSurfaceEvent);
        Assert.True(resumed.PassiveWindowContractAttested);
        Assert.True(surface.PassiveWindowContractAttested);
        Assert.True(interaction.Snapshot.IsDevelopmentInteractionAvailable);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task StaleAndInvalidSystemSurfaceEventsCannotChangeLifecycle()
    {
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
        ProductDesktopHostLifecycleSnapshot ready =
            controller.ApplyProjectionBatch(CreateBatch());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = controller.ApplySystemSurfaceEvent(new(
            ProductDesktopInteractionSystemSurfaceEventKind.FocusLost,
            2,
            now));
        ProductDesktopHostLifecycleSnapshot suspended = controller.Snapshot;

        ProductDesktopHostLifecycleSnapshot stale =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RecoveryCandidate,
                1,
                now.AddSeconds(1)));
        ProductDesktopHostLifecycleSnapshot invalid =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RecoveryCandidate,
                0,
                default));

        Assert.NotEqual(ready, suspended);
        Assert.Equal(suspended, stale);
        Assert.Equal(suspended, invalid);
        Assert.True(Assert.Single(factory.Surfaces).HiddenWindowContractAttested);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task SystemSurfaceEventBeforeSurfaceDoesNotManufactureFault()
    {
        ProductDesktopHostFeatureDecision host =
            ProductDesktopHostFeaturePolicy.Evaluate("1");
        var interaction = new ProductDesktopInteractionDevelopmentController(
            ProductDesktopInteractionFeaturePolicy.Evaluate(host, "1"));
        var controller = new ProductDesktopHostLifecycleController(
            host,
            interaction);
        ProductDesktopHostLifecycleSnapshot awaiting = controller.Snapshot;

        ProductDesktopHostLifecycleSnapshot unchanged =
            controller.ApplySystemSurfaceEvent(new(
                ProductDesktopInteractionSystemSurfaceEventKind.FocusLost,
                1,
                DateTimeOffset.UtcNow));

        Assert.Equal(awaiting, unchanged);
        Assert.Equal(
            ProductDesktopHostLifecycleStatus.AwaitingHost,
            unchanged.Status);
        await controller.DisposeAsync();
    }

    [Fact]
    public void ProductPassiveAdapterRejectsExplicitAndStaleGeneration()
    {
        var surface = new RecordingSurface(101, 201, 301, 401, startHidden: true);
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 9);

        Assert.False(adapter.ApplyPassive(8));
        Assert.True(adapter.Hide(9));
        Assert.False(adapter.ApplyExplicit(new(
            Guid.NewGuid(),
            "container-1",
            1,
            2,
            9,
            DateTimeOffset.UtcNow.AddSeconds(1))));
        Assert.True(adapter.Capture().Evidence!.IsHiddenContract);
    }

    [Fact]
    public async Task PassiveAttestationFailureHidesAndFaultsLifecycle()
    {
        var factory = new RecordingSurfaceFactory
        {
            PassiveWindowAttested = false,
        };
        ProductDesktopHostFeatureDecision host =
            ProductDesktopHostFeaturePolicy.Evaluate("1");
        var interaction = new ProductDesktopInteractionDevelopmentController(
            ProductDesktopInteractionFeaturePolicy.Evaluate(host, "1"));
        var controller = new ProductDesktopHostLifecycleController(
            host,
            factory,
            new FactoryBackedInspector(factory),
            interaction);

        ProductDesktopHostLifecycleSnapshot faulted =
            controller.ApplyProjectionBatch(CreateBatch());
        RecordingSurface surface = Assert.Single(factory.Surfaces);

        Assert.Equal(ProductDesktopHostLifecycleStatus.Faulted, faulted.Status);
        Assert.True(surface.ApplyHiddenCalls > 0);
        Assert.True(surface.IsDisposed);
        Assert.False(interaction.Snapshot.NativeSurfaceAdapterConnected);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task DisposalIsIdempotentAndPublishesAnonymousCompletion()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"));
        var published = new List<ProductDesktopHostLifecycleSnapshot>();
        controller.SnapshotChanged += (_, snapshot) => published.Add(snapshot);

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        ProductDesktopHostLifecycleSnapshot completed = Assert.Single(published);
        Assert.Equal(ProductDesktopHostLifecycleStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Generation);
        Assert.False(completed.FeatureEnabled);
        Assert.False(completed.NativeHostConnected);
        Assert.Equal(0, completed.OwnedWindowCount);
        Assert.Equal(completed, controller.Snapshot);
    }

    [Fact]
    public async Task RefreshingHidesSurfacesAndReadyRecoversSameTopologyGeneration()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));
        _ = controller.ApplyProjectionUpdate(ReadyUpdate(7, 11));
        RecordingSurface first = Assert.Single(factory.Surfaces);

        ProductDesktopHostLifecycleSnapshot suspended =
            controller.ApplyProjectionUpdate(
                ProductDesktopHostProjectionUpdate.Create(
                    7,
                    12,
                    ProductDesktopHostProjectionDisposition.TopologyRefreshing));

        Assert.Equal(
            ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology,
            suspended.Status);
        Assert.True(first.IsDisposed);
        Assert.Equal(0, suspended.OwnedWindowCount);

        ProductDesktopHostLifecycleSnapshot recovered =
            controller.ApplyProjectionUpdate(ReadyUpdate(7, 12, "恢复"));

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, recovered.Status);
        Assert.False(factory.Surfaces[^1].IsDisposed);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task StaleUpdateCannotReplaceLatestSurface()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));
        ProductDesktopHostLifecycleSnapshot latest =
            controller.ApplyProjectionUpdate(ReadyUpdate(9, 15));
        RecordingSurface surface = Assert.Single(factory.Surfaces);

        ProductDesktopHostLifecycleSnapshot ignored =
            controller.ApplyProjectionUpdate(ReadyUpdate(10, 14, "迟到"));

        Assert.Equal(latest, ignored);
        Assert.False(surface.IsDisposed);
        Assert.Single(factory.Surfaces);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task ConflictingTerminalUpdateFailsClosed()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));
        _ = controller.ApplyProjectionUpdate(ReadyUpdate(7, 11, "第一版"));

        ProductDesktopHostLifecycleSnapshot conflict =
            controller.ApplyProjectionUpdate(ReadyUpdate(7, 11, "冲突版"));

        Assert.Equal(ProductDesktopHostLifecycleStatus.Faulted, conflict.Status);
        Assert.Equal(0, conflict.OwnedWindowCount);
        Assert.True(Assert.Single(factory.Surfaces).IsDisposed);

        ProductDesktopHostLifecycleSnapshot recovered =
            controller.ApplyProjectionUpdate(ReadyUpdate(8, 12, "恢复版"));

        Assert.Equal(ProductDesktopHostLifecycleStatus.ReadyReadOnly, recovered.Status);
        Assert.Equal(2, factory.Surfaces.Count);
        Assert.False(factory.Surfaces[^1].IsDisposed);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task EmptyWorkspaceOwnsNoSurfaceAndDisposedControllerRejectsUpdates()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));

        ProductDesktopHostLifecycleSnapshot empty =
            controller.ApplyProjectionUpdate(
                ProductDesktopHostProjectionUpdate.Create(
                    3,
                    4,
                    ProductDesktopHostProjectionDisposition.EmptyWorkspace));

        Assert.Equal(ProductDesktopHostLifecycleStatus.AwaitingWorkspace, empty.Status);
        Assert.Empty(factory.Surfaces);
        await controller.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() =>
            controller.ApplyProjectionUpdate(ReadyUpdate(4, 4)));
    }

    [Fact]
    public async Task RapidRevisionsReleaseEverySupersededSurface()
    {
        var factory = new RecordingSurfaceFactory();
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            factory,
            new FactoryBackedInspector(factory));

        for (int revision = 1; revision <= 100; revision++)
        {
            _ = controller.ApplyProjectionUpdate(
                ReadyUpdate(revision, 11, $"方格 {revision}"));
        }

        Assert.Equal(100, controller.Snapshot.WorkspaceRevision);
        Assert.Equal(100, factory.Surfaces.Count);
        Assert.All(factory.Surfaces[..^1], surface => Assert.True(surface.IsDisposed));
        Assert.False(factory.Surfaces[^1].IsDisposed);
        await controller.DisposeAsync();
        Assert.True(factory.Surfaces[^1].IsDisposed);
    }

    private sealed class RecordingSurfaceFactory
        : IProductDesktopHostReadOnlySurfaceFactory
    {
        private nint nextHandle = 100;

        internal List<RecordingSurface> Surfaces { get; } = [];

        internal bool AccessibilityAttested { get; init; } = true;

        internal bool PassiveWindowAttested { get; init; } = true;

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
                startHidden)
            {
                ReadOnlyAccessibilityAttested = AccessibilityAttested,
                PassiveWindowContractAttested = PassiveWindowAttested,
            };
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
        private bool visible = !startHidden;

        internal bool WasCreatedHidden { get; } = startHidden;

        internal int ApplyPassiveCalls { get; private set; }

        internal int ApplyHiddenCalls { get; private set; }

        public nint Handle { get; } = handle;

        public nint InstanceMarker { get; } = instanceMarker;

        public uint ProcessId { get; } = processId;

        public uint ThreadId { get; } = threadId;

        public bool ReadOnlyAccessibilityAttested { get; init; } = true;

        public bool PassiveWindowContractAttested { get; init; } = true;

        bool IProductDesktopHostReadOnlySurface.PassiveWindowContractAttested =>
            visible && PassiveWindowContractAttested;

        public bool HiddenWindowContractAttested => !visible;

        internal bool IsDisposed { get; private set; }

        public bool ApplyPassive()
        {
            ApplyPassiveCalls++;
            visible = true;
            return PassiveWindowContractAttested;
        }

        public bool ApplyHidden()
        {
            ApplyHiddenCalls++;
            visible = false;
            return true;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FactoryBackedInspector(
        RecordingSurfaceFactory factory,
        bool returnWrongMarker = false,
        int mismatchSurfaceOrdinal = 0) : IProductDesktopHostWindowInspector
    {
        public ProductDesktopHostWindowObservation Inspect(nint handle)
        {
            RecordingSurface? surface = factory.Surfaces
                .LastOrDefault(candidate => candidate.Handle == handle);
            if (surface is null || surface.IsDisposed)
            {
                return ProductDesktopHostWindowObservation.Missing;
            }

            int ordinal = factory.Surfaces.IndexOf(surface) + 1;
            bool mismatch = returnWrongMarker
                || ordinal == mismatchSurfaceOrdinal;
            return new(
                true,
                surface.ProcessId,
                surface.ThreadId,
                mismatch ? surface.InstanceMarker + 1 : surface.InstanceMarker,
                new(24, 36, 360, 240));
        }
    }
}
