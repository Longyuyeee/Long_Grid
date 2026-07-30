using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

internal static class WindowModelProbe
{
    private const uint SystemParametersGetWorkArea = 0x0030;

    internal static ModelAuditResult Run(
        DesktopHostWindowModel model,
        ProbeScenario scenario)
    {
        using Process process = Process.GetCurrentProcess();
        var resourcesBefore = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<DesktopHostSurfacePlan> plan =
            DesktopHostWindowPlanner.Create(
                model,
                [new DesktopDisplayPlacement("primary", scenario.WorkArea)],
                scenario.Containers);

        NativeWindowSet? windows = null;
        ResourceSnapshot resourcesCreated;
        bool activationPreserved;
        int insideHits;
        int gapEscapes;
        int externalGapHits;
        bool stylesValid;
        bool topmostAbsent;

        try
        {
            windows = NativeWindowSet.Create(model, plan);
            resourcesCreated = ResourceSnapshot.Capture(process);
            nint foregroundAfterShow = NativeMethods.GetForegroundWindow();
            activationPreserved = foregroundBefore == foregroundAfterShow;

            insideHits = CountInsideHits(model, scenario, windows.Handles);
            (gapEscapes, externalGapHits) =
                CountGapEscapes(scenario.GapPoints, windows.Handles);
            stylesValid = windows.Handles.All(HasPassiveDesktopStyles);
            topmostAbsent = windows.Handles.All(window =>
                ((ulong)NativeMethods.GetWindowLongPtr(
                    window,
                    NativeMethods.GwlExStyle).ToInt64()
                    & NativeMethods.WsExTopmost) == 0);
        }
        finally
        {
            windows?.Dispose();
        }

        stopwatch.Stop();
        var resourcesAfter = ResourceSnapshot.Capture(process);
        int expectedInsideHits = scenario.Containers.Count;

        return new ModelAuditResult(
            model.ToString(),
            SurfaceCount: plan.Count,
            InteractiveRegionCount: plan.Sum(surface =>
                surface.InteractiveRegions.Count),
            ExpectedInsideHits: expectedInsideHits,
            InsideHits: insideHits,
            GapSamples: scenario.GapPoints.Count,
            GapEscapes: gapEscapes,
            ExternalProcessGapHits: externalGapHits,
            ActivationPreserved: activationPreserved,
            PassiveStylesPresent: stylesValid,
            TopmostStyleAbsent: topmostAbsent,
            UserObjectsBefore: resourcesBefore.UserObjects,
            UserObjectsCreated: resourcesCreated.UserObjects,
            UserObjectsAfter: resourcesAfter.UserObjects,
            GdiObjectsBefore: resourcesBefore.GdiObjects,
            GdiObjectsCreated: resourcesCreated.GdiObjects,
            GdiObjectsAfter: resourcesAfter.GdiObjects,
            ProcessHandlesBefore: resourcesBefore.ProcessHandles,
            ProcessHandlesCreated: resourcesCreated.ProcessHandles,
            ProcessHandlesAfter: resourcesAfter.ProcessHandles,
            DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
            CleanupPassed:
                resourcesAfter.UserObjects <= resourcesBefore.UserObjects + 1
                && resourcesAfter.GdiObjects <= resourcesBefore.GdiObjects + 1
                && resourcesAfter.ProcessHandles <= resourcesBefore.ProcessHandles + 2);
    }

