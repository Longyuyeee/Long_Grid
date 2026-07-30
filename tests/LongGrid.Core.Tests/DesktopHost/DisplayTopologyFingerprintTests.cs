using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class DisplayTopologyFingerprintTests
{
    private static readonly DisplayTopologyNode Primary =
        new(
            "display-a",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            96,
            DisplayRotation.Landscape,
            true);

    private static readonly DisplayTopologyNode Secondary =
        new(
            "display-b",
            new PixelRect(-1280, 120, 1280, 1024),
            new PixelRect(-1280, 120, 1280, 984),
            144,
            DisplayRotation.Portrait,
            false);

    [Fact]
    public void ComputeIsIndependentOfEnumerationOrder()
    {
        string first = DisplayTopologyFingerprint.Compute([Primary, Secondary]);
        string second = DisplayTopologyFingerprint.Compute([Secondary, Primary]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeIsIndependentOfVirtualDesktopTranslation()
    {
        DisplayTopologyNode[] translated =
        [
            Translate(Primary, 400, -250),
            Translate(Secondary, 400, -250),
        ];

        Assert.Equal(
            DisplayTopologyFingerprint.Compute([Primary, Secondary]),
            DisplayTopologyFingerprint.Compute(translated));
    }

    [Theory]
    [InlineData(120u, DisplayRotation.Landscape)]
    [InlineData(96u, DisplayRotation.Portrait)]
    public void ComputeChangesWhenDpiOrRotationChanges(
        uint dpi,
        DisplayRotation rotation)
    {
        DisplayTopologyNode changed = Primary with
        {
            EffectiveDpi = dpi,
            Rotation = rotation,
        };

        Assert.NotEqual(
            DisplayTopologyFingerprint.Compute([Primary, Secondary]),
            DisplayTopologyFingerprint.Compute([changed, Secondary]));
    }

    [Fact]
    public void ComputeChangesWhenRelativeTopologyChanges()
    {
        DisplayTopologyNode moved = Secondary with
        {
            Bounds = Secondary.Bounds.OffsetBy(300, 0),
            WorkArea = Secondary.WorkArea.OffsetBy(300, 0),
        };

        Assert.NotEqual(
            DisplayTopologyFingerprint.Compute([Primary, Secondary]),
            DisplayTopologyFingerprint.Compute([Primary, moved]));
    }

    [Fact]
    public void ComputeRejectsDuplicateStableIds()
    {
        DisplayTopologyNode duplicate = Secondary with
        {
            StableId = Primary.StableId,
        };

        Assert.Throws<ArgumentException>(
            () => DisplayTopologyFingerprint.Compute([Primary, duplicate]));
    }

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
