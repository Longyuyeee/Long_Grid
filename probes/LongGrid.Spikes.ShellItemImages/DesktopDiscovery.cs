using LongGrid.Core.DesktopItems;

internal static class DesktopDiscovery
{
    public static IReadOnlyList<DesktopCatalogEntry> EnumeratePhysical()
    {
        var candidates = new List<DesktopCatalogCandidate>();
        var sources = new[]
        {
            ("user-desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("public-desktop", Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
        };
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        foreach ((string sourceId, string path) in sources)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            foreach (string itemPath in Directory.EnumerateFileSystemEntries(
                path,
                "*",
                enumerationOptions))
            {
                try
                {
                    candidates.Add(new DesktopCatalogCandidate(
                        sourceId,
                        itemPath,
                        File.GetAttributes(itemPath).HasFlag(FileAttributes.Directory)));
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
                {
                    // An inaccessible item is omitted from this read-only snapshot.
                }
            }
        }

        return DesktopCatalog.Build(candidates);
    }
}
