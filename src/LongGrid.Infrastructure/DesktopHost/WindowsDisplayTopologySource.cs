using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsDisplayTopologySource : IProductDisplayTopologySource
{
    private const int MaxBufferAttempts = 8;
    private const uint InvalidModeIndex = 0xFFFFFFFF;
    private const ushort InvalidVirtualModeIndex = 0xFFFF;

    public ProductDisplayTopologySample Read(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Display topology sampling requires Windows.");
        }

        ValidateNativeLayouts();
        DisplayPathReadResult paths = ReadActivePaths(cancellationToken);
        var pathsBySource = paths.Paths.ToDictionary(
            path => path.SourceName,
            StringComparer.OrdinalIgnoreCase);
        var monitors = new List<ProductDisplayTopologySampleMonitor>();
        Exception? callbackException = null;

        bool Callback(nint monitor, nint _, ref NativeRect __, nint ___)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                monitors.Add(ReadMonitor(monitor, pathsBySource));
                return true;
            }
            catch (Exception exception)
            {
                callbackException = exception;
                return false;
            }
        }

        MonitorEnumerationProcedure callback = Callback;
        bool enumerated = NativeMethods.EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            callback,
            nint.Zero);
        GC.KeepAlive(callback);
        if (callbackException is OperationCanceledException cancelled)
        {
            throw cancelled;
        }

        if (callbackException is not null)
        {
            throw new InvalidOperationException(
                "A monitor could not be inspected.",
                callbackException);
        }

        if (!enumerated)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new(
            Array.AsReadOnly(monitors.ToArray()),
            paths.Paths.Count,
            paths.BufferAttempts);
    }

    private static ProductDisplayTopologySampleMonitor ReadMonitor(
        nint monitor,
        IReadOnlyDictionary<string, DisplayPath> pathsBySource)
    {
        var info = new MonitorInfoEx
        {
            Size = unchecked((uint)Marshal.SizeOf<MonitorInfoEx>()),
            DeviceName = string.Empty,
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        pathsBySource.TryGetValue(info.DeviceName, out DisplayPath? path);
        PixelRect bounds = ToPixelRect(info.Monitor);
        DisplayTopologyNode display = new(
            path?.StableTargetId ?? Hash(info.DeviceName),
            bounds,
            ToPixelRect(info.WorkArea),
            ReadWindowDpi(bounds),
            path?.Rotation ?? DisplayRotation.Unknown,
            (info.Flags & NativeMethods.MonitorInfoPrimary) != 0);
        return new(
            display,
            path?.HasMonitorDevicePath == true,
            path is not null,
            path?.SourceBounds == bounds,
            path?.TargetAvailable == true);
    }

    private static DisplayPathReadResult ReadActivePaths(
        CancellationToken cancellationToken)
    {
        uint flags = NativeMethods.QdcOnlyActivePaths
            | NativeMethods.QdcVirtualModeAware;
        for (int attempt = 1; attempt <= MaxBufferAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailed(NativeMethods.GetDisplayConfigBufferSizes(
                flags,
                out uint pathCapacity,
                out uint modeCapacity));
            var paths = new DisplayConfigPathInfo[pathCapacity];
            var modes = new DisplayConfigModeInfo[modeCapacity];
            uint pathCount = pathCapacity;
            uint modeCount = modeCapacity;
            int result = NativeMethods.QueryDisplayConfig(
                flags,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                nint.Zero);
            if (result == NativeMethods.ErrorInsufficientBuffer)
            {
                continue;
            }

            ThrowIfFailed(result);
            return BuildPaths(
                paths.AsSpan(0, checked((int)pathCount)),
                modes.AsSpan(0, checked((int)modeCount)),
                attempt);
        }

        throw new InvalidOperationException(
            "Display configuration changed during every bounded retry.");
    }

    private static DisplayPathReadResult BuildPaths(
        ReadOnlySpan<DisplayConfigPathInfo> paths,
        ReadOnlySpan<DisplayConfigModeInfo> modes,
        int bufferAttempts)
    {
        var activePaths = new List<DisplayPath>(paths.Length);
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stableTargetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (DisplayConfigPathInfo path in paths)
        {
            if ((path.Flags & NativeMethods.DisplayConfigPathActive) == 0)
            {
                continue;
            }

            string sourceName = ReadSourceName(path.SourceInfo);
            TargetIdentity target = ReadTargetIdentity(path.TargetInfo);
            if (!sourceNames.Add(sourceName)
                || !stableTargetIds.Add(target.StableId))
            {
                throw new InvalidOperationException(
                    "Active display paths did not expose unique identities.");
            }

            bool virtualMode =
                (path.Flags & NativeMethods.DisplayConfigPathSupportVirtualMode) != 0;
            int sourceModeIndex = GetSourceModeIndex(
                path.SourceInfo.ModeInfo,
                virtualMode);
            activePaths.Add(new(
                sourceName,
                target.StableId,
                target.HasMonitorDevicePath,
                ToCoreRotation(path.TargetInfo.Rotation),
                ReadSourceBounds(
                    modes,
                    sourceModeIndex,
                    path.SourceInfo.AdapterId,
                    path.SourceInfo.Id),
                path.TargetInfo.TargetAvailable));
        }

        return new(Array.AsReadOnly(activePaths.ToArray()), bufferAttempts);
    }

    private static string ReadSourceName(DisplayConfigPathSourceInfo source)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new()
            {
                Type = DisplayConfigDeviceInfoType.GetSourceName,
                Size = checked((uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>()),
                AdapterId = source.AdapterId,
                Id = source.Id,
            },
            ViewGdiDeviceName = string.Empty,
        };
        ThrowIfFailed(NativeMethods.DisplayConfigGetDeviceInfo(ref request));
        return string.IsNullOrWhiteSpace(request.ViewGdiDeviceName)
            ? throw new InvalidOperationException(
                "An active path did not expose a GDI source name.")
            : request.ViewGdiDeviceName;
    }

    private static TargetIdentity ReadTargetIdentity(
        DisplayConfigPathTargetInfo target)
    {
        var request = new DisplayConfigTargetDeviceName
        {
            Header = new()
            {
                Type = DisplayConfigDeviceInfoType.GetTargetName,
                Size = checked((uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>()),
                AdapterId = target.AdapterId,
                Id = target.Id,
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty,
        };
        ThrowIfFailed(NativeMethods.DisplayConfigGetDeviceInfo(ref request));
        bool hasMonitorDevicePath =
            !string.IsNullOrWhiteSpace(request.MonitorDevicePath);
        string identitySource = hasMonitorDevicePath
            ? request.MonitorDevicePath
            : FormattableString.Invariant(
                $"{target.AdapterId.HighPart}:{target.AdapterId.LowPart}:{target.Id}");
        return new(Hash(identitySource), hasMonitorDevicePath);
    }

    private static int GetSourceModeIndex(uint packedIndex, bool virtualMode)
    {
        uint index = virtualMode ? packedIndex >> 16 : packedIndex;
        if (index == InvalidModeIndex
            || (virtualMode && index == InvalidVirtualModeIndex))
        {
            throw new InvalidOperationException(
                "An active path did not expose a source mode.");
        }

        return checked((int)index);
    }

    private static PixelRect ReadSourceBounds(
        ReadOnlySpan<DisplayConfigModeInfo> modes,
        int index,
        LocallyUniqueIdentifier adapterId,
        uint sourceId)
    {
        if ((uint)index >= (uint)modes.Length)
        {
            throw new InvalidOperationException(
                "An active path referenced a mode outside the mode table.");
        }

        DisplayConfigModeInfo mode = modes[index];
        if (mode.InfoType != DisplayConfigModeInfoType.Source
            || mode.Id != sourceId
            || mode.AdapterId != adapterId)
        {
            throw new InvalidOperationException(
                "An active path referenced a mismatched source mode.");
        }

        return new(
            mode.SourceMode.Position.X,
            mode.SourceMode.Position.Y,
            checked((int)mode.SourceMode.Width),
            checked((int)mode.SourceMode.Height));
    }

    private static uint ReadWindowDpi(PixelRect bounds)
    {
        nint window = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate,
            "STATIC",
            string.Empty,
            NativeMethods.WsPopup,
            bounds.Left,
            bounds.Top,
            1,
            1,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (window == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(window);
            return dpi == 0
                ? throw new Win32Exception(Marshal.GetLastWin32Error())
                : dpi;
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(window);
        }
    }

    private static DisplayRotation ToCoreRotation(DisplayConfigRotation rotation) =>
        rotation switch
        {
            DisplayConfigRotation.Identity => DisplayRotation.Landscape,
            DisplayConfigRotation.Rotate90 => DisplayRotation.Portrait,
            DisplayConfigRotation.Rotate180 => DisplayRotation.LandscapeFlipped,
            DisplayConfigRotation.Rotate270 => DisplayRotation.PortraitFlipped,
            _ => DisplayRotation.Unknown,
        };

    private static PixelRect ToPixelRect(NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            checked(rectangle.Right - rectangle.Left),
            checked(rectangle.Bottom - rectangle.Top));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ThrowIfFailed(int result)
    {
        if (result != 0)
        {
            throw new Win32Exception(result);
        }
    }

    private static void ValidateNativeLayouts()
    {
        if (Marshal.SizeOf<DisplayConfigPathInfo>() != 72
            || Marshal.SizeOf<DisplayConfigModeInfo>() != 64
            || Marshal.SizeOf<DisplayConfigSourceDeviceName>() != 84
            || Marshal.SizeOf<DisplayConfigTargetDeviceName>() != 420)
        {
            throw new PlatformNotSupportedException(
                "The managed DisplayConfig structures do not match the Windows ABI.");
        }
    }

    private sealed record DisplayPathReadResult(
        IReadOnlyList<DisplayPath> Paths,
        int BufferAttempts);

    private sealed record DisplayPath(
        string SourceName,
        string StableTargetId,
        bool HasMonitorDevicePath,
        DisplayRotation Rotation,
        PixelRect SourceBounds,
        bool TargetAvailable);

    private sealed record TargetIdentity(
        string StableId,
        bool HasMonitorDevicePath);

    private static class NativeMethods
    {
        internal const uint MonitorInfoPrimary = 0x00000001;
        internal const uint WsPopup = 0x80000000;
        internal const uint WsExToolWindow = 0x00000080;
        internal const uint WsExNoActivate = 0x08000000;
        internal const uint QdcOnlyActivePaths = 0x00000002;
        internal const uint QdcVirtualModeAware = 0x00000010;
        internal const uint DisplayConfigPathActive = 0x00000001;
        internal const uint DisplayConfigPathSupportVirtualMode = 0x00000008;
        internal const int ErrorInsufficientBuffer = 122;

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

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LocallyUniqueIdentifier(uint LowPart, int HighPart);

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
