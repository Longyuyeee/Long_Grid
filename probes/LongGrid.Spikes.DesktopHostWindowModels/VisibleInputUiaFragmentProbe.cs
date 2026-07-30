using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

internal static class VisibleInputUiaFragmentProbe
{
    private const uint SystemParametersGetWorkArea = 0x0030;

    internal static VisibleInputUiaFragmentReport Run(
        bool perMonitorV2Requested)
    {
        WarmUp();
        using Process process = Process.GetCurrentProcess();
        CollectGarbage();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        ResourceSnapshot created = before;
        VisibleFragmentOutcome outcome;

        try
        {
            outcome = ExecuteScenario(process);
            created = outcome.ResourcesCreated;
        }
        finally
        {
            CollectGarbage();
        }

        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        bool cleanupPassed =
            outcome.HostDisposed
            && after.UserObjects <= before.UserObjects + 1
            && after.GdiObjects <= before.GdiObjects + 1
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool foregroundPreserved =
            outcome.ForegroundPreserved
            && NativeMethods.GetForegroundWindow() == foregroundBefore;
        bool passed =
            perMonitorV2Requested
            && outcome.PassiveStylesPresent
            && outcome.TopmostStyleAbsent
            && outcome.OpenHitCount == outcome.ContainerCount
            && outcome.ClosedEscapeCount == outcome.ContainerCount
            && outcome.ReopenedHitCount == outcome.ContainerCount
            && outcome.ClosedExternalProcessHitCount
                == outcome.ContainerCount
            && outcome.UiaTreeVerified
            && outcome.FragmentProviderPointHitCount
                == outcome.ContainerCount
            && outcome.ClosedProviderPointReturnsNull
            && outcome.ClosedUiaPointExcludesFragments
            && foregroundPreserved
            && cleanupPassed;

        return new VisibleInputUiaFragmentReport(
            Probe: "P0-07b2b2b2b4a-visible-input-uia-fragment",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            ContainerCount: outcome.ContainerCount,
            OpenHitCount: outcome.OpenHitCount,
            ClosedEscapeCount: outcome.ClosedEscapeCount,
            ClosedExternalProcessHitCount:
                outcome.ClosedExternalProcessHitCount,
            ReopenedHitCount: outcome.ReopenedHitCount,
            PassiveStylesPresent: outcome.PassiveStylesPresent,
            TopmostStyleAbsent: outcome.TopmostStyleAbsent,
            UiaTreeVerified: outcome.UiaTreeVerified,
            FragmentProviderPointHitCount:
                outcome.FragmentProviderPointHitCount,
            ClosedProviderPointReturnsNull:
                outcome.ClosedProviderPointReturnsNull,
            ClosedUiaPointExcludesFragments:
                outcome.ClosedUiaPointExcludesFragments,
            ForegroundPreserved: foregroundPreserved,
            SyntheticInputUsed: false,
            DisplayStateChanged: false,
            ExternalWindowStateChanged: false,
            UserObjectsBefore: before.UserObjects,
            UserObjectsCreated: created.UserObjects,
            UserObjectsAfter: after.UserObjects,
            GdiObjectsBefore: before.GdiObjects,
            GdiObjectsCreated: created.GdiObjects,
            GdiObjectsAfter: after.GdiObjects,
            ProcessHandlesBefore: before.ProcessHandles,
            ProcessHandlesCreated: created.ProcessHandles,
            ProcessHandlesAfter: after.ProcessHandles,
            CleanupPassed: cleanupPassed,
            Result: passed ? "Conditional Pass" : "Fail",
            Limitations:
            [
                "The probe briefly shows one alpha-1, non-activating, non-topmost, probe-owned HWND in the primary work area.",
                "WindowFromPoint validates routing without moving the real cursor or synthesizing mouse, keyboard, touch, or pen input.",
                "The real UIA Raw View tree, sibling navigation, runtime IDs, physical-screen bounds, and point lookup are verified in-process; Narrator, cross-process assistive technology, focus, UIA events, and patterns remain unverified.",
                "Input closure uses an empty Window Region. The fragments remain discoverable by tree navigation with IsEnabled=false, while fragment point lookup returns null.",
                "No display, DPI, rotation, projection, device, power, Explorer, or RDP transition was induced.",
            ]);
    }

