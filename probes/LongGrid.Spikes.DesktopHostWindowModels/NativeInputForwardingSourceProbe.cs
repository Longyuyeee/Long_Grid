using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

internal static class NativeInputForwardingSourceProbe
{
    internal static NativeInputForwardingSourceReport Run(
        bool perMonitorV2Requested)
    {
        WarmUpUia();
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        uint userBefore = Resources(NativeMethods.GrUserObjects);
        uint gdiBefore = Resources(NativeMethods.GrGdiObjects);
        int handlesBefore = Process.GetCurrentProcess().HandleCount;
        bool pointerPrepared;
        bool keyboardPrepared;
        bool uiaPrepared;
        bool autoRepeatRejected;
        bool unsupportedKeyIgnored;
        bool foregroundStable;
        uint userCreated;
        uint gdiCreated;
        int handlesCreated;

        ProductDesktopInteractionIntentBridgeFeatureDecision bridgeDecision =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                ProductDesktopInteractionFeaturePolicy.Evaluate(
                    ProductDesktopHostFeaturePolicy.Evaluate("1"),
                    "1"),
                "1",
                "1");
        var bridge = new ProductDesktopInteractionIntentPreparationBridge(
            bridgeDecision);
        var forwarding = new ProductDesktopInteractionInputForwardingAdapter(
            ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                bridgeDecision,
                "1",
                "1"),
            bridge);
        ProductDesktopHostProjectionBatch batch = Batch();
        ProductDesktopInteractionEvidence evidence = Evidence();
        var results = new List<ProductDesktopInteractionInputForwardingResult>();
        long sequence = 0;

        using (var host = NativeInputForwardingProbeWindow.Create(
            (kind, x, y, autoRepeat) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                ProductDesktopInteractionInputForwardingResult result =
                    forwarding.Forward(
                        new(
                            Guid.NewGuid(),
                            Interlocked.Increment(ref sequence),
                            now,
                            kind,
                            "display-probe",
                            x,
                            y,
                            SourceAttested: true,
                            IsInjected: false,
                            IsAutoRepeat: autoRepeat),
                        batch,
                        evidence,
                        now);
                results.Add(result);
            }))
        {
            int before = results.Count;
            _ = NativeMethods.SendMessage(
                host.Window,
                NativeMethods.WmLeftButtonDown,
                nint.Zero,
                PackPoint(20, 20));
            pointerPrepared = results.Count == before + 1
                && results[^1].IsPrepared;

            before = results.Count;
            _ = NativeMethods.SendMessage(
                host.Window,
                NativeMethods.WmKeyDown,
                new nint(NativeMethods.VkReturn),
                nint.Zero);
            keyboardPrepared = results.Count == before + 1
                && results[^1].IsPrepared;

            before = results.Count;
            _ = NativeMethods.SendMessage(
                host.Window,
                NativeMethods.WmKeyDown,
                new nint(NativeMethods.VkSpace),
                new nint(1L << 30));
            autoRepeatRejected = results.Count == before + 1
                && results[^1].Snapshot.Status
                    == ProductDesktopInteractionInputForwardingStatus.InvalidInput
                && results[^1].PreparedIntent is null;

            before = results.Count;
            _ = NativeMethods.SendMessage(
                host.Window,
                NativeMethods.WmKeyDown,
                new nint('A'),
                nint.Zero);
            unsupportedKeyIgnored = results.Count == before;

            before = results.Count;
            AutomationElement element = AutomationElement.FromHandle(host.Window);
            bool patternAvailable = element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out object? pattern);
            if (patternAvailable)
            {
                ((InvokePattern)pattern!).Invoke();
            }

            uiaPrepared = patternAvailable
                && results.Count == before + 1
                && results[^1].IsPrepared;
            foregroundStable = NativeMethods.GetForegroundWindow()
                == foregroundBefore;
            userCreated = Resources(NativeMethods.GrUserObjects);
            gdiCreated = Resources(NativeMethods.GrGdiObjects);
            handlesCreated = Process.GetCurrentProcess().HandleCount;
        }

        _ = forwarding.Complete();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        uint userAfter = Resources(NativeMethods.GrUserObjects);
        uint gdiAfter = Resources(NativeMethods.GrGdiObjects);
        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        bool cleanup = userAfter == userBefore
            && gdiAfter <= gdiBefore + 1
            && handlesAfter <= handlesBefore + 2;
        bool passed = perMonitorV2Requested
            && pointerPrepared
            && keyboardPrepared
            && uiaPrepared
            && autoRepeatRejected
            && unsupportedKeyIgnored
            && foregroundStable
            && cleanup;
        return new(
            Probe: "B6c4-native-input-forwarding-source",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            ProbeOwnedWindowOnly: true,
            PointerMessagePreparedOnce: pointerPrepared,
            KeyboardMessagePreparedOnce: keyboardPrepared,
            UiaInvokePreparedOnce: uiaPrepared,
            AutoRepeatRejected: autoRepeatRejected,
            UnsupportedKeyIgnored: unsupportedKeyIgnored,
            ForegroundStable: foregroundStable,
            SyntheticWindowMessagesUsed: true,
            SendInputUsed: false,
            GlobalHooksInstalled: false,
            RawInputRegistered: false,
            PhysicalDeviceInputVerified: false,
            ExplicitInteractionEntered: false,
            DesktopFilesReadOrChanged: false,
            UserObjectsBefore: userBefore,
            UserObjectsCreated: userCreated,
            UserObjectsAfter: userAfter,
            GdiObjectsBefore: gdiBefore,
            GdiObjectsCreated: gdiCreated,
            GdiObjectsAfter: gdiAfter,
            ProcessHandlesBefore: handlesBefore,
            ProcessHandlesCreated: handlesCreated,
            ProcessHandlesAfter: handlesAfter,
            CleanupPassed: cleanup,
            Result: passed ? "Conditional Pass" : "Fail",
            Limitations:
            [
                "The HWND is owned only by this probe and is never attached to the production App or Explorer.",
                "Pointer and keyboard normalization are driven by synchronous window messages, not physical devices; SendInput is never used.",
                "UIA Invoke crosses the real HWND provider boundary, but Narrator speech and user intent remain manual evidence.",
                "Touch, pen, IME, key-state transitions, physical auto-repeat, accessibility clients, Win+D, full-screen, RDP and Explorer restart remain in the B6C3 manual matrix.",
                "The probe validates normalization-to-preparation only; it never consumes an Intent or enters Explicit interaction.",
            ]);
    }

    private static nint PackPoint(int x, int y) =>
        new((y << 16) | (x & 0xFFFF));

    private static void WarmUpUia()
    {
        using (var host = NativeInputForwardingProbeWindow.Create(
            (_, _, _, _) => { }))
        {
            AutomationElement element = AutomationElement.FromHandle(host.Window);
            if (element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out object? pattern))
            {
                ((InvokePattern)pattern!).Invoke();
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static uint Resources(uint type) =>
        NativeMethods.GetGuiResources(Process.GetCurrentProcess().Handle, type);

    private static ProductDesktopHostProjectionBatch Batch() =>
        ProductDesktopHostProjectionBatch.Create(
            7,
            9,
            new string('A', 64),
            [ProductDesktopHostDisplayProjection.Create(
                "display-probe",
                new(0, 0, 320, 240),
                96,
                [ProductDesktopHostReadOnlyProjection.Create(
                    "container-probe",
                    "Probe",
                    ["item"],
                    "#336699",
                    0.8,
                    isCollapsed: false,
                    xDip: 0,
                    yDip: 0,
                    widthDip: 200,
                    heightDip: 180)])]);

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: new HashSet<string>(
                ["container-probe"],
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(StringComparer.Ordinal));
}

internal sealed class NativeInputForwardingProbeWindow : IDisposable
{
    private readonly string className;
    private readonly nint instance;
    private readonly WindowProcedure windowProcedure;
    private readonly NativeInputForwardingInvokeProvider provider;
    private readonly Action<
        ProductDesktopInteractionForwardedInputKind,
        int,
        int,
        bool> forward;
    private bool disposed;

    private NativeInputForwardingProbeWindow(
        string className,
        nint instance,
        WindowProcedure windowProcedure,
        nint window,
        NativeInputForwardingInvokeProvider provider,
        Action<
            ProductDesktopInteractionForwardedInputKind,
            int,
            int,
            bool> forward)
    {
        this.className = className;
        this.instance = instance;
        this.windowProcedure = windowProcedure;
        Window = window;
        this.provider = provider;
        this.forward = forward;
    }

    internal nint Window { get; }

    internal static NativeInputForwardingProbeWindow Create(
        Action<
            ProductDesktopInteractionForwardedInputKind,
            int,
            int,
            bool> forward)
    {
        ArgumentNullException.ThrowIfNull(forward);
        string className = $"LongGrid.B6c4.InputSource.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        NativeInputForwardingProbeWindow? host = null;
        NativeInputForwardingInvokeProvider? provider = null;
        WindowProcedure procedure = (window, message, word, data) =>
        {
            if (message == NativeMethods.WmGetObject
                && data.ToInt64() == AutomationInteropProvider.RootObjectId
                && provider is not null)
            {
                return AutomationInteropProvider.ReturnRawElementProvider(
                    window,
                    word,
                    data,
                    provider);
            }

            if (message == NativeMethods.WmDestroy)
            {
                _ = NativeMethods.UiaReturnRawElementProvider(
                    window,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero);
                return nint.Zero;
            }

            return host?.HandleMessage(message, word, data)
                ?? NativeMethods.DefWindowProc(window, message, word, data);
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
                NativeMethods.WsExToolWindow
                | NativeMethods.WsExNoActivate
                | NativeMethods.WsExLayered,
                className,
                "Long Grid isolated input source probe",
                NativeMethods.WsPopup,
                32,
                32,
                320,
                240,
                nint.Zero,
                nint.Zero,
                instance,
                nint.Zero);
            if (window == nint.Zero
                || !NativeMethods.SetLayeredWindowAttributes(
                    window,
                    0,
                    1,
                    NativeMethods.LwaAlpha))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            provider = new(window, () => host!.ForwardAssistive());
            host = new(
                className,
                instance,
                procedure,
                window,
                provider,
                forward);
            if (!NativeMethods.SetWindowPos(
                window,
                NativeMethods.HwndTop,
                32,
                32,
                320,
                240,
                NativeMethods.SwpNoActivate
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpShowWindow))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
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

    private nint HandleMessage(uint message, nint word, nint data)
    {
        if (message == NativeMethods.WmLeftButtonDown)
        {
            long packed = data.ToInt64();
            int x = unchecked((short)(packed & 0xFFFF));
            int y = unchecked((short)((packed >> 16) & 0xFFFF));
            forward(
                ProductDesktopInteractionForwardedInputKind.PrimaryPointerPress,
                x,
                y,
                false);
            return nint.Zero;
        }

        if (message == NativeMethods.WmKeyDown)
        {
            int key = unchecked((int)word.ToInt64());
            if (key is NativeMethods.VkReturn or NativeMethods.VkSpace)
            {
                bool autoRepeat = (data.ToInt64() & (1L << 30)) != 0;
                forward(
                    ProductDesktopInteractionForwardedInputKind
                        .KeyboardActivation,
                    20,
                    20,
                    autoRepeat);
                return nint.Zero;
            }
        }

        return NativeMethods.DefWindowProc(Window, message, word, data);
    }

    private void ForwardAssistive() =>
        forward(
            ProductDesktopInteractionForwardedInputKind
                .AssistiveTechnologyActivation,
            20,
            20,
            false);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        GC.KeepAlive(provider);
        GC.KeepAlive(windowProcedure);
        if (NativeMethods.IsWindow(Window))
        {
            _ = NativeMethods.DestroyWindow(Window);
        }

        _ = NativeMethods.UnregisterClass(className, instance);
    }
}

internal sealed class NativeInputForwardingInvokeProvider(
    nint window,
    Action invoke) : IRawElementProviderSimple, IInvokeProvider
{
    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(window);

    public object? GetPatternProvider(int patternId) =>
        patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;

    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            "Long Grid isolated input source probe",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Button.Id,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            false,
        _ => null,
    };

    public void Invoke() => invoke();
}

internal sealed record NativeInputForwardingSourceReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    bool ProbeOwnedWindowOnly,
    bool PointerMessagePreparedOnce,
    bool KeyboardMessagePreparedOnce,
    bool UiaInvokePreparedOnce,
    bool AutoRepeatRejected,
    bool UnsupportedKeyIgnored,
    bool ForegroundStable,
    bool SyntheticWindowMessagesUsed,
    bool SendInputUsed,
    bool GlobalHooksInstalled,
    bool RawInputRegistered,
    bool PhysicalDeviceInputVerified,
    bool ExplicitInteractionEntered,
    bool DesktopFilesReadOrChanged,
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
