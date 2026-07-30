namespace LongGrid.Core.DesktopHost;

public readonly record struct DipRect(int Left, int Top, int Width, int Height)
{
    public bool HasArea => Width > 0 && Height > 0;
}

public sealed record SavedContainerLayout(
    string ContainerId,
    string DisplayId,
    DipRect Bounds);

public enum DisplayMatchKind
{
    ExactIdentity,
    SimilarGeometry,
}

public enum LayoutRecoveryStatus
{
    Automatic,
    ReviewRequired,
    Blocked,
}

public sealed record DisplayRecoveryMapping(
    string SavedDisplayId,
    string CurrentDisplayId,
    DisplayMatchKind MatchKind);

public sealed record ContainerRecoveryPlacement(
    string ContainerId,
    string SavedDisplayId,
    string CurrentDisplayId,
    PixelRect RequestedBounds,
    PixelRect ProposedBounds,
    bool WasVisibilityCorrected);

public sealed record LayoutRecoveryPlan(
    LayoutRecoveryStatus Status,
    IReadOnlyList<DisplayRecoveryMapping> DisplayMappings,
    IReadOnlyList<string> UnresolvedSavedDisplayIds,
    IReadOnlyList<ContainerRecoveryPlacement> ContainerPlacements)
{
    public bool CanApplyAutomatically =>
        Status == LayoutRecoveryStatus.Automatic;
}

public static class LayoutRecoveryPlanner
{
    private const int DefaultMinimumVisibleDip = 48;

