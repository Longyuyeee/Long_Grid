namespace LongGrid.Core.DesktopHost;

public enum DesktopHostWindowModel
{
    PerContainer,
    PerDisplay,
}

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);

    public bool HasArea => Width > 0 && Height > 0;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return new PixelRect(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    public PixelRect OffsetBy(int horizontal, int vertical) =>
        new(
            checked(Left + horizontal),
            checked(Top + vertical),
            Width,
            Height);
}

public sealed record DesktopDisplayPlacement(string DisplayId, PixelRect Bounds);

public sealed record DesktopContainerPlacement(
    string ContainerId,
    string DisplayId,
    PixelRect Bounds);

public sealed record DesktopHostSurfacePlan(
    string DisplayId,
    PixelRect WindowBounds,
    IReadOnlyList<PixelRect> InteractiveRegions);

public static class DesktopHostWindowPlanner
{
    public static IReadOnlyList<DesktopHostSurfacePlan> Create(
        DesktopHostWindowModel model,
        IEnumerable<DesktopDisplayPlacement> displays,
        IEnumerable<DesktopContainerPlacement> containers)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(containers);

        DesktopDisplayPlacement[] displayArray = displays.ToArray();
        DesktopContainerPlacement[] containerArray = containers.ToArray();
        Validate(displayArray, containerArray);

        return model switch
        {
            DesktopHostWindowModel.PerContainer =>
                CreatePerContainer(displayArray, containerArray),
            DesktopHostWindowModel.PerDisplay =>
                CreatePerDisplay(displayArray, containerArray),
            _ => throw new ArgumentOutOfRangeException(nameof(model)),
        };
    }

    private static List<DesktopHostSurfacePlan> CreatePerContainer(
        IReadOnlyList<DesktopDisplayPlacement> displays,
        IEnumerable<DesktopContainerPlacement> containers)
    {
        IReadOnlyDictionary<string, DesktopDisplayPlacement> displayById =
            displays.ToDictionary(display => display.DisplayId, StringComparer.Ordinal);
        var result = new List<DesktopHostSurfacePlan>();

        foreach (DesktopContainerPlacement container in containers)
        {
            PixelRect visibleBounds =
                container.Bounds.Intersect(displayById[container.DisplayId].Bounds);
            if (!visibleBounds.HasArea)
            {
                continue;
            }

            result.Add(new DesktopHostSurfacePlan(
                container.DisplayId,
                visibleBounds,
                [new PixelRect(0, 0, visibleBounds.Width, visibleBounds.Height)]));
        }

        return result;
    }

    private static List<DesktopHostSurfacePlan> CreatePerDisplay(
        IEnumerable<DesktopDisplayPlacement> displays,
        IReadOnlyList<DesktopContainerPlacement> containers)
    {
        var result = new List<DesktopHostSurfacePlan>();

        foreach (DesktopDisplayPlacement display in displays)
        {
            PixelRect[] regions = containers
                .Where(container =>
                    string.Equals(
                        container.DisplayId,
                        display.DisplayId,
                        StringComparison.Ordinal))
                .Select(container => container.Bounds.Intersect(display.Bounds))
                .Where(bounds => bounds.HasArea)
                .Select(bounds =>
                    bounds.OffsetBy(-display.Bounds.Left, -display.Bounds.Top))
                .ToArray();
            if (regions.Length == 0)
            {
                continue;
            }

            result.Add(new DesktopHostSurfacePlan(
                display.DisplayId,
                display.Bounds,
                regions));
        }

        return result;
    }

    private static void Validate(
        IReadOnlyList<DesktopDisplayPlacement> displays,
        IReadOnlyList<DesktopContainerPlacement> containers)
    {
        if (displays.Count == 0)
        {
            throw new ArgumentException("At least one display is required.", nameof(displays));
        }

        if (displays.Any(display =>
            string.IsNullOrWhiteSpace(display.DisplayId)
            || !display.Bounds.HasArea))
        {
            throw new ArgumentException(
                "Every display requires an ID and non-empty bounds.",
                nameof(displays));
        }

        if (displays
            .Select(display => display.DisplayId)
            .Distinct(StringComparer.Ordinal)
            .Count() != displays.Count)
        {
            throw new ArgumentException("Display IDs must be unique.", nameof(displays));
        }

        HashSet<string> displayIds = displays
            .Select(display => display.DisplayId)
            .ToHashSet(StringComparer.Ordinal);
        if (containers.Any(container =>
            string.IsNullOrWhiteSpace(container.ContainerId)
            || string.IsNullOrWhiteSpace(container.DisplayId)
            || !container.Bounds.HasArea
            || !displayIds.Contains(container.DisplayId)))
        {
            throw new ArgumentException(
                "Every container requires valid bounds and a known display.",
                nameof(containers));
        }
    }
}
