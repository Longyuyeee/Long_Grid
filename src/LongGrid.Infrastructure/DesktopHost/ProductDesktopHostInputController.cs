using System.Runtime.InteropServices;

namespace LongGrid.Infrastructure.DesktopHost;

internal interface IProductDesktopHostInputController
{
    bool SetEnabled(IReadOnlyList<nint> windows, bool enabled);

    bool Hide(IReadOnlyList<nint> windows);
}

internal interface IWindowsProductDesktopHostInputApi
{
    bool IsSupported { get; }

    void Enable(nint window, bool enabled);

    bool IsEnabled(nint window);

    void Hide(nint window);

    bool IsVisible(nint window);
}

internal sealed class WindowsProductDesktopHostInputController
    : IProductDesktopHostInputController
{
    private readonly IWindowsProductDesktopHostInputApi api;

    internal WindowsProductDesktopHostInputController()
        : this(new WindowsProductDesktopHostInputApi())
    {
    }

    internal WindowsProductDesktopHostInputController(
        IWindowsProductDesktopHostInputApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    public bool SetEnabled(IReadOnlyList<nint> windows, bool enabled)
    {
        if (!IsValid(windows))
        {
            return false;
        }

        foreach (nint window in windows)
        {
            api.Enable(window, enabled);
        }

        if (windows.All(window => api.IsEnabled(window) == enabled))
        {
            return true;
        }

        if (enabled)
        {
            HideUnchecked(windows);
            return false;
        }

        foreach (nint window in windows)
        {
            api.Enable(window, enabled: true);
        }

        if (windows.All(api.IsEnabled))
        {
            return false;
        }

        HideUnchecked(windows);
        return false;
    }

    public bool Hide(IReadOnlyList<nint> windows)
    {
        if (!IsValid(windows))
        {
            return false;
        }

        HideUnchecked(windows);
        return windows.All(window => !api.IsVisible(window));
    }

    private bool IsValid(IReadOnlyList<nint>? windows) =>
        api.IsSupported
        && windows is not null
        && windows.Count > 0
        && windows.All(window => window != nint.Zero)
        && windows.Distinct().Count() == windows.Count;

    private void HideUnchecked(IReadOnlyList<nint> windows)
    {
        foreach (nint window in windows)
        {
            api.Hide(window);
        }
    }
}

internal sealed class WindowsProductDesktopHostInputApi
    : IWindowsProductDesktopHostInputApi
{
    private const int HideCommand = 0;

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Enable(nint window, bool enabled) =>
        NativeMethods.EnableWindow(window, enabled);

    public bool IsEnabled(nint window) => NativeMethods.IsWindowEnabled(window);

    public void Hide(nint window) => NativeMethods.ShowWindow(window, HideCommand);

    public bool IsVisible(nint window) => NativeMethods.IsWindowVisible(window);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow(
            nint window,
            [MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);
    }
}
