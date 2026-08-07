using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCompositeLifecycleGuardTests
{
    private static readonly ProductDesktopHostIdentity Host = new(
        Guid.Parse("2dd3b2d1-9a19-4388-a672-cf3f576c10d9"),
        7,
        120,
        240);

    [Fact]
    public async Task ExactLifecycleAllowsBindingExchange()
    {
        await using TestContext context = await TestContext.CreateAsync();
        ProductWorkspaceWindowCompositeBinding replacement =
            context.Binding with
            {
                EditRevision = context.Binding.EditRevision + 1,
                ConfigurationFingerprint = new('B', 64),
            };

        Assert.True(context.Guard.Matches(context.Binding));
        Assert.True(context.Guard.TryExchange(context.Binding, replacement));
        Assert.Equal(replacement, context.Guard.Current);
        Assert.Equal(ProductWorkspaceCompositeLifecycleStatus.Ready, context.Guard.Status);
    }

    [Fact]
    public async Task TopologyRefreshInvalidatesOldBindingImmediately()
    {
        await using TestContext context = await TestContext.CreateAsync();

        await context.Topology.RefreshAsync();

        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.TopologyChanged,
            context.Guard.Status);
        Assert.False(context.Guard.Matches(context.Binding));
        Assert.Throws<InvalidOperationException>(() => context.Guard.Current);
    }

    [Fact]
    public async Task WindowRegistryChangeInvalidatesOldBindingImmediately()
    {
        await using TestContext context = await TestContext.CreateAsync();

        context.Bridge.Refresh();

        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.DesktopHostChanged,
            context.Guard.Status);
        Assert.False(context.Guard.Matches(context.Binding));
    }

    [Fact]
    public async Task ShutdownAndDisposeAreTerminal()
    {
        await using TestContext context = await TestContext.CreateAsync();

        context.Guard.BeginShutdown();
        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.ShuttingDown,
            context.Guard.Status);
        Assert.False(context.Guard.Matches(context.Binding));

        context.Guard.Dispose();
        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.Disposed,
            context.Guard.Status);
        context.Guard.BeginShutdown();
        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.Disposed,
            context.Guard.Status);
    }

    [Fact]
    public async Task ExchangeCannotRebindLifecycleIdentity()
    {
        await using TestContext context = await TestContext.CreateAsync();

        Assert.False(context.Guard.TryExchange(
            context.Binding,
            context.Binding with
            {
                TopologyGeneration = context.Binding.TopologyGeneration + 1,
            }));
        Assert.False(context.Guard.TryExchange(
            context.Binding,
            context.Binding with
            {
                WindowRegistryGeneration =
                    context.Binding.WindowRegistryGeneration + 1,
            }));
        Assert.Equal(context.Binding, context.Guard.Current);
    }

    [Fact]
    public async Task ConstructorRejectsNonAuthoritativeOrMismatchedEvidence()
    {
        var reader = new FixedReader();
        await using var topology = new ProductDisplayTopologyController(reader);
        var inspector = new FakeInspector();
        var bridge = new ProductDesktopHostWindowBridge(inspector);
        bridge.Connect(Host);
        Assert.True(bridge.Register(Claim()).IsRegistered);
        ProductWorkspaceWindowCompositeBinding binding = Binding(
            topologyGeneration: 1,
            bridge.Snapshot.Generation);

        Assert.Throws<ArgumentException>(() =>
            new ProductWorkspaceCompositeLifecycleGuard(
                binding,
                topology,
                bridge));

        await topology.RefreshAsync();
        Assert.Throws<ArgumentException>(() =>
            new ProductWorkspaceCompositeLifecycleGuard(
                binding with { WindowRegistryGeneration = 999 },
                topology,
                bridge));
        bridge.Disconnect(Host.InstanceId);
    }

    private static ProductDesktopHostWindowClaim Claim() =>
        new("container-1", Host, 1, 10, 50);

    private static ProductWorkspaceWindowCompositeBinding Binding(
        long topologyGeneration,
        long registryGeneration) =>
        new(
            topologyGeneration,
            EditRevision: 1,
            registryGeneration,
            Host.InstanceId,
            Host.Generation,
            new string('A', 64));

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(
            ProductDisplayTopologyController topology,
            ProductDesktopHostWindowBridge bridge,
            ProductWorkspaceWindowCompositeBinding binding,
            ProductWorkspaceCompositeLifecycleGuard guard)
        {
            Topology = topology;
            Bridge = bridge;
            Binding = binding;
            Guard = guard;
        }

        internal ProductDisplayTopologyController Topology { get; }

        internal ProductDesktopHostWindowBridge Bridge { get; }

        internal ProductWorkspaceWindowCompositeBinding Binding { get; }

        internal ProductWorkspaceCompositeLifecycleGuard Guard { get; }

        internal static async Task<TestContext> CreateAsync()
        {
            var topology = new ProductDisplayTopologyController(new FixedReader());
            ProductDisplayTopologyRefreshResult refreshed =
                await topology.RefreshAsync();
            Assert.True(refreshed.Snapshot.IsAuthoritative);
            var bridge = new ProductDesktopHostWindowBridge(new FakeInspector());
            bridge.Connect(Host);
            Assert.True(bridge.Register(Claim()).IsRegistered);
            ProductWorkspaceWindowCompositeBinding binding = Binding(
                refreshed.Generation,
                bridge.Snapshot.Generation);
            var guard = new ProductWorkspaceCompositeLifecycleGuard(
                binding,
                topology,
                bridge);
            return new(topology, bridge, binding, guard);
        }

        public async ValueTask DisposeAsync()
        {
            Guard.Dispose();
            Bridge.Disconnect(Host.InstanceId);
            await Topology.DisposeAsync();
        }
    }

    private sealed class FixedReader : IProductDisplayTopologyReader
    {
        public Task<ProductDisplayTopologyReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductDisplayTopologyReadResult(
                ProductDisplayTopologyReadStatus.Ready,
                [new(
                    "display-current",
                    new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040),
                    96,
                    DisplayRotation.Landscape,
                    IsPrimary: true)],
                ActivePathCount: 1,
                StableIdentityCount: 1,
                BufferAttempts: 1));
    }

    private sealed class FakeInspector : IProductDesktopHostWindowInspector
    {
        public ProductDesktopHostWindowObservation Inspect(nint handle) =>
            handle == 10
                ? new(true, 120, 240, 50, new(32, 48, 360, 240))
                : ProductDesktopHostWindowObservation.Missing;
    }
}
