using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsProductDesktopHostReadOnlySurfaceFactory
    : IProductDesktopHostReadOnlySurfaceFactory
{
    public IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden) =>
        WindowsProductDesktopHostReadOnlySurface.Create(
            projection,
            instanceMarker,
            startHidden);
}

internal sealed class WindowsProductDesktopHostReadOnlySurface
    : IProductDesktopHostReadOnlySurface
{
    private readonly string className;
    private readonly nint module;
    private readonly WindowProcedure windowProcedure;
    private readonly ProductDesktopHostDisplayProjection projection;
    private readonly bool startHidden;
#if WINDOWS
    private WindowsProductDesktopHostUiaRootProvider? uiaProvider;
#endif
    private bool disposed;

    private WindowsProductDesktopHostReadOnlySurface(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DesktopHost read-only surface requires Windows.");
        }

        this.projection = projection;
        this.startHidden = startHidden;
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

    public bool ReadOnlyAccessibilityAttested => uiaProvider is not null;

    public bool PassiveWindowContractAttested =>
        !disposed
        && Handle != nint.Zero
        && NativeMethods.IsWindowVisible(Handle)
        && AttestStableWindowPolicy()
        && AttestWindowRegion(expectEmpty: false);

    public bool HiddenWindowContractAttested =>
        !disposed
        && Handle != nint.Zero
        && !NativeMethods.IsWindowVisible(Handle)
        && AttestStableWindowPolicy()
        && AttestWindowRegion(expectEmpty: true);

    internal static WindowsProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var surface = new WindowsProductDesktopHostReadOnlySurface(
            projection,
            instanceMarker,
            startHidden);
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

        PixelRect workArea = projection.WorkArea;

        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered
                | NativeMethods.WsExNoActivate
                | NativeMethods.WsExTransparent,
            className,
            "Long方格桌面只读宿主",
            NativeMethods.WsPopup,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            nint.Zero,
            nint.Zero,
            module,
            nint.Zero);
        if (Handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

#if WINDOWS
        uiaProvider = new(Handle, projection, InstanceMarker);
#endif

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

        if (!NativeMethods.SetLayeredWindowAttributes(
                Handle,
                0,
                byte.MaxValue,
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
        bool applied = startHidden ? ApplyHidden() : ApplyPassive();
        if (!applied)
        {
            throw new InvalidOperationException(
                "DesktopHost surface failed its initial mode attestation.");
        }
    }

    private nint WindowProc(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        switch (message)
        {
#if WINDOWS
            case NativeMethods.WmGetObject
                when longParameter.ToInt64() ==
                    System.Windows.Automation.Provider.AutomationInteropProvider.RootObjectId
                    && uiaProvider is not null:
                return System.Windows.Automation.Provider.AutomationInteropProvider
                    .ReturnRawElementProvider(
                        window,
                        wordParameter,
                        longParameter,
                        uiaProvider);
#endif
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

        try
        {
            _ = NativeMethods.SetBkMode(
                deviceContext,
                NativeMethods.TransparentBackground);
            _ = NativeMethods.SetTextColor(deviceContext, 0x00FFFFFF);
            nint previousFont = NativeMethods.SelectObject(
                deviceContext,
                NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont));
            try
            {
                foreach (ProductDesktopHostReadOnlyProjection container
                    in projection.Containers)
                {
                    DrawContainer(deviceContext, container);
                }
            }
            finally
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void DrawContainer(
        nint deviceContext,
        ProductDesktopHostReadOnlyProjection container)
    {
        NativeRect bounds = GetContainerBounds(container);
        double scale = projection.EffectiveDpi / 96d;
        int headerHeight = ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int horizontalPadding = ToPixels(18, scale);
        uint background = BlendWithDesktop(
            ParseColor(container.Color),
            container.Opacity);
        nint backgroundBrush = NativeMethods.CreateSolidBrush(background);
        nint borderPen = NativeMethods.CreatePen(
            NativeMethods.PsSolid,
            1,
            Lighten(background));
        try
        {
            nint previousBrush = NativeMethods.SelectObject(
                deviceContext,
                backgroundBrush);
            nint previousPen = NativeMethods.SelectObject(
                deviceContext,
                borderPen);
            _ = NativeMethods.Rectangle(
                deviceContext,
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);

            DrawText(
                deviceContext,
                container.Title,
                new(
                    bounds.Left + horizontalPadding,
                    bounds.Top + ToPixels(12, scale),
                    Math.Max(
                        bounds.Left + horizontalPadding,
                        bounds.Right - horizontalPadding),
                    bounds.Top + ToPixels(36, scale)),
                NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine
                    | NativeMethods.DtEndEllipsis);
            if (container.IsCollapsed)
            {
                return;
            }

            DrawItems(deviceContext, container, bounds, scale, headerHeight);
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
        }
    }

    private static void DrawItems(
        nint deviceContext,
        ProductDesktopHostReadOnlyProjection container,
        NativeRect bounds,
        double scale,
        int headerHeight)
    {
        IReadOnlyList<string> items = container.ItemNames.Count == 0
            ? ["空方格 · 只读预览"]
            : container.ItemNames;
        int itemHeight = ToPixels(
            ProductDesktopHostSurfaceLayout.ItemHeightDip,
            scale);
        int horizontalPadding = ToPixels(18, scale);
        int top = bounds.Top + headerHeight;
        foreach (string item in items)
        {
            if (top + itemHeight > bounds.Bottom)
            {
                break;
            }

            DrawText(
                deviceContext,
                container.ItemNames.Count == 0 ? item : $"•  {item}",
                new(
                    bounds.Left + horizontalPadding,
                    top,
                    Math.Max(
                        bounds.Left + horizontalPadding,
                        bounds.Right - horizontalPadding),
                    top + itemHeight),
                NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine
                    | NativeMethods.DtEndEllipsis);
            top += itemHeight;
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
#if WINDOWS
            _ = NativeMethods.UiaReturnRawElementProvider(
                Handle,
                nint.Zero,
                nint.Zero,
                nint.Zero);
            uiaProvider = null;
#endif
            _ = NativeMethods.RemoveProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty);
            _ = NativeMethods.DestroyWindow(Handle);
            Handle = nint.Zero;
        }

        _ = NativeMethods.UnregisterClass(className, module);
        GC.KeepAlive(windowProcedure);
    }

    public bool ApplyPassive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ApplyWindowRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _ = NativeMethods.UpdateWindow(Handle);
        return PassiveWindowContractAttested;
    }

    public bool ApplyHidden()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ApplyEmptyWindowRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        return HiddenWindowContractAttested;
    }

    private void ApplyWindowRegion()
    {
        nint combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (combined == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool transferred = false;
        try
        {
            foreach (ProductDesktopHostReadOnlyProjection container
                in projection.Containers)
            {
                NativeRect bounds = GetContainerBounds(container);
                nint card = NativeMethods.CreateRectRgn(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right,
                    bounds.Bottom);
                if (card == nint.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (NativeMethods.CombineRgn(
                            combined,
                            combined,
                            card,
                            NativeMethods.RgnOr) == NativeMethods.Error)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(card);
                }
            }

            if (NativeMethods.SetWindowRgn(Handle, combined, redraw: true) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                _ = NativeMethods.DeleteObject(combined);
            }
        }
    }

    private void ApplyEmptyWindowRegion()
    {
        nint empty = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (empty == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool transferred = false;
        try
        {
            if (NativeMethods.SetWindowRgn(Handle, empty, redraw: true) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                _ = NativeMethods.DeleteObject(empty);
            }
        }
    }

    private bool AttestStableWindowPolicy()
    {
        nint extendedStyle = NativeMethods.GetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle);
        long style = extendedStyle.ToInt64();
        const long required = NativeMethods.WsExToolWindow
            | NativeMethods.WsExLayered
            | NativeMethods.WsExNoActivate
            | NativeMethods.WsExTransparent;
        return (style & required) == required
            && (style & NativeMethods.WsExTopmost) == 0
            && NativeMethods.GetWindow(Handle, NativeMethods.GwOwner) == nint.Zero
            && NativeMethods.GetForegroundWindow() != Handle;
    }

    private bool AttestWindowRegion(bool expectEmpty)
    {
        nint region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == nint.Zero)
        {
            return false;
        }

        try
        {
            int result = NativeMethods.GetWindowRgn(Handle, region);
            return expectEmpty
                ? result == NativeMethods.NullRegion
                : result is not (NativeMethods.Error
                    or NativeMethods.NullRegion);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private NativeRect GetContainerBounds(
        ProductDesktopHostReadOnlyProjection container)
    {
        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            projection,
            container);
        return new(
            bounds.Left,
            bounds.Top,
            checked(bounds.Left + bounds.Width),
            checked(bounds.Top + bounds.Height));
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

    private static uint BlendWithDesktop(uint color, double opacity)
    {
        const uint desktopChannel = 28;
        uint red = BlendChannel(color & 0xFF, desktopChannel, opacity);
        uint green = BlendChannel((color >> 8) & 0xFF, desktopChannel, opacity);
        uint blue = BlendChannel((color >> 16) & 0xFF, desktopChannel, opacity);
        return red | (green << 8) | (blue << 16);
    }

    private static uint BlendChannel(
        uint foreground,
        uint background,
        double opacity) =>
        (uint)Math.Clamp(
            (int)Math.Round((foreground * opacity) + (background * (1 - opacity))),
            0,
            255);

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
        internal const uint WsExTopmost = 0x00000008;
        internal const uint LwaAlpha = 0x00000002;
        internal const int SwHide = 0;
        internal const int SwShowNoActivate = 4;
        internal const uint WmPaint = 0x000F;
        internal const uint WmEraseBackground = 0x0014;
        internal const uint WmNcHitTest = 0x0084;
        internal const uint WmMouseActivate = 0x0021;
        internal const uint WmGetObject = 0x003D;
        internal const int HtTransparent = -1;
        internal const int MaNoActivate = 3;
        internal const int TransparentBackground = 1;
        internal const int DefaultGuiFont = 17;
        internal const int PsSolid = 0;
        internal const uint DtLeft = 0x0000;
        internal const uint DtVCenter = 0x0004;
        internal const uint DtSingleLine = 0x0020;
        internal const uint DtEndEllipsis = 0x8000;
        internal const int RgnOr = 2;
        internal const int NullRegion = 1;
        internal const int Error = 0;
        internal const int DwmWindowCornerPreference = 33;
        internal const int DwmWindowCornerPreferenceRound = 2;
        internal const int GwlExStyle = -20;
        internal const uint GwOwner = 4;
        internal static readonly nint ArrowCursor = new(32512);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll")]
        internal static extern nint GetWindow(nint window, uint command);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

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
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateWindow(nint window);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateRectRgn(
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("gdi32.dll")]
        internal static extern int CombineRgn(
            nint destination,
            nint source1,
            nint source2,
            int mode);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowRgn(
            nint window,
            nint region,
            [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("user32.dll")]
        internal static extern int GetWindowRgn(nint window, nint region);

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

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(
            nint window,
            int attribute,
            ref int value,
            int size);

#if WINDOWS
        [DllImport("uiautomationcore.dll")]
        internal static extern nint UiaReturnRawElementProvider(
            nint window,
            nint wordParameter,
            nint longParameter,
            nint provider);
#endif
    }
}
