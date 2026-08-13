using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

internal static class NativeInteractionSurfaceModeProbe
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 13, 1, 0, 0, TimeSpan.Zero);

    internal static NativeInteractionSurfaceModeReport Run(
        bool perMonitorV2Requested)
    {
        WarmUp();
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        uint userBefore = Resources(NativeMethods.GrUserObjects);
        uint gdiBefore = Resources(NativeMethods.GrGdiObjects);
        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        bool successRoundTrip;
        bool uiaPassive;
        bool uiaExplicit;
        bool messagesVerified;
        bool applyFailureRestored;
        bool verificationFailureRestored;
        bool restoreFailureHidden;
        bool hideFailureReported;
        bool generationDriftRestored;
        bool baselineForegroundStable;
        bool initiallyHidden;
        uint userCreated;
        uint gdiCreated;
        int handlesCreated;

        using (var host = NativeInteractionProbeHost.Create())
        {
            initiallyHidden = !NativeMethods.IsWindowVisible(host.Window);
            host.PreparePassive();
            NativeInteractionSurfaceAdapter adapter = host.Adapter;
            ProductDesktopInteractionSurfaceEvidence passive =
                adapter.Capture().Evidence!;
            uiaPassive = host.VerifyUia(explicitMode: false);
            messagesVerified = passive.IsPassiveContract
                && host.NcHitTestResult == NativeMethods.HtTransparent
                && host.MouseActivateResult == NativeMethods.MaNoActivate
                && host.HasFullRegion;

            ProductDesktopInteractionSurfaceModeTransaction transaction =
                CreateTransaction(adapter);
            ProductDesktopInteractionSurfaceTransactionSnapshot entered =
                transaction.TryEnter(
                    Intent(),
                    Evidence(),
                    ["item-1", "item-2"],
                    FixedNow);
            uiaExplicit = entered.IsExplicit
                && host.VerifyUia(explicitMode: true)
                && host.NcHitTestResult == NativeMethods.HtClient
                && host.MouseActivateResult == NativeMethods.MaNoActivate;
            ProductDesktopInteractionSurfaceTransactionSnapshot cancelled =
                transaction.Cancel(
                    ProductDesktopInteractionCancellationSignal.EscapePressed,
                    FixedNow.AddMilliseconds(1));
            successRoundTrip = entered.IsExplicit
                && cancelled.Status
                    == ProductDesktopInteractionSurfaceTransactionStatus
                        .ReturnedPassive
                && cancelled.Surface?.IsPassiveContract == true
                && host.VerifyUia(explicitMode: false);

            host.PreparePassive();
            adapter.FailExplicitAfterMutation = true;
            ProductDesktopInteractionSurfaceTransactionSnapshot applyFailure =
                CreateTransaction(adapter).TryEnter(
                    Intent(Guid.NewGuid()),
                    Evidence(),
                    ["item-1"],
                    FixedNow);
            applyFailureRestored = applyFailure.Status
                    == ProductDesktopInteractionSurfaceTransactionStatus
                        .SurfaceApplyFailed
                && applyFailure.Surface?.IsPassiveContract == true
                && host.NcHitTestResult == NativeMethods.HtTransparent;
            adapter.FailExplicitAfterMutation = false;

            host.PreparePassive();
            adapter.CorruptExplicitEvidenceOnce = true;
            ProductDesktopInteractionSurfaceTransactionSnapshot verifyFailure =
                CreateTransaction(adapter).TryEnter(
                    Intent(Guid.NewGuid()),
                    Evidence(),
                    ["item-1"],
                    FixedNow);
            verificationFailureRestored = verifyFailure.Status
                    == ProductDesktopInteractionSurfaceTransactionStatus
                        .SurfaceVerificationFailed
                && verifyFailure.Surface?.IsPassiveContract == true;

            host.PreparePassive();
            adapter.FailExplicitAfterMutation = true;
            adapter.FailRestore = true;
            ProductDesktopInteractionSurfaceTransactionSnapshot hidden =
                CreateTransaction(adapter).TryEnter(
                    Intent(Guid.NewGuid()),
                    Evidence(),
                    ["item-1"],
                    FixedNow);
            restoreFailureHidden = hidden.Status
                    == ProductDesktopInteractionSurfaceTransactionStatus
                        .HiddenFailClosed
                && hidden.Surface?.IsHiddenContract == true
                && !NativeMethods.IsWindowVisible(host.Window)
                && host.HasEmptyRegion;

            host.PreparePassive();
            adapter.FailHide = true;
            ProductDesktopInteractionSurfaceTransactionSnapshot hideFailure =
                CreateTransaction(adapter).TryEnter(
                    Intent(Guid.NewGuid()),
                    Evidence(),
                    ["item-1"],
                    FixedNow);
            hideFailureReported = hideFailure.Status
                == ProductDesktopInteractionSurfaceTransactionStatus
                    .EmergencyHideFailed;
            adapter.FailHide = false;
            adapter.FailRestore = false;
            adapter.FailExplicitAfterMutation = false;

            host.PreparePassive();
            ProductDesktopInteractionSurfaceModeTransaction driftTransaction =
                CreateTransaction(adapter);
            _ = driftTransaction.TryEnter(
                Intent(Guid.NewGuid()),
                Evidence(),
                ["item-1"],
                FixedNow);
            ProductDesktopInteractionSurfaceTransactionSnapshot drift =
                driftTransaction.Cancel(
                    ProductDesktopInteractionCancellationSignal.EvidenceChanged,
                    FixedNow.AddMilliseconds(1),
                    Evidence() with { TopologyGeneration = 10 });
            generationDriftRestored = drift.Status
                    == ProductDesktopInteractionSurfaceTransactionStatus
                        .ReturnedPassive
                && drift.Surface?.IsPassiveContract == true;

            baselineForegroundStable =
                NativeMethods.GetForegroundWindow() == foregroundBefore
                && !passive.OwnsForeground;
            userCreated = Resources(NativeMethods.GrUserObjects);
            gdiCreated = Resources(NativeMethods.GrGdiObjects);
            handlesCreated = Process.GetCurrentProcess().HandleCount;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        uint userAfter = Resources(NativeMethods.GrUserObjects);
        uint gdiAfter = Resources(NativeMethods.GrGdiObjects);
        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        bool resourcePlateau = VerifyResourcePlateau(
            userAfter,
            gdiAfter,
            handlesAfter);
        bool cleanup = userAfter == userBefore
            && gdiAfter <= gdiBefore + 1
            && handlesAfter <= handlesBefore + 2
            && resourcePlateau;
        bool passed = perMonitorV2Requested
            && successRoundTrip
            && uiaPassive
            && uiaExplicit
            && messagesVerified
            && applyFailureRestored
            && verificationFailureRestored
            && restoreFailureHidden
            && hideFailureReported
            && generationDriftRestored
            && baselineForegroundStable
            && cleanup;
        return new(
            Probe: "B5-native-interaction-surface-mode-adapter",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            ProbeOwnedWindowOnly: true,
            InitiallyHidden: initiallyHidden,
            PassiveExplicitPassiveRoundTrip: successRoundTrip,
            PassiveUiaPatternFree: uiaPassive,
            ExplicitUiaSelectionAvailable: uiaExplicit,
            NcHitTestAndMouseActivateVerified: messagesVerified,
            ApplyFailureRestored: applyFailureRestored,
            VerificationFailureRestored: verificationFailureRestored,
            RestoreFailureHidden: restoreFailureHidden,
            HideFailureReported: hideFailureReported,
            GenerationDriftRestored: generationDriftRestored,
            ForegroundStable: baselineForegroundStable,
            SyntheticInputUsed: false,
            DesktopFilesReadOrChanged: false,
            ExplorerWindowInspected: false,
            UserObjectsBefore: userBefore,
            UserObjectsCreated: userCreated,
            UserObjectsAfter: userAfter,
            GdiObjectsBefore: gdiBefore,
            GdiObjectsCreated: gdiCreated,
            GdiObjectsAfter: gdiAfter,
            ProcessHandlesBefore: handlesBefore,
            ProcessHandlesCreated: handlesCreated,
            ProcessHandlesAfter: handlesAfter,
            RepeatedResourcePlateau: resourcePlateau,
            CleanupPassed: cleanup,
            Result: passed ? "Conditional Pass" : "Fail",
            Limitations:
            [
                "The adapter owns one anonymous probe HWND and never attaches to the production App or Explorer.",
                "Message behavior is verified synchronously without synthetic pointer or keyboard input.",
                "UI Automation is queried through the real HWND provider, but Narrator speech, touch, pen, drag-and-drop, and visual focus remain manual evidence.",
                "The window is shown nearly transparent and without activation only during bounded verification; it starts hidden and is destroyed before exit.",
                "UI Automation keeps one process-level GDI object and two process handles after first measured use; three further create/query/destroy cycles must remain exactly on that plateau.",
            ]);
    }

    private static ProductDesktopInteractionSurfaceModeTransaction
        CreateTransaction(NativeInteractionSurfaceAdapter adapter) =>
        new(
            new(ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("1"),
                "1")),
            adapter);

    private static void WarmUp()
    {
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var host = NativeInteractionProbeHost.Create();
            host.PreparePassive();
            _ = host.VerifyUia(explicitMode: false);
            _ = host.SetMode(ProductDesktopInteractionSurfaceMode.Explicit);
            _ = host.VerifyUia(explicitMode: true);
            _ = host.SetMode(ProductDesktopInteractionSurfaceMode.Hidden);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static bool VerifyResourcePlateau(
        uint expectedUser,
        uint expectedGdi,
        int expectedHandles)
    {
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using (var host = NativeInteractionProbeHost.Create())
            {
                host.PreparePassive();
                _ = host.VerifyUia(explicitMode: false);
                _ = host.SetMode(
                    ProductDesktopInteractionSurfaceMode.Explicit);
                _ = host.VerifyUia(explicitMode: true);
                _ = host.SetMode(ProductDesktopInteractionSurfaceMode.Hidden);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (Resources(NativeMethods.GrUserObjects) != expectedUser
                || Resources(NativeMethods.GrGdiObjects) != expectedGdi
                || Process.GetCurrentProcess().HandleCount != expectedHandles)
            {
                return false;
            }
        }

        return true;
    }

    private static ProductDesktopInteractionIntent Intent(Guid? id = null) =>
        new(
            id ?? Guid.Parse("33b34257-d738-41b8-b89a-aaf158ab91ed"),
            "container-1",
            7,
            9,
            11,
            FixedNow,
            FixedNow.AddSeconds(5));

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
                ["container-1"],
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(StringComparer.Ordinal));

    private static uint Resources(uint kind) =>
        NativeMethods.GetGuiResources(
            Process.GetCurrentProcess().Handle,
            kind);
}

