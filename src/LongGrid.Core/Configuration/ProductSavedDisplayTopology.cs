using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public static class ProductSavedDisplayTopology
{
    public static ProductWorkspaceState StampForSave(
        ProductWorkspaceState state,
        IReadOnlyList<DisplayTopologyNode>? displays,
        bool authoritative)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!authoritative || displays is null || displays.Count == 0)
        {
            return state;
        }

        return state with { SavedDisplayTopology = Capture(displays) };
    }

    public static IReadOnlyList<SavedDisplayConfiguration> Capture(
        IReadOnlyList<DisplayTopologyNode> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        return displays.Select(CaptureDisplay).ToArray();
    }

    public static IReadOnlyList<DisplayTopologyNode>? ToNodes(
        IReadOnlyList<SavedDisplayConfiguration>? displays) =>
        displays?.Select(ToNode).ToArray();

    private static SavedDisplayConfiguration CaptureDisplay(
        DisplayTopologyNode display) =>
        new()
        {
            StableId = display.StableId,
            Bounds = CaptureRect(display.Bounds),
            WorkArea = CaptureRect(display.WorkArea),
            EffectiveDpi = display.EffectiveDpi,
            Rotation = display.Rotation,
            IsPrimary = display.IsPrimary,
        };

    private static PixelRectConfiguration CaptureRect(PixelRect rect) =>
        new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
        };

    private static DisplayTopologyNode ToNode(SavedDisplayConfiguration display) =>
        new(
            display.StableId,
            ToRect(display.Bounds),
            ToRect(display.WorkArea),
            display.EffectiveDpi,
            display.Rotation,
            display.IsPrimary);

    private static PixelRect ToRect(PixelRectConfiguration rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}
