namespace LongGrid.Core.DesktopItems;

public static class DesktopInventoryComparison
{
    public static DesktopInventoryComparisonResult Compare(
        IEnumerable<string> physicalPaths,
        IEnumerable<string> shellFileSystemPaths)
    {
        ArgumentNullException.ThrowIfNull(physicalPaths);
        ArgumentNullException.ThrowIfNull(shellFileSystemPaths);

        HashSet<string> physical = Normalize(physicalPaths);
        HashSet<string> shell = Normalize(shellFileSystemPaths);

        string[] matched = physical
            .Intersect(shell, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] physicalOnly = physical
            .Except(shell, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] shellOnly = shell
            .Except(physical, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DesktopInventoryComparisonResult(
            matched,
            physicalOnly,
            shellOnly);
    }

    private static HashSet<string> Normalize(IEnumerable<string> paths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            normalized.Add(Path.GetFullPath(path));
        }

        return normalized;
    }
}
