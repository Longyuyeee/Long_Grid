using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCompositeProductionAdapterTests
{
    [Fact]
    public async Task ProductionAdaptersApplyAndUndoAsOneTransaction()
    {
        using Harness harness = await Harness.CreateAsync();

        ProductWorkspaceWindowCompositeResult applied =
            harness.Coordinator.Execute(harness.Request);

        Assert.True(applied.IsApplied);
        Assert.Equal(harness.Token.After, harness.Binding.Current);
        Assert.Equal("after", (await harness.Store.LoadAsync()).Document?.ProfileId);
        Assert.Equal(harness.AfterBounds, harness.Inspector.Bounds);

        ProductWorkspaceWindowCompositeUndoResult undone =
            harness.Coordinator.Undo(applied.UndoToken!, userConfirmed: true);

        Assert.True(undone.IsUndone);
        Assert.Equal(harness.Token.Undo, harness.Binding.Current);
        Assert.Equal("before", (await harness.Store.LoadAsync()).Document?.ProfileId);
        Assert.Equal(harness.BeforeBounds, harness.Inspector.Bounds);
        Assert.False(harness.Input.IsClosed);
    }

    [Fact]
    public async Task WindowBatchFailureRollsBackWithoutPublishingConfiguration()
    {
        using Harness harness = await Harness.CreateAsync();
        harness.Mutator.FailNext = true;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(harness.Request);

        Assert.Equal(ProductWorkspaceWindowCompositeStatus.RolledBack, result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowApplyFailed,
            result.Failure);
        Assert.Equal(harness.Token.Before, harness.Binding.Current);
        Assert.Equal("before", (await harness.Store.LoadAsync()).Document?.ProfileId);
        Assert.Equal(harness.BeforeBounds, harness.Inspector.Bounds);
        Assert.False(harness.Input.IsClosed);
    }

    [Fact]
    public async Task BindingPublishFailureRestoresDiskAndWindows()
    {
        using Harness harness = await Harness.CreateAsync(
            failFirstBindingExchange: true);

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(harness.Request);

        Assert.Equal(ProductWorkspaceWindowCompositeStatus.RolledBack, result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.ConfigurationApplyFailed,
            result.Failure);
        Assert.Equal(harness.Token.Before, harness.Binding.Current);
        Assert.Equal("before", (await harness.Store.LoadAsync()).Document?.ProfileId);
        Assert.Equal(harness.BeforeBounds, harness.Inspector.Bounds);
        Assert.False(harness.Input.IsClosed);
    }

    [Fact]
    public async Task ExternalConfigurationConflictIsPreservedAndHostsAreHidden()
    {
        using Harness harness = await Harness.CreateAsync();
        harness.Mutator.AfterNextSuccess = () =>
            harness.Store.SaveAsync(Document(State("external")))
                .GetAwaiter()
                .GetResult();

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(harness.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.ConfigurationRestoreFailed,
            result.Failure);
        Assert.True(result.HostsHidden);
        Assert.True(harness.Input.IsClosed);
        Assert.Equal(harness.Token.Before, harness.Binding.Current);
        Assert.Equal("external", (await harness.Store.LoadAsync()).Document?.ProfileId);
        Assert.Equal(harness.BeforeBounds, harness.Inspector.Bounds);
    }

    private static ProductWorkspaceState State(string profileId) =>
        new()
        {
            ProfileId = profileId,
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = profileId == "before"
                            ? "display-saved"
                            : "display-current",
                        XDip = profileId == "before" ? 32 : 40,
                        YDip = 48,
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = [],
                },
            ],
        };

    private static ProductConfigurationDocument Document(
        ProductWorkspaceState state)
    {
        ProductWorkspaceProjectionResult projected =
            ProductWorkspaceConfigurationProjector.Project(state);
        Assert.True(projected.IsSuccess);
        return projected.Document!;
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryDirectory directory;

        private Harness(
            TemporaryDirectory directory,
            ProductConfigurationStore store,
            ProductWorkspaceCompositeBindingState binding,
            ProductDesktopHostWindowBridge bridge,
            FakeInspector inspector,
            FakeMutator mutator,
            FakeInputGate input,
            ProductWorkspaceWindowCompositeToken token,
            ProductWorkspaceWindowCompositeRequest request,
            PixelRect beforeBounds,
            PixelRect afterBounds,
            IProductWorkspaceCompositeBindingExchange? exchange = null)
        {
            this.directory = directory;
            Store = store;
            Binding = binding;
            Bridge = bridge;
            Inspector = inspector;
            Mutator = mutator;
            Input = input;
            Token = token;
            Request = request;
            BeforeBounds = beforeBounds;
            AfterBounds = afterBounds;
            var configuration = new ProductWorkspaceCompositeConfigurationAdapter(
                store,
                exchange ?? binding);
            var windows = new ProductDesktopHostVerifiedWindowBatchAdapter(
                bridge,
                mutator,
                new InlineDispatcher(Host.ThreadId));
            Coordinator = new(
                () => Binding.Current,
                configuration,
                windows,
                input);
        }

        private static readonly ProductDesktopHostIdentity Host = new(
            Guid.Parse("ec9a9080-f56d-41c6-b625-1ad4b5440b10"),
            13,
            120,
            240);

        internal ProductConfigurationStore Store { get; }

        internal ProductWorkspaceCompositeBindingState Binding { get; }

        internal ProductDesktopHostWindowBridge Bridge { get; }

        internal FakeInspector Inspector { get; }

        internal FakeMutator Mutator { get; }

        internal FakeInputGate Input { get; }

        internal ProductWorkspaceWindowCompositeToken Token { get; }

        internal ProductWorkspaceWindowCompositeRequest Request { get; }

        internal PixelRect BeforeBounds { get; }

        internal PixelRect AfterBounds { get; }

        internal ProductWorkspaceWindowCompositeTransactionCoordinator Coordinator
        {
            get;
        }

        internal static async Task<Harness> CreateAsync(
            bool failFirstBindingExchange = false)
        {
            var directory = new TemporaryDirectory();
            var store = new ProductConfigurationStore(directory.Path);
            ProductWorkspaceState before = State("before");
            ProductWorkspaceState after = State("after");
            await store.SaveAsync(Document(before));

            PixelRect beforeBounds = new(32, 48, 360, 240);
            PixelRect afterBounds = new(40, 48, 360, 240);
            var inspector = new FakeInspector(beforeBounds);
            var bridge = new ProductDesktopHostWindowBridge(inspector);
            bridge.Connect(Host);
            Assert.True(bridge.Register(new(
                "container-1",
                Host,
                1,
                10,
                50)).IsRegistered);
            long registryGeneration = bridge.Snapshot.Generation;
            LayoutRecoveryPlan plan = new(
                LayoutRecoveryStatus.ReviewRequired,
                [new("display-saved", "display-current", DisplayMatchKind.SimilarGeometry)],
                [],
                [new(
                    "container-1",
                    "display-saved",
                    "display-current",
                    beforeBounds,
                    afterBounds,
                    WasVisibilityCorrected: false)]);
            ProductWorkspaceWindowCompositeToken token =
                ProductWorkspaceWindowCompositeTransactionCoordinator.PrepareToken(
                    before,
                    after,
                    plan,
                    ["container-1"],
                    windowOwnershipAttested: true,
                    topologyGeneration: 5,
                    beforeEditRevision: 7,
                    windowRegistryGeneration: registryGeneration,
                    desktopHostInstanceId: Host.InstanceId,
                    desktopHostGeneration: Host.Generation,
                    reviewApproved: true,
                    operationId: Guid.Parse(
                        "96b11fa1-e13c-4445-ae7f-697b827e6273"))!;
            var binding = new ProductWorkspaceCompositeBindingState(token.Before);
            IProductWorkspaceCompositeBindingExchange exchange =
                failFirstBindingExchange
                    ? new FailFirstBindingExchange(binding)
                    : binding;
            var request = new ProductWorkspaceWindowCompositeRequest(
                before,
                after,
                plan,
                ["container-1"],
                true,
                token,
                UserConfirmed: true);
            var mutator = new FakeMutator(inspector);
            return new(
                directory,
                store,
                binding,
                bridge,
                inspector,
                mutator,
                new FakeInputGate(),
                token,
                request,
                beforeBounds,
                afterBounds,
                exchange);
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            Bridge.Disconnect(Host.InstanceId);
            directory.Dispose();
        }
    }

    private sealed class FakeInspector(PixelRect initial)
        : IProductDesktopHostWindowInspector
    {
        internal PixelRect Bounds { get; set; } = initial;

        public ProductDesktopHostWindowObservation Inspect(nint handle) =>
            handle == 10
                ? new(true, 120, 240, 50, Bounds)
                : ProductDesktopHostWindowObservation.Missing;
    }

    private sealed class FakeMutator(FakeInspector inspector)
        : IProductDesktopHostWindowBatchMutator
    {
        internal bool FailNext { get; set; }

        internal Action? AfterNextSuccess { get; set; }

        public bool Apply(IReadOnlyList<ProductDesktopHostWindowMutation> windows)
        {
            ProductDesktopHostWindowMutation window = Assert.Single(windows);
            if (FailNext)
            {
                FailNext = false;
                return false;
            }

            inspector.Bounds = window.Bounds;
            Action? callback = AfterNextSuccess;
            AfterNextSuccess = null;
            callback?.Invoke();
            return true;
        }
    }

    private sealed class InlineDispatcher(uint targetThreadId)
        : IProductDesktopHostThreadDispatcher
    {
        public uint TargetThreadId { get; } = targetThreadId;

        public ProductDesktopHostDispatchResult Invoke(
            Func<bool> operation,
            TimeSpan queueTimeout) =>
            new(ProductDesktopHostDispatchStatus.Executed, operation());
    }

    private sealed class FakeInputGate : IProductWorkspaceCompositeInputGate
    {
        internal bool IsClosed { get; private set; }

        public bool Close()
        {
            IsClosed = true;
            return true;
        }

        public bool Reopen()
        {
            IsClosed = false;
            return true;
        }

        public bool HideAffectedHosts() => true;
    }

    private sealed class FailFirstBindingExchange(
        ProductWorkspaceCompositeBindingState inner)
        : IProductWorkspaceCompositeBindingExchange
    {
        private bool first = true;

        public bool Matches(ProductWorkspaceWindowCompositeBinding expected) =>
            inner.Matches(expected);

        public bool TryExchange(
            ProductWorkspaceWindowCompositeBinding expected,
            ProductWorkspaceWindowCompositeBinding replacement)
        {
            if (first)
            {
                first = false;
                return false;
            }

            return inner.TryExchange(expected, replacement);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LongGrid.CompositeProduction.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