    private static void WarmUp()
    {
        using Process process = Process.GetCurrentProcess();
        VisibleFragmentOutcome outcome = ExecuteScenario(process);
        if (outcome.OpenHitCount != outcome.ContainerCount
            || outcome.ClosedEscapeCount != outcome.ContainerCount
            || outcome.ReopenedHitCount != outcome.ContainerCount
            || !outcome.UiaTreeVerified
            || outcome.FragmentProviderPointHitCount
                != outcome.ContainerCount
            || !outcome.ClosedProviderPointReturnsNull
            || !outcome.ClosedUiaPointExcludesFragments
            || !outcome.ForegroundPreserved
            || !outcome.HostDisposed)
        {
            throw new InvalidOperationException(
                "The visible input/UIA Fragment warm-up failed: "
                + $"{outcome}.");
        }
    }

    private static VisibleFragmentOutcome ExecuteScenario(Process process)
    {
        PixelRect hostBounds = SelectHostBounds();
        VisibleFragmentDefinition[] definitions =
        [
            new(
                101,
                "LongGrid.Container.Alpha",
                "Alpha container",
                new PixelRect(20, 20, 130, 80)),
            new(
                102,
                "LongGrid.Container.Beta",
                "Beta container",
                new PixelRect(200, 110, 130, 80)),
        ];
        NativePoint[] samplePoints = definitions
            .Select(definition => new NativePoint(
                hostBounds.Left
                    + definition.LocalBounds.Left
                    + (definition.LocalBounds.Width / 2),
                hostBounds.Top
                    + definition.LocalBounds.Top
                    + (definition.LocalBounds.Height / 2)))
            .ToArray();
        nint[] underlyingWindows = samplePoints
            .Select(NativeMethods.WindowFromPoint)
            .ToArray();
        uint currentProcessId = unchecked((uint)Environment.ProcessId);
        if (underlyingWindows.Any(window =>
                !IsExternalProcessWindow(window, currentProcessId)))
        {
            throw new InvalidOperationException(
                "The selected work-area samples do not resolve to external-process windows.");
        }

        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        ResourceSnapshot created;
        int openHits;
        int closedEscapes;
        int closedExternalHits;
        int reopenedHits;
        bool passiveStyles;
        bool topmostAbsent;
        bool uiaTreeVerified;
        int fragmentProviderPointHits;
        bool closedProviderPointReturnsNull;
        bool closedUiaPointExcludesFragments;
        bool foregroundPreserved;
        bool disposed;
        nint hostWindow;

        using (var host = VisibleFragmentHost.Create(
            hostBounds,
            definitions))
        {
            hostWindow = host.Window;
            created = ResourceSnapshot.Capture(process);
            passiveStyles = HasPassiveStyles(host.Window);
            topmostAbsent = !HasTopmostStyle(host.Window);
            openHits = samplePoints.Count(point =>
                NativeMethods.WindowFromPoint(point) == host.Window);
            VisibleFragmentClientSnapshot client =
                ReadWithUiaClient(host);
            uiaTreeVerified = client.TreeVerified;
            fragmentProviderPointHits = samplePoints
                .Select((point, index) =>
                    ReferenceEquals(
                        host.Provider.ElementProviderFromPoint(
                            point.X,
                            point.Y),
                        host.Provider.Children[index]))
                .Count(matches => matches);

            host.CloseInput();
            closedEscapes = samplePoints
                .Select((point, index) =>
                    NativeMethods.WindowFromPoint(point)
                        == underlyingWindows[index])
                .Count(matches => matches);
            closedExternalHits = samplePoints.Count(point =>
                IsExternalProcessWindow(
                    NativeMethods.WindowFromPoint(point),
                    currentProcessId));
            closedProviderPointReturnsNull = samplePoints.All(point =>
                host.Provider.ElementProviderFromPoint(
                    point.X,
                    point.Y) is null);
            string[] childAutomationIds = definitions
                .Select(definition => definition.AutomationId)
                .ToArray();
            closedUiaPointExcludesFragments = samplePoints.All(point =>
                !childAutomationIds.Contains(
                    AutomationElement.FromPoint(
                        new Point(point.X, point.Y))
                        .Current.AutomationId,
                    StringComparer.Ordinal));

            host.OpenInput();
            reopenedHits = samplePoints.Count(point =>
                NativeMethods.WindowFromPoint(point) == host.Window);
            foregroundPreserved =
                NativeMethods.GetForegroundWindow() == foregroundBefore;
        }

        disposed = !NativeMethods.IsWindow(hostWindow);
        return new VisibleFragmentOutcome(
            definitions.Length,
            openHits,
            closedEscapes,
            closedExternalHits,
            reopenedHits,
            passiveStyles,
            topmostAbsent,
            uiaTreeVerified,
            fragmentProviderPointHits,
            closedProviderPointReturnsNull,
            closedUiaPointExcludesFragments,
            foregroundPreserved,
            created,
            disposed);
    }

