using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerLayoutPreviewTests
{
    [Fact]
    public void MoveSnapsToAdjacentContainerEdgeWithoutMutatingSource()
    {
        ProductWorkspaceState state = State();

        ProductWorkspaceContainerLayoutPreviewDecision result = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            deltaX: 8,
            deltaY: 0);

        Assert.True(result.CanPreview);
        Assert.True(result.Changed);
        Assert.True(result.SnappedX);
        Assert.Equal(110, result.Placement!.XDip);
        Assert.Equal(200, result.Placement.WidthDip);
        Assert.Equal(100, state.Containers[0].Placement.XDip);
    }

    [Fact]
    public void ShiftReversesTheConfiguredSnapBehavior()
    {
        ProductWorkspaceState state = State();

        ProductWorkspaceContainerLayoutPreviewDecision disabled = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            deltaX: 8,
            deltaY: 0,
            snapEnabled: true,
            shiftPressed: true);
        ProductWorkspaceContainerLayoutPreviewDecision enabled = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            deltaX: 8,
            deltaY: 0,
            snapEnabled: false,
            shiftPressed: true);

        Assert.Equal(108, disabled.Placement!.XDip);
        Assert.False(disabled.SnappedX);
        Assert.Equal(110, enabled.Placement!.XDip);
        Assert.True(enabled.SnappedX);
    }

    [Fact]
    public void RightResizeSnapsAndLeftResizeKeepsMinimumWidth()
    {
        ProductWorkspaceState state = State();

        ProductWorkspaceContainerLayoutPreviewDecision right = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.ResizeRight,
            deltaX: 8,
            deltaY: 0);
        ProductWorkspaceContainerLayoutPreviewDecision left = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.ResizeLeft,
            deltaX: 500,
            deltaY: 0,
            snapEnabled: false);

        Assert.Equal(210, right.Placement!.WidthDip);
        Assert.True(right.SnappedX);
        Assert.Equal(
            ProductWorkspaceContainerLayoutPreview.MinimumWidthDip,
            left.Placement!.WidthDip);
        Assert.Equal(140, left.Placement.XDip);
    }

    [Theory]
    [InlineData(ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft)]
    [InlineData(ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight)]
    [InlineData(ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft)]
    [InlineData(ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight)]
    public void CornerResizeChangesBothAxes(
        ProductWorkspaceContainerLayoutGestureKind kind)
    {
        ProductWorkspaceContainerLayoutPreviewDecision result = Evaluate(
            State(),
            kind,
            deltaX: kind is
                ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft
                    or ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft
                        ? -16
                        : 16,
            deltaY: kind is
                ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft
                    or ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight
                        ? -16
                        : 16,
            snapEnabled: false);

        Assert.True(result.CanPreview);
        Assert.True(result.Changed);
        Assert.Equal(216, result.Placement!.WidthDip);
        Assert.Equal(176, result.Placement.HeightDip);
    }

    [Fact]
    public void LockedContainerIsRejectedWithoutPreview()
    {
        ProductWorkspaceState state = State() with
        {
            Containers =
            [
                State().Containers[0] with { IsLocked = true },
                State().Containers[1],
            ],
        };

        ProductWorkspaceContainerLayoutPreviewDecision result = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            8,
            0);

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.ContainerLocked,
            result.Status);
        Assert.Null(result.Placement);
    }

    [Fact]
    public void StaleRevisionAndTopologyAreIndependentlyRejected()
    {
        ProductWorkspaceContainerLayoutPreviewRequest request = Request(
            ProductWorkspaceContainerLayoutGestureKind.Move,
            8,
            0);

        ProductWorkspaceContainerLayoutPreviewDecision revision =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                currentEditRevision: 6,
                currentTopologyGeneration: 7,
                Displays(),
                request);
        ProductWorkspaceContainerLayoutPreviewDecision topology =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                currentEditRevision: 5,
                currentTopologyGeneration: 8,
                Displays(),
                request);

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.StaleEditRevision,
            revision.Status);
        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.StaleTopology,
            topology.Status);
    }

    [Fact]
    public void DpiScaledWorkAreaConstrainsTheWholeContainer()
    {
        DisplayTopologyNode[] displays =
        [
            new(
                "display-1",
                new(0, 0, 3840, 2160),
                new(0, 0, 3840, 2160),
                288,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ];

        ProductWorkspaceContainerLayoutPreviewDecision result =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(includeSecond: false),
                5,
                7,
                displays,
                Request(
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    5000,
                    5000,
                    snapEnabled: false));

        Assert.Equal(1080, result.Placement!.XDip);
        Assert.Equal(560, result.Placement.YDip);
        Assert.Equal(1280, result.Placement.XDip + result.Placement.WidthDip);
        Assert.Equal(720, result.Placement.YDip + result.Placement.HeightDip);
    }

    [Theory]
    [InlineData(96u, 1920, 1040)]
    [InlineData(144u, 2880, 1560)]
    [InlineData(192u, 3840, 2080)]
    [InlineData(288u, 5760, 3120)]
    [InlineData(384u, 7680, 4160)]
    public void CommonDpiScalesKeepTheSameDipBoundary(
        uint dpi,
        int pixelWidth,
        int pixelHeight)
    {
        DisplayTopologyNode[] displays =
        [
            new(
                "display-1",
                new(-pixelWidth, 0, pixelWidth, pixelHeight),
                new(-pixelWidth, 0, pixelWidth, pixelHeight),
                dpi,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ];

        ProductWorkspaceContainerLayoutPreviewDecision result =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(includeSecond: false),
                5,
                7,
                displays,
                Request(
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    5000,
                    5000,
                    snapEnabled: false));

        Assert.Equal(1720, result.Placement!.XDip);
        Assert.Equal(880, result.Placement.YDip);
    }

    [Fact]
    public void MissingOrDifferentDisplayFailsClosed()
    {
        ProductWorkspaceContainerLayoutPreviewDecision missing =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                5,
                7,
                Displays(),
                Request(
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    1,
                    1) with
                { DisplayId = "display-2" });

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable,
            missing.Status);
    }

    [Fact]
    public void InvalidDeltaFailsClosed()
    {
        ProductWorkspaceContainerLayoutPreviewDecision result =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                5,
                7,
                Displays(),
                Request(
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    double.NaN,
                    0));

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest,
            result.Status);
    }

    [Fact]
    public void ExtremeDeltaAndDuplicateDisplayFailClosed()
    {
        ProductWorkspaceContainerLayoutPreviewRequest request = Request(
            ProductWorkspaceContainerLayoutGestureKind.Move,
            1_000_001,
            0);
        ProductWorkspaceContainerLayoutPreviewDecision extreme =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                5,
                7,
                Displays(),
                request);
        DisplayTopologyNode display = Displays()[0];
        ProductWorkspaceContainerLayoutPreviewDecision duplicate =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                State(),
                5,
                7,
                [display, display],
                request with { DeltaXDip = 1 });

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest,
            extreme.Status);
        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable,
            duplicate.Status);
    }

    [Fact]
    public void OutOfBoundsSourceFailsWithoutThrowing()
    {
        ProductWorkspaceState state = State(includeSecond: false);
        state = state with
        {
            Containers =
            [
                state.Containers[0] with
                {
                    Placement = state.Containers[0].Placement with
                    {
                        XDip = 2_000,
                    },
                },
            ],
        };

        ProductWorkspaceContainerLayoutPreviewDecision result = Evaluate(
            state,
            ProductWorkspaceContainerLayoutGestureKind.ResizeRight,
            10,
            0);

        Assert.Equal(
            ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest,
            result.Status);
        Assert.Null(result.Placement);
    }

    private static ProductWorkspaceContainerLayoutPreviewDecision Evaluate(
        ProductWorkspaceState state,
        ProductWorkspaceContainerLayoutGestureKind kind,
        double deltaX,
        double deltaY,
        bool snapEnabled = true,
        bool shiftPressed = false) =>
        ProductWorkspaceContainerLayoutPreview.Evaluate(
            state,
            5,
            7,
            Displays(),
            Request(kind, deltaX, deltaY, snapEnabled, shiftPressed));

    private static ProductWorkspaceContainerLayoutPreviewRequest Request(
        ProductWorkspaceContainerLayoutGestureKind kind,
        double deltaX,
        double deltaY,
        bool snapEnabled = true,
        bool shiftPressed = false) =>
        new(
            kind,
            "container-1",
            ExpectedEditRevision: 5,
            ExpectedTopologyGeneration: 7,
            DisplayId: "display-1",
            DeltaXDip: deltaX,
            DeltaYDip: deltaY,
            snapEnabled,
            shiftPressed);

    private static ProductWorkspaceState State(bool includeSecond = true)
    {
        ProductContainerState first = Container("container-1", 100, 100);
        ProductContainerState second = Container("container-2", 310, 100);
        return new()
        {
            ProfileId = "pf003-layout-preview",
            Containers = includeSecond ? [first, second] : [first],
        };
    }

    private static ProductContainerState Container(string id, double x, double y) =>
        new()
        {
            Id = id,
            Name = id,
            Appearance = new()
            {
                Color = "#2457D6",
                Opacity = 0.8,
                Collapsed = false,
            },
            Placement = new()
            {
                DisplayKey = "display-1",
                XDip = x,
                YDip = y,
                WidthDip = 200,
                HeightDip = 160,
            },
            Items = [],
        };

    private static DisplayTopologyNode[] Displays() =>
    [
        new(
            "display-1",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            IsPrimary: true),
    ];
}
