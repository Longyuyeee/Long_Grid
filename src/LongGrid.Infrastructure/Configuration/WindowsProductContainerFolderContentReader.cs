using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public static class WindowsProductContainerFolderContentReader
{
    public const int MaximumProjectedEntries =
        ProductWorkspaceContainerFolderContent.MaximumItems;
    public const int MaximumExaminedEntries = 1024;

    public static ProductWorkspaceFolderContentSet ReadWorkspace(
        ProductWorkspaceState state,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);

        var contents = new Dictionary<
            string,
            ProductWorkspaceContainerFolderContent>(StringComparer.Ordinal);
        foreach (ProductContainerState container in state.Containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (container.FolderBinding is null)
            {
                continue;
            }

            contents[container.Id] = ReadContainer(
                container.Id,
                container.FolderBinding,
                generation,
                cancellationToken);
        }

        return new(generation, contents);
    }

    private static ProductWorkspaceContainerFolderContent ReadContainer(
        string containerId,
        ProductContainerFolderBindingState binding,
        long generation,
        CancellationToken cancellationToken)
    {
        ProductContainerFolderBindingState resolved =
            WindowsProductContainerFolderBinding.Resolve(binding);
        if (!Enum.IsDefined(resolved.SortMode))
        {
            return Failure(
                containerId,
                generation,
                ProductWorkspaceFolderContentStatus.EnumerationFailed,
                ProductContainerFolderBindingResolution.Unavailable);
        }
        if (resolved.Resolution !=
            ProductContainerFolderBindingResolution.Resolved
            || string.IsNullOrWhiteSpace(resolved.ResolvedTarget))
        {
            return Failure(
                containerId,
                generation,
                MapBindingFailure(resolved.Resolution),
                resolved.Resolution);
        }

        string root = resolved.ResolvedTarget;
        var entries = new List<(string Target, string Name,
            ConfigurationItemKind Kind, string TypeKey, long LastWriteUtcTicks)>(
                MaximumProjectedEntries + 1);
        int skippedReparsePoints = 0;
        int examined = 0;
        bool truncated = false;
        try
        {
            foreach (string target in Directory.EnumerateFileSystemEntries(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++examined > MaximumExaminedEntries)
                {
                    truncated = true;
                    break;
                }

                FileAttributes attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedReparsePoints++;
                    continue;
                }

                string name = Path.GetFileName(target);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                ConfigurationItemKind kind = MapKind(name, attributes);
                entries.Add((
                    Path.GetFullPath(target),
                    name,
                    kind,
                    kind == ConfigurationItemKind.Folder
                        ? string.Empty
                        : Path.GetExtension(name),
                    File.GetLastWriteTimeUtc(target).Ticks));
                if (entries.Count > MaximumProjectedEntries)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                containerId,
                generation,
                ProductWorkspaceFolderContentStatus.AccessDenied,
                ProductContainerFolderBindingResolution.AccessDenied);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException
                or PathTooLongException)
        {
            return Failure(
                containerId,
                generation,
                ProductWorkspaceFolderContentStatus.EnumerationFailed,
                ProductContainerFolderBindingResolution.Unavailable);
        }

        IEnumerable<(string Target, string Name, ConfigurationItemKind Kind,
            string TypeKey, long LastWriteUtcTicks)> sorted =
            resolved.SortMode switch
            {
                ProductContainerFolderSortMode.FoldersFirstNameAscending => entries
                    .OrderBy(entry =>
                        entry.Kind == ConfigurationItemKind.Folder ? 0 : 1)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.NameAscending => entries
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.NameDescending => entries
                    .OrderByDescending(
                        entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.TypeAscending => entries
                    .OrderBy(FolderRank)
                    .ThenBy(entry => entry.TypeKey,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.TypeKey, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.TypeDescending => entries
                    .OrderBy(FolderRank)
                    .ThenByDescending(entry => entry.TypeKey,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(entry => entry.TypeKey,
                        StringComparer.Ordinal)
                    .ThenBy(entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.ModifiedNewestFirst => entries
                    .OrderBy(FolderRank)
                    .ThenByDescending(entry => entry.LastWriteUtcTicks)
                    .ThenBy(entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                ProductContainerFolderSortMode.ModifiedOldestFirst => entries
                    .OrderBy(FolderRank)
                    .ThenBy(entry => entry.LastWriteUtcTicks)
                    .ThenBy(entry => entry.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal),
                _ => throw new InvalidOperationException(
                    "Folder content sort mode was validated before enumeration."),
            };
        var ordered = sorted
            .Take(MaximumProjectedEntries)
            .Select((entry, index) => new ProductWorkspaceFolderContentItem(
                $"folder:{generation}:{index + 1}",
                entry.Name,
                entry.Kind,
                entry.Target))
            .ToArray();
        ProductWorkspaceFolderContentStatus status = truncated
            ? ProductWorkspaceFolderContentStatus.Truncated
            : skippedReparsePoints > 0
                ? ProductWorkspaceFolderContentStatus.ReadyWithSkippedEntries
                : ordered.Length == 0
                    ? ProductWorkspaceFolderContentStatus.Empty
                    : ProductWorkspaceFolderContentStatus.Ready;
        return new(
            containerId,
            generation,
            status,
            ordered,
            skippedReparsePoints,
            ProductContainerFolderBindingResolution.Resolved);
    }

    private static int FolderRank((string Target, string Name,
        ConfigurationItemKind Kind, string TypeKey, long LastWriteUtcTicks) entry) =>
        entry.Kind == ConfigurationItemKind.Folder ? 0 : 1;

    private static ProductWorkspaceFolderContentStatus MapBindingFailure(
        ProductContainerFolderBindingResolution resolution) => resolution switch
        {
            ProductContainerFolderBindingResolution.AccessDenied =>
                ProductWorkspaceFolderContentStatus.AccessDenied,
            ProductContainerFolderBindingResolution.InvalidTarget =>
                ProductWorkspaceFolderContentStatus.InvalidTarget,
            _ => ProductWorkspaceFolderContentStatus.BindingUnavailable,
        };

    private static ConfigurationItemKind MapKind(
        string name,
        FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            return ConfigurationItemKind.Folder;
        }

        string extension = Path.GetExtension(name);
        if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationItemKind.Shortcut;
        }
        return string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase)
            ? ConfigurationItemKind.Url
            : ConfigurationItemKind.File;
    }

    private static ProductWorkspaceContainerFolderContent Failure(
        string containerId,
        long generation,
        ProductWorkspaceFolderContentStatus status,
        ProductContainerFolderBindingResolution bindingResolution) => new(
            containerId,
            generation,
            status,
            Array.Empty<ProductWorkspaceFolderContentItem>(),
            BindingResolution: bindingResolution);
}