    private static VisibleFragmentClientSnapshot ReadWithUiaClient(
        VisibleFragmentHost host)
    {
        AutomationElement root = AutomationElement.FromHandle(host.Window);
        TreeWalker walker = TreeWalker.RawViewWalker;
        AutomationElement? first = walker.GetFirstChild(root);
        AutomationElement? second = first is null
            ? null
            : walker.GetNextSibling(first);
        AutomationElement? third = second is null
            ? null
            : walker.GetNextSibling(second);
        if (first is null || second is null || third is not null)
        {
            return new VisibleFragmentClientSnapshot(false);
        }

        AutomationElement[] children = [first, second];
        int[][] runtimeIds = children
            .Select(child => child.GetRuntimeId())
            .ToArray();
        bool treeVerified =
            root.Current.AutomationId == "LongGrid.VisibleFragmentRoot"
            && children[0].Current.AutomationId
                == "LongGrid.Container.Alpha"
            && children[1].Current.AutomationId
                == "LongGrid.Container.Beta"
            && children[0].Current.Name == "Alpha container"
            && children[1].Current.Name == "Beta container"
            && children.All(child =>
                child.Current.ControlType == ControlType.Group
                && child.Current.IsEnabled)
            && runtimeIds.All(runtimeId => runtimeId.Length >= 2)
            && !runtimeIds[0].SequenceEqual(runtimeIds[1])
            && children.Select(child => child.GetRuntimeId())
                .Zip(runtimeIds)
                .All(pair => pair.First.SequenceEqual(pair.Second))
            && children.Select(child => child.Current.BoundingRectangle)
                .Zip(host.Provider.Children)
                .All(pair => pair.First == pair.Second.BoundingRectangle);
        return new VisibleFragmentClientSnapshot(treeVerified);
    }

