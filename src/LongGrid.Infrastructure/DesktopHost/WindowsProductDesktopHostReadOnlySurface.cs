using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsProductDesktopHostReadOnlySurfaceFactory
    : IProductDesktopHostReadOnlySurfaceFactory
{
    public IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostReadOnlyProjection projection,
        nint instanceMarker) =>
        WindowsProductDesktopHostReadOnlySurface.Create(
            projection,
            instanceMarker);
}

internal sealed class WindowsProductDesktopHostReadOnlySurface
    : IProductDesktopHostReadOnlySurface
{
    private const int HeaderHeightDip = 54;
    private const int ItemHeightDip = 28;
    private readonly string className;
    private readonly nint module;
    private readonly WindowProcedure windowProcedure;
    private readonly ProductDesktopHostReadOnlyProjection projection;
    private bool disposed;

    private WindowsProductDesktopHostReadOnlySurface(
        ProductDesktopHostReadOnlyProjection projection,
        nint instanceMarker)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DesktopHost read-only surface requires Windows.");
        }

        this.projection = projection;
        InstanceMarker = instanceMarker != nint.Zero
            ? instanceMarker
            : throw new ArgumentOutOfRangeException(nameof(instanceMarker));
        module = NativeMethods.GetModuleHandle(null);
        if (module == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        className = $"LongGrid.ReadOnlySurface.{Guid.NewGuid():N}";
        windowProcedure = WindowProc;
    }

    public nint Handle { get; private set; }

    public nint InstanceMarker { get; }

    public uint ProcessId { get; private set; }

    public uint ThreadId { get; private set; }

    internal static WindowsProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostReadOnlyProjection projection,
        nint instanceMarker)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var surface = new WindowsProductDesktopHostReadOnlySurface(
            projection,
            instanceMarker);
        try
        {
            surface.CreateNativeWindow();
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private void CreateNativeWindow()
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProcedure = windowProcedure,
            Instance = module,
            ClassName = className,
            Cursor = NativeMethods.LoadCursor(
                nint.Zero,
                NativeMethods.ArrowCursor),
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        NativeRect workArea = ReadPrimaryWorkArea();
        uint dpi = NativeMethods.GetDpiForSystem();
        double scale = dpi == 0 ? 1 : dpi / 96d;
        int width = Math.Clamp(
            ToPixels(projection.WidthDip, scale),
            160,
            Math.Max(160, workArea.Width));
        int requestedHeight = (int)(projection.IsCollapsed
            ? HeaderHeightDip
            : Math.Max(
                projection.HeightDip,
                HeaderHeightDip
                    + (Math.Max(1, projection.ItemNames.Count) * ItemHeightDip)
                    + 18));
        int height = Math.Clamp(
            ToPixels(requestedHeight, scale),
            ToPixels(HeaderHeightDip, scale),
            Math.Max(ToPixels(HeaderHeightDip, scale), workArea.Height));
        int x = Math.Clamp(
            checked(workArea.Left + ToPixels(projection.XDip, scale)),
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - width));
        int y = Math.Clamp(
            checked(workArea.Top + ToPixels(projection.YDip, scale)),
            workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - height));

        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered
                | NativeMethods.WsExNoActivate
                | NativeMethods.WsExTransparent,
            className,
            $"Long方格桌面只读宿主 · {projection.Title}",
            NativeMethods.WsPopup,
            x,
            y,
            width,
            height,
            nint.Zero,
            nint.Zero,
            module,
            nint.Zero);
        if (Handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        ThreadId = NativeMethods.GetWindowThreadProcessId(
            Handle,
            out uint processId);
        ProcessId = processId;
        if (ThreadId == 0
            || ProcessId == 0
            || !NativeMethods.SetProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty,
                InstanceMarker))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        byte alpha = (byte)Math.Clamp(
            (int)Math.Round(projection.Opacity * byte.MaxValue),
            0,
            byte.MaxValue);
        if (!NativeMethods.SetLayeredWindowAttributes(
                Handle,
                0,
                alpha,
                NativeMethods.LwaAlpha))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        int cornerPreference = NativeMethods.DwmWindowCornerPreferenceRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _ = NativeMethods.UpdateWindow(Handle);
    }

    private nint WindowProc(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        switch (message)
        {
            case NativeMethods.WmEraseBackground:
                return new nint(1);
            case NativeMethods.WmNcHitTest:
                return new nint(NativeMethods.HtTransparent);
            case NativeMethods.WmMouseActivate:
                return new nint(NativeMethods.MaNoActivate);
            case NativeMethods.WmPaint:
                Paint(window);
                return nint.Zero;
            default:
                return NativeMethods.DefWindowProc(
                    window,
                    message,
                    wordParameter,
                    longParameter);
        }
    }

    private void Paint(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(
            window,
            out PaintStruct paint);
        if (deviceContext == nint.Zero)
        {
            return;
        }

        nint backgroundBrush = nint.Zero;
        nint borderPen = nint.Zero;
        try
        {
            if (!NativeMethods.GetClientRect(window, out NativeRect client))
            {
                return;
            }

            uint background = ParseColor(projection.Color);
            backgroundBrush = NativeMethods.CreateSolidBrush(background);
            borderPen = NativeMethods.CreatePen(
                NativeMethods.PsSolid,
                1,
                Lighten(background));
            nint previousBrush = NativeMethods.SelectObject(
                deviceContext,
                backgroundBrush);
            nint previousPen = NativeMethods.SelectObject(
                deviceContext,
                borderPen);
            _ = NativeMethods.Rectangle(
                deviceContext,
                client.Left,
                client.Top,
                client.Right,
                client.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);

            _ = NativeMethods.SetBkMode(
                deviceContext,
                NativeMethods.TransparentBackground);
            _ = NativeMethods.SetTextColor(deviceContext, 0x00FFFFFF);
            nint previousFont = NativeMethods.SelectObject(
                deviceContext,
                NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont));
            try
            {
                DrawText(
                    deviceContext,
                    projection.Title,
                    new(18, 12, Math.Max(18, client.Right - 18), 36),
                    NativeMethods.DtLeft
                        | NativeMethods.DtVCenter
                        | NativeMethods.DtSingleLine
                        | NativeMethods.DtEndEllipsis);
                if (!projection.IsCollapsed)
                {
                    DrawItems(deviceContext, client);
                }
            }
            finally
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }
        }
        finally
        {
            if (borderPen != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(borderPen);
            }

            if (backgroundBrush != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(backgroundBrush);
            }

            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void DrawItems(nint deviceContext, NativeRect client)
    {
        if (projection.ItemNames.Count == 0)
        {
            DrawText(
                deviceContext,
                "空方格 · 只读预览",
                new(18, 54, Math.Max(18, client.Right - 18), 86),
                NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);
            return;
        }

        int top = 54;
        foreach (string item in projection.ItemNames)
        {
            if (top + ItemHeightDip > client.Bottom)
            {
                break;
            }

            DrawText(
                deviceContext,
                $"•  {item}",
                new(18, top, Math.Max(18, client.Right - 18), top + ItemHeightDip),
                NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine
                    | NativeMethods.DtEndEllipsis);
            top += ItemHeightDip;
        }
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeRect bounds,
        uint format) =>
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            -1,
            ref bounds,
            format);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (Handle != nint.Zero)
        {
            _ = NativeMethods.RemoveProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty);
            _ = NativeMethods.DestroyWindow(Handle);
            Handle = nint.Zero;
        }

        _ = NativeMethods.UnregisterClass(className, module);
        GC.KeepAlive(windowProcedure);
    }

    private static NativeRect ReadPrimaryWorkArea()
    {
        if (!NativeMethods.SystemParametersInfo(
                NativeMethods.SpiGetWorkArea,
                0,
                out NativeRect workArea,
                0)
            || workArea.Width <= 0
            || workArea.Height <= 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return workArea;
    }

    private static int ToPixels(double value, double scale) =>
        checked((int)Math.Round(value * scale));

    private static uint ParseColor(string value)
    {
        byte red = byte.Parse(
            value.AsSpan(1, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        byte green = byte.Parse(
            value.AsSpan(3, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        byte blue = byte.Parse(
            value.AsSpan(5, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        return (uint)(red | (green << 8) | (blue << 16));
    }

    private static uint Lighten(uint color)
    {
        uint red = Math.Min(255, (color & 0xFF) + 48);
        uint green = Math.Min(255, ((color >> 8) & 0xFF) + 48);
        uint blue = Math.Min(255, ((color >> 16) & 0xFF) + 48);
        return red | (green << 8) | (blue << 16);
    }

    private delegate nint WindowProcedure(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
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
    private readonly struct NativeRect
    {
        internal NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal readonly int Left;
        internal readonly int Top;
        internal readonly int Right;
        internal readonly int Bottom;

        internal int Width => checked(Right - Left);

        internal int Height => checked(Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal nint DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Erase;
        internal NativeRect PaintRectangle;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }

    private static class NativeMethods
    {
        internal const uint WsPopup = 0x80000000;
        internal const uint WsExToolWindow = 0x00000080;
        internal const uint WsExTransparent = 0x00000020;
        internal const uint WsExLayered = 0x00080000;
        internal const uint WsExNoActivate = 0x08000000;
        internal const uint LwaAlpha = 0x00000002;
        internal const int SwShowNoActivate = 4;
        internal const uint WmPaint = 0x000F;
        internal const uint WmEraseBackground = 0x0014;
        internal const uint WmNcHitTest = 0x0084;
        internal const uint WmMouseActivate = 0x0021;
        internal const int HtTransparent = -1;
        internal const int MaNoActivate = 3;
        internal const int TransparentBackground = 1;
        internal const int DefaultGuiFont = 17;
        internal const int PsSolid = 0;
        internal const uint DtLeft = 0x0000;
        internal const uint DtVCenter = 0x0004;
        internal const uint DtSingleLine = 0x0020;
        internal const uint DtEndEllipsis = 0x8000;
        internal const uint SpiGetWorkArea = 0x0030;
        internal const int DwmWindowCornerPreference = 33;
        internal const int DwmWindowCornerPreferenceRound = 2;
        internal static readonly nint ArrowCursor = new(32512);

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

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
        internal static extern nint DefWindowProc(
            nint window,
            uint message,
            nint wordParameter,
            nint longParameter);

        [DllImport("user32.dll")]
        internal static extern nint LoadCursor(nint instance, nint cursorName);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            nint window,
            out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProp(
            nint window,
            string name,
            nint value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint RemoveProp(nint window, string name);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetLayeredWindowAttributes(
            nint window,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(
            nint window,
            out NativeRect rectangle);

        [DllImport("user32.dll")]
        internal static extern nint BeginPaint(
            nint window,
            out PaintStruct paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndPaint(
            nint window,
            ref PaintStruct paint);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        internal static extern nint CreatePen(int style, int width, uint color);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint deviceContext, nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Rectangle(
            nint deviceContext,
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("gdi32.dll")]
        internal static extern int SetBkMode(nint deviceContext, int mode);

        [DllImport("gdi32.dll")]
        internal static extern uint SetTextColor(nint deviceContext, uint color);

        [DllImport("gdi32.dll")]
        internal static extern nint GetStockObject(int objectIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int DrawText(
            nint deviceContext,
            string text,
            int characterCount,
            ref NativeRect rectangle,
            uint format);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            out NativeRect value,
            uint flags);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(
            nint window,
            int attribute,
            ref int value,
            int size);
    }
}
