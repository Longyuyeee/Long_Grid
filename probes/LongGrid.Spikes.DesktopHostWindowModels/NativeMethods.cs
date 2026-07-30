using System.Runtime.InteropServices;

internal static class NativeMethods
{
    internal const uint WsPopup = 0x80000000;
    internal const uint WsExTopmost = 0x00000008;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExLayered = 0x00080000;
    internal const uint WsExNoActivate = 0x08000000;
    internal const uint LwaAlpha = 0x00000002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const int GwlExStyle = -20;
    internal const int RgnOr = 2;
    internal const uint GrGdiObjects = 0;
    internal const uint GrUserObjects = 1;
    internal static readonly nint HwndTop = nint.Zero;
    internal static readonly nint PerMonitorAwareV2 = new(-4);

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
    internal static extern nint DefWindowProc(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(
        nint window,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(
        nint window,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        out NativeRect rectangle,
        uint updateFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int CombineRgn(
        nint destination,
        nint sourceOne,
        nint sourceTwo,
        int combineMode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint objectHandle);
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
    internal uint Size;
    internal uint Style;
    internal WindowProcedure WindowProcedure;
    internal int ClassExtra;
    internal int WindowExtra;
    internal nint Instance;
    internal nint Icon;
    internal nint Cursor;
    internal nint Background;
    internal string? MenuName;
    internal string ClassName;
    internal nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativePoint(int X, int Y);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
