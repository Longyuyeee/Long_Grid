using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

internal static class InteractiveDesktopHostSliceProbe
{
    private const uint SystemParametersGetWorkArea = 0x0030;

    internal static InteractiveDesktopHostSliceReport RunSmoke(
        bool perMonitorV2Requested)
    {
        WarmUp();
        CollectGarbage();
        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        ResourceSnapshot created = before;
        InteractiveSliceClientResult clientResult;
        bool passiveWindowStyle = false;
        bool topmostAbsent = false;
        bool nativeWindowTitleUnicodeVerified = false;
        bool initiallyDidNotActivate = false;
        bool hostNotForegroundAtCheckpoints = true;
        bool hiddenBeforeAutomatedPatterns = false;
        bool disposed = false;
        nint window = nint.Zero;

        try
        {
            using var host = InteractiveDesktopHostWindow.Create();
            window = host.Window;
            created = ResourceSnapshot.Capture(process);
            nativeWindowTitleUnicodeVerified =
                NativeMethods.ReadWindowText(host.Window)
                    == InteractiveDesktopHostWindow.WindowTitle;
            ulong extendedStyle = unchecked((ulong)
                NativeMethods.GetWindowLongPtr(
                    host.Window,
                    NativeMethods.GwlExStyle).ToInt64());
            passiveWindowStyle =
                (extendedStyle & NativeMethods.WsExToolWindow) != 0
                && (extendedStyle & NativeMethods.WsExNoActivate) == 0;
            topmostAbsent =
                (extendedStyle & NativeMethods.WsExTopmost) == 0;
            initiallyDidNotActivate =
                NativeMethods.GetForegroundWindow() != host.Window;
            hostNotForegroundAtCheckpoints &= initiallyDidNotActivate;
            hiddenBeforeAutomatedPatterns =
                host.HideForAutomationSmoke();
            clientResult = VerifyWithUiaClient(host);
            hostNotForegroundAtCheckpoints &=
                NativeMethods.GetForegroundWindow() != host.Window;
        }
        finally
        {
            disposed = window != nint.Zero
                && !NativeMethods.IsWindow(window);
            CollectGarbage();
        }

        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        bool externalForegroundStable =
            NativeMethods.GetForegroundWindow() == foregroundBefore;
        bool cleanupPassed =
            disposed
            && after.UserObjects <= before.UserObjects + 1
            && after.GdiObjects <= before.GdiObjects + 1
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool passed =
            perMonitorV2Requested
            && passiveWindowStyle
            && topmostAbsent
            && nativeWindowTitleUnicodeVerified
            && initiallyDidNotActivate
            && hiddenBeforeAutomatedPatterns
            && clientResult.TreeVerified
            && clientResult.PatternsVerified
            && clientResult.SelectionVerified
            && clientResult.InvokeVerified
            && clientResult.SelectionEventReceived
            && clientResult.InvokeEventReceived
            && hostNotForegroundAtCheckpoints
            && cleanupPassed;

        return new InteractiveDesktopHostSliceReport(
            Probe: "P0-04-P0-05b1-interactive-desktop-host-slice",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            ContainerCount: 1,
            ItemCount: 3,
            NativeWindowTitleUnicodeVerified:
                nativeWindowTitleUnicodeVerified,
            UiaTreeVerified: clientResult.TreeVerified,
            PatternsVerified: clientResult.PatternsVerified,
            SelectionVerified: clientResult.SelectionVerified,
            InvokeVerified: clientResult.InvokeVerified,
            SelectionEventReceived:
                clientResult.SelectionEventReceived,
            InvokeEventReceived: clientResult.InvokeEventReceived,
            ToolWindowPresent: passiveWindowStyle,
            TopmostAbsent: topmostAbsent,
            InitiallyDidNotActivate: initiallyDidNotActivate,
            HiddenBeforeAutomatedPatterns:
                hiddenBeforeAutomatedPatterns,
            HostNotForegroundAtCheckpoints:
                hostNotForegroundAtCheckpoints,
            ExternalForegroundStable: externalForegroundStable,
            SyntheticInputUsed: false,
            ExternalContentOpened: false,
            DesktopFilesReadOrChanged: false,
            DisplayStateChanged: false,
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
                "The slice renders one probe-owned container and three in-memory demonstration items; it never enumerates or opens desktop content.",
                "The smoke checks the initial visible no-activate state, then hides the activatable HWND before real UI Automation SelectionItem and Invoke calls so automation cannot take foreground.",
                "Manual --interactive-slice testing is still required for keyboard focus visuals, pointer hit targets, Narrator speech, touch, pen, drag-and-drop, high contrast, text scaling, and user judgment.",
                "The prototype uses GDI system colors to validate interaction semantics; DirectComposition visual quality and final design tokens remain separate work.",
                "The window is a non-topmost ToolWindow. It is shown without activation, but can receive focus after intentional user or assistive-technology action.",
            ]);
    }

    internal static int RunInteractive(bool perMonitorV2Requested)
    {
        Console.WriteLine(
            """
            Long Grid interactive DesktopHost slice
            - Click an item or use Tab/arrow keys after focusing the window.
            - Press Enter or Space to invoke an in-memory demonstration item.
            - Press Esc to close.
            - No desktop file is read, opened, moved, or changed.
            """);
        using var host = InteractiveDesktopHostWindow.Create();
        int result = host.RunMessageLoop();
        return perMonitorV2Requested && result == 0 ? 0 : 2;
    }

    private static InteractiveSliceClientResult VerifyWithUiaClient(
        InteractiveDesktopHostWindow host)
    {
        AutomationElement root =
            AutomationElement.FromHandle(host.Window);
        TreeWalker walker = TreeWalker.RawViewWalker;
        AutomationElement? container = walker.GetFirstChild(root);
        if (container is null
            || walker.GetNextSibling(container) is not null)
        {
            return InteractiveSliceClientResult.Failed;
        }

        var items = new List<AutomationElement>(3);
        AutomationElement? current = walker.GetFirstChild(container);
        while (current is not null)
        {
            items.Add(current);
            current = walker.GetNextSibling(current);
        }

        bool treeVerified =
            root.Current.AutomationId
                == "LongGrid.InteractiveSlice.Root"
            && container.Current.AutomationId
                == "LongGrid.InteractiveSlice.Container"
            && container.Current.ControlType == ControlType.List
            && items.Count == 3
            && items.Select(item => item.Current.AutomationId)
                .SequenceEqual(
                [
                    "LongGrid.InteractiveSlice.Item.1",
                    "LongGrid.InteractiveSlice.Item.2",
                    "LongGrid.InteractiveSlice.Item.3",
                ])
            && items.All(item =>
                item.Current.ControlType == ControlType.ListItem
                && item.Current.IsKeyboardFocusable
                && item.Current.IsEnabled);
        bool patternsVerified =
            container.TryGetCurrentPattern(
                SelectionPattern.Pattern,
                out object selectionObject)
            && items.All(item =>
                item.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out _)
                && item.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out _));
        if (!treeVerified
            || !patternsVerified
            || selectionObject is not SelectionPattern selection)
        {
            return new InteractiveSliceClientResult(
                treeVerified,
                patternsVerified,
                false,
                false,
                false,
                false);
        }

        using var selectionEvent = new ManualResetEventSlim();
        using var invokeEvent = new ManualResetEventSlim();
        AutomationEventHandler selectionHandler = (
            sender,
            args) =>
        {
            if (args.EventId
                == SelectionItemPattern.ElementSelectedEvent)
            {
                selectionEvent.Set();
            }
        };
        AutomationEventHandler invokeHandler = (
            sender,
            args) =>
        {
            if (args.EventId == InvokePattern.InvokedEvent)
            {
                invokeEvent.Set();
            }
        };
        Automation.AddAutomationEventHandler(
            SelectionItemPattern.ElementSelectedEvent,
            container,
            TreeScope.Descendants,
            selectionHandler);
        Automation.AddAutomationEventHandler(
            InvokePattern.InvokedEvent,
            container,
            TreeScope.Descendants,
            invokeHandler);
        bool selectionVerified;
        bool invokeVerified;
        try
        {
            var thirdSelection = (SelectionItemPattern)
                items[2].GetCurrentPattern(
                    SelectionItemPattern.Pattern);
            thirdSelection.Select();
            AutomationElement[] selected =
                selection.Current.GetSelection();
            selectionVerified =
                host.SelectedIndex == 2
                && selected.Length == 1
                && selected[0].Current.AutomationId
                    == items[2].Current.AutomationId;

            var secondInvoke = (InvokePattern)
                items[1].GetCurrentPattern(InvokePattern.Pattern);
            secondInvoke.Invoke();
            invokeVerified =
                host.InvocationCount == 1
                && host.LastInvokedIndex == 1;
        }
        finally
        {
            Automation.RemoveAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                container,
                selectionHandler);
            Automation.RemoveAutomationEventHandler(
                InvokePattern.InvokedEvent,
                container,
                invokeHandler);
        }

        return new InteractiveSliceClientResult(
            treeVerified,
            patternsVerified,
            selectionVerified,
            invokeVerified,
            selectionEvent.Wait(TimeSpan.FromSeconds(2)),
            invokeEvent.Wait(TimeSpan.FromSeconds(2)));
    }

    private static void WarmUp()
    {
        using var host = InteractiveDesktopHostWindow.Create();
        if (!host.HideForAutomationSmoke())
        {
            throw new InvalidOperationException(
                "The interactive slice warm-up could not hide its window.");
        }

        InteractiveSliceClientResult result =
            VerifyWithUiaClient(host);
        if (!result.TreeVerified
            || !result.PatternsVerified
            || !result.SelectionVerified
            || !result.InvokeVerified)
        {
            throw new InvalidOperationException(
                "The interactive DesktopHost slice warm-up failed.");
        }
    }

    internal static PixelRect SelectWindowBounds()
    {
        if (!NativeMethods.SystemParametersInfo(
            SystemParametersGetWorkArea,
            0,
            out NativeRect workArea,
            0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        const int width = 640;
        const int height = 360;
        const int margin = 32;
        int availableWidth = workArea.Right - workArea.Left;
        int availableHeight = workArea.Bottom - workArea.Top;
        if (availableWidth < width + (margin * 2)
            || availableHeight < height + (margin * 2))
        {
            throw new InvalidOperationException(
                "The work area is too small for the interactive slice.");
        }

        return new PixelRect(
            workArea.Right - width - margin,
            workArea.Bottom - height - margin,
            width,
            height);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal sealed class InteractiveDesktopHostWindow : IDisposable
{
    internal const string WindowTitle =
        "Long Grid interactive DesktopHost slice";

    private static readonly string[] ItemNames =
    [
        "需求文档",
        "设计参考",
        "项目计划",
    ];

    private readonly string _className;
    private readonly nint _instance;
    private readonly WindowProcedure _windowProcedure;
    private readonly PixelRect _windowBounds;
    private bool _disposed;
    private bool _messageLoopRunning;
    private string _status = "选择演示项目；调用不会打开外部内容。";

    private InteractiveDesktopHostWindow(
        string className,
        nint instance,
        WindowProcedure windowProcedure,
        nint window,
        PixelRect windowBounds)
    {
        _className = className;
        _instance = instance;
        _windowProcedure = windowProcedure;
        Window = window;
        _windowBounds = windowBounds;
    }

    internal nint Window { get; }

    internal InteractiveSliceRootProvider Provider { get; private set; } =
        null!;

    internal int SelectedIndex { get; private set; }

    internal int InvocationCount { get; private set; }

    internal int LastInvokedIndex { get; private set; } = -1;

    internal bool HasKeyboardFocus =>
        NativeMethods.GetFocus() == Window;

    internal PixelRect ContainerLocalBounds =>
        new(20, 20, _windowBounds.Width - 40, _windowBounds.Height - 40);

    internal PixelRect RootLocalBounds =>
        new(0, 0, _windowBounds.Width, _windowBounds.Height);

    internal IReadOnlyList<PixelRect> ItemLocalBounds
    {
        get
        {
            PixelRect container = ContainerLocalBounds;
            int gap = 16;
            int availableWidth = container.Width - 40 - (gap * 2);
            int itemWidth = availableWidth / 3;
            return Enumerable.Range(0, 3)
                .Select(index => new PixelRect(
                    container.Left + 20
                        + (index * (itemWidth + gap)),
                    container.Top + 90,
                    itemWidth,
                    150))
                .ToArray();
        }
    }

    internal static InteractiveDesktopHostWindow Create()
    {
        PixelRect bounds =
            InteractiveDesktopHostSliceProbe.SelectWindowBounds();
        string className =
            $"LongGrid.P0.InteractiveSlice.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        InteractiveDesktopHostWindow? host = null;
        InteractiveSliceRootProvider? provider = null;
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
                if (host?._messageLoopRunning == true)
                {
                    NativeMethods.PostQuitMessage(0);
                }

                return nint.Zero;
            }

            return host?.HandleMessage(
                message,
                wordParameter,
                longParameter)
                ?? NativeMethods.DefWindowProc(
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
        try
        {
            window = NativeMethods.CreateWindowEx(
                NativeMethods.WsExToolWindow,
                className,
                WindowTitle,
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

            host = new InteractiveDesktopHostWindow(
                className,
                instance,
                procedure,
                window,
                bounds);
            provider = new InteractiveSliceRootProvider(host);
            host.Provider = provider;
            if (!NativeMethods.SetWindowPos(
                window,
                NativeMethods.HwndTop,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate
                | NativeMethods.SwpShowWindow)
                || !NativeMethods.UpdateWindow(window))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return host;
        }
        catch
        {
            if (window != nint.Zero)
            {
                _ = NativeMethods.DestroyWindow(window);
            }

            _ = NativeMethods.UnregisterClass(className, instance);
            throw;
        }
    }

    internal int RunMessageLoop()
    {
        _messageLoopRunning = true;
        int messageResult;
        while ((messageResult = NativeMethods.GetMessage(
            out WindowMessage message,
            nint.Zero,
            0,
            0)) > 0)
        {
            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
        }

        _messageLoopRunning = false;
        return messageResult < 0 ? 2 : 0;
    }

    internal bool HideForAutomationSmoke()
    {
        _ = NativeMethods.ShowWindow(Window, NativeMethods.SwHide);
        return !NativeMethods.IsWindowVisible(Window);
    }

    internal Rect GetScreenBounds(PixelRect local)
    {
        if (!NativeMethods.GetWindowRect(
            Window,
            out NativeRect windowRectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new Rect(
            windowRectangle.Left + local.Left,
            windowRectangle.Top + local.Top,
            local.Width,
            local.Height);
    }

    internal void SelectItem(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            ItemNames.Length);

        if (SelectedIndex == index)
        {
            return;
        }

        SelectedIndex = index;
        _status = $"已选择“{ItemNames[index]}”。";
        _ = NativeMethods.InvalidateRect(Window, nint.Zero, erase: true);
        Provider.Items[index].RaiseSelected();
    }

    internal void FocusItem(int index)
    {
        SelectItem(index);
        _ = NativeMethods.SetFocus(Window);
    }

    internal void InvokeItem(int index)
    {
        SelectItem(index);
        InvocationCount++;
        LastInvokedIndex = index;
        _status =
            $"已调用“{ItemNames[index]}”（仅原型状态，无外部操作）。";
        _ = NativeMethods.InvalidateRect(Window, nint.Zero, erase: true);
        Provider.Items[index].RaiseInvoked();
    }

    private nint HandleMessage(
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        switch (message)
        {
            case NativeMethods.WmPaint:
                Paint();
                return nint.Zero;
            case NativeMethods.WmLeftButtonDown:
                OnPointer(longParameter);
                return nint.Zero;
            case NativeMethods.WmKeyDown:
                OnKeyDown(unchecked((int)wordParameter.ToInt64()));
                return nint.Zero;
            case NativeMethods.WmSetFocus:
            case NativeMethods.WmKillFocus:
                _ = NativeMethods.InvalidateRect(
                    Window,
                    nint.Zero,
                    erase: true);
                if (message == NativeMethods.WmSetFocus)
                {
                    Provider.Items[SelectedIndex]
                        .RaiseFocusChanged();
                }

                return nint.Zero;
            case NativeMethods.WmClose:
                _ = NativeMethods.DestroyWindow(Window);
                return nint.Zero;
            default:
                return NativeMethods.DefWindowProc(
                    Window,
                    message,
                    wordParameter,
                    longParameter);
        }
    }

    private void OnPointer(nint longParameter)
    {
        long packed = longParameter.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        IReadOnlyList<PixelRect> items = ItemLocalBounds;
        for (int index = 0; index < items.Count; index++)
        {
            if (Contains(items[index], x, y))
            {
                SelectItem(index);
                _ = NativeMethods.SetFocus(Window);
                return;
            }
        }
    }

    private void OnKeyDown(int virtualKey)
    {
        switch (virtualKey)
        {
            case NativeMethods.VkTab:
                bool backwards =
                    NativeMethods.GetKeyState(NativeMethods.VkShift) < 0;
                MoveSelection(backwards ? -1 : 1);
                break;
            case NativeMethods.VkLeft:
            case NativeMethods.VkUp:
                MoveSelection(-1);
                break;
            case NativeMethods.VkRight:
            case NativeMethods.VkDown:
                MoveSelection(1);
                break;
            case NativeMethods.VkHome:
                SelectItem(0);
                break;
            case NativeMethods.VkEnd:
                SelectItem(ItemNames.Length - 1);
                break;
            case NativeMethods.VkReturn:
            case NativeMethods.VkSpace:
                InvokeItem(SelectedIndex);
                break;
            case NativeMethods.VkEscape:
                _ = NativeMethods.DestroyWindow(Window);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int next = (SelectedIndex + delta + ItemNames.Length)
            % ItemNames.Length;
        SelectItem(next);
    }

    private void Paint()
    {
        nint deviceContext = NativeMethods.BeginPaint(
            Window,
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
            nint previousFont = NativeMethods.SelectObject(
                deviceContext,
                NativeMethods.GetStockObject(
                    NativeMethods.DefaultGuiFont));
            try
            {
                NativeRect client = ToNativeRect(
                    new PixelRect(
                        0,
                        0,
                        _windowBounds.Width,
                        _windowBounds.Height));
                _ = NativeMethods.FillRect(
                    deviceContext,
                    ref client,
                    NativeMethods.GetSysColorBrush(
                        NativeMethods.ColorWindow));

                NativeRect container = ToNativeRect(
                    ContainerLocalBounds);
                _ = NativeMethods.FillRect(
                    deviceContext,
                    ref container,
                    NativeMethods.GetSysColorBrush(
                        NativeMethods.ColorBtnFace));
                _ = NativeMethods.FrameRect(
                    deviceContext,
                    ref container,
                    NativeMethods.GetSysColorBrush(
                        NativeMethods.ColorWindowText));

                _ = NativeMethods.SetTextColor(
                    deviceContext,
                    NativeMethods.GetSysColor(
                        NativeMethods.ColorBtnText));
                DrawLabel(
                    deviceContext,
                    "当前项目 · 3 个演示项目",
                    new PixelRect(40, 36, 560, 28),
                    NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);
                DrawLabel(
                    deviceContext,
                    "Tab / 方向键选择 · Enter / Space 调用 · Esc 关闭",
                    new PixelRect(40, 64, 560, 22),
                    NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);

                IReadOnlyList<PixelRect> items = ItemLocalBounds;
                for (int index = 0; index < items.Count; index++)
                {
                    bool selected = index == SelectedIndex;
                    NativeRect item = ToNativeRect(items[index]);
                    _ = NativeMethods.FillRect(
                        deviceContext,
                        ref item,
                        NativeMethods.GetSysColorBrush(
                            selected
                                ? NativeMethods.ColorHighlight
                                : NativeMethods.ColorWindow));
                    _ = NativeMethods.FrameRect(
                        deviceContext,
                        ref item,
                        NativeMethods.GetSysColorBrush(
                            selected
                                ? NativeMethods.ColorHighlight
                                : NativeMethods.ColorWindowText));
                    _ = NativeMethods.SetTextColor(
                        deviceContext,
                        NativeMethods.GetSysColor(
                            selected
                                ? NativeMethods.ColorHighlightText
                                : NativeMethods.ColorWindowText));
                    DrawLabel(
                        deviceContext,
                        ItemNames[index],
                        new PixelRect(
                            items[index].Left + 8,
                            items[index].Top + 52,
                            items[index].Width - 16,
                            40),
                        NativeMethods.DtCenter
                        | NativeMethods.DtVCenter
                        | NativeMethods.DtSingleLine
                        | NativeMethods.DtEndEllipsis);
                    if (selected && HasKeyboardFocus)
                    {
                        PixelRect focusBounds = new(
                            items[index].Left + 4,
                            items[index].Top + 4,
                            items[index].Width - 8,
                            items[index].Height - 8);
                        NativeRect focus = ToNativeRect(focusBounds);
                        _ = NativeMethods.FrameRect(
                            deviceContext,
                            ref focus,
                            NativeMethods.GetSysColorBrush(
                                NativeMethods.ColorHighlightText));
                    }
                }

                _ = NativeMethods.SetTextColor(
                    deviceContext,
                    NativeMethods.GetSysColor(
                        NativeMethods.ColorWindowText));
                DrawLabel(
                    deviceContext,
                    _status,
                    new PixelRect(40, 286, 560, 32),
                    NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine
                    | NativeMethods.DtEndEllipsis);
            }
            finally
            {
                _ = NativeMethods.SelectObject(
                    deviceContext,
                    previousFont);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(Window, ref paint);
        }
    }

    private static void DrawLabel(
        nint deviceContext,
        string text,
        PixelRect bounds,
        uint format)
    {
        NativeRect rectangle = ToNativeRect(bounds);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            -1,
            ref rectangle,
            format);
    }

    private static NativeRect ToNativeRect(PixelRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

    private static bool Contains(
        PixelRect rectangle,
        int x,
        int y) =>
        x >= rectangle.Left
        && x < rectangle.Right
        && y >= rectangle.Top
        && y < rectangle.Bottom;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (NativeMethods.IsWindow(Window))
        {
            _ = NativeMethods.DestroyWindow(Window);
        }

        _ = NativeMethods.UnregisterClass(_className, _instance);
        _disposed = true;
        GC.KeepAlive(_windowProcedure);
        GC.KeepAlive(Provider);
    }
}

internal sealed class InteractiveSliceRootProvider
    : IRawElementProviderFragmentRoot
{
    private readonly InteractiveDesktopHostWindow _host;

    internal InteractiveSliceRootProvider(
        InteractiveDesktopHostWindow host)
    {
        _host = host;
        Container = new InteractiveSliceContainerProvider(
            host,
            this);
        Items = Container.Items;
    }

    internal InteractiveSliceContainerProvider Container { get; }

    internal IReadOnlyList<InteractiveSliceItemProvider> Items { get; }

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider
        | ProviderOptions.ProviderOwnsSetFocus
        | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(_host.Window);

    public Rect BoundingRectangle =>
        _host.GetScreenBounds(_host.RootLocalBounds);

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId) =>
        propertyId switch
        {
            var id when id
                == AutomationElementIdentifiers.NameProperty.Id =>
                "Long Grid 交互原型",
            var id when id
                == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                "LongGrid.InteractiveSlice.Root",
            var id when id
                == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.Pane.Id,
            var id when id
                == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id
                    == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                true,
            _ => null,
        };

    public IRawElementProviderFragment? Navigate(
        NavigateDirection direction) =>
        direction is NavigateDirection.FirstChild
            or NavigateDirection.LastChild
            ? Container
            : null;

    public int[]? GetRuntimeId() => null;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus() =>
        _host.FocusItem(_host.SelectedIndex);

    public IRawElementProviderFragment? ElementProviderFromPoint(
        double x,
        double y)
    {
        Point point = new(x, y);
        InteractiveSliceItemProvider? item = Items
            .FirstOrDefault(candidate =>
                candidate.BoundingRectangle.Contains(point));
        if (item is not null)
        {
            return item;
        }

        return Container.BoundingRectangle.Contains(point)
            ? Container
            : this;
    }

    public IRawElementProviderFragment? GetFocus() =>
        _host.HasKeyboardFocus
            ? Items[_host.SelectedIndex]
            : null;
}

internal sealed class InteractiveSliceContainerProvider
    : IRawElementProviderFragment, ISelectionProvider
{
    private readonly InteractiveDesktopHostWindow _host;
    private readonly InteractiveSliceRootProvider _root;

    internal InteractiveSliceContainerProvider(
        InteractiveDesktopHostWindow host,
        InteractiveSliceRootProvider root)
    {
        _host = host;
        _root = root;
        Items = Enumerable.Range(0, 3)
            .Select(index => new InteractiveSliceItemProvider(
                host,
                root,
                this,
                index))
            .ToArray();
    }

    internal IReadOnlyList<InteractiveSliceItemProvider> Items { get; }

    public ProviderOptions ProviderOptions => _root.ProviderOptions;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public Rect BoundingRectangle =>
        _host.GetScreenBounds(_host.ContainerLocalBounds);

    public IRawElementProviderFragmentRoot FragmentRoot => _root;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public object? GetPatternProvider(int patternId) =>
        patternId == SelectionPatternIdentifiers.Pattern.Id
            ? this
            : null;

    public object? GetPropertyValue(int propertyId) =>
        propertyId switch
        {
            var id when id
                == AutomationElementIdentifiers.NameProperty.Id =>
                "当前项目，容器，3 个演示项目，已展开",
            var id when id
                == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                "LongGrid.InteractiveSlice.Container",
            var id when id
                == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.List.Id,
            var id when id
                == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id
                    == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                true,
            _ => null,
        };

    public IRawElementProviderFragment? Navigate(
        NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => _root,
            NavigateDirection.FirstChild => Items[0],
            NavigateDirection.LastChild => Items[^1],
            _ => null,
        };

    public int[] GetRuntimeId() =>
    [
        AutomationInteropProvider.AppendRuntimeId,
        200,
    ];

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus() =>
        _host.FocusItem(_host.SelectedIndex);

    public IRawElementProviderSimple[] GetSelection() =>
    [
        Items[_host.SelectedIndex],
    ];
}

internal sealed class InteractiveSliceItemProvider
    : IRawElementProviderFragment,
      ISelectionItemProvider,
      IInvokeProvider
{
    private static readonly string[] Names =
    [
        "需求文档，演示项目，目标可用",
        "设计参考，演示项目，目标可用",
        "项目计划，演示项目，目标可用",
    ];

    private readonly InteractiveDesktopHostWindow _host;
    private readonly InteractiveSliceRootProvider _root;
    private readonly InteractiveSliceContainerProvider _container;
    private readonly int _index;

    internal InteractiveSliceItemProvider(
        InteractiveDesktopHostWindow host,
        InteractiveSliceRootProvider root,
        InteractiveSliceContainerProvider container,
        int index)
    {
        _host = host;
        _root = root;
        _container = container;
        _index = index;
    }

    public ProviderOptions ProviderOptions => _root.ProviderOptions;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public Rect BoundingRectangle =>
        _host.GetScreenBounds(_host.ItemLocalBounds[_index]);

    public IRawElementProviderFragmentRoot FragmentRoot => _root;

    public bool IsSelected => _host.SelectedIndex == _index;

    public IRawElementProviderSimple SelectionContainer => _container;

    public object? GetPatternProvider(int patternId)
    {
        if (patternId == SelectionItemPatternIdentifiers.Pattern.Id
            || patternId == InvokePatternIdentifiers.Pattern.Id)
        {
            return this;
        }

        return null;
    }

    public object? GetPropertyValue(int propertyId) =>
        propertyId switch
        {
            var id when id
                == AutomationElementIdentifiers.NameProperty.Id =>
                Names[_index],
            var id when id
                == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                $"LongGrid.InteractiveSlice.Item.{_index + 1}",
            var id when id
                == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.ListItem.Id,
            var id when id
                == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id
                    == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id =>
                _host.HasKeyboardFocus && IsSelected,
            var id when id
                == AutomationElementIdentifiers.ItemStatusProperty.Id =>
                "演示项目；调用不会打开外部文件",
            _ => null,
        };

    public IRawElementProviderFragment? Navigate(
        NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => _container,
            NavigateDirection.PreviousSibling when _index > 0 =>
                _container.Items[_index - 1],
            NavigateDirection.NextSibling
                when _index + 1 < _container.Items.Count =>
                _container.Items[_index + 1],
            _ => null,
        };

    public int[] GetRuntimeId() =>
    [
        AutomationInteropProvider.AppendRuntimeId,
        301 + _index,
    ];

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus() => _host.FocusItem(_index);

    public void AddToSelection() => _host.SelectItem(_index);

    public void RemoveFromSelection()
    {
        throw new InvalidOperationException(
            "The prototype requires one selected item.");
    }

    public void Select() => _host.SelectItem(_index);

    public void Invoke() => _host.InvokeItem(_index);

    internal void RaiseSelected()
    {
        if (!AutomationInteropProvider.ClientsAreListening)
        {
            return;
        }

        var args = new AutomationEventArgs(
            SelectionItemPatternIdentifiers.ElementSelectedEvent);
        AutomationInteropProvider.RaiseAutomationEvent(
            SelectionItemPatternIdentifiers.ElementSelectedEvent,
            this,
            args);
    }

    internal void RaiseInvoked()
    {
        if (!AutomationInteropProvider.ClientsAreListening)
        {
            return;
        }

        var args = new AutomationEventArgs(
            InvokePatternIdentifiers.InvokedEvent);
        AutomationInteropProvider.RaiseAutomationEvent(
            InvokePatternIdentifiers.InvokedEvent,
            this,
            args);
    }

    internal void RaiseFocusChanged()
    {
        if (!AutomationInteropProvider.ClientsAreListening)
        {
            return;
        }

        var args = new AutomationEventArgs(
            AutomationElementIdentifiers.AutomationFocusChangedEvent);
        AutomationInteropProvider.RaiseAutomationEvent(
            AutomationElementIdentifiers.AutomationFocusChangedEvent,
            this,
            args);
    }
}

internal sealed record InteractiveSliceClientResult(
    bool TreeVerified,
    bool PatternsVerified,
    bool SelectionVerified,
    bool InvokeVerified,
    bool SelectionEventReceived,
    bool InvokeEventReceived)
{
    internal static InteractiveSliceClientResult Failed { get; } =
        new(false, false, false, false, false, false);
}

internal sealed record InteractiveDesktopHostSliceReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int ContainerCount,
    int ItemCount,
    bool NativeWindowTitleUnicodeVerified,
    bool UiaTreeVerified,
    bool PatternsVerified,
    bool SelectionVerified,
    bool InvokeVerified,
    bool SelectionEventReceived,
    bool InvokeEventReceived,
    bool ToolWindowPresent,
    bool TopmostAbsent,
    bool InitiallyDidNotActivate,
    bool HiddenBeforeAutomatedPatterns,
    bool HostNotForegroundAtCheckpoints,
    bool ExternalForegroundStable,
    bool SyntheticInputUsed,
    bool ExternalContentOpened,
    bool DesktopFilesReadOrChanged,
    bool DisplayStateChanged,
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
