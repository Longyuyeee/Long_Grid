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
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint SwpShowWindow = 0x0040;
    internal const int SwHide = 0;
    internal const int GwlExStyle = -20;
    internal const int RgnOr = 2;
    internal const int RgnCopy = 5;
    internal const uint GrGdiObjects = 0;
    internal const uint GrUserObjects = 1;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmSetFocus = 0x0007;
    internal const uint WmKillFocus = 0x0008;
    internal const uint WmPaint = 0x000F;
    internal const uint WmClose = 0x0010;
    internal const uint WmGetObject = 0x003D;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmLeftButtonDown = 0x0201;
    internal const int VkTab = 0x09;
    internal const int VkReturn = 0x0D;
    internal const int VkShift = 0x10;
    internal const int VkEscape = 0x1B;
    internal const int VkSpace = 0x20;
    internal const int VkEnd = 0x23;
    internal const int VkHome = 0x24;
    internal const int VkLeft = 0x25;
    internal const int VkUp = 0x26;
    internal const int VkRight = 0x27;
    internal const int VkDown = 0x28;
    internal const int ColorWindow = 5;
    internal const int ColorWindowText = 8;
    internal const int ColorHighlight = 13;
    internal const int ColorHighlightText = 14;
    internal const int ColorBtnFace = 15;
    internal const int ColorBtnText = 18;
    internal const int ColorGrayText = 17;
    internal const int DefaultGuiFont = 17;
    internal const int TransparentBackground = 1;
    internal const uint DtLeft = 0x0000;
    internal const uint DtCenter = 0x0001;
    internal const uint DtVCenter = 0x0004;
    internal const uint DtSingleLine = 0x0020;
    internal const uint DtEndEllipsis = 0x8000;
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

    [DllImport(
        "user32.dll",
        EntryPoint = "DefWindowProcW",
        ExactSpelling = true)]
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

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint BeginDeferWindowPos(int windowCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint DeferWindowPos(
        nint windowPosition,
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndDeferWindowPos(nint windowPosition);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        nint window,
        out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(
        nint window,
        int command);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetFocus();

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

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
    internal static extern int GetWindowRgn(
        nint window,
        nint region);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(
        nint window,
        out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(
        nint window,
        nint rectangle,
        [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(
        nint window,
        out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(
        nint window,
        ref PaintStruct paint);

    [DllImport("user32.dll")]
    internal static extern int FillRect(
        nint deviceContext,
        ref NativeRect rectangle,
        nint brush);

    [DllImport("user32.dll")]
    internal static extern int FrameRect(
        nint deviceContext,
        ref NativeRect rectangle,
        nint brush);

    [DllImport("user32.dll")]
    internal static extern nint GetSysColorBrush(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetSysColor(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(
        nint deviceContext,
        string text,
        int characterCount,
        ref NativeRect rectangle,
        uint format);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowTextLengthW",
        ExactSpelling = true,
        SetLastError = true)]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowTextW",
        ExactSpelling = true,
        SetLastError = true)]
    internal static extern int GetWindowText(
        nint window,
        nint text,
        int maximumCount);

    internal static string ReadWindowText(nint window)
    {
        int length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        int maximumCount = checked(length + 1);
        nint text = Marshal.AllocHGlobal(
            checked(maximumCount * sizeof(char)));
        try
        {
            int copied = GetWindowText(window, text, maximumCount);
            return copied > 0
                ? Marshal.PtrToStringUni(text, copied)
                : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMessageW",
        ExactSpelling = true,
        SetLastError = true)]
    internal static extern int GetMessage(
        out WindowMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(
        ref WindowMessage message);

    [DllImport(
        "user32.dll",
        EntryPoint = "DispatchMessageW",
        ExactSpelling = true)]
    internal static extern nint DispatchMessage(
        ref WindowMessage message);

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

    [DllImport("dcomp.dll", PreserveSig = true)]
    internal static extern int DCompositionCreateDevice(
        nint renderingDevice,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)]
        out IDCompositionDevice device);

    [DllImport("uiautomationcore.dll")]
    internal static extern nint UiaReturnRawElementProvider(
        nint window,
        nint wordParameter,
        nint longParameter,
        nint provider);

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
    internal static extern bool EqualRgn(
        nint first,
        nint second);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint objectHandle);

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int objectIndex);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(
        nint deviceContext,
        nint graphicsObject);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(
        nint deviceContext,
        int mode);

    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(
        nint deviceContext,
        uint color);
}

[UnmanagedFunctionPointer(
    CallingConvention.Winapi,
    CharSet = CharSet.Unicode)]
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

[StructLayout(LayoutKind.Sequential)]
internal struct PaintStruct
{
    internal nint DeviceContext;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool Erase;

    internal NativeRect PaintRectangle;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool Restore;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool IncrementalUpdate;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    internal byte[] Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WordParameter;
    internal nint LongParameter;
    internal uint Time;
    internal NativePoint Point;
    internal uint Private;
}