    internal static ProbeScenario CreateScenario()
    {
        if (!NativeMethods.SystemParametersInfo(
            SystemParametersGetWorkArea,
            0,
            out NativeRect workArea,
            0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var bounds = new PixelRect(
            workArea.Left,
            workArea.Top,
            workArea.Right - workArea.Left,
            workArea.Bottom - workArea.Top);
        if (bounds.Width < 320 || bounds.Height < 240)
        {
            throw new InvalidOperationException(
                "The primary work area is too small for the window model probe.");
        }

        int gridWidth = Math.Min(bounds.Width - 40, 1000);
        int gridHeight = Math.Min(bounds.Height - 40, 700);
        int cellWidth = gridWidth / 10;
        int cellHeight = gridHeight / 10;
        int containerWidth = Math.Max(8, cellWidth - Math.Max(4, cellWidth / 5));
        int containerHeight = Math.Max(8, cellHeight - Math.Max(4, cellHeight / 5));
        int startX = bounds.Left + 20;
        int startY = bounds.Top + 20;
        var containers = new List<DesktopContainerPlacement>(100);
        var gaps = new List<NativePoint>(100);

        for (int row = 0; row < 10; row++)
        {
            for (int column = 0; column < 10; column++)
            {
                int left = startX + (column * cellWidth);
                int top = startY + (row * cellHeight);
                containers.Add(new DesktopContainerPlacement(
                    $"container-{row:D2}-{column:D2}",
                    "primary",
                    new PixelRect(left, top, containerWidth, containerHeight)));
                gaps.Add(new NativePoint(
                    left + containerWidth + ((cellWidth - containerWidth) / 2),
                    top + containerHeight + ((cellHeight - containerHeight) / 2)));
            }
        }

        return new ProbeScenario(bounds, containers, gaps);
    }

    private static int CountInsideHits(
        DesktopHostWindowModel model,
        ProbeScenario scenario,
        IReadOnlyList<nint> handles)
    {
        int hits = 0;
        for (int index = 0; index < scenario.Containers.Count; index++)
        {
            PixelRect bounds = scenario.Containers[index].Bounds;
            nint actual = NativeMethods.WindowFromPoint(new NativePoint(
                bounds.Left + (bounds.Width / 2),
                bounds.Top + (bounds.Height / 2)));
            nint expected = model == DesktopHostWindowModel.PerContainer
                ? handles[index]
                : handles[0];
            if (actual == expected)
            {
                hits++;
            }
        }

        return hits;
    }

    private static (int Escapes, int ExternalProcessHits) CountGapEscapes(
        IEnumerable<NativePoint> points,
        IReadOnlyCollection<nint> handles)
    {
        var probeWindows = handles.ToHashSet();
        uint currentProcessId = unchecked((uint)Environment.ProcessId);
        int escapes = 0;
        int externalProcessHits = 0;

        foreach (NativePoint point in points)
        {
            nint actual = NativeMethods.WindowFromPoint(point);
            if (!probeWindows.Contains(actual))
            {
                escapes++;
            }

            if (actual != nint.Zero)
            {
                uint threadId = NativeMethods.GetWindowThreadProcessId(
                    actual,
                    out uint processId);
                if (threadId != 0 && processId != currentProcessId)
                {
                    externalProcessHits++;
                }
            }
        }

        return (escapes, externalProcessHits);
    }

    private static bool HasPassiveDesktopStyles(nint window)
    {
        ulong style = unchecked((ulong)NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlExStyle).ToInt64());
        ulong expected = NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        return (style & expected) == expected;
    }

}

internal sealed class NativeWindowSet : IDisposable
{
    private readonly string _className;
    private readonly nint _instance;
    private bool _disposed;

    private NativeWindowSet(
        string className,
        nint instance,
        IReadOnlyList<nint> handles)
    {
        _className = className;
        _instance = instance;
        Handles = handles;
    }

    internal IReadOnlyList<nint> Handles { get; }

