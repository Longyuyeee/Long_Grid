using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostWindowBridgeTests
{
    private static readonly ProductDesktopHostIdentity Host = new(
        Guid.Parse("86d5841f-94b1-4dc2-8824-287cf8a02855"),
        7,
        120,
        240);

    [Fact]
    public void PublicSnapshotIsAnonymousAndStartsDisconnected()
    {
        var bridge = new ProductDesktopHostWindowBridge(new FakeInspector());

        ProductDesktopHostWindowSnapshot snapshot = bridge.Snapshot;

        Assert.Equal(ProductDesktopHostWindowStatus.Disconnected, snapshot.Status);
        Assert.Equal(0, snapshot.RegisteredWindowCount);
        Assert.DoesNotContain(
            typeof(ProductDesktopHostWindowSnapshot).GetProperties(),
            property => property.PropertyType == typeof(nint)
                || property.Name.Contains("Container", StringComparison.Ordinal)
                || property.Name.Contains("Handle", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterPublishesVerifiedOwnedRecordAndRereadsBounds()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(700, 800, marker: 30));
        var bridge = new ProductDesktopHostWindowBridge(inspector);
        bridge.Connect(Host);

        ProductDesktopHostWindowRegistrationResult result = bridge.Register(
            Claim("alpha", handle: 10, marker: 30, windowGeneration: 3));
        inspector.Set(10, Observation(900, 1000, marker: 30));
        ProductDesktopHostWindowSnapshot refreshed = bridge.Refresh();
        ProductDesktopHostWindowEvidence evidence = bridge.ReadEvidence();

        Assert.True(result.IsRegistered);
        Assert.Equal(ProductDesktopHostWindowStatus.Ready, refreshed.Status);
        Assert.Equal(1, refreshed.RegisteredWindowCount);
        Assert.Equal(1, refreshed.VerifiedWindowCount);
        Assert.True(refreshed.OwnershipAttested);
        ProductDesktopHostOwnedWindowRecord window = Assert.Single(evidence.Windows);
        Assert.Equal("alpha", window.ContainerId);
        Assert.Equal(Host.InstanceId, window.HostInstanceId);
        Assert.Equal(Host.Generation, window.HostGeneration);
        Assert.Equal(Host.ThreadId, window.HostThreadId);
        Assert.Equal(3, window.WindowGeneration);
        Assert.Equal(new PixelRect(900, 1000, 200, 100), window.LastObservedBounds);
        Assert.True(window.Verified);
    }

    [Theory]
    [InlineData(121, 240, 30)]
    [InlineData(120, 241, 30)]
    [InlineData(120, 240, 31)]
    public void RegisterRejectsForeignOwnership(
        uint processId,
        uint threadId,
        int marker)
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(
            700,
            800,
            processId,
            threadId,
            marker));
        var bridge = Connected(inspector);

        ProductDesktopHostWindowRegistrationResult result = bridge.Register(
            Claim("alpha", handle: 10, marker: 30));

        Assert.Equal(
            ProductDesktopHostWindowRegistrationStatus.OwnershipMismatch,
            result.Status);
        Assert.Equal(ProductDesktopHostWindowStatus.Degraded, result.Snapshot.Status);
        Assert.Equal(0, result.Snapshot.RegisteredWindowCount);
        Assert.Equal(1, result.Snapshot.RejectedOperationCount);
    }

    [Fact]
    public void RegisterRejectsDuplicateContainerAndHandle()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(700, 800, marker: 30));
        inspector.Set(11, Observation(900, 1000, marker: 31));
        var bridge = Connected(inspector);
        Assert.True(bridge.Register(Claim("alpha", 10, 30)).IsRegistered);

        ProductDesktopHostWindowRegistrationResult duplicateContainer =
            bridge.Register(Claim("alpha", 11, 31));
        ProductDesktopHostWindowRegistrationResult duplicateHandle =
            bridge.Register(Claim("beta", 10, 30));

        Assert.Equal(
            ProductDesktopHostWindowRegistrationStatus.DuplicateContainer,
            duplicateContainer.Status);
        Assert.Equal(
            ProductDesktopHostWindowRegistrationStatus.DuplicateHandle,
            duplicateHandle.Status);
        Assert.Single(bridge.ReadEvidence().Windows);
        Assert.Equal(2, bridge.Snapshot.RejectedOperationCount);
    }

    [Fact]
    public void RefreshDegradesDestroyedOrReusedHandleWithoutReplacingEvidence()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(700, 800, marker: 30));
        inspector.Set(11, Observation(900, 1000, marker: 31));
        var bridge = Connected(inspector);
        Assert.True(bridge.Register(Claim("alpha", 10, 30)).IsRegistered);
        Assert.True(bridge.Register(Claim("beta", 11, 31)).IsRegistered);

        inspector.Set(10, ProductDesktopHostWindowObservation.Missing);
        inspector.Set(11, Observation(1, 2, marker: 99));
        ProductDesktopHostWindowSnapshot snapshot = bridge.Refresh();

        Assert.Equal(ProductDesktopHostWindowStatus.Degraded, snapshot.Status);
        Assert.Equal(2, snapshot.RegisteredWindowCount);
        Assert.Equal(0, snapshot.VerifiedWindowCount);
        Assert.False(snapshot.OwnershipAttested);
        Assert.All(bridge.ReadEvidence().Windows, window => Assert.False(window.Verified));
    }

    [Fact]
    public void HostRestartClearsRecordsAndRejectsOldGeneration()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(700, 800, marker: 30));
        var bridge = Connected(inspector);
        Assert.True(bridge.Register(Claim("alpha", 10, 30)).IsRegistered);
        var replacement = Host with
        {
            InstanceId = Guid.Parse("17e427bd-585f-4ec1-80e4-b6b8acd427f2"),
            Generation = 8,
        };

        bridge.Connect(replacement);
        ProductDesktopHostWindowRegistrationResult stale = bridge.Register(
            Claim("alpha", 10, 30));

        Assert.Empty(bridge.ReadEvidence().Windows);
        Assert.Equal(ProductDesktopHostWindowRegistrationStatus.HostMismatch, stale.Status);
        Assert.Equal(ProductDesktopHostWindowStatus.Degraded, bridge.Snapshot.Status);
    }

    [Fact]
    public void StaleUnregisterCannotRemoveNewerWindowGeneration()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(700, 800, marker: 30));
        var bridge = Connected(inspector);
        Assert.True(bridge.Register(
            Claim("alpha", 10, 30, windowGeneration: 4)).IsRegistered);

        bool stale = bridge.Unregister("alpha", windowGeneration: 3);
        bool current = bridge.Unregister("alpha", windowGeneration: 4);

        Assert.False(stale);
        Assert.True(current);
        Assert.Equal(ProductDesktopHostWindowStatus.Empty, bridge.Snapshot.Status);
    }

    [Fact]
    public void ConcurrentRegistrationsRemainUniqueAndFinite()
    {
        var inspector = new FakeInspector();
        var bridge = Connected(inspector);
        const int count = 32;
        for (int index = 1; index <= count; index++)
        {
            inspector.Set(index, Observation(index, index, marker: 100 + index));
        }

        Parallel.For(1, count + 1, index =>
        {
            ProductDesktopHostWindowRegistrationResult result = bridge.Register(
                Claim($"container-{index:D2}", index, 100 + index));
            Assert.True(result.IsRegistered);
        });

        ProductDesktopHostWindowEvidence evidence = bridge.ReadEvidence();
        Assert.Equal(count, evidence.Windows.Count);
        Assert.Equal(count, evidence.RegisteredContainerIds.Distinct().Count());
        Assert.True(evidence.OwnershipAttested);
    }

    private static ProductDesktopHostWindowBridge Connected(FakeInspector inspector)
    {
        var bridge = new ProductDesktopHostWindowBridge(inspector);
        bridge.Connect(Host);
        return bridge;
    }

    private static ProductDesktopHostWindowClaim Claim(
        string containerId,
        int handle,
        int marker,
        long windowGeneration = 1) =>
        new(containerId, Host, windowGeneration, handle, marker);

    private static ProductDesktopHostWindowObservation Observation(
        int left,
        int top,
        uint processId = 120,
        uint threadId = 240,
        int marker = 30) =>
        new(
            true,
            processId,
            threadId,
            marker,
            new PixelRect(left, top, 200, 100));

    private sealed class FakeInspector : IProductDesktopHostWindowInspector
    {
        private readonly object sync = new();
        private readonly Dictionary<nint, ProductDesktopHostWindowObservation>
            observations = new();

        public ProductDesktopHostWindowObservation Inspect(nint handle)
        {
            lock (sync)
            {
                return observations.TryGetValue(handle, out var observation)
                    ? observation
                    : ProductDesktopHostWindowObservation.Missing;
            }
        }

        public void Set(
            nint handle,
            ProductDesktopHostWindowObservation observation)
        {
            lock (sync)
            {
                observations[handle] = observation;
            }
        }
    }
}
