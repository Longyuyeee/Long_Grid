using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class LayoutRecoveryPlannerTests
{
    private static readonly DisplayTopologyNode Primary = Display(
        "primary",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        true);

    private static readonly DisplayTopologyNode Secondary = Display(
        "secondary",
        new PixelRect(-1280, 100, 1280, 1024),
        new PixelRect(-1280, 100, 1280, 984),
        144,
        DisplayRotation.Portrait,
        false);

    [Fact]
    public void EquivalentTopologyCanApplyAutomatically()
    {
        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary, Secondary],
            [Secondary, Primary],
            [new SavedContainerLayout(
                "one",
                "primary",
                new DipRect(100, 80, 300, 200))]);

        Assert.Equal(LayoutRecoveryStatus.Automatic, plan.Status);
        Assert.True(plan.CanApplyAutomatically);
        Assert.All(
            plan.DisplayMappings,
            mapping => Assert.Equal(
                DisplayMatchKind.ExactIdentity,
                mapping.MatchKind));
    }

    [Fact]
    public void VirtualDesktopTranslationRemainsAutomatic()
    {
        DisplayTopologyNode[] translated =
        [
            Translate(Primary, 500, -300),
            Translate(Secondary, 500, -300),
        ];

        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary, Secondary],
            translated,
            []);

        Assert.Equal(LayoutRecoveryStatus.Automatic, plan.Status);
    }

    [Fact]
    public void DpiChangeRequiresReviewEvenWithExactIdentity()
    {
        DisplayTopologyNode changed = Primary with
        {
            EffectiveDpi = 192,
        };

        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary],
            [changed],
            []);

        Assert.Equal(LayoutRecoveryStatus.ReviewRequired, plan.Status);
        Assert.False(plan.CanApplyAutomatically);
        Assert.Equal(
            DisplayMatchKind.ExactIdentity,
            Assert.Single(plan.DisplayMappings).MatchKind);
    }

    [Fact]
    public void UniqueSimilarDisplayRequiresReview()
    {
        DisplayTopologyNode replacement = Primary with
        {
            StableId = "replacement",
        };

        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary],
            [replacement],
            []);

        Assert.Equal(LayoutRecoveryStatus.ReviewRequired, plan.Status);
        Assert.Equal(
            DisplayMatchKind.SimilarGeometry,
            Assert.Single(plan.DisplayMappings).MatchKind);
    }

    [Fact]
    public void AmbiguousSimilarDisplaysBlockRecovery()
    {
        DisplayTopologyNode savedLeft = Secondary with
        {
            StableId = "saved-left",
        };
        DisplayTopologyNode savedRight = Secondary with
        {
            StableId = "saved-right",
            Bounds = Secondary.Bounds.OffsetBy(2560, 0),
            WorkArea = Secondary.WorkArea.OffsetBy(2560, 0),
        };
        DisplayTopologyNode currentLeft = savedLeft with
        {
            StableId = "current-left",
        };
        DisplayTopologyNode currentRight = savedRight with
        {
            StableId = "current-right",
        };

        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary, savedLeft, savedRight],
            [Primary, currentLeft, currentRight],
            []);

        Assert.Equal(LayoutRecoveryStatus.Blocked, plan.Status);
        Assert.Equal(
            ["saved-left", "saved-right"],
            plan.UnresolvedSavedDisplayIds);
    }

    [Fact]
    public void MissingDisplayBlocksRecovery()
    {
        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary, Secondary],
            [Primary],
            [new SavedContainerLayout(
                "missing",
                "secondary",
                new DipRect(0, 0, 200, 100))]);

        Assert.Equal(LayoutRecoveryStatus.Blocked, plan.Status);
        Assert.Equal(
            ["secondary"],
            plan.UnresolvedSavedDisplayIds);
        Assert.Empty(plan.ContainerPlacements);
    }

    [Fact]
    public void PlacementScalesDipToTargetDpi()
    {
        DisplayTopologyNode target = Primary with
        {
            EffectiveDpi = 192,
            Bounds = new PixelRect(100, 200, 3840, 2160),
            WorkArea = new PixelRect(100, 200, 3840, 2080),
        };

        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary],
            [target],
            [new SavedContainerLayout(
                "scaled",
                "primary",
                new DipRect(10, 20, 300, 200))]);

        Assert.Equal(
            new PixelRect(120, 240, 600, 400),
            Assert.Single(plan.ContainerPlacements).RequestedBounds);
        Assert.Equal(LayoutRecoveryStatus.ReviewRequired, plan.Status);
    }

    [Fact]
    public void VisibilityCorrectionUsesMinimumMovement()
    {
        LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
            [Primary],
            [Primary],
            [new SavedContainerLayout(
                "offscreen",
                "primary",
                new DipRect(1900, 1020, 300, 200))]);

        ContainerRecoveryPlacement placement =
            Assert.Single(plan.ContainerPlacements);

        Assert.Equal(
            new PixelRect(1900, 1020, 300, 200),
            placement.RequestedBounds);
        Assert.Equal(
            new PixelRect(1872, 992, 300, 200),
            placement.ProposedBounds);
        Assert.True(placement.WasVisibilityCorrected);
        Assert.Equal(LayoutRecoveryStatus.ReviewRequired, plan.Status);
        Assert.False(plan.CanApplyAutomatically);
    }

    [Fact]
    public void UnknownSavedDisplayIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => LayoutRecoveryPlanner.Create(
                [Primary],
                [Primary],
                [new SavedContainerLayout(
                    "orphan",
                    "unknown",
                    new DipRect(0, 0, 100, 100))]));
    }

    private static DisplayTopologyNode Display(
        string id,
        PixelRect bounds,
        PixelRect workArea,
        uint dpi,
        DisplayRotation rotation,
        bool primary) =>
        new(id, bounds, workArea, dpi, rotation, primary);

    private static DisplayTopologyNode Translate(
        DisplayTopologyNode display,
        int horizontal,
        int vertical) =>
        display with
        {
            Bounds = display.Bounds.OffsetBy(horizontal, vertical),
            WorkArea = display.WorkArea.OffsetBy(horizontal, vertical),
        };
}
