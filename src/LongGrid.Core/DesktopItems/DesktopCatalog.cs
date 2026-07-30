namespace LongGrid.Core.DesktopItems;

public static class DesktopCatalog
{
    public static IReadOnlyList<DesktopCatalogEntry> Build(
        IEnumerable<DesktopCatalogCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var entries = new Dictionary<string, DesktopCatalogEntry>(
            StringComparer.OrdinalIgnoreCase);

        foreach (DesktopCatalogCandidate candidate in candidates)
        {
            ValidateCandidate(candidate);

            string canonicalPath = Path.GetFullPath(candidate.Path);
            if (entries.ContainsKey(canonicalPath))
            {
                continue;
            }

            string displayName = Path.GetFileName(
                canonicalPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = canonicalPath;
            }

            entries.Add(
                canonicalPath,
                new DesktopCatalogEntry(
                    new DesktopItemIdentity(
                        Provider: "filesystem",
                        CanonicalTarget: canonicalPath),
                    candidate.SourceId,
                    displayName,
                    Classify(candidate)));
        }

        return entries.Values
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Identity.CanonicalTarget, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DesktopItemKind Classify(DesktopCatalogCandidate candidate)
    {
        if (candidate.IsDirectory)
        {
            return DesktopItemKind.Directory;
        }

        return Path.GetExtension(candidate.Path).ToLowerInvariant() switch
        {
            ".lnk" => DesktopItemKind.Shortcut,
            ".url" => DesktopItemKind.InternetShortcut,
            _ => DesktopItemKind.File,
        };
    }

    private static void ValidateCandidate(DesktopCatalogCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Path);
    }
}
