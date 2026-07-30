using System.Runtime.InteropServices;

internal static class NativeMethods
{
    [DllImport("shell32.dll", ExactSpelling = true)]
    internal static extern int SHGetDesktopFolder(
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder desktopFolder);

    [DllImport("shell32.dll", ExactSpelling = true)]
    internal static extern int SHGetNameFromIDList(
        nint itemIdList,
        ShellDisplayName displayName,
        out nint name);
}

[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(
        nint owner,
        nint bindContext,
        [MarshalAs(UnmanagedType.LPWStr)] string displayName,
        ref uint charactersEaten,
        out nint itemIdList,
        ref uint attributes);

    [PreserveSig]
    int EnumObjects(
        nint owner,
        ShellEnumerationFlags flags,
        [MarshalAs(UnmanagedType.Interface)] out IEnumIDList? enumerator);

    [PreserveSig]
    int BindToObject(nint itemIdList, nint bindContext, ref Guid interfaceId, out nint value);

    [PreserveSig]
    int BindToStorage(nint itemIdList, nint bindContext, ref Guid interfaceId, out nint value);

    [PreserveSig]
    int CompareIDs(nint parameter, nint firstItemIdList, nint secondItemIdList);

    [PreserveSig]
    int CreateViewObject(nint owner, ref Guid interfaceId, out nint value);

    [PreserveSig]
    int GetAttributesOf(
        uint itemCount,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] itemIdLists,
        ref ShellItemAttributes attributes);

    [PreserveSig]
    int GetUIObjectOf(
        nint owner,
        uint itemCount,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] itemIdLists,
        ref Guid interfaceId,
        nint reserved,
        out nint value);

    [PreserveSig]
    int GetDisplayNameOf(nint itemIdList, uint flags, nint name);

    [PreserveSig]
    int SetNameOf(
        nint owner,
        nint itemIdList,
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        uint flags,
        out nint newItemIdList);
}

[ComImport]
[Guid("000214F2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumIDList
{
    [PreserveSig]
    int Next(uint requested, out nint itemIdList, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumIDList enumerator);
}

[Flags]
internal enum ShellEnumerationFlags : uint
{
    Folders = 0x20,
    NonFolders = 0x40,
    IncludeHidden = 0x80,
    IncludeSuperHidden = 0x10000,
}

[Flags]
internal enum ShellItemAttributes : uint
{
    Link = 0x00010000,
    Hidden = 0x00080000,
    Folder = 0x20000000,
    FileSystem = 0x40000000,
}

internal enum ShellDisplayName : uint
{
    NormalDisplay = 0,
    FileSystemPath = 0x80058000,
}

internal enum HResult
{
    False = 1,
}
