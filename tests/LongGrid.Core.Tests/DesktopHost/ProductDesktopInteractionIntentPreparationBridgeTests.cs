using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionIntentPreparationBridgeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 4, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null,
        ProductDesktopInteractionIntentBridgeFeatureStatus
            .DisabledByIntentBridgePolicy)]
    [InlineData("true", "1",
        ProductDesktopInteractionIntentBridgeFeatureStatus
            .DisabledByIntentBridgePolicy)]
    [InlineData("1", null,
        ProductDesktopInteractionIntentBridgeFeatureStatus
            .DisabledByManualSessionPolicy)]
    [InlineData("1", "true",
        ProductDesktopInteractionIntentBridgeFeatureStatus
            .DisabledByManualSessionPolicy)]
    [InlineData("1", "1",
        ProductDesktopInteractionIntentBridgeFeatureStatus
            .EnabledForControlledManualSession)]
    public void PolicyRequiresExactBridgeAndManualSessionValues(
        string? bridge,
        string? session,
        ProductDesktopInteractionIntentBridgeFeatureStatus expected)
    {
        ProductDesktopInteractionIntentBridgeFeatureDecision decision =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                EnabledInteraction(),
                bridge,
                session);

        Assert.Equal(expected, decision.Status);
        Assert.Equal(
            expected == ProductDesktopInteractionIntentBridgeFeatureStatus
                .EnabledForControlledManualSession,
            decision.IsEnabled);
    }

    [Fact]
    public void DisabledInteractionOverridesDownstreamValues()
    {
        ProductDesktopInteractionFeatureDecision interaction =
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("0"),
                "1");

        ProductDesktopInteractionIntentBridgeFeatureDecision decision =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                interaction,
                "1",
                "1");

        Assert.Equal(
            ProductDesktopInteractionIntentBridgeFeatureStatus
                .DisabledByInteractionPolicy,
            decision.Status);
    }

    [Fact]
    public void ConfirmedFreshUniqueHitPreparesButDoesNotConsumeIntent()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();

        ProductDesktopInteractionIntentPreparationResult result = bridge.Prepare(
            Request(sequence: 1),
            Batch(),
            Evidence(),
            Now);

        Assert.True(result.IsPrepared);
        ProductDesktopInteractionPreparedIntent prepared = result.PreparedIntent!;
        Assert.Equal("container-1", prepared.Intent.TargetContainerId);
        Assert.Equal(7, prepared.Intent.WorkspaceRevision);
        Assert.Equal(9, prepared.Intent.TopologyGeneration);
        Assert.Equal(11, prepared.Intent.WindowRegistryGeneration);
        Assert.Equal(Now.AddSeconds(5), prepared.Intent.ExpiresAtUtc);
        Assert.False(result.Snapshot.ExplicitInteractionEntered);
        Assert.False(result.Snapshot.RealFileOperationsAllowed);
        Assert.True(bridge.IsCurrent(prepared, Evidence(), Now));
    }

    [Fact]
    public void MissingPerActionConfirmationFailsClosedAndRevokesPreparedIntent()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        ProductDesktopInteractionPreparedIntent prepared = bridge.Prepare(
            Request(sequence: 1),
            Batch(),
            Evidence(),
            Now).PreparedIntent!;

        ProductDesktopInteractionIntentPreparationResult rejected = bridge.Prepare(
            Request(sequence: 2) with { ExplicitUserActionConfirmed = false },
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.InvalidUserAction,
            rejected.Snapshot.Status);
        Assert.Null(rejected.PreparedIntent);
        Assert.False(bridge.IsCurrent(prepared, Evidence(), Now));
    }

    [Fact]
    public void StaleActionAndMissingPassiveEvidenceCannotPrepare()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();

        ProductDesktopInteractionIntentPreparationResult stale = bridge.Prepare(
            Request(sequence: 1) with
            {
                ObservedAtUtc = Now.Subtract(
                    ProductDesktopInteractionIntentPreparationBridge
                        .MaximumUserActionAge).AddTicks(-1),
            },
            Batch(),
            Evidence(),
            Now);
        ProductDesktopInteractionIntentPreparationResult passive = bridge.Prepare(
            Request(sequence: 2),
            Batch(),
            Evidence() with { PassiveWindowContractAttested = false },
            Now);

        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.StaleUserAction,
            stale.Snapshot.Status);
        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus
                .AwaitingPassiveSurface,
            passive.Snapshot.Status);
        Assert.Null(passive.PreparedIntent);
    }

    [Fact]
    public void DisplayMissAndLockedTargetAreFiniteRejections()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();

        ProductDesktopInteractionIntentPreparationResult display = bridge.Prepare(
            Request(sequence: 1) with { DisplayId = "missing" },
            Batch(),
            Evidence(),
            Now);
        ProductDesktopInteractionIntentPreparationResult miss = bridge.Prepare(
            Request(sequence: 2) with { ClientX = 1000, ClientY = 700 },
            Batch(),
            Evidence(),
            Now);
        ProductDesktopInteractionIntentPreparationResult locked = bridge.Prepare(
            Request(sequence: 3) with { ClientX = 410, ClientY = 10 },
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.DisplayUnavailable,
            display.Snapshot.Status);
        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.HitRejected,
            miss.Snapshot.Status);
        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.IntentRejected,
            locked.Snapshot.Status);
    }

    [Fact]
    public void ReplayedActionCannotReplaceCurrentPreparedIntent()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        ProductDesktopInteractionPreparedIntent prepared = bridge.Prepare(
            Request(sequence: 2),
            Batch(),
            Evidence(),
            Now).PreparedIntent!;

        ProductDesktopInteractionIntentPreparationResult replay = bridge.Prepare(
            Request(sequence: 1),
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.ReplayedUserAction,
            replay.Snapshot.Status);
        Assert.Equal(prepared, replay.PreparedIntent);
        Assert.True(bridge.IsCurrent(prepared, Evidence(), Now));
    }

    [Fact]
    public void NewActionSystemInvalidationGenerationDriftAndExpiryRevokeIntent()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        ProductDesktopInteractionPreparedIntent first = bridge.Prepare(
            Request(sequence: 1),
            Batch(),
            Evidence(),
            Now).PreparedIntent!;
        ProductDesktopInteractionPreparedIntent second = bridge.Prepare(
            Request(sequence: 2) with { UserActionId = Guid.NewGuid() },
            Batch(),
            Evidence(),
            Now).PreparedIntent!;

        Assert.False(bridge.IsCurrent(first, Evidence(), Now));
        Assert.True(bridge.IsCurrent(second, Evidence(), Now));
        Assert.False(bridge.IsCurrent(
            second,
            Evidence() with { TopologyGeneration = 10 },
            Now));
        Assert.False(bridge.IsCurrent(second, Evidence(), Now.AddSeconds(5)));

        _ = bridge.Invalidate();
        Assert.False(bridge.IsCurrent(second, Evidence(), Now));
    }

    [Fact]
    public void CompletionIsTerminalAndIdempotent()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        _ = bridge.Prepare(Request(sequence: 1), Batch(), Evidence(), Now);

        ProductDesktopInteractionIntentPreparationSnapshot completed =
            bridge.Complete();
        ProductDesktopInteractionIntentPreparationSnapshot repeated =
            bridge.Complete();
        ProductDesktopInteractionIntentPreparationResult after = bridge.Prepare(
            Request(sequence: 2),
            Batch(),
            Evidence(),
            Now);

        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.Completed,
            completed.Status);
        Assert.Equal(completed, repeated);
        Assert.Equal(completed, after.Snapshot);
        Assert.Null(after.PreparedIntent);
    }

    [Fact]
    public void CurrentPreparedIntentCanBeConsumedExactlyOnce()
    {
        ProductDesktopInteractionIntentPreparationBridge bridge = Bridge();
        ProductDesktopInteractionPreparedIntent prepared = bridge.Prepare(
            Request(sequence: 1),
            Batch(),
            Evidence(),
            Now).PreparedIntent!;

        bool first = bridge.TryConsume(
            prepared,
            Evidence(),
            Now,
            out ProductDesktopInteractionIntent? intent);
        bool replay = bridge.TryConsume(
            prepared,
            Evidence(),
            Now,
            out ProductDesktopInteractionIntent? replayed);

        Assert.True(first);
        Assert.Equal(prepared.Intent, intent);
        Assert.False(replay);
        Assert.Null(replayed);
        Assert.Equal(
            ProductDesktopInteractionIntentPreparationStatus.Consumed,
            bridge.Snapshot.Status);
        Assert.False(bridge.Snapshot.PreparedIntentAvailable);
    }

    private static ProductDesktopInteractionIntentPreparationBridge Bridge() =>
        new(ProductDesktopInteractionIntentBridgePolicy.Evaluate(
            EnabledInteraction(),
            "1",
            "1"));

    private static ProductDesktopInteractionFeatureDecision EnabledInteraction() =>
        ProductDesktopInteractionFeaturePolicy.Evaluate(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            "1");

    private static ProductDesktopInteractionIntentPreparationRequest Request(
        long sequence) =>
        new(
            Guid.Parse("8368e8a2-1583-4d49-8e39-36c34d39c755"),
            sequence,
            Now,
            ExplicitUserActionConfirmed: true,
            ProductDesktopInteractionActivationKind.PrimaryPointerPress,
            "display-1",
            ClientX: 10,
            ClientY: 10);

    private static ProductDesktopHostProjectionBatch Batch() =>
        ProductDesktopHostProjectionBatch.Create(
            7,
            9,
            new string('A', 64),
            [ProductDesktopHostDisplayProjection.Create(
                "display-1",
                new(0, 0, 1920, 1080),
                96,
                [
                    Container("container-1", x: 0, locked: false),
                    Container("container-2", x: 400, locked: true),
                ])]);

    private static ProductDesktopHostReadOnlyProjection Container(
        string id,
        double x,
        bool locked) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            id,
            ["item"],
            "#336699",
            0.8,
            isCollapsed: false,
            x,
            yDip: 0,
            widthDip: 300,
            heightDip: 240,
            isLocked: locked);

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
                ["container-1", "container-2"],
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(
                ["container-2"],
                StringComparer.Ordinal));
}
