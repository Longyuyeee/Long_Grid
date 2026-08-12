using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionInputAdapterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 12, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HeaderAndVisibleItemsUseSharedDpiScaledLayout()
    {
        ProductDesktopHostDisplayProjection display = Display(
            dpi: 192,
            Container("container-1", x: 10, y: 20, items: ["A", "B"]));

        ProductDesktopInteractionHitTestResult header =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 30,
                clientY: 50);
        ProductDesktopInteractionHitTestResult first =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 30,
                clientY: 20 * 2 + 54 * 2);
        ProductDesktopInteractionHitTestResult second =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 30,
                clientY: 20 * 2 + 54 * 2 + 28 * 2);

        Assert.Equal(ProductDesktopInteractionHitRegion.Header, header.Region);
        Assert.Equal(
            ProductDesktopInteractionHitRegion.VisibleItem,
            first.Region);
        Assert.Equal(0, first.VisibleItemIndex);
        Assert.Equal(
            ProductDesktopInteractionHitRegion.VisibleItem,
            second.Region);
        Assert.Equal(1, second.VisibleItemIndex);
    }

    [Fact]
    public void HalfOpenBoundsDoNotLeakIntoAdjacentPixels()
    {
        ProductDesktopHostDisplayProjection display = Display(
            dpi: 96,
            Container("container-1", x: 20, y: 30, width: 200, height: 180));

        ProductDesktopInteractionHitTestResult inside =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 219,
                clientY: 209);
        ProductDesktopInteractionHitTestResult rightEdge =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 220,
                clientY: 100);
        ProductDesktopInteractionHitTestResult bottomEdge =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 100,
                clientY: 210);

        Assert.True(inside.IsHit);
        Assert.Equal(ProductDesktopInteractionHitStatus.NoTarget, rightEdge.Status);
        Assert.Equal(ProductDesktopInteractionHitStatus.NoTarget, bottomEdge.Status);
    }

    [Fact]
    public void CollapsedContainerNeverExposesItemHitRegion()
    {
        ProductDesktopHostDisplayProjection display = Display(
            dpi: 96,
            Container(
                "container-1",
                x: 0,
                y: 0,
                collapsed: true,
                items: ["A"]));

        ProductDesktopInteractionHitTestResult result =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 10,
                clientY: 53);

        Assert.Equal(ProductDesktopInteractionHitRegion.Header, result.Region);
        Assert.Equal(-1, result.VisibleItemIndex);
    }

    [Fact]
    public void EmptyBodySpaceIsContentRatherThanInventedItem()
    {
        ProductDesktopHostDisplayProjection display = Display(
            dpi: 96,
            Container("container-1", x: 0, y: 0, height: 220, items: ["A"]));

        ProductDesktopInteractionHitTestResult result =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 10,
                clientY: 150);

        Assert.Equal(ProductDesktopInteractionHitRegion.Content, result.Region);
        Assert.Equal(-1, result.VisibleItemIndex);
    }

    [Fact]
    public void PartialItemRowClampedByWorkAreaIsNotHittable()
    {
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-1",
                new PixelRect(0, 0, 320, 70),
                96,
                [Container("container-1", x: 0, y: 0, items: ["A"])]);

        ProductDesktopInteractionHitTestResult result =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 10,
                clientY: 60);

        Assert.Equal(ProductDesktopInteractionHitRegion.Content, result.Region);
        Assert.Equal(-1, result.VisibleItemIndex);
    }

    [Fact]
    public void OverlapFailsClosedWithoutAssumingZOrder()
    {
        ProductDesktopHostDisplayProjection display = Display(
            dpi: 96,
            Container("container-1", x: 10, y: 10),
            Container("container-2", x: 20, y: 20));

        ProductDesktopInteractionHitTestResult result =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                display,
                clientX: 30,
                clientY: 30);

        Assert.Equal(
            ProductDesktopInteractionHitStatus.AmbiguousTarget,
            result.Status);
        Assert.False(result.IsHit);
        Assert.Null(result.ContainerId);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    public void OutsideSurfaceIsDistinctFromEmptySurfacePoint(int x, int y)
    {
        ProductDesktopInteractionHitTestResult result =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                Display(96, Container("container-1", x: 100, y: 100)),
                x,
                y);

        Assert.Equal(
            ProductDesktopInteractionHitStatus.OutsideSurface,
            result.Status);
    }

    [Fact]
    public void IntentFactoryBindsHitAndAllCurrentGenerationsForFiveSeconds()
    {
        ProductDesktopInteractionHitTestResult hit =
            ProductDesktopInteractionHitTestAdapter.HitTest(
                Display(96, Container("container-1", x: 0, y: 0)),
                10,
                10);
        Guid id = Guid.Parse("14f1fb5d-a8ab-43ae-8fcc-0aad15d5f266");

        ProductDesktopInteractionIntentCreationResult result =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.PrimaryPointerPress,
                hit,
                Evidence(),
                id,
                Now);

        Assert.True(result.IsCreated);
        Assert.Equal(id, result.Intent!.IntentId);
        Assert.Equal("container-1", result.Intent.TargetContainerId);
        Assert.Equal(7, result.Intent.WorkspaceRevision);
        Assert.Equal(9, result.Intent.TopologyGeneration);
        Assert.Equal(11, result.Intent.WindowRegistryGeneration);
        Assert.Equal(Now, result.Intent.IssuedAtUtc);
        Assert.Equal(Now.AddSeconds(5), result.Intent.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(ProductDesktopInteractionActivationKind.PrimaryPointerPress)]
    [InlineData(ProductDesktopInteractionActivationKind.KeyboardActivation)]
    [InlineData(ProductDesktopInteractionActivationKind.AssistiveTechnologyActivation)]
    public void EverySupportedExplicitActivationCreatesSameBoundedIntent(
        ProductDesktopInteractionActivationKind activation)
    {
        ProductDesktopInteractionIntentCreationResult result =
            ProductDesktopInteractionIntentFactory.Create(
                activation,
                Hit("container-1"),
                Evidence(),
                Guid.NewGuid(),
                Now);

        Assert.True(result.IsCreated);
        Assert.Equal(Now.AddSeconds(5), result.Intent!.ExpiresAtUtc);
    }

    [Fact]
    public void MissInvalidIdAndInvalidGenerationCannotCreateIntent()
    {
        ProductDesktopInteractionIntentCreationResult miss =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.KeyboardActivation,
                null,
                Evidence(),
                Guid.NewGuid(),
                Now);
        ProductDesktopInteractionIntentCreationResult id =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.KeyboardActivation,
                Hit("container-1"),
                Evidence(),
                Guid.Empty,
                Now);
        ProductDesktopInteractionIntentCreationResult generation =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.KeyboardActivation,
                Hit("container-1"),
                Evidence() with { TopologyGeneration = 0 },
                Guid.NewGuid(),
                Now);

        Assert.Equal(
            ProductDesktopInteractionIntentCreationStatus.HitRequired,
            miss.Status);
        Assert.Equal(
            ProductDesktopInteractionIntentCreationStatus.InvalidActivation,
            id.Status);
        Assert.Equal(
            ProductDesktopInteractionIntentCreationStatus.InvalidEvidence,
            generation.Status);
    }

    [Fact]
    public void UndefinedActivationFailsClosed()
    {
        ProductDesktopInteractionIntentCreationResult result =
            ProductDesktopInteractionIntentFactory.Create(
                (ProductDesktopInteractionActivationKind)999,
                Hit("container-1"),
                Evidence(),
                Guid.NewGuid(),
                Now);

        Assert.Equal(
            ProductDesktopInteractionIntentCreationStatus.InvalidActivation,
            result.Status);
    }

    [Fact]
    public void IntentExpiryOverflowFailsClosed()
    {
        ProductDesktopInteractionIntentCreationResult result =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.KeyboardActivation,
                Hit("container-1"),
                Evidence(),
                Guid.NewGuid(),
                DateTimeOffset.MaxValue);

        Assert.Equal(
            ProductDesktopInteractionIntentCreationStatus.InvalidEvidence,
            result.Status);
        Assert.Null(result.Intent);
    }

    [Theory]
    [InlineData(ProductDesktopInteractionCancellationSignal.EscapePressed,
        ProductDesktopInteractionCancellationReason.EscapePressed)]
    [InlineData(ProductDesktopInteractionCancellationSignal.FocusLost,
        ProductDesktopInteractionCancellationReason.FocusLost)]
    [InlineData(ProductDesktopInteractionCancellationSignal.DesktopRevealRequested,
        ProductDesktopInteractionCancellationReason.DesktopRevealRequested)]
    [InlineData(ProductDesktopInteractionCancellationSignal.FullScreenTransition,
        ProductDesktopInteractionCancellationReason.FullScreenTransition)]
    [InlineData(ProductDesktopInteractionCancellationSignal.SessionLockedOrDisconnected,
        ProductDesktopInteractionCancellationReason.SessionUnavailable)]
    [InlineData(ProductDesktopInteractionCancellationSignal.RemoteSessionTransition,
        ProductDesktopInteractionCancellationReason.RemoteSessionTransition)]
    [InlineData(ProductDesktopInteractionCancellationSignal.ExplorerRestarted,
        ProductDesktopInteractionCancellationReason.ExplorerRestarted)]
    [InlineData(ProductDesktopInteractionCancellationSignal.ApplicationShutdown,
        ProductDesktopInteractionCancellationReason.ApplicationShutdown)]
    public void DirectSystemSignalsCancelActiveLease(
        ProductDesktopInteractionCancellationSignal signal,
        ProductDesktopInteractionCancellationReason reason)
    {
        ProductDesktopInteractionAdmissionController controller =
            ActiveController();
        var adapter = new ProductDesktopInteractionCancellationAdapter(controller);

        ProductDesktopInteractionSnapshot snapshot =
            adapter.Handle(signal, Now);

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.Null(snapshot.Lease);
        Assert.Equal(reason, snapshot.LastCancellationReason);
    }

    [Fact]
    public void EvidenceSignalDelegatesToGenerationRevalidation()
    {
        ProductDesktopInteractionAdmissionController controller =
            ActiveController();
        var adapter = new ProductDesktopInteractionCancellationAdapter(controller);

        ProductDesktopInteractionSnapshot snapshot = adapter.Handle(
            ProductDesktopInteractionCancellationSignal.EvidenceChanged,
            Now,
            Evidence() with { TopologyGeneration = 10 });

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.Equal(
            ProductDesktopInteractionCancellationReason.TopologyChanged,
            snapshot.LastCancellationReason);
    }

    [Fact]
    public void TimerSignalKeepsLiveLeaseAndExpiresAtBoundary()
    {
        ProductDesktopInteractionAdmissionController controller =
            ActiveController();
        var adapter = new ProductDesktopInteractionCancellationAdapter(controller);

        ProductDesktopInteractionSnapshot live = adapter.Handle(
            ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed,
            Now.AddSeconds(4),
            Evidence());
        ProductDesktopInteractionSnapshot expired = adapter.Handle(
            ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed,
            Now.AddSeconds(5),
            Evidence());

        Assert.True(live.HasActiveLease);
        Assert.False(expired.HasActiveLease);
        Assert.Equal(
            ProductDesktopInteractionCancellationReason.IntentExpired,
            expired.LastCancellationReason);
    }

    [Fact]
    public void AdapterRejectsMissingOrUnrelatedEvidence()
    {
        var adapter = new ProductDesktopInteractionCancellationAdapter(
            ActiveController());

        Assert.Throws<ArgumentNullException>(() => adapter.Handle(
            ProductDesktopInteractionCancellationSignal.EvidenceChanged,
            Now));
        Assert.Throws<ArgumentException>(() => adapter.Handle(
            ProductDesktopInteractionCancellationSignal.EscapePressed,
            Now,
            Evidence()));
    }

    [Fact]
    public void DirectSignalWhilePassiveIsIdempotent()
    {
        ProductDesktopInteractionAdmissionController controller = Controller();
        var adapter = new ProductDesktopInteractionCancellationAdapter(controller);

        ProductDesktopInteractionSnapshot snapshot = adapter.Handle(
            ProductDesktopInteractionCancellationSignal.EscapePressed,
            Now);

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.Equal(
            ProductDesktopInteractionCancellationReason.None,
            snapshot.LastCancellationReason);
    }

    [Fact]
    public void UndefinedCancellationSignalFailsClosed()
    {
        var adapter = new ProductDesktopInteractionCancellationAdapter(
            ActiveController());

        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.Handle(
            (ProductDesktopInteractionCancellationSignal)999,
            Now));
    }

    private static ProductDesktopInteractionAdmissionController
        ActiveController()
    {
        ProductDesktopInteractionAdmissionController controller = Controller();
        ProductDesktopInteractionIntent intent =
            ProductDesktopInteractionIntentFactory.Create(
                ProductDesktopInteractionActivationKind.PrimaryPointerPress,
                Hit("container-1"),
                Evidence(),
                Guid.NewGuid(),
                Now).Intent!;
        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(intent, Evidence(), Now);
        Assert.True(snapshot.HasActiveLease);
        return controller;
    }

    private static ProductDesktopInteractionAdmissionController Controller() =>
        new(ProductDesktopInteractionFeaturePolicy.Evaluate(
            ProductDesktopHostFeaturePolicy.Evaluate("1"),
            "1"));

    private static ProductDesktopInteractionHitTestResult Hit(
        string containerId) =>
        ProductDesktopInteractionHitTestAdapter.HitTest(
            Display(96, Container(containerId, x: 0, y: 0)),
            10,
            10);

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

    private static ProductDesktopHostDisplayProjection Display(
        uint dpi,
        params ProductDesktopHostReadOnlyProjection[] containers) =>
        ProductDesktopHostDisplayProjection.Create(
            "display-1",
            new PixelRect(0, 0, 1920, 1080),
            dpi,
            containers);

    private static ProductDesktopHostReadOnlyProjection Container(
        string id,
        double x,
        double y,
        double width = 200,
        double height = 180,
        bool collapsed = false,
        string[]? items = null) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            id,
            items ?? [],
            "#336699",
            0.8,
            collapsed,
            x,
            y,
            width,
            height);
}
