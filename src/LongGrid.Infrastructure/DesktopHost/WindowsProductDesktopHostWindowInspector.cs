using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsProductDesktopHostWindowInspector
    : IProductDesktopHostWindowInspector
{
    internal const string InstanceMarkerProperty =
        "LongGrid.ProductDesktopHost.WindowInstance";

    public ProductDesktopHostWindowObservation Inspect(nint handle)
    {
        if (!OperatingSystem.IsWindows()
            || handle == nint.Zero
            || !NativeMethods.IsWindow(handle))
        {
            return ProductDesktopHostWindowObservation.Missing;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(
            handle,
            out uint processId);
        if (threadId == 0
            || processId == 0
            || !NativeMethods.GetWindowRect(handle, out NativeRect bounds))
        {
            return ProductDesktopHostWindowObservation.Missing;
        }

        return new(
            true,
            processId,
            threadId,
            NativeMethods.GetProp(handle, InstanceMarkerProperty),
            new PixelRect(
                bounds.Left,
                bounds.Top,
                checked(bounds.Right - bounds.Left),
                checked(bounds.Bottom - bounds.Top)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        internal readonly int Left;
        internal readonly int Top;
        internal readonly int Right;
        internal readonly int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            nint window,
            out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(
            nint window,
            out NativeRect rectangle);

        [DllImport(
            "user32.dll",
            EntryPoint = "GetPropW",
            CharSet = CharSet.Unicode)]
        internal static extern nint GetProp(nint window, string name);
    }
}
