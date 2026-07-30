using System.Runtime.InteropServices;

internal static class NativeMethods
{
    internal const uint MonitorInfoPrimary = 0x00000001;
    internal const int SmXVirtualScreen = 76;
    internal const int SmYVirtualScreen = 77;
    internal const int SmCxVirtualScreen = 78;
    internal const int SmCyVirtualScreen = 79;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExNoActivate = 0x08000000;
    internal const uint GrGdiObjects = 0;
    internal const uint GrUserObjects = 1;
    internal const uint QdcOnlyActivePaths = 0x00000002;
    internal const uint QdcVirtualModeAware = 0x00000010;
    internal const uint DisplayConfigPathActive = 0x00000001;
    internal const uint DisplayConfigPathSupportVirtualMode = 0x00000008;
    internal const int ErrorInsufficientBuffer = 122;
    internal static readonly nint PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumerationProcedure callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

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

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint pathCount,
        out uint modeCount);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DisplayConfigGetDeviceInfo(
        ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DisplayConfigGetDeviceInfo(
        ref DisplayConfigTargetDeviceName request);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[return: MarshalAs(UnmanagedType.Bool)]
internal delegate bool MonitorEnumerationProcedure(
    nint monitor,
    nint deviceContext,
    ref NativeRect monitorRectangle,
    nint data);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    internal uint Size;
    internal NativeRect Monitor;
    internal NativeRect WorkArea;
    internal uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string DeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayDevice
{
    internal uint Size;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string DeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceString;

    internal uint StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceKey;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LocallyUniqueIdentifier(
    uint LowPart,
    int HighPart);

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    internal DisplayConfigPathSourceInfo SourceInfo;
    internal DisplayConfigPathTargetInfo TargetInfo;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    internal LocallyUniqueIdentifier AdapterId;
    internal uint Id;
    internal uint ModeInfo;
    internal uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    internal LocallyUniqueIdentifier AdapterId;
    internal uint Id;
    internal uint ModeInfo;
    internal DisplayConfigVideoOutputTechnology OutputTechnology;
    internal DisplayConfigRotation Rotation;
    internal DisplayConfigScaling Scaling;
    internal DisplayConfigRational RefreshRate;
    internal DisplayConfigScanLineOrdering ScanLineOrdering;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool TargetAvailable;

    internal uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    internal uint Numerator;
    internal uint Denominator;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfo
{
    [FieldOffset(0)]
    internal DisplayConfigModeInfoType InfoType;

    [FieldOffset(4)]
    internal uint Id;

    [FieldOffset(8)]
    internal LocallyUniqueIdentifier AdapterId;

    [FieldOffset(16)]
    internal DisplayConfigSourceMode SourceMode;

    [FieldOffset(16)]
    internal DisplayConfigTargetMode TargetMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    internal uint Width;
    internal uint Height;
    internal DisplayConfigPixelFormat PixelFormat;
    internal NativePoint Position;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativePoint(int X, int Y);

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetMode
{
    internal DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    internal ulong PixelRate;
    internal DisplayConfigRational HorizontalSyncFrequency;
    internal DisplayConfigRational VerticalSyncFrequency;
    internal DisplayConfig2DRegion ActiveSize;
    internal DisplayConfig2DRegion TotalSize;
    internal uint VideoStandard;
    internal DisplayConfigScanLineOrdering ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    internal uint Width;
    internal uint Height;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    internal DisplayConfigDeviceInfoHeader Header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string ViewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    internal DisplayConfigDeviceInfoHeader Header;
    internal uint Flags;
    internal DisplayConfigVideoOutputTechnology OutputTechnology;
    internal ushort EdidManufactureId;
    internal ushort EdidProductCodeId;
    internal uint ConnectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    internal string MonitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    internal DisplayConfigDeviceInfoType Type;
    internal uint Size;
    internal LocallyUniqueIdentifier AdapterId;
    internal uint Id;
}

internal enum DisplayConfigDeviceInfoType : uint
{
    GetSourceName = 1,
    GetTargetName = 2,
}

internal enum DisplayConfigModeInfoType : uint
{
    Source = 1,
    Target = 2,
    DesktopImage = 3,
}

internal enum DisplayConfigRotation : uint
{
    Identity = 1,
    Rotate90 = 2,
    Rotate180 = 3,
    Rotate270 = 4,
}

internal enum DisplayConfigScaling : uint
{
    Identity = 1,
    Centered = 2,
    Stretched = 3,
    AspectRatioCenteredMax = 4,
    Custom = 5,
    Preferred = 128,
}

internal enum DisplayConfigPixelFormat : uint
{
    EightBitsPerPixel = 1,
    SixteenBitsPerPixel = 2,
    TwentyFourBitsPerPixel = 3,
    ThirtyTwoBitsPerPixel = 4,
    NotSpecified = 8,
}

internal enum DisplayConfigScanLineOrdering : uint
{
    Unspecified = 0,
    Progressive = 1,
    Interlaced = 2,
    InterlacedLowerFieldFirst = 3,
}

internal enum DisplayConfigVideoOutputTechnology : uint
{
    Other = 0xFFFFFFFF,
    Hd15 = 0,
    SVideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    SdtvDongle = 14,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    Internal = 0x80000000,
}