    private static PixelRect SelectHostBounds()
    {
        if (!NativeMethods.SystemParametersInfo(
            SystemParametersGetWorkArea,
            0,
            out NativeRect workArea,
            0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        const int width = 360;
        const int height = 220;
        const int margin = 24;
        if (workArea.Right - workArea.Left < width + (margin * 2)
            || workArea.Bottom - workArea.Top < height + (margin * 2))
        {
            throw new InvalidOperationException(
                "The primary work area is too small for the visible Fragment probe.");
        }

        NativePoint cursor = NativeMethods.GetCursorPos(out NativePoint value)
            ? value
            : new NativePoint(workArea.Left, workArea.Top);
        PixelRect[] candidates =
        [
            new(workArea.Left + margin, workArea.Top + margin, width, height),
            new(workArea.Right - width - margin, workArea.Top + margin, width, height),
            new(workArea.Left + margin, workArea.Bottom - height - margin, width, height),
            new(workArea.Right - width - margin, workArea.Bottom - height - margin, width, height),
        ];
        return candidates
            .OrderByDescending(candidate =>
            {
                long deltaX = candidate.Left + (width / 2L) - cursor.X;
                long deltaY = candidate.Top + (height / 2L) - cursor.Y;
                return (deltaX * deltaX) + (deltaY * deltaY);
            })
            .First();
    }

    private static bool IsExternalProcessWindow(
        nint window,
        uint currentProcessId)
    {
        if (window == nint.Zero)
        {
            return false;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(
            window,
            out uint processId);
        return threadId != 0 && processId != currentProcessId;
    }

    private static bool HasPassiveStyles(nint window)
    {
        ulong style = unchecked((ulong)NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlExStyle).ToInt64());
        ulong expected = NativeMethods.WsExToolWindow
            | NativeMethods.WsExNoActivate;
        return (style & expected) == expected;
    }

    private static bool HasTopmostStyle(nint window) =>
        (unchecked((ulong)NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlExStyle).ToInt64())
            & NativeMethods.WsExTopmost) != 0;

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal sealed class VisibleFragmentHost : IDisposable
{
    private readonly string _className;
    private readonly nint _instance;
    private readonly WindowProcedure _windowProcedure;
    private readonly IReadOnlyList<VisibleFragmentDefinition> _definitions;
    private bool _disposed;

    private VisibleFragmentHost(
        string className,
        nint instance,
        WindowProcedure windowProcedure,
        nint window,
        PixelRect bounds,
        IReadOnlyList<VisibleFragmentDefinition> definitions,
        VisibleFragmentRootProvider provider)
    {
        _className = className;
        _instance = instance;
        _windowProcedure = windowProcedure;
        Window = window;
        Bounds = bounds;
        _definitions = definitions;
        Provider = provider;
    }

    internal nint Window { get; }

    internal PixelRect Bounds { get; }

    internal VisibleFragmentRootProvider Provider { get; }

    internal static VisibleFragmentHost Create(
        PixelRect bounds,
        IReadOnlyList<VisibleFragmentDefinition> definitions)
    {
        string className =
            $"LongGrid.P0.VisibleFragment.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        VisibleFragmentRootProvider? provider = null;
        WindowProcedure procedure = (
            window,
            message,
            wordParameter,
            longParameter) =>
        {
            if (message == NativeMethods.WmGetObject
                && longParameter.ToInt64()
                    == AutomationInteropProvider.RootObjectId
                && provider is not null)
            {
                return AutomationInteropProvider
                    .ReturnRawElementProvider(
                        window,
                        wordParameter,
                        longParameter,
                        provider);
            }

            if (message == NativeMethods.WmDestroy)
            {
                _ = NativeMethods.UiaReturnRawElementProvider(
                    window,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero);
            }

            return NativeMethods.DefWindowProc(
                window,
                message,
                wordParameter,
                longParameter);
        };
        var windowClass = new WindowClass
        {
            Size = checked((uint)Marshal.SizeOf<WindowClass>()),
            Instance = instance,
            WindowProcedure = procedure,
            ClassName = className,
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        nint window = nint.Zero;
        VisibleFragmentHost? host = null;
        try
        {
            window = NativeMethods.CreateWindowEx(
                NativeMethods.WsExToolWindow
                | NativeMethods.WsExNoActivate
                | NativeMethods.WsExLayered,
                className,
                "Long Grid visible Fragment probe",
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
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            provider = new VisibleFragmentRootProvider(
                window,
                bounds,
                definitions);
            host = new VisibleFragmentHost(
                className,
                instance,
                procedure,
                window,
                bounds,
                definitions,
                provider);
            if (!NativeMethods.SetLayeredWindowAttributes(
                window,
                0,
                1,
                NativeMethods.LwaAlpha))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            host.OpenInput();
            if (!NativeMethods.SetWindowPos(
                window,
                NativeMethods.HwndTop,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate
                | NativeMethods.SwpShowWindow))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return host;
        }
        catch
        {
            host?.Dispose();
            if (host is null)
            {
                if (window != nint.Zero)
                {
                    _ = NativeMethods.DestroyWindow(window);
                }

                _ = NativeMethods.UnregisterClass(className, instance);
            }

            throw;
        }
    }

    internal void OpenInput()
    {
        ApplyRegion(_definitions.Select(definition =>
            definition.LocalBounds));
        Provider.SetInputOpen(true);
    }

    internal void CloseInput()
    {
        ApplyRegion([]);
        Provider.SetInputOpen(false);
    }

    private void ApplyRegion(IEnumerable<PixelRect> rectangles)
    {
        nint aggregate = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (aggregate == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool ownershipTransferred = false;
        try
        {
            foreach (PixelRect rectangle in rectangles)
            {
                nint item = NativeMethods.CreateRectRgn(
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Right,
                    rectangle.Bottom);
                if (item == nint.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error());
                }

                try
                {
                    if (NativeMethods.CombineRgn(
                        aggregate,
                        aggregate,
                        item,
                        NativeMethods.RgnOr) == 0)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(item);
                }
            }

            if (NativeMethods.SetWindowRgn(
                Window,
                aggregate,
                redraw: true) == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            ownershipTransferred = true;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                _ = NativeMethods.DeleteObject(aggregate);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = NativeMethods.DestroyWindow(Window);
        _ = NativeMethods.UnregisterClass(_className, _instance);
        _disposed = true;
        GC.KeepAlive(_windowProcedure);
        GC.KeepAlive(Provider);
    }
}

internal sealed class VisibleFragmentRootProvider
    : IRawElementProviderFragmentRoot
{
    private readonly nint _window;
    private readonly PixelRect _bounds;
    private bool _inputOpen = true;

    internal VisibleFragmentRootProvider(
        nint window,
        PixelRect bounds,
        IReadOnlyList<VisibleFragmentDefinition> definitions)
    {
        _window = window;
        _bounds = bounds;
        Children = definitions
            .Select((definition, index) =>
                new VisibleFragmentChildProvider(
                    this,
                    definition,
                    index))
            .ToArray();
    }

    internal IReadOnlyList<VisibleFragmentChildProvider> Children
    {
        get;
    }

    internal bool InputOpen => Volatile.Read(ref _inputOpen);

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(_window);

    public Rect BoundingRectangle =>
        new(_bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height);

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return "Long Grid visible Fragment root";
        }

        if (propertyId
            == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return "LongGrid.VisibleFragmentRoot";
        }

        if (propertyId
            == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Pane.Id;
        }

        if (propertyId
            == AutomationElementIdentifiers.IsControlElementProperty.Id
            || propertyId
                == AutomationElementIdentifiers.IsContentElementProperty.Id)
        {
            return true;
        }

        if (propertyId
            == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return InputOpen;
        }

        return null;
    }

    public IRawElementProviderFragment? Navigate(
        NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.FirstChild when Children.Count > 0 =>
                Children[0],
            NavigateDirection.LastChild when Children.Count > 0 =>
                Children[^1],
            _ => null,
        };

    public int[]? GetRuntimeId() => null;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
        throw new InvalidOperationException(
            "The visible Fragment root cannot receive keyboard focus.");
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(
        double x,
        double y)
    {
        if (!InputOpen)
        {
            return null;
        }

        return Children.FirstOrDefault(child =>
            child.BoundingRectangle.Contains(new Point(x, y)));
    }

    public IRawElementProviderFragment? GetFocus() => null;

    internal void SetInputOpen(bool inputOpen) =>
        Volatile.Write(ref _inputOpen, inputOpen);
}

internal sealed class VisibleFragmentChildProvider
    : IRawElementProviderFragment
{
    private readonly VisibleFragmentRootProvider _root;
    private readonly VisibleFragmentDefinition _definition;
    private readonly int _index;

    internal VisibleFragmentChildProvider(
        VisibleFragmentRootProvider root,
        VisibleFragmentDefinition definition,
        int index)
    {
        _root = root;
        _definition = definition;
        _index = index;
    }

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public Rect BoundingRectangle
    {
        get
        {
            Rect rootBounds = _root.BoundingRectangle;
            return new Rect(
                rootBounds.X + _definition.LocalBounds.Left,
                rootBounds.Y + _definition.LocalBounds.Top,
                _definition.LocalBounds.Width,
                _definition.LocalBounds.Height);
        }
    }

    public IRawElementProviderFragmentRoot FragmentRoot => _root;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return _definition.Name;
        }

        if (propertyId
            == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return _definition.AutomationId;
        }

        if (propertyId
            == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Group.Id;
        }

        if (propertyId
            == AutomationElementIdentifiers.IsControlElementProperty.Id
            || propertyId
                == AutomationElementIdentifiers.IsContentElementProperty.Id)
        {
            return true;
        }

        if (propertyId
            == AutomationElementIdentifiers.IsEnabledProperty.Id)
        {
            return _root.InputOpen;
        }

        return null;
    }

    public IRawElementProviderFragment? Navigate(
        NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => _root,
            NavigateDirection.PreviousSibling when _index > 0 =>
                _root.Children[_index - 1],
            NavigateDirection.NextSibling
                when _index + 1 < _root.Children.Count =>
                _root.Children[_index + 1],
            _ => null,
        };

    public int[] GetRuntimeId() =>
    [
        AutomationInteropProvider.AppendRuntimeId,
        _definition.RuntimeId,
    ];

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
        throw new InvalidOperationException(
            "The probe container cannot receive keyboard focus.");
    }
}

internal sealed record VisibleFragmentDefinition(
    int RuntimeId,
    string AutomationId,
    string Name,
    PixelRect LocalBounds);

internal sealed record VisibleFragmentClientSnapshot(
    bool TreeVerified);

internal sealed record VisibleFragmentOutcome(
    int ContainerCount,
    int OpenHitCount,
    int ClosedEscapeCount,
    int ClosedExternalProcessHitCount,
    int ReopenedHitCount,
    bool PassiveStylesPresent,
    bool TopmostStyleAbsent,
    bool UiaTreeVerified,
    int FragmentProviderPointHitCount,
    bool ClosedProviderPointReturnsNull,
    bool ClosedUiaPointExcludesFragments,
    bool ForegroundPreserved,
    ResourceSnapshot ResourcesCreated,
    bool HostDisposed);

internal sealed record VisibleInputUiaFragmentReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int ContainerCount,
    int OpenHitCount,
    int ClosedEscapeCount,
    int ClosedExternalProcessHitCount,
    int ReopenedHitCount,
    bool PassiveStylesPresent,
    bool TopmostStyleAbsent,
    bool UiaTreeVerified,
    int FragmentProviderPointHitCount,
    bool ClosedProviderPointReturnsNull,
    bool ClosedUiaPointExcludesFragments,
    bool ForegroundPreserved,
    bool SyntheticInputUsed,
    bool DisplayStateChanged,
    bool ExternalWindowStateChanged,
    uint UserObjectsBefore,
    uint UserObjectsCreated,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsCreated,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesCreated,
    int ProcessHandlesAfter,
    bool CleanupPassed,
    string Result,
    IReadOnlyList<string> Limitations);
