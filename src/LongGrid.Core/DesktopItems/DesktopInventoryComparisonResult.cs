namespace LongGrid.Core.DesktopItems;

public sealed record DesktopInventoryComparisonResult(
    IReadOnlyList<string> MatchedPaths,
    IReadOnlyList<string> PhysicalOnlyPaths,
    IReadOnlyList<string> ShellOnlyPaths);
