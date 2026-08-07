using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCompositeDesktopHostInputGateTests
{
    private static readonly ProductDesktopHostIdentity Host = new(
        Guid.Parse("bdfe05e4-889e-46ba-8918-631915fe13f2"),
        4,
        120,
        240);

    [Fact]
    public async Task CloseReopenAndEmergencyHideUseExactVerifiedHandles()
    {
        await using TestContext context = await TestContext.CreateAsync();

        Assert.True(context.Gate.Close());
        Assert.True(context.Gate.InputClosed);
        Assert.True(context.Gate.Reopen());
        Assert.False(context.Gate.InputClosed);
        Assert.True(context.Gate.Close());
        Assert.True(context.Gate.HideAffectedHosts());

        Assert.True(context.Gate.HostsHidden);
        Assert.Equal(4, context.Controller.Calls.Count);
        Assert.All(context.Controller.Calls, call =>
            Assert.Equal([(nint)10, (nint)11], call.Windows));
        Assert.Equal(
            ["disable", "enable", "disable"],
            context.Controller.Calls
                .Where(call => call.Kind != "hide")
                .Select(call => call.Kind));
        Assert.Equal("hide", context.Controller.Calls[^1].Kind);
    }

    [Fact]
    public async Task LifecycleDriftPreventsReopenWithoutNativeCall()
    {
        await using TestContext context = await TestContext.CreateAsync();
        Assert.True(context.Gate.Close());

        await context.Topology.RefreshAsync();

        Assert.False(context.Gate.Reopen());
        Assert.True(context.Gate.InputClosed);
        Assert.Single(context.Controller.Calls);
    }

    [Fact]
    public async Task DispatchDelayRereadsOwnershipBeforeNativeInputCall()
    {
        await using TestContext context = await TestContext.CreateAsync(
            beforeInvoke: bridge => bridge.Refresh());

        Assert.False(context.Gate.Close());
        Assert.Empty(context.Controller.Calls);
    }

    [Fact]
    public async Task ShutdownInvalidatesLifecycleAndHidesOpenHosts()
    {
        await using TestContext context = await TestContext.CreateAsync();

        ProductWorkspaceCompositeInputShutdownResult result =
            context.Gate.ShutdownAndHide(TimeSpan.FromMilliseconds(100));

        Assert.True(result.IsComplete);
        Assert.True(result.InputOperationDrained);
        Assert.True(result.HostsHidden);
        Assert.Equal(
            ProductWorkspaceCompositeLifecycleStatus.ShuttingDown,
            context.Guard.Status);
        Assert.False(context.Gate.Close());
        Assert.False(context.Gate.Reopen());
        Assert.Equal("hide", Assert.Single(context.Controller.Calls).Kind);
        Assert.Equal(
            ProductWorkspaceCompositeInputShutdownStatus.AlreadyHidden,
            context.Gate.ShutdownAndHide(
                TimeSpan.FromMilliseconds(100)).Status);
    }

    [Fact]
    public async Task ShutdownDrainTimesOutThenCanBeRetriedSafely()
    {
        await using TestContext context = await TestContext.CreateAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        context.Controller.BeforeSetEnabled = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };
        Task<bool> close = Task.Run(context.Gate.Close);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        ProductWorkspaceCompositeInputShutdownResult timedOut =
            context.Gate.ShutdownAndHide(TimeSpan.FromMilliseconds(25));

        Assert.Equal(
            ProductWorkspaceCompositeInputShutdownStatus.DrainTimedOut,
            timedOut.Status);
        Assert.False(timedOut.InputOperationDrained);
        Assert.False(context.Gate.Reopen());
        release.Set();
        Assert.True(await close);

        ProductWorkspaceCompositeInputShutdownResult retried =
            context.Gate.ShutdownAndHide(TimeSpan.FromSeconds(1));
        Assert.True(retried.IsComplete);
        Assert.True(retried.HostsHidden);
    }

    [Fact]
    public async Task ShutdownReportsFiniteHideFailureAndAllowsRetry()
    {
        await using TestContext context = await TestContext.CreateAsync();
        context.Controller.HideSucceeds = false;

        ProductWorkspaceCompositeInputShutdownResult failed =
            context.Gate.ShutdownAndHide(TimeSpan.FromMilliseconds(100));

        Assert.Equal(
            ProductWorkspaceCompositeInputShutdownStatus.HideFailed,
            failed.Status);
        Assert.Throws<InvalidOperationException>(context.Gate.Dispose);
        context.Controller.HideSucceeds = true;
        Assert.True(context.Gate.ShutdownAndHide(
            TimeSpan.FromMilliseconds(100)).IsComplete);
    }

    [Fact]
    public async Task ConstructorRejectsStaleRegistryAndWrongThread()
    {
        await using TestContext context = await TestContext.CreateAsync();

        Assert.Throws<ArgumentException>(() =>
            new ProductWorkspaceCompositeDesktopHostInputGate(
                context.Bridge,
                context.Controller,
                new InlineDispatcher(999),
                context.Guard,
                ["alpha", "beta"],
                context.Binding.WindowRegistryGeneration));
        Assert.Throws<ArgumentException>(() =>
            new ProductWorkspaceCompositeDesktopHostInputGate(
                context.Bridge,
                context.Controller,
                new InlineDispatcher(Host.ThreadId),
                context.Guard,
                ["alpha", "beta"],
                context.Binding.WindowRegistryGeneration - 1));
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(
            ProductDisplayTopologyController topology,
            ProductDesktopHostWindowBridge bridge,
            ProductWorkspaceWindowCompositeBinding binding,
            ProductWorkspaceCompositeLifecycleGuard guard,
            FakeController controller,
            ProductWorkspaceCompositeDesktopHostInputGate gate)
        {
            Topology = topology;
            Bridge = bridge;
            Binding = binding;
            Guard = guard;
            Controller = controller;
            Gate = gate;
        }

        internal ProductDisplayTopologyController Topology { get; }

        internal ProductDesktopHostWindowBridge Bridge { get; }

        internal ProductWorkspaceWindowCompositeBinding Binding { get; }

        internal ProductWorkspaceCompositeLifecycleGuard Guard { get; }

        internal FakeController Controller { get; }

        internal ProductWorkspaceCompositeDesktopHostInputGate Gate { get; }

        internal static async Task<TestContext> CreateAsync(
            Action<ProductDesktopHostWindowBridge>? beforeInvoke = null)
        {
            var topology = new ProductDisplayTopologyController(new FixedReader());
            ProductDisplayTopologyRefreshResult refreshed =
                await topology.RefreshAsync();
            var inspector = new FakeInspector();
            var bridge = new ProductDesktopHostWindowBridge(inspector);
            bridge.Connect(Host);
            Assert.True(bridge.Register(Claim("alpha", 10, 50)).IsRegistered);
            Assert.True(bridge.Register(Claim("beta", 11, 51)).IsRegistered);
            var binding = new ProductWorkspaceWindowCompositeBinding(
                refreshed.Generation,
                EditRevision: 1,
                bridge.Snapshot.Generation,
                Host.InstanceId,
                Host.Generation,
                new string('A', 64));
            var guard = new ProductWorkspaceCompositeLifecycleGuard(
                binding,
                topology,
                bridge);
            var controller = new FakeController();
            var dispatcher = new InlineDispatcher(Host.ThreadId)
            {
                BeforeInvoke = beforeInvoke is null
                    ? null
                    : () => beforeInvoke(bridge),
            };
            var gate = new ProductWorkspaceCompositeDesktopHostInputGate(
                bridge,
                controller,
                dispatcher,
                guard,
                ["beta", "alpha"],
                binding.WindowRegistryGeneration);
            return new(topology, bridge, binding, guard, controller, gate);
        }

        public async ValueTask DisposeAsync()
        {
            if (Gate.InputClosed && !Gate.HostsHidden)
            {
                Gate.ShutdownAndHide(TimeSpan.FromSeconds(1));
            }

            Gate.Dispose();
            Guard.Dispose();
            Bridge.Disconnect(Host.InstanceId);
            await Topology.DisposeAsync();
        }
    }

    private static ProductDesktopHostWindowClaim Claim(
        string containerId,
        int handle,
        int marker) =>
        new(containerId, Host, 1, handle, marker);

    private sealed class FakeController : IProductDesktopHostInputController
    {
        internal List<(string Kind, nint[] Windows)> Calls { get; } = [];

        internal Action? BeforeSetEnabled { get; set; }

        internal bool HideSucceeds { get; set; } = true;

        public bool SetEnabled(IReadOnlyList<nint> windows, bool enabled)
        {
            Calls.Add((enabled ? "enable" : "disable", windows.ToArray()));
            BeforeSetEnabled?.Invoke();
            return true;
        }

        public bool Hide(IReadOnlyList<nint> windows)
        {
            Calls.Add(("hide", windows.ToArray()));
            return HideSucceeds;
        }
    }

    private sealed class InlineDispatcher(uint targetThreadId)
        : IProductDesktopHostThreadDispatcher
    {
        public uint TargetThreadId { get; } = targetThreadId;

        internal Action? BeforeInvoke { get; init; }

        public ProductDesktopHostDispatchResult Invoke(
            Func<bool> operation,
            TimeSpan queueTimeout)
        {
            BeforeInvoke?.Invoke();
            return new(
                ProductDesktopHostDispatchStatus.Executed,
                operation());
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
            handle switch
            {
                10 => new(true, 120, 240, 50, new(10, 20, 300, 200)),
                11 => new(true, 120, 240, 51, new(40, 50, 320, 240)),
                _ => ProductDesktopHostWindowObservation.Missing,
            };
    }
}
