using System.Runtime.InteropServices;

internal static class NativeWindowMethods
{
    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(
        nint reserved,
        ComInitialization initialization);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(
        out WindowMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref WindowMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref WindowMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("shell32.dll")]
    internal static extern int SHGetSpecialFolderLocation(
        nint owner,
        int folderId,
        out nint itemIdList);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(
        string name,
        nint bindContext,
        out nint itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll")]
    internal static extern uint SHChangeNotifyRegister(
        nint window,
        ShellChangeRegistrationFlags sources,
        ShellChangeEvent events,
        uint message,
        int entryCount,
        ref ShellChangeNotifyEntry entries);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SHChangeNotifyDeregister(uint registrationId);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Lock")]
    internal static extern nint SHChangeNotificationLock(
        nint change,
        uint processId,
        out nint itemIdListArray,
        out ShellChangeEvent changeEvent);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Unlock")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SHChangeNotificationUnlock(nint notificationLock);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SHGetPathFromIDList(
        nint itemIdList,
        [Out] char[] path);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint WindowProcedure(
    nint window,
    uint message,
    nint wordParameter,
    nint longParameter);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowClass
{
    public uint Size;
    public uint Style;
    public WindowProcedure WindowProcedure;
    public int ClassExtraBytes;
    public int WindowExtraBytes;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint BackgroundBrush;
    public string? MenuName;
    public string ClassName;
    public nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowMessage
{
    public nint Window;
    public uint Message;
    public nint WordParameter;
    public nint LongParameter;
    public uint Time;
    public WindowPoint Point;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ShellChangeNotifyEntry
{
    public nint ItemIdList;

    [MarshalAs(UnmanagedType.Bool)]
    public bool Recursive;
}

[Flags]
internal enum ComInitialization : uint
{
    ApartmentThreaded = 0x2,
}

[Flags]
internal enum ShellChangeRegistrationFlags : uint
{
    InterruptLevel = 0x1,
    ShellLevel = 0x2,
    NewDelivery = 0x8000,
}

[Flags]
internal enum ShellChangeEvent : int
{
    None = 0,
    RenameItem = 0x00000001,
    Create = 0x00000002,
    Delete = 0x00000004,
    MakeDirectory = 0x00000008,
    RemoveDirectory = 0x00000010,
    UpdateDirectory = 0x00001000,
    UpdateItem = 0x00002000,
    RenameFolder = 0x00020000,
}
