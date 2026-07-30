using System.Runtime.InteropServices;

internal static class ShellDesktopEnumerator
{
    public static IReadOnlyList<ShellDesktopItem> Enumerate()
    {
        Marshal.ThrowExceptionForHR(NativeMethods.SHGetDesktopFolder(out IShellFolder desktop));
        IEnumIDList? enumerator = null;

        try
        {
            HResult enumerationResult = (HResult)desktop.EnumObjects(
                nint.Zero,
                ShellEnumerationFlags.Folders
                    | ShellEnumerationFlags.NonFolders
                    | ShellEnumerationFlags.IncludeHidden
                    | ShellEnumerationFlags.IncludeSuperHidden,
                out enumerator);

            if (enumerationResult == HResult.False || enumerator is null)
            {
                return [];
            }

            Marshal.ThrowExceptionForHR((int)enumerationResult);
            var items = new List<ShellDesktopItem>();

            while (true)
            {
                nint relativeItemIdList = nint.Zero;

                try
                {
                    HResult nextResult = (HResult)enumerator.Next(
                        1,
                        out relativeItemIdList,
                        out uint fetched);

                    if (nextResult == HResult.False || fetched == 0)
                    {
                        break;
                    }

                    Marshal.ThrowExceptionForHR((int)nextResult);
                    items.Add(CreateItem(desktop, relativeItemIdList));
                }
                finally
                {
                    if (relativeItemIdList != nint.Zero)
                    {
                        Marshal.FreeCoTaskMem(relativeItemIdList);
                    }
                }
            }

            return items
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            if (enumerator is not null)
            {
                _ = Marshal.FinalReleaseComObject(enumerator);
            }

            _ = Marshal.FinalReleaseComObject(desktop);
        }
    }

    private static ShellDesktopItem CreateItem(
        IShellFolder desktop,
        nint relativeItemIdList)
    {
        ShellItemAttributes attributes =
            ShellItemAttributes.FileSystem
            | ShellItemAttributes.Folder
            | ShellItemAttributes.Link
            | ShellItemAttributes.Hidden;
        nint[] itemIdLists = [relativeItemIdList];

        Marshal.ThrowExceptionForHR(
            desktop.GetAttributesOf(1, itemIdLists, ref attributes));

        string displayName = GetName(relativeItemIdList, ShellDisplayName.NormalDisplay)
            ?? "(unnamed Shell item)";
        string? fileSystemPath = attributes.HasFlag(ShellItemAttributes.FileSystem)
            ? GetName(relativeItemIdList, ShellDisplayName.FileSystemPath)
            : null;

        return new ShellDesktopItem(
            displayName,
            fileSystemPath,
            attributes.HasFlag(ShellItemAttributes.Folder),
            attributes.HasFlag(ShellItemAttributes.Link),
            attributes.HasFlag(ShellItemAttributes.Hidden));
    }

    private static string? GetName(nint absoluteItemIdList, ShellDisplayName displayName)
    {
        int result = NativeMethods.SHGetNameFromIDList(
            absoluteItemIdList,
            displayName,
            out nint namePointer);

        if (result < 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(namePointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePointer);
        }
    }
}

internal sealed record ShellDesktopItem(
    string DisplayName,
    string? FileSystemPath,
    bool IsFolder,
    bool IsLink,
    bool IsHidden);