internal sealed class NativeInteractionProbeHost : IDisposable
{
    private const int Width = 220;
    private const int Height = 140;
    private readonly string className;
    private readonly nint instance;
    private readonly WindowProcedure procedure;
    private bool disposed;
    private ProductDesktopInteractionSurfaceMode mode =
        ProductDesktopInteractionSurfaceMode.Hidden;

    private NativeInteractionProbeHost(
        string className,
        nint instance,
        WindowProcedure procedure,
        nint window)
    {
        this.className = className;
        this.instance = instance;
        this.procedure = procedure;
        Window = window;
        Provider = new(this);
        Adapter = new(this);
    }

    internal nint Window { get; }

    internal NativeInteractionRootProvider Provider { get; }

    internal NativeInteractionSurfaceAdapter Adapter { get; }

    internal ProductDesktopInteractionSurfaceMode Mode => mode;

    internal int NcHitTestResult => unchecked((int)NativeMethods.SendMessage(
        Window,
        NativeMethods.WmNcHitTest,
        nint.Zero,
        nint.Zero).ToInt64());

    internal int MouseActivateResult => checked((int)NativeMethods.SendMessage(
        Window,
        NativeMethods.WmMouseActivate,
        nint.Zero,
        nint.Zero).ToInt64());

    internal bool HasFullRegion =>
        ReadRegion(out bool containsPoint) > 0 && containsPoint;

