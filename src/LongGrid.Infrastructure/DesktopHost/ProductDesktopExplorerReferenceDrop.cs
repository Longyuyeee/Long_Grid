using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopExplorerReferenceDropStatus
{
    Accepted,
    UnsupportedData,
    Empty,
    TooManyItems,
    DuplicatePath,
    MissingPath,
    NotInAuthoritativeCatalog,
    InvalidTarget,
    LockedTarget,
}

public sealed record ProductDesktopExplorerReferenceDropPreparation(
    ProductDesktopExplorerReferenceDropStatus Status,
    string? ContainerId,
    IReadOnlyList<string> CanonicalPaths,
    ProductWorkspaceResolvedReferenceBatchCommitRequest? CommitRequest)
{
    public bool IsAccepted =>
        Status == ProductDesktopExplorerReferenceDropStatus.Accepted
        && CommitRequest is not null;
}

public static class ProductDesktopExplorerReferenceDropAdapter
{
    public const int MaximumItemCount = 256;
    private const short ClipboardFormatHDrop = 15;

    public static ProductDesktopExplorerReferenceDropPreparation Prepare(
        object dataObject,
        ProductWorkspaceState state,
        long expectedEditRevision,
        long catalogGeneration,
        IReadOnlyList<DesktopCatalogEntry> authoritativeCatalog,
        string targetContainerId)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(authoritativeCatalog);

        ProductDesktopExplorerReferenceDropStatus extractionStatus =
            TryExtractCanonicalPaths(dataObject, out string[] paths);
        if (extractionStatus != ProductDesktopExplorerReferenceDropStatus.Accepted)
        {
            return Failure(extractionStatus, paths);
        }

        if (catalogGeneration <= 0
            || string.IsNullOrWhiteSpace(targetContainerId))
        {
            return Failure(
                ProductDesktopExplorerReferenceDropStatus.InvalidTarget,
                paths);
        }

        int containerIndex = state.Containers.FindIndex(container =>
            string.Equals(container.Id, targetContainerId,
                StringComparison.Ordinal));
        if (containerIndex < 0)
        {
            return Failure(
                ProductDesktopExplorerReferenceDropStatus.InvalidTarget,
                paths);
        }
        if (state.Containers[containerIndex].IsLocked)
        {
            return Failure(
                ProductDesktopExplorerReferenceDropStatus.LockedTarget,
                paths,
                targetContainerId);
        }

        var indexesByPath = authoritativeCatalog
            .Select((entry, index) => (entry, index))
            .GroupBy(pair => pair.entry.Identity.CanonicalTarget,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().index,
                StringComparer.OrdinalIgnoreCase);
        if (paths.Any(path => !indexesByPath.ContainsKey(path)))
        {
            return Failure(
                ProductDesktopExplorerReferenceDropStatus
                    .NotInAuthoritativeCatalog,
                paths,
                targetContainerId);
        }

        int[] catalogIndexes = paths.Select(path => indexesByPath[path]).ToArray();
        return new(
            ProductDesktopExplorerReferenceDropStatus.Accepted,
            targetContainerId,
            Array.AsReadOnly(paths),
            new(
                expectedEditRevision,
                catalogGeneration,
                containerIndex + 1,
                Array.AsReadOnly(catalogIndexes)));
    }

    public static ProductDesktopExplorerReferenceDropStatus
        TryExtractCanonicalPaths(
            object dataObject,
            out string[] canonicalPaths)
    {
        canonicalPaths = [];
        if (dataObject is not IDataObject source)
        {
            return ProductDesktopExplorerReferenceDropStatus.UnsupportedData;
        }

        var format = new FORMATETC
        {
            cfFormat = ClipboardFormatHDrop,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            source.GetData(ref format, out medium);
            if (medium.tymed != TYMED.TYMED_HGLOBAL
                || medium.unionmember == nint.Zero)
            {
                return ProductDesktopExplorerReferenceDropStatus.UnsupportedData;
            }

            uint count = NativeMethods.DragQueryFile(
                medium.unionmember,
                uint.MaxValue,
                null,
                0);
            if (count == 0)
            {
                return ProductDesktopExplorerReferenceDropStatus.Empty;
            }
            if (count > MaximumItemCount)
            {
                return ProductDesktopExplorerReferenceDropStatus.TooManyItems;
            }

            var paths = new List<string>((int)count);
            for (uint index = 0; index < count; index++)
            {
                uint length = NativeMethods.DragQueryFile(
                    medium.unionmember,
                    index,
                    null,
                    0);
                var value = new char[checked((int)length + 1)];
                _ = NativeMethods.DragQueryFile(
                    medium.unionmember,
                    index,
                    value,
                    checked((uint)value.Length));
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(
                        new string(value, 0, checked((int)length)));
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                        or NotSupportedException
                        or PathTooLongException)
                {
                    return ProductDesktopExplorerReferenceDropStatus.MissingPath;
                }
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    return ProductDesktopExplorerReferenceDropStatus.MissingPath;
                }
                paths.Add(fullPath);
            }

            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != paths.Count)
            {
                return ProductDesktopExplorerReferenceDropStatus.DuplicatePath;
            }
            canonicalPaths = paths.ToArray();
            return ProductDesktopExplorerReferenceDropStatus.Accepted;
        }
        catch (COMException)
        {
            return ProductDesktopExplorerReferenceDropStatus.UnsupportedData;
        }
        finally
        {
            if (medium.tymed != TYMED.TYMED_NULL)
            {
                NativeMethods.ReleaseStgMedium(ref medium);
            }
        }
    }

    private static ProductDesktopExplorerReferenceDropPreparation Failure(
        ProductDesktopExplorerReferenceDropStatus status,
        IReadOnlyList<string> paths,
        string? containerId = null) => new(
            status,
            containerId,
            Array.AsReadOnly(paths.ToArray()),
            null);

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint DragQueryFile(
            nint drop,
            uint fileIndex,
            [Out] char[]? fileName,
            uint characterCount);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    }
}

internal static class ProductContainerStateListExtensions
{
    internal static int FindIndex(
        this IReadOnlyList<ProductContainerState> containers,
        Func<ProductContainerState, bool> predicate)
    {
        for (int index = 0; index < containers.Count; index++)
        {
            if (predicate(containers[index]))
            {
                return index;
            }
        }
        return -1;
    }
}
