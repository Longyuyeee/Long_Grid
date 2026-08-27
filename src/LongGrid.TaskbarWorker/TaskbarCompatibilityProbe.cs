using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using LongGrid.Core.Taskbar;

namespace LongGrid.TaskbarWorker;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(TaskbarCompatibilityReport))]
internal sealed partial class TaskbarJsonContext : JsonSerializerContext;

internal static class TaskbarCompatibilityProbe
{
    private static readonly string[] KnownConflictProcessNames =
    [
        "TranslucentTB",
        "RoundedTB",
        "Start11",
        "Start11_64",
        "StartAllBackCfg",
        "ExplorerPatcher",
        "windhawk",
    ];

    internal static TaskbarCompatibilityReport Capture()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Version version = GetWindowsVersion();
        IReadOnlyList<TaskbarWindowSnapshot> windows = CaptureTaskbarWindows();
        IReadOnlyList<string> conflicts = CaptureConflictingProcesses();
        stopwatch.Stop();

        TaskbarCompatibilityActual actual = new(
            OperatingSystemVersion: version.ToString(),
            WindowsBuild: version.Build,
            SessionId: GetCurrentSessionId(),
            TaskbarWindows: windows,
            ConflictingProcesses: conflicts,
            ModifiedSystemState: false,
            ProbeMilliseconds: stopwatch.Elapsed.TotalMilliseconds);
        return TaskbarCompatibilityPolicy.Evaluate(
            actual,
            OperatingSystem.IsWindows());
    }

    private static Version GetWindowsVersion()
    {
        RTL_OSVERSIONINFOEX version = new()
        {
            Size = Marshal.SizeOf<RTL_OSVERSIONINFOEX>(),
            ServicePack = string.Empty,
        };
        int status = RtlGetVersion(ref version);
        if (status != 0)
        {
            return Environment.OSVersion.Version;
        }

        return new Version(
            version.MajorVersion,
            version.MinorVersion,
            version.BuildNumber,
            version.ServicePackMajor);
    }

    private static TaskbarWindowSnapshot[] CaptureTaskbarWindows()
    {
        List<TaskbarWindowSnapshot> result = [];
        EnumWindows((handle, _) =>
        {
            string windowClass = GetWindowClass(handle);
            if (string.Equals(windowClass, "Shell_TrayWnd", StringComparison.Ordinal)
                || string.Equals(
                    windowClass,
                    "Shell_SecondaryTrayWnd",
                    StringComparison.Ordinal))
            {
                uint threadId = GetWindowThreadProcessId(handle, out uint processId);
                if (threadId == 0)
                {
                    processId = 0;
                }
                result.Add(new TaskbarWindowSnapshot(
                    Handle: handle.ToInt64(),
                    WindowClass: windowClass,
                    ProcessId: checked((int)processId),
                    ProcessName: GetProcessName(processId)));
            }

            return true;
        }, IntPtr.Zero);
        return result.OrderBy(window => window.Handle).ToArray();
    }

    private static string GetWindowClass(IntPtr handle)
    {
        char[] buffer = new char[256];
        int length = GetClassName(handle, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static int GetCurrentSessionId()
    {
        using Process process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static string[] CaptureConflictingProcesses()
    {
        HashSet<string> known = new(
            KnownConflictProcessNames,
            StringComparer.OrdinalIgnoreCase);
        List<string> conflicts = [];
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (known.Contains(process.ProcessName))
                    {
                        conflicts.Add(process.ProcessName);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process may exit between enumeration and name lookup.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Some protected processes can deny metadata access.
                }
            }
        }

        return conflicts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX version);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr window,
        [Out] char[] className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RTL_OSVERSIONINFOEX
    {
        internal int Size;
        internal int MajorVersion;
        internal int MinorVersion;
        internal int BuildNumber;
        internal int PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string ServicePack;

        internal ushort ServicePackMajor;
        internal ushort ServicePackMinor;
        internal ushort SuiteMask;
        internal byte ProductType;
        internal byte Reserved;
    }
}
