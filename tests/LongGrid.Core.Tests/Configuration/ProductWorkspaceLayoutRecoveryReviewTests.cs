using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceLayoutRecoveryReviewTests
{
    private static readonly DisplayTopologyNode Saved = new(
        "display-a",
        new(0, 0, 1920, 1080),
        new(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        IsPrimary: true);

    [Fact]
    public void ReviewTokenBindsBothTopologiesConfigurationAndRevisions()
    {
        ProductWorkspaceState state = State();
        DisplayTopologyNode current = Saved with
        {
            StableId = "display-current",
            EffectiveDpi = 144,
        };

        ProductWorkspaceLayoutRecoveryReviewResult review =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state,
                [current],
                currentTopologyAuthoritative: true,
                topologyGeneration: 7,
                editRevision: 3);

        Assert.True(review.CanConfirm);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired,
            review.Preview.Status);
        ProductWorkspaceLayoutRecoveryReviewToken token = review.Token!;
        Assert.Equal(7, token.TopologyGeneration);
        Assert.Equal(3, token.EditRevision);
        Assert.Equal(64, token.SavedTopologyFingerprint.Length);
        Assert.Equal(64, token.CurrentTopologyFingerprint.Length);
        Assert.Equal(64, token.ConfigurationFingerprint.Length);
        Assert.NotEqual(
            token.SavedTopologyFingerprint,
            token.CurrentTopologyFingerprint);
        Assert.Equal(1, token.ContainerCount);
        Assert.Equal(1, token.DisplayMappingCount);
    }

    [Fact]
    public void ExplicitConfirmationProducesConfigurationOnlyRecoveryEdit()
    {
        ProductWorkspaceState state = State();
        DisplayTopologyNode current = Saved with
        {
            StableId = "display-current",
            EffectiveDpi = 144,
        };
        ProductWorkspaceLayoutRecoveryReviewToken token =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state,
                [current],
                true,
                7,
                3).Token!;

        ProductWorkspaceLayoutRecoveryConfirmationResult result =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state,
                [current],
                true,
                7,
                3,
                token,
                confirmed: true);

        Assert.True(result.IsAccepted);
        Assert.True(result.Edit!.Changed);
        ProductWorkspaceState recovered = result.Edit.State!;
        DisplayTopologyNode savedCurrent = Assert.Single(
            ProductSavedDisplayTopology.ToNodes(recovered.SavedDisplayTopology)!);
        Assert.Equal(144u, savedCurrent.EffectiveDpi);
        Assert.Equal("display-current", savedCurrent.StableId);
        Assert.Equal(
            "display-current",
            recovered.Containers[0].Placement.DisplayKey);
        Assert.Equal(32, recovered.Containers[0].Placement.XDip);
        Assert.Equal(48, recovered.Containers[0].Placement.YDip);
        Assert.True(ProductWorkspaceConfigurationProjector.Project(recovered).IsSuccess);
    }

    [Fact]
    public void CancellationAndStaleEvidenceNeverProduceAnEdit()
    {
        ProductWorkspaceState state = State();
        DisplayTopologyNode current = Saved with { EffectiveDpi = 144 };
        ProductWorkspaceLayoutRecoveryReviewToken token =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state,
                [current],
                true,
                7,
                3).Token!;

        ProductWorkspaceLayoutRecoveryConfirmationResult cancelled =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state, [current], true, 7, 3, token, confirmed: false);
        ProductWorkspaceLayoutRecoveryConfirmationResult topologyChanged =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state, [current], true, 8, 3, token, confirmed: true);
        ProductWorkspaceLayoutRecoveryConfirmationResult editChanged =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state, [current], true, 7, 4, token, confirmed: true);
        ProductWorkspaceLayoutRecoveryConfirmationResult tokenMismatch =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state with { ProfileId = "changed" },
                [current],
                true,
                7,
                3,
                token,
                confirmed: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.ConfirmationRequired,
            cancelled.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.TopologyGenerationChanged,
            topologyChanged.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.EditRevisionChanged,
            editChanged.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.TokenMismatch,
            tokenMismatch.Status);
        Assert.All(
            new[] { cancelled, topologyChanged, editChanged, tokenMismatch },
            result => Assert.Null(result.Edit));
    }

    [Fact]
    public void NonAuthoritativeBlockedAutomaticAndInvalidInputsCannotBeConfirmed()
    {
        ProductWorkspaceState state = State();
        DisplayTopologyNode current = Saved with { EffectiveDpi = 144 };
        ProductWorkspaceLayoutRecoveryReviewResult automatic =
            ProductWorkspaceLayoutRecoveryReview.Prepare(state, [Saved], true, 1, 0);
        ProductWorkspaceLayoutRecoveryReviewResult awaiting =
            ProductWorkspaceLayoutRecoveryReview.Prepare(state, null, false, 1, 0);
        ProductWorkspaceLayoutRecoveryReviewResult invalidGeneration =
            ProductWorkspaceLayoutRecoveryReview.Prepare(state, [current], true, 0, 0);
        ProductWorkspaceState missingSaved = state with { SavedDisplayTopology = null };
        ProductWorkspaceLayoutRecoveryReviewResult missing =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                missingSaved, [current], true, 1, 0);
        DisplayTopologyNode secondSaved = new(
            "display-b",
            new(1920, 0, 1920, 1080),
            new(1920, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            IsPrimary: false);
        ProductWorkspaceState blockedState = state with
        {
            SavedDisplayTopology = ProductSavedDisplayTopology.Capture(
                [Saved, secondSaved]),
        };
        ProductWorkspaceLayoutRecoveryReviewResult blocked =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                blockedState, [Saved], true, 1, 0);

        Assert.False(automatic.CanConfirm);
        Assert.False(awaiting.CanConfirm);
        Assert.False(invalidGeneration.CanConfirm);
        Assert.False(missing.CanConfirm);
        Assert.False(blocked.CanConfirm);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.Blocked,
            blocked.Preview.Status);

        ProductWorkspaceLayoutRecoveryReviewToken validToken =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state, [current], true, 1, 0).Token!;
        ProductWorkspaceLayoutRecoveryConfirmationResult unavailable =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state, null, false, 1, 0, validToken, true);
        ProductWorkspaceLayoutRecoveryConfirmationResult blockedConfirmation =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                blockedState, [Saved], true, 1, 0, validToken, true);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.PlanUnavailable,
            unavailable.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.PlanBlocked,
            blockedConfirmation.Status);
    }

    [Fact]
    public void LockedContainerRejectsAPlacementCorrection()
    {
        ProductWorkspaceState state = State() with
        {
            Containers =
            [
                State().Containers[0] with
                {
                    IsLocked = true,
                    Placement = State().Containers[0].Placement with
                    {
                        XDip = 999_999,
                    },
                },
            ],
        };
        ProductWorkspaceLayoutRecoveryReviewToken token =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                state, [Saved], true, 2, 0).Token!;

        ProductWorkspaceLayoutRecoveryConfirmationResult result =
            ProductWorkspaceLayoutRecoveryReview.Confirm(
                state, [Saved], true, 2, 0, token, true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.ContainerLocked,
            result.Status);
        Assert.Null(result.Edit);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        ProductWorkspaceLayoutRecoveryReviewToken token = new(
            1, 0, "saved", "current", "configuration", 1, 1, 0);
        Assert.Throws<ArgumentNullException>(
            () => ProductWorkspaceLayoutRecoveryReview.Confirm(
                null!, [Saved], true, 1, 0, token, true));
        Assert.Throws<ArgumentNullException>(
            () => ProductWorkspaceLayoutRecoveryReview.Confirm(
                State(), [Saved], true, 1, 0, null!, true));
    }

    private static ProductWorkspaceState State() =>
        new()
        {
            ProfileId = "default",
            SavedDisplayTopology = ProductSavedDisplayTopology.Capture([Saved]),
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };
}