    internal bool HasEmptyRegion =>
        ReadRegion(out bool containsPoint) == 1 && !containsPoint;

    internal static NativeInteractionProbeHost Create()
    {
        string className = $"LongGrid.B5.NativeSurface.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        NativeInteractionProbeHost? host = null;
        WindowProcedure procedure = (window, message, word, parameter) =>
        {
            if (message == NativeMethods.WmNcHitTest)
            {
                return new(host?.mode
                    == ProductDesktopInteractionSurfaceMode.Explicit
                        ? NativeMethods.HtClient
                        : NativeMethods.HtTransparent);
            }

            if (message == NativeMethods.WmMouseActivate)
            {
                return new(NativeMethods.MaNoActivate);
            }

            if (message == NativeMethods.WmGetObject
                && parameter.ToInt64() == AutomationInteropProvider.RootObjectId
                && host is not null)
            {
                return AutomationInteropProvider.ReturnRawElementProvider(
                    window,
                    word,
                    parameter,
                    host.Provider);
            }

            if (message == NativeMethods.WmDestroy)
            {
                _ = NativeMethods.UiaReturnRawElementProvider(
                    window,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero);
            }

            return NativeMethods.DefWindowProc(window, message, word, parameter);
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
            throw new InvalidOperationException("B5 window class registration failed.");
        }

        nint window = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow
            | NativeMethods.WsExNoActivate
            | NativeMethods.WsExLayered,
            className,
            "Long Grid B5 anonymous native surface",
            NativeMethods.WsPopup,
            40,
            40,
            Width,
            Height,
            nint.Zero,
            nint.Zero,
            instance,
            nint.Zero);
        if (window == nint.Zero)
        {
            _ = NativeMethods.UnregisterClass(className, instance);
            throw new InvalidOperationException("B5 probe HWND creation failed.");
        }

