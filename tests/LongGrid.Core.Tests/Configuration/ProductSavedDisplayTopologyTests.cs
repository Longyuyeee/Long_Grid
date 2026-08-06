using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductSavedDisplayTopologyTests
{
    [Fact]
    public void AuthoritativeNodesCaptureAndRestoreWithoutGeometryLoss()
    {
        DisplayTopologyNode[] nodes =
        [
            new(
                "display-left",
                new(-1920, 0, 1920, 1080),
                new(-1920, 0, 1920, 1040),
                120,
                DisplayRotation.PortraitFlipped,
                IsPrimary: false),
            new(
                "display-main",
                new(0, 0, 2560, 1440),
                new(0, 0, 2560, 1400),
                144,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ];

        IReadOnlyList<SavedDisplayConfiguration> saved =
            ProductSavedDisplayTopology.Capture(nodes);
        IReadOnlyList<DisplayTopologyNode>? restored =
            ProductSavedDisplayTopology.ToNodes(saved);

        Assert.Equal(nodes, restored);
        Assert.NotSame(nodes, restored);
        Assert.Null(ProductSavedDisplayTopology.ToNodes(null));
    }

    [Fact]
    public void NullCaptureIsRejectedBeforeEnumeration()
    {
        Assert.Throws<ArgumentNullException>(
            () => ProductSavedDisplayTopology.Capture(null!));
    }

    [Fact]
    public void SaveStampOnlyReplacesHistoryWithAuthoritativeNonEmptyEvidence()
    {
        SavedDisplayConfiguration existing = new()
        {
            StableId = "saved",
            Bounds = new() { Width = 1920, Height = 1080 },
            WorkArea = new() { Width = 1920, Height = 1040 },
            EffectiveDpi = 96,
            Rotation = DisplayRotation.Landscape,
            IsPrimary = true,
        };
        ProductWorkspaceState state = new()
        {
            ProfileId = "default",
            Containers = Array.Empty<ProductContainerState>(),
            SavedDisplayTopology = [existing],
        };
        DisplayTopologyNode current = new(
            "current",
            new(0, 0, 2560, 1440),
            new(0, 0, 2560, 1400),
            144,
            DisplayRotation.Landscape,
            IsPrimary: true);

        Assert.Same(
            state,
            ProductSavedDisplayTopology.StampForSave(
                state,
                [current],
                authoritative: false));
        Assert.Same(
            state,
            ProductSavedDisplayTopology.StampForSave(
                state,
                Array.Empty<DisplayTopologyNode>(),
                authoritative: true));
        Assert.Same(
            state,
            ProductSavedDisplayTopology.StampForSave(
                state,
                displays: null,
                authoritative: true));

        ProductWorkspaceState stamped = ProductSavedDisplayTopology.StampForSave(
            state,
            [current],
            authoritative: true);

        Assert.NotSame(state, stamped);
        Assert.Equal("current", Assert.Single(stamped.SavedDisplayTopology!).StableId);
        Assert.Equal(
            [current],
            ProductSavedDisplayTopology.ToNodes(stamped.SavedDisplayTopology));
        Assert.Throws<ArgumentNullException>(
            () => ProductSavedDisplayTopology.StampForSave(null!, [current], true));
    }
}
