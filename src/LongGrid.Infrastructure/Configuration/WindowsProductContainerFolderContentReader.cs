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
            ConfigurationItemKind Kind)>(MaximumProjectedEntries + 1);
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

                entries.Add((
                    Path.GetFullPath(target),
                    name,
                    MapKind(name, attributes)));
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

        var ordered = entries
            .OrderBy(entry => entry.Kind == ConfigurationItemKind.Folder ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
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
