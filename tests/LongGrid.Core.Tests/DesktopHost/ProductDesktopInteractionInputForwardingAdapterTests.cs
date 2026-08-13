using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionInputForwardingAdapterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null,
        ProductDesktopInteractionInputForwardingFeatureStatus
            .DisabledByInputForwardingPolicy)]
    [InlineData("true", "1",
        ProductDesktopInteractionInputForwardingFeatureStatus
            .DisabledByInputForwardingPolicy)]
    [InlineData("1", null,
        ProductDesktopInteractionInputForwardingFeatureStatus
            .DisabledByManualSessionPolicy)]
    [InlineData("1", "true",
        ProductDesktopInteractionInputForwardingFeatureStatus
            .DisabledByManualSessionPolicy)]
    [InlineData("1", "1",
        ProductDesktopInteractionInputForwardingFeatureStatus
            .EnabledForControlledManualSession)]
    public void PolicyRequiresExactForwardingAndManualSessionValues(
        string? forwarding,
        string? session,
        ProductDesktopInteractionInputForwardingFeatureStatus expected)
    {
        ProductDesktopInteractionInputForwardingFeatureDecision decision =
            ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                IntentDecision(),
                forwarding,
                session);

        Assert.Equal(expected, decision.Status);
        Assert.Equal(
            expected == ProductDesktopInteractionInputForwardingFeatureStatus
                .EnabledForControlledManualSession,
            decision.IsEnabled);
    }

    [Fact]
    public void DisabledIntentBridgeOverridesDownstreamValues()
    {
        ProductDesktopInteractionIntentBridgeFeatureDecision intent =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                ProductDesktopInteractionFeaturePolicy.Evaluate(
                    ProductDesktopHostFeaturePolicy.Evaluate(null),
                    "1"),
                "1",
                "1");

        ProductDesktopInteractionInputForwardingFeatureDecision decision =
            ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                intent,
                "1",
                "1");

        Assert.Equal(
            ProductDesktopInteractionInputForwardingFeatureStatus
                .DisabledByIntentBridgePolicy,
            decision.Status);
    }

    [Theory]
    [InlineData(
        ProductDesktopInteractionForwardedInputKind.PrimaryPointerPress)]
    [InlineData(
        ProductDesktopInteractionForwardedInputKind.KeyboardActivation)]
    [InlineData(
        ProductDesktopInteractionForwardedInputKind
            .AssistiveTechnologyActivation)]
    public void AttestedInputForwardsExactlyOnePreparationWithoutConsumption(
        ProductDesktopInteractionForwardedInputKind kind)
    {
        ProductDesktopInteractionInputForwardingAdapter adapter = Adapter();

        ProductDesktopInteractionInputForwardingResult result = adapter.Forward(
            Input(sequence: 1) with { Kind = kind },
            Batch(),
            Evidence(),
            Now);

        Assert.True(result.IsPrepared);
        Assert.Equal("container-1", result.PreparedIntent!.Intent.TargetContainerId);
        Assert.Equal(1, result.PreparedIntent.UserActionSequence);
        Assert.False(result.Snapshot.CapturesGlobalInput);
        Assert.False(result.Snapshot.SendsSyntheticInput);
        Assert.False(result.Snapshot.ExplicitInteractionEntered);
        Assert.False(result.Snapshot.RealFileOperationsAllowed);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void UnattestedInjectedAndAutoRepeatInputFailClosed(
        bool attested,
        bool injected,
        bool autoRepeat)
    {
        ProductDesktopInteractionInputForwardingAdapter adapter = Adapter();

        ProductDesktopInteractionInputForwardingResult result = adapter.Forward(
            Input(sequence: 1) with
            {
                SourceAttested = attested,
                IsInjected = injected,
                IsAutoRepeat = autoRepeat,
            },
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.InvalidInput,
            result.Snapshot.Status);
        Assert.Null(result.PreparedIntent);
        Assert.False(result.Snapshot.PreparedIntentAvailable);
    }

    [Fact]
    public void SequenceAndActionIdReplayCannotPrepareTwice()
    {
        ProductDesktopInteractionInputForwardingAdapter adapter = Adapter();
        ProductDesktopInteractionForwardedInput first = Input(sequence: 1);
        ProductDesktopInteractionInputForwardingResult prepared = adapter.Forward(
            first,
            Batch(),
            Evidence(),
            Now);

        ProductDesktopInteractionInputForwardingResult sequenceReplay =
            adapter.Forward(
                first with { UserActionId = Guid.NewGuid() },
                Batch(),
                Evidence(),
                Now);
        ProductDesktopInteractionInputForwardingResult idReplay = adapter.Forward(
            first with { UserActionSequence = 2 },
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.ReplayedInput,
            sequenceReplay.Snapshot.Status);
        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.ReplayedInput,
            idReplay.Snapshot.Status);
        Assert.Equal(prepared.PreparedIntent, idReplay.PreparedIntent);
        Assert.Equal(1, idReplay.Snapshot.LastUserActionSequence);
    }

    [Fact]
    public void BridgeRejectionRemainsPreparationOnlyAndFinite()
    {
        ProductDesktopInteractionInputForwardingAdapter adapter = Adapter();

        ProductDesktopInteractionInputForwardingResult result = adapter.Forward(
            Input(sequence: 1) with { ClientX = 1000, ClientY = 900 },
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.PreparationRejected,
            result.Snapshot.Status);
        Assert.Null(result.PreparedIntent);
        Assert.False(result.Snapshot.ExplicitInteractionEntered);
    }

    [Fact]
    public void InvalidateAwaitAndCompleteRevokePreparationAndAreBounded()
    {
        ProductDesktopInteractionInputForwardingAdapter adapter = Adapter();
        _ = adapter.Forward(Input(sequence: 1), Batch(), Evidence(), Now);

        ProductDesktopInteractionInputForwardingSnapshot invalidated =
            adapter.Invalidate();
        ProductDesktopInteractionInputForwardingSnapshot awaiting =
            adapter.AwaitPassiveSurface();
        ProductDesktopInteractionInputForwardingSnapshot completed =
            adapter.Complete();
        ProductDesktopInteractionInputForwardingSnapshot repeated =
            adapter.Complete();

        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.Invalidated,
            invalidated.Status);
        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.AwaitingPassiveSurface,
            awaiting.Status);
        Assert.Equal(
            ProductDesktopInteractionInputForwardingStatus.Completed,
            completed.Status);
        Assert.Equal(completed, repeated);
        Assert.False(completed.PreparedIntentAvailable);
    }

    private static ProductDesktopInteractionInputForwardingAdapter Adapter()
    {
        ProductDesktopInteractionIntentBridgeFeatureDecision intent =
            IntentDecision();
        return new(
            ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                intent,
                "1",
                "1"),
            new ProductDesktopInteractionIntentPreparationBridge(intent));
    }

    private static ProductDesktopInteractionIntentBridgeFeatureDecision
        IntentDecision() =>
        ProductDesktopInteractionIntentBridgePolicy.Evaluate(
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("1"),
                "1"),
            "1",
            "1");

    private static ProductDesktopInteractionForwardedInput Input(long sequence) =>
        new(
            Guid.Parse("717da931-c2e5-43cc-8695-804c5d4e18e0"),
            sequence,
            Now,
            ProductDesktopInteractionForwardedInputKind.PrimaryPointerPress,
            "display-1",
            ClientX: 10,
            ClientY: 10,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false);

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
                    "Container",
                    ["item"],
                    "#336699",
                    0.8,
                    isCollapsed: false,
                    xDip: 0,
                    yDip: 0,
                    widthDip: 300,
                    heightDip: 240)])]);

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: new HashSet<string>(
                ["container-1"],
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(StringComparer.Ordinal));
}