    public static LayoutRecoveryPlan Create(
        IEnumerable<DisplayTopologyNode> savedDisplays,
        IEnumerable<DisplayTopologyNode> currentDisplays,
        IEnumerable<SavedContainerLayout> containers,
        int minimumVisibleDip = DefaultMinimumVisibleDip)
    {
        ArgumentNullException.ThrowIfNull(savedDisplays);
        ArgumentNullException.ThrowIfNull(currentDisplays);
        ArgumentNullException.ThrowIfNull(containers);

        DisplayTopologyNode[] saved = savedDisplays.ToArray();
        DisplayTopologyNode[] current = currentDisplays.ToArray();
        SavedContainerLayout[] containerArray = containers.ToArray();
        string savedFingerprint = DisplayTopologyFingerprint.Compute(saved);
        string currentFingerprint = DisplayTopologyFingerprint.Compute(current);
        ValidateContainers(saved, containerArray, minimumVisibleDip);

        var mappings = new List<DisplayRecoveryMapping>();
        var currentById = current.ToDictionary(
            display => display.StableId,
            StringComparer.Ordinal);
        var unmatchedSaved = new List<DisplayTopologyNode>();
        var unmatchedCurrent = current.ToList();

        foreach (DisplayTopologyNode display in saved)
        {
            if (currentById.TryGetValue(
                display.StableId,
                out DisplayTopologyNode? exact))
            {
                mappings.Add(
                    new DisplayRecoveryMapping(
                        display.StableId,
                        exact.StableId,
                        DisplayMatchKind.ExactIdentity));
                unmatchedCurrent.Remove(exact);
            }
            else
            {
                unmatchedSaved.Add(display);
            }
        }

        MatchMutuallyUniqueDisplays(
            unmatchedSaved,
            unmatchedCurrent,
            mappings);

        string[] unresolved = unmatchedSaved
            .Select(display => display.StableId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, DisplayRecoveryMapping> mappingBySavedId =
            mappings.ToDictionary(
                mapping => mapping.SavedDisplayId,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, DisplayTopologyNode> currentByStableId =
            current.ToDictionary(
                display => display.StableId,
                StringComparer.Ordinal);
        ContainerRecoveryPlacement[] placements = containerArray
            .Where(container => mappingBySavedId.ContainsKey(container.DisplayId))
            .Select(container => CreatePlacement(
                container,
                mappingBySavedId[container.DisplayId],
                currentByStableId[
                    mappingBySavedId[container.DisplayId].CurrentDisplayId],
                minimumVisibleDip))
            .OrderBy(placement => placement.ContainerId, StringComparer.Ordinal)
            .ToArray();

        LayoutRecoveryStatus status;
        if (unresolved.Length > 0)
        {
            status = LayoutRecoveryStatus.Blocked;
        }
        else if (string.Equals(
            savedFingerprint,
            currentFingerprint,
            StringComparison.Ordinal)
            && mappings.All(mapping =>
                mapping.MatchKind == DisplayMatchKind.ExactIdentity)
            && placements.All(placement =>
                !placement.WasVisibilityCorrected))
        {
            status = LayoutRecoveryStatus.Automatic;
        }
        else
        {
            status = LayoutRecoveryStatus.ReviewRequired;
        }

        return new LayoutRecoveryPlan(
            status,
            mappings
                .OrderBy(mapping => mapping.SavedDisplayId, StringComparer.Ordinal)
                .ToArray(),
            unresolved,
            placements);
    }

    private static void MatchMutuallyUniqueDisplays(
        List<DisplayTopologyNode> unmatchedSaved,
        List<DisplayTopologyNode> unmatchedCurrent,
        List<DisplayRecoveryMapping> mappings)
    {
        while (unmatchedSaved.Count > 0 && unmatchedCurrent.Count > 0)
        {
            var candidates = new List<DisplayPair>();
            foreach (DisplayTopologyNode saved in unmatchedSaved)
            {
                long bestScore = unmatchedCurrent.Min(current =>
                    Score(saved, current));
                DisplayTopologyNode[] bestTargets = unmatchedCurrent
                    .Where(current => Score(saved, current) == bestScore)
                    .ToArray();
                if (bestTargets.Length == 1)
                {
                    candidates.Add(
                        new DisplayPair(saved, bestTargets[0]));
                }
            }

            DisplayPair[] mutual = candidates
                .Where(candidate =>
                {
                    long reverseBest = unmatchedSaved.Min(saved =>
                        Score(saved, candidate.Current));
                    DisplayTopologyNode[] reverseSources = unmatchedSaved
                        .Where(saved =>
                            Score(saved, candidate.Current) == reverseBest)
                        .ToArray();
                    return reverseSources.Length == 1
                        && ReferenceEquals(
                            reverseSources[0],
                            candidate.Saved);
                })
                .ToArray();
            if (mutual.Length == 0)
            {
                return;
            }

            foreach (DisplayPair pair in mutual)
            {
                mappings.Add(
                    new DisplayRecoveryMapping(
                        pair.Saved.StableId,
                        pair.Current.StableId,
                        DisplayMatchKind.SimilarGeometry));
                unmatchedSaved.Remove(pair.Saved);
                unmatchedCurrent.Remove(pair.Current);
            }
        }
    }

    private static long Score(
        DisplayTopologyNode saved,
        DisplayTopologyNode current)
    {
        long savedWidth = ToDip(saved.WorkArea.Width, saved.EffectiveDpi);
        long savedHeight = ToDip(saved.WorkArea.Height, saved.EffectiveDpi);
        long currentWidth = ToDip(
            current.WorkArea.Width,
            current.EffectiveDpi);
        long currentHeight = ToDip(
            current.WorkArea.Height,
            current.EffectiveDpi);
        long rotationPenalty = saved.Rotation == current.Rotation
            ? 0
            : IsSameOrientationFamily(saved.Rotation, current.Rotation)
                ? 250
                : 2_000;
        long primaryPenalty = saved.IsPrimary == current.IsPrimary
            ? 0
            : 500;

        return checked(
            Math.Abs(savedWidth - currentWidth)
            + Math.Abs(savedHeight - currentHeight)
            + rotationPenalty
            + primaryPenalty);
    }

    private static bool IsSameOrientationFamily(
        DisplayRotation first,
        DisplayRotation second) =>
        first != DisplayRotation.Unknown
        && second != DisplayRotation.Unknown
        && IsLandscape(first) == IsLandscape(second);

    private static bool IsLandscape(DisplayRotation rotation) =>
        rotation is DisplayRotation.Landscape
            or DisplayRotation.LandscapeFlipped;

    private static ContainerRecoveryPlacement CreatePlacement(
        SavedContainerLayout container,
        DisplayRecoveryMapping mapping,
        DisplayTopologyNode target,
        int minimumVisibleDip)
    {
        int left = checked(
            target.WorkArea.Left
            + ToPixels(container.Bounds.Left, target.EffectiveDpi));
        int top = checked(
            target.WorkArea.Top
            + ToPixels(container.Bounds.Top, target.EffectiveDpi));
        int width = Math.Max(
            1,
            ToPixels(container.Bounds.Width, target.EffectiveDpi));
        int height = Math.Max(
            1,
            ToPixels(container.Bounds.Height, target.EffectiveDpi));
        var requested = new PixelRect(left, top, width, height);
        PixelRect proposed = EnsureMinimumVisible(
            requested,
            target.WorkArea,
            Math.Max(
                1,
                ToPixels(minimumVisibleDip, target.EffectiveDpi)));

        return new ContainerRecoveryPlacement(
            container.ContainerId,
            container.DisplayId,
            mapping.CurrentDisplayId,
            requested,
            proposed,
            requested != proposed);
    }

    private static PixelRect EnsureMinimumVisible(
        PixelRect requested,
        PixelRect workArea,
        int minimumVisiblePixels)
    {
        int requiredWidth = Math.Min(
            Math.Min(requested.Width, minimumVisiblePixels),
            workArea.Width);
        int requiredHeight = Math.Min(
            Math.Min(requested.Height, minimumVisiblePixels),
            workArea.Height);
        int minimumLeft = checked(
            workArea.Left + requiredWidth - requested.Width);
        int maximumLeft = checked(workArea.Right - requiredWidth);
        int minimumTop = checked(
            workArea.Top + requiredHeight - requested.Height);
        int maximumTop = checked(workArea.Bottom - requiredHeight);

        return new PixelRect(
            Math.Clamp(requested.Left, minimumLeft, maximumLeft),
            Math.Clamp(requested.Top, minimumTop, maximumTop),
            requested.Width,
            requested.Height);
    }

    private static int ToPixels(int dip, uint dpi) =>
        checked((int)Math.Round(
            dip * (double)dpi / 96d,
            MidpointRounding.AwayFromZero));

    private static long ToDip(int pixels, uint dpi) =>
        checked((long)Math.Round(
            pixels * 96d / dpi,
            MidpointRounding.AwayFromZero));

    private static void ValidateContainers(
        IReadOnlyList<DisplayTopologyNode> savedDisplays,
        IReadOnlyList<SavedContainerLayout> containers,
        int minimumVisibleDip)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumVisibleDip);

        HashSet<string> savedIds = savedDisplays
            .Select(display => display.StableId)
            .ToHashSet(StringComparer.Ordinal);
        if (containers.Any(container =>
            string.IsNullOrWhiteSpace(container.ContainerId)
            || string.IsNullOrWhiteSpace(container.DisplayId)
            || !container.Bounds.HasArea
            || !savedIds.Contains(container.DisplayId)))
        {
            throw new ArgumentException(
                "Every container requires valid bounds and a saved display.",
                nameof(containers));
        }

        if (containers
            .Select(container => container.ContainerId)
            .Distinct(StringComparer.Ordinal)
            .Count() != containers.Count)
        {
            throw new ArgumentException(
                "Container IDs must be unique.",
                nameof(containers));
        }
    }

    private sealed record DisplayPair(
        DisplayTopologyNode Saved,
        DisplayTopologyNode Current);
}
