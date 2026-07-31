using System.Runtime.InteropServices;

internal static class NativeMethods
{
    internal static readonly Guid ShellItemInterfaceId =
        new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);
}

[ComImport]
[Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
internal class FileOperationComObject
{
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig]
    int BindToHandler(
        nint bindContext,
        in Guid handlerId,
        in Guid interfaceId,
        out nint value);

    [PreserveSig]
    int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);

    [PreserveSig]
    int GetDisplayName(uint displayName, out nint name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
        uint hint,
        out int order);
}

[ComImport]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    [PreserveSig]
    int Advise(
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink sink,
        out uint cookie);

    [PreserveSig]
    int Unadvise(uint cookie);

    [PreserveSig]
    int SetOperationFlags(FileOperationFlags operationFlags);

    [PreserveSig]
    int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

    [PreserveSig]
    int SetProgressDialog(nint progressDialog);

    [PreserveSig]
    int SetProperties(nint propertyChangeArray);

    [PreserveSig]
    int SetOwnerWindow(nint ownerWindow);

    [PreserveSig]
    int ApplyPropertiesToItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem);

    [PreserveSig]
    int ApplyPropertiesToItems(nint shellItems);

    [PreserveSig]
    int RenameItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
        [MarshalAs(UnmanagedType.LPWStr)] string newName,
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? sink);

    [PreserveSig]
    int RenameItems(nint shellItems, [MarshalAs(UnmanagedType.LPWStr)] string newName);

    [PreserveSig]
    int MoveItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
        [MarshalAs(UnmanagedType.Interface)] IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? newName,
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? sink);

    [PreserveSig]
    int MoveItems(
        nint shellItems,
        [MarshalAs(UnmanagedType.Interface)] IShellItem destinationFolder);

    [PreserveSig]
    int CopyItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
        [MarshalAs(UnmanagedType.Interface)] IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? copyName,
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? sink);

    [PreserveSig]
    int CopyItems(
        nint shellItems,
        [MarshalAs(UnmanagedType.Interface)] IShellItem destinationFolder);

    [PreserveSig]
    int DeleteItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? sink);

    [PreserveSig]
    int DeleteItems(nint shellItems);

    [PreserveSig]
    int NewItem(
        [MarshalAs(UnmanagedType.Interface)] IShellItem destinationFolder,
        uint fileAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string? templateName,
        [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink? sink);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

[ComImport]
[Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int result);

    [PreserveSig]
    int PreRenameItem(
        uint flags,
        IShellItem item,
        [MarshalAs(UnmanagedType.LPWStr)] string newName);

    [PreserveSig]
    int PostRenameItem(
        uint flags,
        IShellItem item,
        [MarshalAs(UnmanagedType.LPWStr)] string newName,
        int result,
        IShellItem? newItem);

    [PreserveSig]
    int PreMoveItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? newName);

    [PreserveSig]
    int PostMoveItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? newName,
        int result,
        IShellItem? newItem);

    [PreserveSig]
    int PreCopyItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? newName);

    [PreserveSig]
    int PostCopyItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string? newName,
        int result,
        IShellItem? newItem);

    [PreserveSig]
    int PreDeleteItem(uint flags, IShellItem item);

    [PreserveSig]
    int PostDeleteItem(
        uint flags,
        IShellItem item,
        int result,
        IShellItem? newItem);

    [PreserveSig]
    int PreNewItem(
        uint flags,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string newName);

    [PreserveSig]
    int PostNewItem(
        uint flags,
        IShellItem destinationFolder,
        [MarshalAs(UnmanagedType.LPWStr)] string newName,
        [MarshalAs(UnmanagedType.LPWStr)] string? templateName,
        uint fileAttributes,
        int result,
        IShellItem? newItem);

    [PreserveSig]
    int UpdateProgress(uint workTotal, uint workSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}

[Flags]
internal enum FileOperationFlags : uint
{
    Silent = 0x0004,
    NoConfirmation = 0x0010,
    NoErrorUi = 0x0400,
    NoConnectedElements = 0x2000,
    EarlyFailure = 0x00100000,
}
