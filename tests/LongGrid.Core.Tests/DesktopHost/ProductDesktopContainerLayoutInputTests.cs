using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerLayoutInputTests
{
    [Theory]
    [InlineData(0x25, false, false, ProductWorkspaceContainerLayoutGestureKind.Move, -1, 0)]
    [InlineData(0x26, false, true, ProductWorkspaceContainerLayoutGestureKind.Move, 0, -8)]
    [InlineData(0x27, true, false, ProductWorkspaceContainerLayoutGestureKind.ResizeRight, 1, 0)]
    [InlineData(0x28, true, true, ProductWorkspaceContainerLayoutGestureKind.ResizeBottom, 0, 8)]
    public void TitleFocusMapsFineLargeMoveAndResizeToFiniteLayoutCommand(
        int virtualKey,
        bool alt,
        bool shift,
        ProductWorkspaceContainerLayoutGestureKind expectedKind,
        double expectedX,
        double expectedY)
    {
        ProductDesktopContainerLayoutKeyboardDecision decision =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                titleFocused: true,
                virtualKey,
                alt,
                control: false,
                shift);

        Assert.True(decision.HasLayoutCommand);
        Assert.Equal(expectedKind, decision.Kind);
        Assert.Equal(expectedX, decision.DeltaXDip);
        Assert.Equal(expectedY, decision.DeltaYDip);
        Assert.Equal(shift, decision.ShiftPressed);
    }

    [Fact]
    public void TabOwnsTitleFocusWithoutStealingItemNavigation()
    {
        ProductDesktopContainerLayoutKeyboardDecision enter =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                titleFocused: false,
                virtualKey: 0x09,
                alt: false,
                control: false,
                shift: false);
        ProductDesktopContainerLayoutKeyboardDecision itemArrow =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                titleFocused: false,
                virtualKey: 0x27,
                alt: false,
                control: false,
                shift: false);
        ProductDesktopContainerLayoutKeyboardDecision exit =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                titleFocused: true,
                virtualKey: 0x09,
                alt: false,
                control: false,
                shift: true);
        ProductDesktopContainerLayoutKeyboardDecision controlTab =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                titleFocused: false,
                virtualKey: 0x09,
                alt: false,
                control: true,
                shift: false);

        Assert.True(enter.Handled);
        Assert.True(enter.TitleFocused);
        Assert.False(itemArrow.Handled);
        Assert.True(exit.Handled);
        Assert.False(exit.TitleFocused);
        Assert.False(controlTab.Handled);
    }

    [Theory]
    [InlineData(101, 101, ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft)]
    [InlineData(200, 101, ProductWorkspaceContainerLayoutGestureKind.ResizeTop)]
    [InlineData(299, 101, ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight)]
    [InlineData(101, 180, ProductWorkspaceContainerLayoutGestureKind.ResizeLeft)]
    [InlineData(299, 180, ProductWorkspaceContainerLayoutGestureKind.ResizeRight)]
    [InlineData(101, 259, ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft)]
    [InlineData(200, 259, ProductWorkspaceContainerLayoutGestureKind.ResizeBottom)]
    [InlineData(299, 259, ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight)]
    [InlineData(150, 120, ProductWorkspaceContainerLayoutGestureKind.Move)]
    public void HeaderAndEightBordersMapToOneFiniteGesture(
        int x,
        int y,
        ProductWorkspaceContainerLayoutGestureKind expected)
    {
        ProductDesktopContainerLayoutHitResult result =
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(Display(), x, y);

        Assert.True(result.IsHit);
        Assert.Equal("container-1", result.ContainerId);
        Assert.Equal(expected, result.Kind);
    }

    [Fact]
    public void LockedOverlapContentAndOutsideFailClosed()
    {
        ProductDesktopHostDisplayProjection locked = Display(isLocked: true);
        ProductDesktopHostReadOnlyProjection first = Container("one", 100, 100);
        ProductDesktopHostReadOnlyProjection second = Container("two", 100, 100);
        ProductDesktopHostDisplayProjection overlap =
            ProductDesktopHostDisplayProjection.Create(
                "display-1",
                new(0, 0, 1920, 1040),
                96,
                [first, second]);

        Assert.Equal(
            ProductDesktopContainerLayoutHitStatus.Locked,
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(
                locked,
                150,
                120).Status);
        Assert.Equal(
            ProductDesktopContainerLayoutHitStatus.AmbiguousTarget,
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(
                overlap,
                150,
                120).Status);
        Assert.Equal(
            ProductDesktopContainerLayoutHitStatus.NoTarget,
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(
                Display(),
                150,
                200).Status);
        Assert.Equal(
            ProductDesktopContainerLayoutHitStatus.OutsideSurface,
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(
                Display(),
                -1,
                0).Status);
    }

    [Theory]
    [InlineData(96u, 299, 259)]
    [InlineData(144u, 449, 389)]
    [InlineData(192u, 599, 519)]
    [InlineData(288u, 899, 779)]
    [InlineData(384u, 1199, 1039)]
    public void CommonDpiScalesKeepBottomRightResizeHit(
        uint dpi,
        int x,
        int y)
    {
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-1",
                new(0, 0, 7680, 4160),
                dpi,
                [Container("container-1", 100, 100)]);

        ProductDesktopContainerLayoutHitResult result =
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(display, x, y);

        Assert.Equal(
            ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight,
            result.Kind);
    }

    [Fact]
    public void RealNativeSurfaceBindsFiniteLayoutSequenceWithoutForeground()
    {
        if (!OperatingSystem.IsWindows()) return;

        ProductDesktopHostDisplayProjection display = Display();
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(17401));
        var captured = new List<ProductDesktopContainerLayoutSurfaceInput>();
        surface.BindContainerLayout(input =>
        {
            captured.Add(input);
            return true;
        });

        Assert.True(surface.ApplyExplicit());
        Assert.True(surface.SubmitContainerLayoutInput(Input(
            ProductDesktopContainerLayoutInputPhase.Begin,
            deltaX: 0,
            deltaY: 0)));
        Assert.True(surface.SubmitContainerLayoutInput(Input(
            ProductDesktopContainerLayoutInputPhase.Update,
            deltaX: 32,
            deltaY: 16)));
        Assert.True(surface.SubmitContainerLayoutInput(Input(
            ProductDesktopContainerLayoutInputPhase.Complete,
            deltaX: 32,
            deltaY: 16)));

        Assert.NotEqual(nint.Zero, surface.Handle);
        Assert.True(surface.ExplicitWindowContractAttested);
        Assert.Equal(3, captured.Count);
        Assert.Equal(
            [
                ProductDesktopContainerLayoutInputPhase.Begin,
                ProductDesktopContainerLayoutInputPhase.Update,
                ProductDesktopContainerLayoutInputPhase.Complete,
            ],
            captured.Select(item => item.Phase).ToArray());
        Assert.Equal(32, captured[1].CumulativeDeltaXDip);
        Assert.Equal(16, captured[1].CumulativeDeltaYDip);
    }

    [Fact]
    public void SurfaceConvertsCallbackExceptionToRejectedInput()
    {
        if (!OperatingSystem.IsWindows()) return;

        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                Display(),
                new nint(17402));
        surface.BindContainerLayout(_ => throw new InvalidOperationException());

        Assert.False(surface.SubmitContainerLayoutInput(Input(
            ProductDesktopContainerLayoutInputPhase.Begin,
            0,
            0)));
    }

    [Fact]
    public void RealNativeSurfaceMovesPreviewWithoutChangingProjection()
    {
        if (!OperatingSystem.IsWindows()) return;

        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                Display(),
                new nint(17403));
        ProductContainerPlacementState candidate = new()
        {
            DisplayKey = "display-1",
            XDip = 220,
            YDip = 180,
            WidthDip = 240,
            HeightDip = 200,
        };

        PixelRect original = surface.GetContainerLayoutBoundsForEvidence(
            "container-1")!.Value;
        Assert.True(surface.ApplyContainerLayoutPreview(
            "container-1",
            candidate));
        PixelRect preview = surface.GetContainerLayoutBoundsForEvidence(
            "container-1")!.Value;
        Assert.True(surface.ApplyContainerLayoutPreview(
            "container-1",
            placement: null));
        PixelRect restored = surface.GetContainerLayoutBoundsForEvidence(
            "container-1")!.Value;
        Assert.False(surface.ApplyContainerLayoutPreview(
            "container-1",
            candidate with { XDip = double.MaxValue }));
        PixelRect afterRejected = surface.GetContainerLayoutBoundsForEvidence(
            "container-1")!.Value;

        Assert.Equal(new PixelRect(100, 100, 200, 160), original);
        Assert.Equal(new PixelRect(220, 180, 240, 200), preview);
        Assert.Equal(original, restored);
        Assert.Equal(original, afterRejected);
        Assert.NotEqual(nint.Zero, surface.Handle);
    }

    [Fact]
    public void RealMixedDpiTargetSurfaceDrawsExternalContainerCandidate()
    {
        if (!OperatingSystem.IsWindows()) return;

        ProductDesktopHostDisplayProjection sourceDisplay = Display();
        ProductDesktopHostDisplayProjection targetDisplay =
            ProductDesktopHostDisplayProjection.Create(
                "display-2",
                new(-1920, 0, 1920, 1040),
                192,
                [],
                isPrimary: false,
                workspaceIsEmpty: false);
        using WindowsProductDesktopHostReadOnlySurface sourceSurface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                sourceDisplay,
                new nint(17404));
        using WindowsProductDesktopHostReadOnlySurface targetSurface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                targetDisplay,
                new nint(17405));
        ProductContainerPlacementState target = new()
        {
            DisplayKey = "display-2",
            XDip = 50,
            YDip = 60,
            WidthDip = 200,
            HeightDip = 160,
        };

        Assert.True(targetSurface.ApplyContainerLayoutPreview(
            sourceDisplay.Containers.Single(),
            target));
        PixelRect bounds = targetSurface.GetContainerLayoutBoundsForEvidence(
            "container-1")!.Value;
        Assert.Equal(new PixelRect(100, 120, 400, 320), bounds);
        Assert.True(targetSurface.ApplyContainerLayoutPreview(
            "container-1",
            placement: null));
        Assert.Null(targetSurface.GetContainerLayoutBoundsForEvidence(
            "container-1"));
        Assert.NotEqual(nint.Zero, sourceSurface.Handle);
        Assert.NotEqual(nint.Zero, targetSurface.Handle);
    }

    private static ProductDesktopContainerLayoutSurfaceInput Input(
        ProductDesktopContainerLayoutInputPhase phase,
        double deltaX,
        double deltaY) =>
        new(
            phase,
            ProductWorkspaceContainerLayoutGestureKind.Move,
            "container-1",
            deltaX,
            deltaY,
            SnapEnabled: true,
            ShiftPressed: false,
            ProductDesktopContainerLayoutCancellationReason.None);

    private static ProductDesktopHostDisplayProjection Display(
        bool isLocked = false) =>
        ProductDesktopHostDisplayProjection.Create(
            "display-1",
            new(0, 0, 1920, 1040),
            96,
            [Container("container-1", 100, 100, isLocked)]);

    private static ProductDesktopHostReadOnlyProjection Container(
        string id,
        double x,
        double y,
        bool isLocked = false) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            id,
            itemNames: [],
            "#2457D6",
            0.8,
            isCollapsed: false,
            x,
            y,
            widthDip: 200,
            heightDip: 160,
            isLocked);
}