    internal static NativeWindowSet Create(
        DesktopHostWindowModel model,
        IReadOnlyList<DesktopHostSurfacePlan> plan)
    {
        string className = $"LongGrid.P0.WindowModel.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = unchecked((uint)Marshal.SizeOf<WindowClass>()),
            Instance = instance,
            WindowProcedure = WindowModelProbeWindowProcedure.Instance,
            ClassName = className,
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var handles = new List<nint>(plan.Count);
        var result = new NativeWindowSet(className, instance, handles);
        try
        {
            foreach (DesktopHostSurfacePlan surface in plan)
            {
                nint window = CreateWindow(className, instance, surface.WindowBounds);
                handles.Add(window);
                if (model == DesktopHostWindowModel.PerDisplay)
                {
                    ApplyInteractiveRegion(window, surface.InteractiveRegions);
                }

                if (!NativeMethods.SetWindowPos(
                    window,
                    NativeMethods.HwndTop,
                    surface.WindowBounds.Left,
                    surface.WindowBounds.Top,
                    surface.WindowBounds.Width,
                    surface.WindowBounds.Height,
                    NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int index = Handles.Count - 1; index >= 0; index--)
        {
            NativeMethods.DestroyWindow(Handles[index]);
        }

        NativeMethods.UnregisterClass(_className, _instance);
        _disposed = true;
    }

    private static nint CreateWindow(
        string className,
        nint instance,
        PixelRect bounds)
    {
        uint extendedStyle =
            NativeMethods.WsExToolWindow
            | NativeMethods.WsExNoActivate
            | NativeMethods.WsExLayered;
        nint window = NativeMethods.CreateWindowEx(
            extendedStyle,
            className,
            string.Empty,
            NativeMethods.WsPopup,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            nint.Zero,
            nint.Zero,
            instance,
            nint.Zero);
        if (window == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!NativeMethods.SetLayeredWindowAttributes(
            window,
            0,
            1,
            NativeMethods.LwaAlpha))
        {
            NativeMethods.DestroyWindow(window);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return window;
    }

    private static void ApplyInteractiveRegion(
        nint window,
        IReadOnlyList<PixelRect> regions)
    {
        nint aggregate = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (aggregate == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool ownershipTransferred = false;
        try
        {
            foreach (PixelRect region in regions)
            {
                nint item = NativeMethods.CreateRectRgn(
                    region.Left,
                    region.Top,
                    region.Right,
                    region.Bottom);
                if (item == nint.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (NativeMethods.CombineRgn(
                        aggregate,
                        aggregate,
                        item,
                        NativeMethods.RgnOr) == 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    NativeMethods.DeleteObject(item);
                }
            }

            if (NativeMethods.SetWindowRgn(window, aggregate, redraw: false) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            ownershipTransferred = true;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                NativeMethods.DeleteObject(aggregate);
            }
        }
    }
}

internal static class WindowModelProbeWindowProcedure
{
    internal static readonly WindowProcedure Instance = Process;

    private static nint Process(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter) =>
        NativeMethods.DefWindowProc(
            window,
            message,
            wordParameter,
            longParameter);
}

internal sealed record ProbeScenario(
    PixelRect WorkArea,
    IReadOnlyList<DesktopContainerPlacement> Containers,
    IReadOnlyList<NativePoint> GapPoints);

internal sealed record ResourceSnapshot(
    uint UserObjects,
    uint GdiObjects,
    int ProcessHandles)
{
    internal static ResourceSnapshot Capture(Process process) =>
        new(
            NativeMethods.GetGuiResources(
                process.Handle,
                NativeMethods.GrUserObjects),
            NativeMethods.GetGuiResources(
                process.Handle,
                NativeMethods.GrGdiObjects),
            process.HandleCount);
}

internal sealed record ModelAuditResult(
    string Model,
    int SurfaceCount,
    int InteractiveRegionCount,
    int ExpectedInsideHits,
    int InsideHits,
    int GapSamples,
    int GapEscapes,
    int ExternalProcessGapHits,
    bool ActivationPreserved,
    bool PassiveStylesPresent,
    bool TopmostStyleAbsent,
    uint UserObjectsBefore,
    uint UserObjectsCreated,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsCreated,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesCreated,
    int ProcessHandlesAfter,
    double DurationMilliseconds,
    bool CleanupPassed);
