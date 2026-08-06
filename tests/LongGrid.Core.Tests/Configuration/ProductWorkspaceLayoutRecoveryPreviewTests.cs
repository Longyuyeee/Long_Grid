using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceLayoutRecoveryPreviewTests
{
    private static readonly DisplayTopologyNode Primary = new(
        "display-a",
        new(0, 0, 1920, 1080),
        new(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        IsPrimary: true);

    [Fact]
    public void MissingInputsRemainFiniteAndDoNotClaimWindowChanges()
    {
        ProductWorkspaceLayoutRecoveryPreviewResult unavailable =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                null,
                null,
                null,
                currentTopologyAuthoritative: false);
        ProductWorkspaceLayoutRecoveryPreviewResult awaiting =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                null,
                null,
                currentTopologyAuthoritative: false);
        ProductWorkspaceLayoutRecoveryPreviewResult missingSaved =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                null,
                [Primary],
                currentTopologyAuthoritative: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.UnavailableSession,
            unavailable.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.AwaitingAuthoritativeTopology,
            awaiting.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.SavedTopologyMissing,
            missingSaved.Status);
        Assert.False(unavailable.DesktopWindowsChanged);
        Assert.False(awaiting.DesktopWindowsChanged);
        Assert.False(missingSaved.DesktopWindowsChanged);
    }

    [Fact]
    public void ExactTopologyProducesAutomaticCountOnlyPreview()
    {
        ProductWorkspaceLayoutRecoveryPreviewResult result =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                [Primary],
                [Primary],
                currentTopologyAuthoritative: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.Automatic,
            result.Status);
        Assert.Equal(1, result.ContainerCount);
        Assert.Equal(1, result.DisplayMappingCount);
        Assert.Equal(0, result.UnresolvedDisplayCount);
        Assert.Equal(0, result.VisibilityCorrectionCount);
        Assert.False(result.DesktopWindowsChanged);
    }

    [Fact]
    public void DpiChangeRequiresReviewAndMissingDisplayBlocks()
    {
        DisplayTopologyNode dpiChanged = Primary with { EffectiveDpi = 144 };
        DisplayTopologyNode secondary = new(
            "display-b",
            new(1920, 0, 1920, 1080),
            new(1920, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            IsPrimary: false);

        ProductWorkspaceLayoutRecoveryPreviewResult review =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                [Primary],
                [dpiChanged],
                currentTopologyAuthoritative: true);
        ProductWorkspaceLayoutRecoveryPreviewResult blocked =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                [Primary, secondary],
                [Primary],
                currentTopologyAuthoritative: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired,
            review.Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.Blocked,
            blocked.Status);
        Assert.Equal(1, blocked.UnresolvedDisplayCount);
    }

    [Fact]
    public void InvalidStateReturnsFiniteFailure()
    {
        ProductWorkspaceState invalid = State() with { Containers = null! };

        ProductWorkspaceLayoutRecoveryPreviewResult result =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                invalid,
                [Primary],
                [Primary],
                currentTopologyAuthoritative: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
            result.Status);
        Assert.Equal(0, result.ContainerCount);
    }

    [Fact]
    public void InvalidAuthoritativeTopologyReturnsFiniteFailure()
    {
        DisplayTopologyNode invalid = Primary with { EffectiveDpi = 0 };

        ProductWorkspaceLayoutRecoveryPreviewResult result =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                State(),
                [Primary],
                [invalid],
                currentTopologyAuthoritative: true);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
            result.Status);
        Assert.Equal(1, result.ContainerCount);
        Assert.False(result.DesktopWindowsChanged);
    }

    private static ProductWorkspaceState State() =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new()
                    {
                        Color = "#2563EB",
                        Opacity = 0.88,
                    },
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
