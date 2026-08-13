using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionIntentConsumptionControllerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConsumesPreparedIntentOnceAndAppliesSelection()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        var controller = new ProductDesktopInteractionIntentConsumptionController(
            Interaction(),
            Forwarding(),
            bridge);
        var surface = new FakeSurface();
        Assert.True(controller.AttachSurface(surface));
        ProductDesktopInteractionPreparedIntent prepared = Prepare(bridge);

        ProductDesktopInteractionIntentConsumptionResult entered =
            controller.Consume(
                prepared,
                Evidence(),
                ["container-1:item:1", "container-1:item:2"],
                Now);
        ProductDesktopInteractionIntentConsumptionResult selected =
            controller.ApplySelection(
                new(
                    ProductDesktopSelectionAction.SelectItem,
                    ItemId: "container-1:item:2"),
                ["container-1:item:1", "container-1:item:2"],
                Now.AddMilliseconds(1));
        ProductDesktopInteractionIntentConsumptionResult replay =
            controller.Consume(
                prepared,
                Evidence(),
                ["container-1:item:1", "container-1:item:2"],
                Now.AddMilliseconds(2));

        Assert.True(entered.IsExplicit);
        Assert.True(selected.IsExplicit);
        Assert.Equal(
            ["container-1:item:2"],
            selected.Snapshot.Transaction!.Selection!.SelectedItemIds);
        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.StalePreparedIntent,
            replay.Snapshot.Status);
        Assert.True(replay.Snapshot.Transaction!.IsExplicit);
        Assert.False(replay.Snapshot.PreparedIntentConsumed);
        Assert.Equal(1, surface.ApplyExplicitCalls);
    }

    [Fact]
    public void StaleEvidenceCannotConsumeOrMutateSurface()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        var controller = new ProductDesktopInteractionIntentConsumptionController(
            Interaction(),
            Forwarding(),
            bridge);
        var surface = new FakeSurface();
        Assert.True(controller.AttachSurface(surface));
        ProductDesktopInteractionPreparedIntent prepared = Prepare(bridge);

        ProductDesktopInteractionIntentConsumptionResult result =
            controller.Consume(
                prepared,
                Evidence() with { TopologyGeneration = 10 },
                ["container-1:item:1"],
                Now);

        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.StalePreparedIntent,
            result.Snapshot.Status);
        Assert.Equal(0, surface.ApplyExplicitCalls);
        Assert.True(bridge.Snapshot.PreparedIntentAvailable);
    }

    [Fact]
    public void EntryFailureConsumesOnceAndRestoresPassive()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        var controller = new ProductDesktopInteractionIntentConsumptionController(
            Interaction(),
            Forwarding(),
            bridge);
        var surface = new FakeSurface { ApplyExplicitResult = false };
        Assert.True(controller.AttachSurface(surface));

        ProductDesktopInteractionIntentConsumptionResult result =
            controller.Consume(
                Prepare(bridge),
                Evidence(),
                ["container-1:item:1"],
                Now);

        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.EntryRejected,
            result.Snapshot.Status);
        Assert.True(result.Snapshot.PreparedIntentConsumed);
        Assert.True(result.Snapshot.Transaction!.Surface!.IsPassiveContract);
        Assert.False(bridge.Snapshot.PreparedIntentAvailable);
    }

    [Fact]
    public void CancellationDetachAndCompletionAreFailClosed()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        var controller = new ProductDesktopInteractionIntentConsumptionController(
            Interaction(),
            Forwarding(),
            bridge);
        var surface = new FakeSurface();
        Assert.True(controller.AttachSurface(surface));
        _ = controller.Consume(
            Prepare(bridge),
            Evidence(),
            ["container-1:item:1"],
            Now);

        ProductDesktopInteractionIntentConsumptionResult cancelled =
            controller.Cancel(
                ProductDesktopInteractionCancellationSignal.FocusLost,
                Now.AddMilliseconds(1));
        bool detached = controller.DetachSurface(surface, Now.AddMilliseconds(2));
        ProductDesktopInteractionIntentConsumptionResult completed =
            controller.Complete(Now.AddMilliseconds(3));
        ProductDesktopInteractionIntentConsumptionResult repeated =
            controller.Complete(Now.AddMilliseconds(4));

        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.Cancelled,
            cancelled.Snapshot.Status);
        Assert.True(cancelled.Snapshot.Transaction!.Surface!.IsPassiveContract);
        Assert.True(detached);
        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.Completed,
            completed.Snapshot.Status);
        Assert.Equal(completed, repeated);
        Assert.False(completed.Snapshot.SurfaceAttached);
        Assert.False(completed.Snapshot.RealFileOperationsAllowed);
    }

    [Fact]
    public void MissingForwardingGateKeepsConsumerDisabled()
    {
        ProductDesktopInteractionFeatureDecision interaction = Interaction();
        ProductDesktopInteractionIntentBridgeFeatureDecision bridgeDecision =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                interaction,
                "1",
                "1");
        var bridge = new ProductDesktopInteractionIntentPreparationBridge(
            bridgeDecision);
        var controller = new ProductDesktopInteractionIntentConsumptionController(
            interaction,
            ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                bridgeDecision,
                "0",
                "1"),
            bridge);
        var surface = new FakeSurface();

        Assert.False(controller.IsEnabled);
        Assert.False(controller.AttachSurface(surface));
        Assert.Equal(
            ProductDesktopInteractionIntentConsumptionStatus.DisabledBySafetyPolicy,
            controller.Snapshot.Status);
        Assert.False(controller.Snapshot.RealFileOperationsAllowed);
        Assert.Equal(0, surface.ApplyExplicitCalls);
    }

    private static ProductDesktopInteractionPreparedIntent Prepare(
        ProductDesktopInteractionIntentPreparationBridge bridge) =>
        bridge.Prepare(
            new(
                Guid.NewGuid(),
                1,
                Now,
                ExplicitUserActionConfirmed: true,
                ProductDesktopInteractionActivationKind.PrimaryPointerPress,
                "display-1",
                10,
                10),
            Batch(),
            Evidence(),
            Now).PreparedIntent!;

    private static ProductDesktopInteractionIntentPreparationBridge Bridge() =>
        new(ProductDesktopInteractionIntentBridgePolicy.Evaluate(
            Interaction(),
            "1",
            "1"));

    private static ProductDesktopInteractionFeatureDecision Interaction() =>
        ProductDesktopInteractionFeaturePolicy.Evaluate(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            "1");

    private static ProductDesktopInteractionInputForwardingFeatureDecision
        Forwarding()
    {
        ProductDesktopInteractionFeatureDecision interaction = Interaction();
        ProductDesktopInteractionIntentBridgeFeatureDecision bridge =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                interaction,
                "1",
                "1");
        return ProductDesktopInteractionInputForwardingPolicy.Evaluate(
            bridge,
            "1",
            "1");
    }

    private static ProductDesktopHostProjectionBatch Batch() =>
        ProductDesktopHostProjectionBatch.Create(
            7,
            9,
            new string('A', 64),
            [ProductDesktopHostDisplayProjection.Create(
                "display-1",
                new(0, 0, 1920, 1080),
                96,
                [ProductDesktopHostReadOnlyProjection.Create(
                    "container-1",
                    "工作",
                    ["A", "B"],
                    "#336699",
                    0.8,
                    false,
                    0,
                    0,
                    300,
                    240)])]);

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: new HashSet<string>(["container-1"]),
            LockedContainerIds: new HashSet<string>());

    private sealed class FakeSurface :
        IProductDesktopInteractionSurfaceModeAdapter
    {
        private ProductDesktopInteractionSurfaceEvidence state = Passive();

        internal int ApplyExplicitCalls { get; private set; }

        internal bool ApplyExplicitResult { get; init; } = true;

        public ProductDesktopInteractionSurfaceCapture Capture() =>
            new(true, state);

        public bool ApplyExplicit(ProductDesktopInteractionLease lease)
        {
            ApplyExplicitCalls++;
            if (ApplyExplicitResult)
            {
                state = Explicit();
            }

            return ApplyExplicitResult;
        }

        public bool ApplyPassive(long expectedWindowRegistryGeneration)
        {
            state = Passive();
            return true;
        }

        public bool Restore(ProductDesktopInteractionSurfaceEvidence evidence)
        {
            state = evidence;
            return true;
        }

        public bool Hide(long expectedWindowRegistryGeneration)
        {
            state = Passive() with
            {
                Mode = ProductDesktopInteractionSurfaceMode.Hidden,
                Visible = false,
            };
            return true;
        }

        private static ProductDesktopInteractionSurfaceEvidence Passive() =>
            new(
                ProductDesktopInteractionSurfaceMode.Passive,
                11,
                Visible: true,
                HitTestTransparent: true,
                IsKeyboardFocusable: false,
                SelectionPatternAvailable: false,
                ToolWindow: true,
                NoActivate: true,
                Topmost: false,
                HasOwner: false,
                OwnsForeground: false);

        private static ProductDesktopInteractionSurfaceEvidence Explicit() =>
            Passive() with
            {
                Mode = ProductDesktopInteractionSurfaceMode.Explicit,
                HitTestTransparent = false,
                IsKeyboardFocusable = true,
                SelectionPatternAvailable = true,
            };
    }
}