        host = new(className, instance, procedure, window);
        if (!NativeMethods.SetLayeredWindowAttributes(
                window,
                0,
                1,
                NativeMethods.LwaAlpha))
        {
            host.Dispose();
            throw new InvalidOperationException("B5 probe alpha setup failed.");
        }

        return host;
    }

    internal void PreparePassive()
    {
        mode = ProductDesktopInteractionSurfaceMode.Passive;
        SetFullRegion();
        _ = NativeMethods.ShowWindow(Window, NativeMethods.SwShowNoActivate);
    }

    internal bool SetMode(ProductDesktopInteractionSurfaceMode next)
    {
        mode = next;
        if (next == ProductDesktopInteractionSurfaceMode.Hidden)
        {
            SetEmptyRegion();
            _ = NativeMethods.ShowWindow(Window, NativeMethods.SwHide);
            return !NativeMethods.IsWindowVisible(Window);
        }

        SetFullRegion();
        _ = NativeMethods.ShowWindow(Window, NativeMethods.SwShowNoActivate);
        return NativeMethods.IsWindowVisible(Window);
    }

    internal bool VerifyUia(bool explicitMode)
    {
        AutomationElement root = AutomationElement.FromHandle(Window);
        bool focusable = (bool)root.GetCurrentPropertyValue(
            AutomationElement.IsKeyboardFocusableProperty);
        bool selection = root.TryGetCurrentPattern(
            SelectionPattern.Pattern,
            out object? pattern);
        return focusable == explicitMode
            && selection == explicitMode
            && (explicitMode ? pattern is SelectionPattern : pattern is null);
    }

    internal ProductDesktopInteractionSurfaceEvidence Evidence() =>
        new(
            mode,
            WindowRegistryGeneration: 11,
            Visible: NativeMethods.IsWindowVisible(Window),
            HitTestTransparent:
                NcHitTestResult == NativeMethods.HtTransparent,
            IsKeyboardFocusable:
                mode == ProductDesktopInteractionSurfaceMode.Explicit,
            SelectionPatternAvailable:
                mode == ProductDesktopInteractionSurfaceMode.Explicit,
            ToolWindow: HasExtendedStyle(NativeMethods.WsExToolWindow),
            NoActivate: HasExtendedStyle(NativeMethods.WsExNoActivate),
            Topmost: HasExtendedStyle(NativeMethods.WsExTopmost),
            HasOwner:
                NativeMethods.GetWindow(Window, NativeMethods.GwOwner)
                    != nint.Zero,
            OwnsForeground: NativeMethods.GetForegroundWindow() == Window);

    private bool HasExtendedStyle(uint value) =>
        (unchecked((ulong)NativeMethods.GetWindowLongPtr(
            Window,
            NativeMethods.GwlExStyle).ToInt64()) & value) == value;

    private int ReadRegion(out bool containsPoint)
    {
        containsPoint = false;
        nint region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == nint.Zero)
        {
            return 0;
        }

        try
        {
            int type = NativeMethods.GetWindowRgn(Window, region);
            containsPoint = NativeMethods.PtInRegion(region, 50, 50);
            return type;
        }
        finally
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private void SetFullRegion() => SetRegion(0, 0, Width, Height);

    private void SetEmptyRegion() => SetRegion(0, 0, 0, 0);

    private void SetRegion(int left, int top, int right, int bottom)
    {
        nint region = NativeMethods.CreateRectRgn(left, top, right, bottom);
        if (region == nint.Zero)
        {
            throw new InvalidOperationException("B5 region allocation failed.");
        }

        if (NativeMethods.SetWindowRgn(Window, region, redraw: false) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
            throw new InvalidOperationException("B5 region application failed.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(Window, NativeMethods.SwHide);
        if (NativeMethods.IsWindow(Window))
        {
            _ = NativeMethods.DestroyWindow(Window);
        }

        _ = NativeMethods.UnregisterClass(className, instance);
        disposed = true;
        GC.KeepAlive(procedure);
        GC.KeepAlive(Provider);
    }
}

internal sealed class NativeInteractionSurfaceAdapter(
    NativeInteractionProbeHost host)
    : IProductDesktopInteractionSurfaceModeAdapter
{
    internal bool FailExplicitAfterMutation { get; set; }

    internal bool CorruptExplicitEvidenceOnce { get; set; }

    internal bool FailRestore { get; set; }

    internal bool FailHide { get; set; }

    public ProductDesktopInteractionSurfaceCapture Capture()
    {
        ProductDesktopInteractionSurfaceEvidence evidence = host.Evidence();
        if (CorruptExplicitEvidenceOnce
            && evidence.Mode == ProductDesktopInteractionSurfaceMode.Explicit)
        {
            CorruptExplicitEvidenceOnce = false;
            evidence = evidence with { Topmost = true };
        }

        return new(true, evidence);
    }

    public bool ApplyExplicit(ProductDesktopInteractionLease lease)
    {
        bool applied = lease.WindowRegistryGeneration == 11
            && host.SetMode(ProductDesktopInteractionSurfaceMode.Explicit);
        return applied && !FailExplicitAfterMutation;
    }

    public bool ApplyPassive(long expectedWindowRegistryGeneration) =>
        expectedWindowRegistryGeneration == 11
        && host.SetMode(ProductDesktopInteractionSurfaceMode.Passive);

    public bool Restore(ProductDesktopInteractionSurfaceEvidence evidence) =>
        !FailRestore
        && evidence.WindowRegistryGeneration == 11
        && host.SetMode(evidence.Mode);

    public bool Hide(long expectedWindowRegistryGeneration) =>
        !FailHide
        && expectedWindowRegistryGeneration == 11
        && host.SetMode(ProductDesktopInteractionSurfaceMode.Hidden);
}

internal sealed class NativeInteractionRootProvider(
    NativeInteractionProbeHost host)
    : IRawElementProviderSimple, ISelectionProvider
{
    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider
        | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(host.Window);

    public bool CanSelectMultiple => true;

    public bool IsSelectionRequired => false;

    public object? GetPatternProvider(int patternId) =>
        host.Mode == ProductDesktopInteractionSurfaceMode.Explicit
        && patternId == SelectionPatternIdentifiers.Pattern.Id
            ? this
            : null;

    public object? GetPropertyValue(int propertyId) =>
        propertyId switch
        {
            var id when id == AutomationElementIdentifiers.NameProperty.Id =>
                "Long Grid B5 anonymous native surface",
            var id when id
                == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                "LongGrid.B5.NativeSurface",
            var id when id
                == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.List.Id,
            var id when id
                == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id
                == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                host.Mode != ProductDesktopInteractionSurfaceMode.Hidden,
            var id when id
                == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
                host.Mode == ProductDesktopInteractionSurfaceMode.Explicit,
            var id when id
                == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id =>
                false,
            _ => null,
        };

    public IRawElementProviderSimple[] GetSelection() => [];
}

internal sealed record NativeInteractionSurfaceModeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    bool ProbeOwnedWindowOnly,
    bool InitiallyHidden,
    bool PassiveExplicitPassiveRoundTrip,
    bool PassiveUiaPatternFree,
    bool ExplicitUiaSelectionAvailable,
    bool NcHitTestAndMouseActivateVerified,
    bool ApplyFailureRestored,
    bool VerificationFailureRestored,
    bool RestoreFailureHidden,
    bool HideFailureReported,
    bool GenerationDriftRestored,
    bool ForegroundStable,
    bool SyntheticInputUsed,
    bool DesktopFilesReadOrChanged,
    bool ExplorerWindowInspected,
    uint UserObjectsBefore,
    uint UserObjectsCreated,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsCreated,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesCreated,
    int ProcessHandlesAfter,
    bool RepeatedResourcePlateau,
    bool CleanupPassed,
    string Result,
    IReadOnlyList<string> Limitations);
