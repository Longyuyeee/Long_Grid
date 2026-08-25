using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopMarqueeSelectionTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 8, 25, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BlankContentDragMapsToOneAtomicOrderedSelectionRequest()
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();

        ProductDesktopMarqueeSelectionSession session = Assert.IsType<
            ProductDesktopMarqueeSelectionSession>(
                ProductDesktopMarqueeSelectionAdapter.TryStart(
                    projection,
                    transaction,
                    x: 300,
                    y: 220,
                    control: false,
                    shift: false));
        ProductDesktopMarqueeSelectionCommand command = Assert.IsType<
            ProductDesktopMarqueeSelectionCommand>(
                ProductDesktopMarqueeSelectionAdapter.TryComplete(
                    projection,
                    transaction,
                    session,
                    x: 30,
                    y: 120));

        Assert.Equal("container-1", command.ContainerId);
        Assert.Equal(ProductDesktopSelectionAction.SelectItems,
            command.Request.Action);
        Assert.Equal(ProductDesktopSelectionModifiers.None,
            command.Request.Modifiers);
        Assert.Equal(["item-2", "item-3"], command.Request.ItemIds);
        Assert.True(command.Bounds.HasArea);
    }

    [Fact]
    public void ControlMarqueePreservesModifierAndEmptyClickProducesClearSet()
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();
        ProductDesktopMarqueeSelectionSession session = Assert.IsType<
            ProductDesktopMarqueeSelectionSession>(
                ProductDesktopMarqueeSelectionAdapter.TryStart(
                    projection,
                    transaction,
                    x: 300,
                    y: 220,
                    control: true,
                    shift: false));

        ProductDesktopMarqueeSelectionCommand command = Assert.IsType<
            ProductDesktopMarqueeSelectionCommand>(
                ProductDesktopMarqueeSelectionAdapter.TryComplete(
                    projection,
                    transaction,
                    session,
                    x: 300,
                    y: 220));

        Assert.Equal(ProductDesktopSelectionModifiers.Control,
            command.Request.Modifiers);
        Assert.Empty(command.Request.ItemIds!);
        Assert.False(command.Bounds.HasArea);
    }

    [Theory]
    [InlineData(100, 100, false, false)]
    [InlineData(100, 50, false, false)]
    [InlineData(300, 220, false, true)]
    [InlineData(20, 220, false, false)]
    public void UnsafeMarqueeStartsFailClosed(
        int x,
        int y,
        bool control,
        bool shift)
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();

        Assert.Null(ProductDesktopMarqueeSelectionAdapter.TryStart(
            projection,
            transaction,
            x,
            y,
            control,
            shift));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("workspace")]
    [InlineData("topology")]
    [InlineData("registry")]
    [InlineData("selection")]
    [InlineData("visible")]
    [InlineData("bounds")]
    [InlineData("passive")]
    public void AnyAuthorityDriftCancelsMarquee(string fault)
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();
        ProductDesktopMarqueeSelectionSession session = Assert.IsType<
            ProductDesktopMarqueeSelectionSession>(
                ProductDesktopMarqueeSelectionAdapter.TryStart(
                    projection,
                    transaction,
                    x: 300,
                    y: 220,
                    control: false,
                    shift: false));

        ProductDesktopInteractionLease lease = transaction.Admission.Lease!;
        ProductDesktopSelectionSnapshot selection = transaction.Selection!;
        switch (fault)
        {
            case "intent":
                lease = lease with { IntentId = Guid.NewGuid() };
                break;
            case "workspace":
                lease = lease with { WorkspaceRevision = 8 };
                break;
            case "topology":
                lease = lease with { TopologyGeneration = 10 };
                break;
            case "registry":
                lease = lease with { WindowRegistryGeneration = 12 };
                break;
            case "selection":
                selection = selection with { SelectionRevision = 1 };
                break;
            case "visible":
                selection = selection with
                {
                    VisibleItemIds = ["item-1", "item-2"],
                };
                break;
            case "bounds":
                projection = Projection(widthDip: 400);
                break;
            case "passive":
                transaction = transaction with
                {
                    Status =
                        ProductDesktopInteractionSurfaceTransactionStatus.Passive,
                };
                break;
        }
        if (fault is not "bounds" and not "passive")
        {
            transaction = transaction with
            {
                Admission = transaction.Admission with { Lease = lease },
                Selection = selection,
            };
        }

        Assert.Null(ProductDesktopMarqueeSelectionAdapter.TryUpdate(
            projection,
            transaction,
            session,
            x: 30,
            y: 120));
        Assert.Null(ProductDesktopMarqueeSelectionAdapter.TryComplete(
            projection,
            transaction,
            session,
            x: 30,
            y: 120));
    }

    [Fact]
    public void UpdateClampsCapturedPointerToFrozenContainerContent()
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();
        ProductDesktopMarqueeSelectionSession session = Assert.IsType<
            ProductDesktopMarqueeSelectionSession>(
                ProductDesktopMarqueeSelectionAdapter.TryStart(
                    projection,
                    transaction,
                    x: 300,
                    y: 220,
                    control: false,
                    shift: false));

        ProductDesktopMarqueeSelectionSession updated = Assert.IsType<
            ProductDesktopMarqueeSelectionSession>(
                ProductDesktopMarqueeSelectionAdapter.TryUpdate(
                    projection,
                    transaction,
                    session,
                    x: -500,
                    y: -500));

        Assert.Equal(session.ContainerBounds.Left, updated.Current.X);
        Assert.Equal(session.ContentTop, updated.Current.Y);
    }

    private static (
        ProductDesktopHostDisplayProjection Projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction)
        Context()
    {
        ProductDesktopHostDisplayProjection projection = Projection();
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(),
            "container-1",
            7,
            9,
            11,
            Now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                ["item-1", "item-2", "item-3"],
                Now).Controller!;
        ProductDesktopSelectionSnapshot snapshot = selection.Snapshot;
        return (
            projection,
            new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                snapshot,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(snapshot),
                1));
    }

    private static ProductDesktopHostDisplayProjection Projection(
        double widthDip = 360) =>
        ProductDesktopHostDisplayProjection.Create(
            "display-primary",
            new(0, 0, 1920, 1040),
            96,
            [
                ProductDesktopHostReadOnlyProjection.Create(
                    "container-1",
                    "工作",
                    ["项目 1", "项目 2", "项目 3"],
                    "#2457D6",
                    0.82,
                    false,
                    24,
                    36,
                    widthDip,
                    240,
                    itemIds: ["item-1", "item-2", "item-3"]),
            ]);
}
