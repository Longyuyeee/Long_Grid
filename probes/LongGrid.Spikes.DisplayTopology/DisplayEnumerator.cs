using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.DesktopHost;

internal static class DisplayEnumerator
{
    internal static DisplayEnumerationResult Enumerate(
        IReadOnlyDictionary<string, DisplayConfigurationPath>? pathsBySource =
            null)
    {
        var displays = new List<DisplayTopologyNode>();
        int strongIdentityCount = 0;
        int fallbackIdentityCount = 0;
        int displayConfigMappingCount = 0;
        int sourceBoundsMatchCount = 0;
        Exception? callbackException = null;

        bool Callback(
            nint monitor,
            nint _,
            ref NativeRect __,
            nint ___)
        {
            try
            {
                MonitorReadResult result = ReadMonitor(
                    monitor,
                    pathsBySource);
                displays.Add(result.Display);
                if (result.UsedStableDeviceIdentity)
                {
                    strongIdentityCount++;
                }
                else
                {
                    fallbackIdentityCount++;
                }

                if (result.MappedToDisplayConfig)
                {
                    displayConfigMappingCount++;
                }

                if (result.SourceBoundsMatch)
                {
                    sourceBoundsMatchCount++;
                }

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

        return new DisplayEnumerationResult(
            displays,
            strongIdentityCount,
            fallbackIdentityCount,
            displayConfigMappingCount,
            sourceBoundsMatchCount);
    }

    internal static PixelRect GetVirtualScreenBounds() =>
        new(
            NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));

    private static MonitorReadResult ReadMonitor(
        nint monitor,
        IReadOnlyDictionary<string, DisplayConfigurationPath>? pathsBySource)
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

        var device = new DisplayDevice
        {
            Size = unchecked((uint)Marshal.SizeOf<DisplayDevice>()),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty,
        };
        bool deviceRead = NativeMethods.EnumDisplayDevices(
            info.DeviceName,
            0,
            ref device,
            0);
        DisplayConfigurationPath? displayConfigPath = null;
        pathsBySource?.TryGetValue(info.DeviceName, out displayConfigPath);
        string identitySource;
        bool stableIdentity;
        if (displayConfigPath is not null)
        {
            identitySource = displayConfigPath.StableTargetId;
            stableIdentity = displayConfigPath.HasMonitorDevicePath;
        }
        else if (deviceRead && !string.IsNullOrWhiteSpace(device.DeviceId))
        {
            identitySource = device.DeviceId;
            stableIdentity = true;
        }
        else if (deviceRead && !string.IsNullOrWhiteSpace(device.DeviceKey))
        {
            identitySource = device.DeviceKey;
            stableIdentity = true;
        }
        else
        {
            identitySource = info.DeviceName;
            stableIdentity = false;
        }

        PixelRect bounds = ToPixelRect(info.Monitor);
        PixelRect workArea = ToPixelRect(info.WorkArea);
        uint dpi = ReadWindowDpi(bounds);
        DisplayRotation rotation = displayConfigPath?.Rotation
            ?? (bounds.Width >= bounds.Height
                ? DisplayRotation.Landscape
                : DisplayRotation.Portrait);
        string stableId = displayConfigPath?.StableTargetId
            ?? Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identitySource)));

        return new MonitorReadResult(
            new DisplayTopologyNode(
                stableId,
                bounds,
                workArea,
                dpi,
                rotation,
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0),
            stableIdentity,
            displayConfigPath is not null,
            displayConfigPath?.SourceBounds == bounds);
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
            NativeMethods.DestroyWindow(window);
        }
    }

    private static PixelRect ToPixelRect(NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
}

internal sealed record DisplayEnumerationResult(
    IReadOnlyList<DisplayTopologyNode> Displays,
    int StrongIdentityCount,
    int FallbackIdentityCount,
    int DisplayConfigMappingCount,
    int SourceBoundsMatchCount);

internal sealed record MonitorReadResult(
    DisplayTopologyNode Display,
    bool UsedStableDeviceIdentity,
    bool MappedToDisplayConfig,
    bool SourceBoundsMatch);
