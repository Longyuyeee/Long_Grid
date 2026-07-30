using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.DesktopHost;

internal static class DisplayConfigurationEnumerator
{
    private const int MaxBufferAttempts = 8;
    private const uint InvalidModeIndex = 0xFFFFFFFF;
    private const ushort InvalidVirtualModeIndex = 0xFFFF;

    internal static DisplayConfigurationResult EnumerateActivePaths()
    {
        ValidateNativeLayouts();
        uint flags =
            NativeMethods.QdcOnlyActivePaths
            | NativeMethods.QdcVirtualModeAware;

        for (int attempt = 1; attempt <= MaxBufferAttempts; attempt++)
        {
            int sizeResult = NativeMethods.GetDisplayConfigBufferSizes(
                flags,
                out uint pathCapacity,
                out uint modeCapacity);
            ThrowIfFailed(sizeResult);

            var paths = new DisplayConfigPathInfo[pathCapacity];
            var modes = new DisplayConfigModeInfo[modeCapacity];
            uint pathCount = pathCapacity;
            uint modeCount = modeCapacity;
            int queryResult = NativeMethods.QueryDisplayConfig(
                flags,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                nint.Zero);

            if (queryResult == NativeMethods.ErrorInsufficientBuffer)
            {
                continue;
            }

            ThrowIfFailed(queryResult);
            return BuildResult(
                paths.AsSpan(0, checked((int)pathCount)),
                modes.AsSpan(0, checked((int)modeCount)),
                attempt);
        }

        throw new InvalidOperationException(
            "Display configuration changed during every bounded retry.");
    }

    private static DisplayConfigurationResult BuildResult(
        ReadOnlySpan<DisplayConfigPathInfo> paths,
        ReadOnlySpan<DisplayConfigModeInfo> modes,
        int bufferAttempts)
    {
        var activePaths = new List<DisplayConfigurationPath>(paths.Length);
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stableTargetIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (DisplayConfigPathInfo path in paths)
        {
            if ((path.Flags & NativeMethods.DisplayConfigPathActive) == 0)
            {
                continue;
            }

            string sourceName = ReadSourceName(path.SourceInfo);
            TargetIdentityResult targetIdentity =
                ReadTargetIdentity(path.TargetInfo);
            if (!sourceNames.Add(sourceName))
            {
                throw new InvalidOperationException(
                    "Active display paths did not have unique GDI source names.");
            }

            if (!stableTargetIds.Add(targetIdentity.Value))
            {
                throw new InvalidOperationException(
                    "Active display paths did not have unique target identities.");
            }

            bool virtualMode =
                (path.Flags & NativeMethods.DisplayConfigPathSupportVirtualMode)
                != 0;
            int sourceModeIndex = GetSourceModeIndex(
                path.SourceInfo.ModeInfo,
                virtualMode);
            PixelRect sourceBounds = ReadSourceBounds(
                modes,
                sourceModeIndex,
                path.SourceInfo.AdapterId,
                path.SourceInfo.Id);

            activePaths.Add(
                new DisplayConfigurationPath(
                    sourceName,
                    targetIdentity.Value,
                    targetIdentity.HasMonitorDevicePath,
                    ToCoreRotation(path.TargetInfo.Rotation),
                    sourceBounds,
                    path.TargetInfo.TargetAvailable,
                    virtualMode));
        }

        return new DisplayConfigurationResult(activePaths, bufferAttempts);
    }

    private static string ReadSourceName(
        DisplayConfigPathSourceInfo source)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
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

    private static TargetIdentityResult ReadTargetIdentity(
        DisplayConfigPathTargetInfo target)
    {
        var request = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
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
        string source = hasMonitorDevicePath
            ? request.MonitorDevicePath
            : FormattableString.Invariant(
                $"{target.AdapterId.HighPart}:{target.AdapterId.LowPart}:{target.Id}");
        return new TargetIdentityResult(
            Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            hasMonitorDevicePath);
    }

    private static int GetSourceModeIndex(uint packedIndex, bool virtualMode)
    {
        uint index = virtualMode
            ? packedIndex >> 16
            : packedIndex;
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
                "An active path referenced a source mode outside the mode table.");
        }

        DisplayConfigModeInfo mode = modes[index];
        if (mode.InfoType != DisplayConfigModeInfoType.Source
            || mode.Id != sourceId
            || mode.AdapterId != adapterId)
        {
            throw new InvalidOperationException(
                "An active path referenced a mismatched source mode.");
        }

        return new PixelRect(
            mode.SourceMode.Position.X,
            mode.SourceMode.Position.Y,
            checked((int)mode.SourceMode.Width),
            checked((int)mode.SourceMode.Height));
    }

    private static DisplayRotation ToCoreRotation(
        DisplayConfigRotation rotation) =>
        rotation switch
        {
            DisplayConfigRotation.Identity => DisplayRotation.Landscape,
            DisplayConfigRotation.Rotate90 => DisplayRotation.Portrait,
            DisplayConfigRotation.Rotate180 => DisplayRotation.LandscapeFlipped,
            DisplayConfigRotation.Rotate270 => DisplayRotation.PortraitFlipped,
            _ => DisplayRotation.Unknown,
        };

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
}

internal sealed record DisplayConfigurationResult(
    IReadOnlyList<DisplayConfigurationPath> ActivePaths,
    int BufferAttempts);

internal sealed record DisplayConfigurationPath(
    string SourceName,
    string StableTargetId,
    bool HasMonitorDevicePath,
    DisplayRotation Rotation,
    PixelRect SourceBounds,
    bool TargetAvailable,
    bool UsesVirtualMode);

internal sealed record TargetIdentityResult(
    string Value,
    bool HasMonitorDevicePath);
