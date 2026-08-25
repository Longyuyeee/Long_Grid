using System.ComponentModel;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;
#if WINDOWS
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
#endif

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopContainerHeaderSurfaceInput(
    ProductDesktopContainerHeaderCommandKind Kind,
    string ContainerId,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

internal sealed record ProductDesktopContainerMenuSurfaceInput(
    ProductDesktopContainerMenuAction Action,
    string ContainerId,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

internal sealed record ProductDesktopKeyboardSelectionDecision(
    bool Cancel,
    ProductDesktopSelectionRequest? Request);

internal static class ProductDesktopKeyboardSelectionAdapter
{
    internal static ProductDesktopKeyboardSelectionDecision Map(
        ProductDesktopSelectionSnapshot? selection,
        int virtualKey,
        bool control,
        bool shift)
    {
        if (virtualKey == 0x1B)
        {
            return new(Cancel: true, Request: null);
        }

        if (selection is not null
            && virtualKey == 0x41
            && control
            && !shift)
        {
            return new(
                Cancel: false,
                Request: new(ProductDesktopSelectionAction.SelectAll));
        }

        ProductDesktopSelectionCommand? command = virtualKey switch
        {
            0x25 or 0x26 => ProductDesktopSelectionCommand.Previous,
            0x27 or 0x28 => ProductDesktopSelectionCommand.Next,
            0x24 => ProductDesktopSelectionCommand.First,
            0x23 => ProductDesktopSelectionCommand.Last,
            0x20 => ProductDesktopSelectionCommand.ActivateFocused,
            _ => null,
        };
        if (selection is null || command is null)
        {
            return new(Cancel: false, Request: null);
        }

        ProductDesktopSelectionModifiers modifiers =
            ProductDesktopSelectionModifiers.None;
        if (control)
        {
            modifiers |= ProductDesktopSelectionModifiers.Control;
        }
        if (shift)
        {
            modifiers |= ProductDesktopSelectionModifiers.Shift;
        }
        return new(
            Cancel: false,
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                selection,
                command.Value,
                modifiers));
    }
}

internal enum ProductDesktopActivationRegionKind
{
    EnterInteraction,
    ToggleCollapsed,
    ToggleLocked,
    OpenMoreMenu,
}

internal interface IProductDesktopInteractionActivationSource : IDisposable
{
    nint Handle { get; }

    string DisplayId { get; }

    bool ContractAttested { get; }

    bool IsVisible { get; }

    bool CanActivate { get; }

    bool OwnsForegroundWindow { get; }

    bool ApplyVisible();

    bool ApplyHidden();

    bool RequestKeyboardInteraction();

    void BindSelection(
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?> snapshot,
        Func<ProductDesktopSelectionRequest, bool> apply,
        Func<bool> cancel)
    {
    }

    void BindItemOpen(Func<ProductDesktopItemOpenSurfaceInput, bool> apply)
    {
    }

    void BindContainerLayout(
        Func<ProductDesktopContainerLayoutKeyboardCommand, bool> apply,
        Func<string?, bool> applyTitleFocus)
    {
    }


    void BindContainerHeaderCommand(
        Func<ProductDesktopContainerHeaderSurfaceInput, bool> apply)
    {
    }

    void BindContainerMenu(
        Func<string, ProductDesktopContainerMenuAvailability> availability,
        Func<ProductDesktopContainerMenuSurfaceInput, bool> apply)
    {
    }
}

internal interface IProductDesktopInteractionActivationSourceFactory
{
    IProductDesktopInteractionActivationSource Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        Func<ProductDesktopInteractionForwardedInput, bool> forwardAndConsume);
}

internal sealed class WindowsProductDesktopInteractionActivationSourceFactory
    : IProductDesktopInteractionActivationSourceFactory
{
    public IProductDesktopInteractionActivationSource Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        Func<ProductDesktopInteractionForwardedInput, bool> forwardAndConsume) =>
        WindowsProductDesktopInteractionActivationSource.Create(
            projection,
            instanceMarker,
            forwardAndConsume);
}

/// <summary>
/// Owns one product-controlled, per-display input window whose region contains
/// only the finite activation buttons. It never captures global input and never
/// synthesizes an operating-system input message.
/// </summary>
internal sealed class WindowsProductDesktopInteractionActivationSource
    : IProductDesktopInteractionActivationSource
{
    private const int ActivationButtonSizeDip = 32;
    private static long nextUserActionSequence;
    private static readonly NativeMethods.TimerProcedure MenuEvidenceTimer =
        static (_, _, _, _) => _ = NativeMethods.EndMenu();
    private readonly string className;
    private readonly nint module;
    private readonly WindowProcedure windowProcedure;
    private readonly ProductDesktopHostDisplayProjection projection;
    private readonly Func<ProductDesktopInteractionForwardedInput, bool>
        forwardAndConsume;
    private readonly ActivationRegion[] regions;
    private bool activationAvailable = true;
    private bool keyboardProxy;
    private Func<ProductDesktopInteractionSurfaceTransactionSnapshot?>
        selectionSnapshot = static () => null;
    private Func<ProductDesktopSelectionRequest, bool> applySelection =
        static _ => false;
    private Func<bool> cancelSelection = static () => false;
    private Func<ProductDesktopItemOpenSurfaceInput, bool> requestItemOpen =
        static _ => false;
    private Func<ProductDesktopContainerLayoutKeyboardCommand, bool>
        applyContainerLayout = static _ => false;
    private Func<string?, bool> applyContainerLayoutTitleFocus =
        static _ => false;
    private Func<ProductDesktopContainerHeaderSurfaceInput, bool>
        applyContainerHeaderCommand = static _ => false;
    private Func<string, ProductDesktopContainerMenuAvailability>
        containerMenuAvailability = static _ =>
            ProductDesktopContainerMenuAvailability.Unavailable;
    private Func<ProductDesktopContainerMenuSurfaceInput, bool>
        applyContainerMenu = static _ => false;
    private readonly object menuGate = new();
    private PendingMenuOpen? pendingMenuOpen;
    private bool menuOpen;
    private bool containerLayoutTitleFocused;
#if WINDOWS
    private ActivationUiaProvider? uiaProvider;
#endif
    private bool disposed;

    private WindowsProductDesktopInteractionActivationSource(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        Func<ProductDesktopInteractionForwardedInput, bool> forwardAndConsume)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Desktop interaction activation source requires Windows.");
        }

        this.projection = projection;
        this.forwardAndConsume = forwardAndConsume;
        InstanceMarker = instanceMarker != nint.Zero
            ? instanceMarker
            : throw new ArgumentOutOfRangeException(nameof(instanceMarker));
        regions = CreateRegions(projection);
        if (regions.Length == 0)
        {
            throw new ArgumentException(
                "An activation source requires at least one container.",
                nameof(projection));
        }

        module = NativeMethods.GetModuleHandle(null);
        if (module == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        className = $"LongGrid.ActivationSource.{Guid.NewGuid():N}";
        windowProcedure = WindowProc;
    }

    public nint Handle { get; private set; }

    public nint InstanceMarker { get; }

    public string DisplayId => projection.DisplayId;

    public bool IsVisible =>
        !disposed && Handle != nint.Zero && NativeMethods.IsWindowVisible(Handle);

    public bool CanActivate =>
        CommandSurfaceAvailable
        && regions.Any(region =>
            region.Kind == ProductDesktopActivationRegionKind.EnterInteraction
            && !region.IsLocked);

    private bool CommandSurfaceAvailable =>
        activationAvailable && !keyboardProxy && IsVisible && ContractAttested;

    public bool OwnsForegroundWindow =>
        !disposed
        && Handle != nint.Zero
        && NativeMethods.GetForegroundWindow() == Handle;

    public bool ContractAttested
    {
        get
        {
            if (disposed || Handle == nint.Zero)
            {
                return false;
            }

            long style = NativeMethods.GetWindowLongPtr(
                Handle,
                NativeMethods.GwlExStyle).ToInt64();
            const long stable = NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered;
            bool activationContract = !keyboardProxy
                && (style & NativeMethods.WsExNoActivate) != 0
                && NativeMethods.GetForegroundWindow() != Handle;
            bool proxyContract = keyboardProxy
                && (style & NativeMethods.WsExNoActivate) == 0
                && NativeMethods.GetForegroundWindow() == Handle
                && NativeMethods.GetFocus() == Handle;
            return (style & stable) == stable
                && (style & NativeMethods.WsExTopmost) == 0
                && NativeMethods.GetWindow(Handle, NativeMethods.GwOwner)
                    == nint.Zero
                && AttestFiniteRegion()
                && NativeMethods.GetProp(
                    Handle,
                    WindowsProductDesktopHostWindowInspector
                        .InstanceMarkerProperty) == InstanceMarker
#if WINDOWS
                && uiaProvider is not null
#endif
                && (activationContract || proxyContract);
        }
    }

    internal static WindowsProductDesktopInteractionActivationSource Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        Func<ProductDesktopInteractionForwardedInput, bool> forwardAndConsume)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(forwardAndConsume);
        var source = new WindowsProductDesktopInteractionActivationSource(
            projection,
            instanceMarker,
            forwardAndConsume);
        try
        {
            source.CreateNativeWindow();
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public bool ApplyVisible()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RestoreActivationWindowPolicy();
        activationAvailable = true;
        ApplyFiniteRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _ = NativeMethods.UpdateWindow(Handle);
        return IsVisible && ContractAttested;
    }

    public bool ApplyHidden()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CloseContainerMenu();
        RestoreActivationWindowPolicy();
        activationAvailable = false;
        ApplyEmptyRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        return !IsVisible && AttestRegion(expectEmpty: true);
    }

    public bool RequestKeyboardInteraction()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ActivationRegion? target = regions
            .Cast<ActivationRegion?>()
            .FirstOrDefault(region => region is { } candidate
                && candidate.Kind ==
                    ProductDesktopActivationRegionKind.EnterInteraction
                && !candidate.IsLocked);
        return CanActivate && target is { } region
            && Forward(
                region,
                ProductDesktopInteractionForwardedInputKind
                    .KeyboardActivation,
                isInjected: false,
                isAutoRepeat: false);
    }

    public void BindSelection(
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?> snapshot,
        Func<ProductDesktopSelectionRequest, bool> apply,
        Func<bool> cancel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(cancel);
        selectionSnapshot = snapshot;
        applySelection = apply;
        cancelSelection = cancel;
    }

    public void BindItemOpen(Func<ProductDesktopItemOpenSurfaceInput, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        requestItemOpen = apply;
    }

    public void BindContainerLayout(
        Func<ProductDesktopContainerLayoutKeyboardCommand, bool> apply,
        Func<string?, bool> applyTitleFocus)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(applyTitleFocus);
        applyContainerLayout = apply;
        applyContainerLayoutTitleFocus = applyTitleFocus;
    }

    public void BindContainerHeaderCommand(
        Func<ProductDesktopContainerHeaderSurfaceInput, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        applyContainerHeaderCommand = apply;
    }

    public void BindContainerMenu(
        Func<string, ProductDesktopContainerMenuAvailability> availability,
        Func<ProductDesktopContainerMenuSurfaceInput, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(apply);
        containerMenuAvailability = availability;
        applyContainerMenu = apply;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CloseContainerMenu();
        if (Handle != nint.Zero)
        {
#if WINDOWS
            _ = NativeMethods.UiaReturnRawElementProvider(
                Handle,
                nint.Zero,
                nint.Zero,
                nint.Zero);
            uiaProvider = null;
#endif
            _ = NativeMethods.RemoveProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty);
            _ = NativeMethods.DestroyWindow(Handle);
            Handle = nint.Zero;
        }

        _ = NativeMethods.UnregisterClass(className, module);
        GC.KeepAlive(windowProcedure);
    }

    private void CreateNativeWindow()
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProcedure = windowProcedure,
            Instance = module,
            ClassName = className,
            Cursor = NativeMethods.LoadCursor(nint.Zero, NativeMethods.ArrowCursor),
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        PixelRect workArea = projection.WorkArea;
        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered
                | NativeMethods.WsExNoActivate,
            className,
            "LongGrid desktop interaction activation source",
            NativeMethods.WsPopup,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            nint.Zero,
            nint.Zero,
            module,
            nint.Zero);
        if (Handle == nint.Zero
            || !NativeMethods.SetProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty,
                InstanceMarker))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

#if WINDOWS
        uiaProvider = new(
            Handle,
            projection,
            regions,
            InstanceMarker,
            CanInvokeRegion,
            region => InvokeRegion(
                region,
                sourceAttested: true,
                isInjected: false,
                isAutoRepeat: false,
                assistiveTechnology: true));
#endif

        if (!NativeMethods.SetLayeredWindowAttributes(
                Handle,
                0,
                byte.MaxValue,
                NativeMethods.LwaAlpha)
            || !ApplyVisible())
        {
            throw new InvalidOperationException(
                "Activation source failed its initial finite-region attestation.");
        }
    }

    private nint WindowProc(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        switch (message)
        {
#if WINDOWS
            case NativeMethods.WmGetObject
                when longParameter.ToInt64()
                    == AutomationInteropProvider.RootObjectId
                    && uiaProvider is not null:
                return AutomationInteropProvider.ReturnRawElementProvider(
                    window,
                    wordParameter,
                    longParameter,
                    uiaProvider);
#endif
            case NativeMethods.WmMouseActivate:
                return new nint(keyboardProxy
                    ? NativeMethods.MaActivate
                    : NativeMethods.MaNoActivate);
            case NativeMethods.WmNcHitTest:
                return new nint(NativeMethods.HtClient);
            case NativeMethods.WmLButtonDown:
                ActivationRegion? hit = FindRegion(
                    SignedLowWord(longParameter),
                    SignedHighWord(longParameter));
                if (hit is not null)
                {
                    InputMessageSource source = default;
                    bool observed = NativeMethods.GetCurrentInputMessageSource(
                        ref source);
                    _ = InvokeRegion(
                        hit.Value,
                        sourceAttested: observed,
                        isInjected: !observed
                            || source.OriginId == NativeMethods.ImoInjected,
                        isAutoRepeat: false,
                        assistiveTechnology: false);
                }
                return nint.Zero;
            case NativeMethods.WmOpenContainerMenu:
                ShowPendingContainerMenu(window);
                return nint.Zero;
            case NativeMethods.WmTimer
                when wordParameter.ToInt64() ==
                    NativeMethods.MenuEvidenceTimerId:
                _ = NativeMethods.KillTimer(
                    window,
                    NativeMethods.MenuEvidenceTimerId);
                _ = NativeMethods.EndMenu();
                return nint.Zero;
            case NativeMethods.WmKeyDown:
            case NativeMethods.WmSysKeyDown:
                if (keyboardProxy)
                {
                    HandleSelectionKey(wordParameter, longParameter);
                    return nint.Zero;
                }
                if (wordParameter.ToInt64() is NativeMethods.VkReturn
                    or NativeMethods.VkSpace)
                {
                    bool autoRepeat = (longParameter.ToInt64() & (1L << 30)) != 0;
                    InputMessageSource source = default;
                    bool observed = NativeMethods.GetCurrentInputMessageSource(
                        ref source);
                    ActivationRegion? target = regions
                        .Cast<ActivationRegion?>()
                        .FirstOrDefault(region => region is { } candidate
                            && candidate.Kind ==
                                ProductDesktopActivationRegionKind
                                    .EnterInteraction
                            && !candidate.IsLocked);
                    if (target is { } region)
                    {
                        _ = Forward(
                            region,
                            ProductDesktopInteractionForwardedInputKind
                                .KeyboardActivation,
                            isInjected: !observed
                                || source.OriginId == NativeMethods.ImoInjected,
                            autoRepeat);
                    }
                }
                return nint.Zero;
            case NativeMethods.WmEraseBackground:
                return new nint(1);
            case NativeMethods.WmPaint:
                PaintActivationButtons(window);
                return nint.Zero;
        }

        return NativeMethods.DefWindowProc(
            window,
            message,
            wordParameter,
            longParameter);
    }

    private void PaintActivationButtons(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out PaintStruct paint);
        if (deviceContext == nint.Zero)
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(0x00D67524);
        try
        {
            _ = NativeMethods.SetBkMode(deviceContext, NativeMethods.Transparent);
            _ = NativeMethods.SetTextColor(deviceContext, 0x00FFFFFF);
            foreach (ActivationRegion region in regions)
            {
                NativeRect bounds = new(
                    region.Left,
                    region.Top,
                    checked(region.Left + region.Width),
                    checked(region.Top + region.Height));
                _ = NativeMethods.FillRect(deviceContext, ref bounds, brush);
                _ = NativeMethods.DrawText(
                    deviceContext,
                    ButtonText(region),
                    -1,
                    ref bounds,
                    NativeMethods.DtCenter
                        | NativeMethods.DtVCenter
                        | NativeMethods.DtSingleLine);
            }
        }
        finally
        {
            _ = NativeMethods.DeleteObject(brush);
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private bool InvokeRegion(
        ActivationRegion region,
        bool sourceAttested,
        bool isInjected,
        bool isAutoRepeat,
        bool assistiveTechnology)
    {
        if (region.Kind == ProductDesktopActivationRegionKind.EnterInteraction)
        {
            return !region.IsLocked
                && Forward(
                    region,
                    assistiveTechnology
                        ? ProductDesktopInteractionForwardedInputKind
                            .AssistiveTechnologyActivation
                        : ProductDesktopInteractionForwardedInputKind
                            .PrimaryPointerPress,
                    isInjected,
                    isAutoRepeat);
        }
        if (region.Kind == ProductDesktopActivationRegionKind.OpenMoreMenu)
        {
            return QueueContainerMenu(
                region,
                sourceAttested,
                isInjected,
                isAutoRepeat);
        }
        if (!CommandSurfaceAvailable
            || !sourceAttested
            || isInjected
            || isAutoRepeat
            || string.IsNullOrWhiteSpace(region.ContainerId))
        {
            return false;
        }

        return applyContainerHeaderCommand(new(
            region.Kind == ProductDesktopActivationRegionKind.ToggleCollapsed
                ? ProductDesktopContainerHeaderCommandKind.ToggleCollapsed
                : ProductDesktopContainerHeaderCommandKind.ToggleLocked,
            region.ContainerId,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false));
    }

    private bool CanInvokeRegion(ActivationRegion region) =>
        CommandSurfaceAvailable
        && (region.Kind switch
        {
            ProductDesktopActivationRegionKind.EnterInteraction =>
                !region.IsLocked,
            ProductDesktopActivationRegionKind.OpenMoreMenu =>
                MenuHasAvailableAction(region.ContainerId),
            _ => true,
        });

    private static string ButtonText(ActivationRegion region) => region.Kind switch
    {
        ProductDesktopActivationRegionKind.EnterInteraction => "↗",
        ProductDesktopActivationRegionKind.ToggleCollapsed =>
            region.IsCollapsed ? "▾" : "▸",
        ProductDesktopActivationRegionKind.ToggleLocked =>
            region.IsLocked ? "解" : "锁",
        ProductDesktopActivationRegionKind.OpenMoreMenu => "⋯",
        _ => string.Empty,
    };

    private bool MenuHasAvailableAction(string containerId)
    {
        ProductDesktopContainerMenuAvailability availability =
            containerMenuAvailability(containerId);
        return availability.CanOpenRename
            || availability.CanOpenAppearance
            || availability.CanOpenSort;
    }

    private bool QueueContainerMenu(
        ActivationRegion region,
        bool sourceAttested,
        bool isInjected,
        bool isAutoRepeat)
    {
        if (!CommandSurfaceAvailable
            || !sourceAttested
            || isInjected
            || isAutoRepeat
            || string.IsNullOrWhiteSpace(region.ContainerId)
            || !MenuHasAvailableAction(region.ContainerId))
        {
            return false;
        }

        lock (menuGate)
        {
            if (pendingMenuOpen is not null || menuOpen)
            {
                return false;
            }
            pendingMenuOpen = new(
                region,
                sourceAttested,
                isInjected,
                isAutoRepeat);
        }

        if (NativeMethods.PostMessage(
                Handle,
                NativeMethods.WmOpenContainerMenu,
                nint.Zero,
                nint.Zero))
        {
            return true;
        }
        lock (menuGate)
        {
            pendingMenuOpen = null;
        }
        return false;
    }

    private void ShowPendingContainerMenu(nint window)
    {
        PendingMenuOpen? pending;
        lock (menuGate)
        {
            pending = pendingMenuOpen;
            pendingMenuOpen = null;
            if (pending is null || menuOpen)
            {
                return;
            }
            menuOpen = true;
        }

        nint menu = nint.Zero;
        try
        {
            if (!CommandSurfaceAvailable)
            {
                return;
            }
            ProductDesktopContainerMenuAvailability availability =
                containerMenuAvailability(pending.Region.ContainerId);
            menu = CreateContainerMenu(availability);
            if (menu == nint.Zero)
            {
                return;
            }

            NativePoint point = new(
                pending.Region.Left,
                checked(pending.Region.Top + pending.Region.Height));
            if (!NativeMethods.ClientToScreen(window, ref point))
            {
                return;
            }

            uint command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton
                    | NativeMethods.TpmNoNotify
                    | NativeMethods.TpmReturnCommand,
                point.X,
                point.Y,
                window,
                nint.Zero);
            ProductDesktopContainerMenuAction? action = command switch
            {
                NativeMethods.MenuRenameCommand =>
                    ProductDesktopContainerMenuAction.OpenRename,
                NativeMethods.MenuAppearanceCommand =>
                    ProductDesktopContainerMenuAction.OpenAppearance,
                NativeMethods.MenuSortCommand =>
                    ProductDesktopContainerMenuAction.OpenSort,
                NativeMethods.MenuDeleteCommand =>
                    ProductDesktopContainerMenuAction
                        .DeleteContainerConfiguration,
                _ => null,
            };
            if (action is { } selected)
            {
                _ = applyContainerMenu(new(
                    selected,
                    pending.Region.ContainerId,
                    pending.SourceAttested,
                    pending.IsInjected,
                    pending.IsAutoRepeat));
            }
        }
        finally
        {
            if (menu != nint.Zero)
            {
                _ = NativeMethods.DestroyMenu(menu);
            }
            lock (menuGate)
            {
                menuOpen = false;
            }
        }
    }

    private static nint CreateContainerMenu(
        ProductDesktopContainerMenuAvailability availability)
    {
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return nint.Zero;
        }

        bool appended = Append(
                NativeMethods.MenuRenameCommand,
                "重命名…",
                availability.CanOpenRename)
            && Append(
                NativeMethods.MenuAppearanceCommand,
                "外观…",
                availability.CanOpenAppearance)
            && Append(
                NativeMethods.MenuSortCommand,
                "方格列表排序…",
                availability.CanOpenSort)
            && NativeMethods.AppendMenu(
                menu,
                NativeMethods.MfSeparator,
                0,
                string.Empty)
            && Append(0, "创建规则（后续功能）", enabled: false)
            && Append(0, "生成 Portal / Tab（后续功能）", enabled: false)
            && NativeMethods.AppendMenu(
                menu,
                NativeMethods.MfSeparator,
                0,
                string.Empty)
            && Append(
                NativeMethods.MenuDeleteCommand,
                "删除方格配置…",
                availability.CanDeleteContainerConfiguration);
        if (appended)
        {
            return menu;
        }

        _ = NativeMethods.DestroyMenu(menu);
        return nint.Zero;

        bool Append(uint command, string text, bool enabled) =>
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MfString
                    | (enabled ? 0 : NativeMethods.MfGray),
                command,
                text);
    }

    private void CloseContainerMenu()
    {
        lock (menuGate)
        {
            pendingMenuOpen = null;
            if (!menuOpen)
            {
                return;
            }
        }
        _ = NativeMethods.EndMenu();
    }

    internal bool IsContainerMenuOpenForEvidence
    {
        get
        {
            lock (menuGate)
            {
                return menuOpen;
            }
        }
    }

    internal void ShowPendingContainerMenuForEvidence()
    {
        nuint timer = NativeMethods.SetTimer(
            Handle,
            NativeMethods.MenuEvidenceTimerId,
            1200,
            MenuEvidenceTimer);
        if (timer == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            ShowPendingContainerMenu(Handle);
        }
        finally
        {
            _ = NativeMethods.KillTimer(
                Handle,
                NativeMethods.MenuEvidenceTimerId);
        }
    }

    internal void SubmitSelectionKeyForEvidence(
        int virtualKey,
        bool control = false,
        bool shift = false,
        bool alt = false,
        bool isAutoRepeat = false) =>
        HandleSelectionKeyCore(
            virtualKey,
            control,
            shift,
            alt,
            isAutoRepeat);

    private bool Forward(
        ActivationRegion region,
        ProductDesktopInteractionForwardedInputKind kind,
        bool isInjected,
        bool isAutoRepeat)
    {
        if (!CanActivate)
        {
            return false;
        }

        activationAvailable = false;
        bool consumed = forwardAndConsume(new(
            Guid.NewGuid(),
            Interlocked.Increment(ref nextUserActionSequence),
            DateTimeOffset.UtcNow,
            kind,
            projection.DisplayId,
            region.ActivationX,
            region.ActivationY,
            SourceAttested: ContractAttested,
            IsInjected: isInjected,
            IsAutoRepeat: isAutoRepeat));
        if (!consumed)
        {
            activationAvailable = true;
        }
        else if (kind != ProductDesktopInteractionForwardedInputKind
                .AssistiveTechnologyActivation
            && !EnterKeyboardProxy())
        {
            _ = cancelSelection();
            RestoreActivationWindowPolicy();
            activationAvailable = true;
            _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
            _ = NativeMethods.UpdateWindow(Handle);
            consumed = false;
        }
        return consumed;
    }

    private void HandleSelectionKey(nint wordParameter, nint longParameter)
    {
        InputMessageSource inputSource = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref inputSource)
            || inputSource.OriginId == NativeMethods.ImoInjected)
        {
            return;
        }

        bool control =
            (NativeMethods.GetKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        bool shift =
            (NativeMethods.GetKeyState(NativeMethods.VkShift) & 0x8000) != 0;
        bool alt =
            (NativeMethods.GetKeyState(NativeMethods.VkMenu) & 0x8000) != 0;
        HandleSelectionKeyCore(
            checked((int)wordParameter.ToInt64()),
            control,
            shift,
            alt,
            (longParameter.ToInt64() & (1L << 30)) != 0);
    }

    private void HandleSelectionKeyCore(
        int virtualKey,
        bool control,
        bool shift,
        bool alt,
        bool isAutoRepeat)
    {
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
            selectionSnapshot();
        if (virtualKey == NativeMethods.VkReturn
            && !control
            && !shift
            && !alt
            && transaction?.Selection is
            {
                ContainerId: { } openContainerId,
                FocusedItemId: { } openItemId
            })
        {
            _ = requestItemOpen(new(
                openContainerId,
                openItemId,
                ProductDesktopItemOpenSource.KeyboardEnter,
                SourceAttested: true,
                IsInjected: false,
                IsAutoRepeat: isAutoRepeat));
            return;
        }
        ProductDesktopContainerLayoutKeyboardDecision layout =
            ProductDesktopContainerLayoutKeyboardAdapter.Map(
                containerLayoutTitleFocused,
                virtualKey,
                alt,
                control,
                shift);
        if (layout.Handled)
        {
            string? containerId = transaction?.Selection?.ContainerId;
            if (layout.TitleFocused is { } titleFocused)
            {
                if (!titleFocused
                    || (!string.IsNullOrWhiteSpace(containerId)
                        && applyContainerLayoutTitleFocus(containerId)))
                {
                    if (!titleFocused)
                    {
                        _ = applyContainerLayoutTitleFocus(null);
                    }
                    containerLayoutTitleFocused = titleFocused;
                }
                return;
            }
            if (layout.HasLayoutCommand
                && !string.IsNullOrWhiteSpace(containerId))
            {
                _ = applyContainerLayout(new(
                    containerId,
                    layout.Kind!.Value,
                    layout.DeltaXDip,
                    layout.DeltaYDip,
                    layout.ShiftPressed));
            }
            return;
        }
        ProductDesktopKeyboardSelectionDecision decision =
            ProductDesktopKeyboardSelectionAdapter.Map(
                transaction?.Selection,
                virtualKey,
                control,
                shift);
        if (decision.Cancel)
        {
            _ = cancelSelection();
            return;
        }
        if (decision.Request is not null)
        {
            _ = applySelection(decision.Request);
        }
    }

    private bool EnterKeyboardProxy()
    {
        long style = NativeMethods.GetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle).ToInt64();
        _ = NativeMethods.SetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle,
            new nint(style & ~NativeMethods.WsExNoActivate));
        keyboardProxy = true;
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShow);
        _ = NativeMethods.SetForegroundWindow(Handle);
        _ = NativeMethods.SetFocus(Handle);
        return ContractAttested;
    }

    private void RestoreActivationWindowPolicy()
    {
        if (Handle == nint.Zero)
        {
            return;
        }
        long style = NativeMethods.GetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle).ToInt64();
        _ = NativeMethods.SetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle,
            new nint(style | NativeMethods.WsExNoActivate));
        if (containerLayoutTitleFocused)
        {
            _ = applyContainerLayoutTitleFocus(null);
            containerLayoutTitleFocused = false;
        }
        keyboardProxy = false;
    }

    private ActivationRegion? FindRegion(int x, int y) => regions
        .Cast<ActivationRegion?>()
        .FirstOrDefault(region => region is { } value && value.Contains(x, y));

    private static ActivationRegion[] CreateRegions(
        ProductDesktopHostDisplayProjection projection)
    {
        double scale = projection.EffectiveDpi / 96d;
        int buttonSize = Math.Max(
            20,
            ProductDesktopHostSurfaceLayout.ToPixels(
                ActivationButtonSizeDip,
                scale));
        return projection.Containers
            .SelectMany((container, containerIndex) =>
            {
                PixelRect bounds = ProductDesktopHostSurfaceLayout
                    .GetContainerBounds(projection, container);
                int size = Math.Min(
                    buttonSize,
                    Math.Min(bounds.Width / 4, bounds.Height));
                int right = checked(bounds.Left + bounds.Width);
                return new[]
                {
                    CreateRegion(
                        right - (size * 4),
                        ProductDesktopActivationRegionKind.ToggleLocked),
                    CreateRegion(
                        right - (size * 3),
                        ProductDesktopActivationRegionKind.ToggleCollapsed),
                    CreateRegion(
                        right - (size * 2),
                        ProductDesktopActivationRegionKind.EnterInteraction),
                    CreateRegion(
                        right - size,
                        ProductDesktopActivationRegionKind.OpenMoreMenu),
                };

                ActivationRegion CreateRegion(
                    int left,
                    ProductDesktopActivationRegionKind kind) =>
                    new(
                        left,
                        bounds.Top,
                        size,
                        size,
                        checked(left + (size / 2)),
                        checked(bounds.Top + (size / 2)),
                        container.ContainerId,
                        container.Title,
                        containerIndex,
                        kind,
                        container.IsLocked,
                        container.IsCollapsed);
            })
            .ToArray();
    }

    private void ApplyFiniteRegion()
    {
        nint combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (combined == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool transferred = false;
        try
        {
            foreach (ActivationRegion region in regions)
            {
                nint button = NativeMethods.CreateRectRgn(
                    region.Left,
                    region.Top,
                    checked(region.Left + region.Width),
                    checked(region.Top + region.Height));
                if (button == nint.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (NativeMethods.CombineRgn(
                            combined,
                            combined,
                            button,
                            NativeMethods.RgnOr) == NativeMethods.Error)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(button);
                }
            }

            if (NativeMethods.SetWindowRgn(Handle, combined, redraw: true) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                _ = NativeMethods.DeleteObject(combined);
            }
        }
    }

    private void ApplyEmptyRegion()
    {
        nint empty = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (empty == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool transferred = false;
        try
        {
            if (NativeMethods.SetWindowRgn(Handle, empty, redraw: true) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                _ = NativeMethods.DeleteObject(empty);
            }
        }
    }

    private bool AttestFiniteRegion() => AttestRegion(expectEmpty: false);

    private bool AttestRegion(bool expectEmpty)
    {
        nint region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == nint.Zero)
        {
            return false;
        }

        try
        {
            int result = NativeMethods.GetWindowRgn(Handle, region);
            return expectEmpty
                ? result == NativeMethods.NullRegion
                : result is not (NativeMethods.Error or NativeMethods.NullRegion);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private static int SignedLowWord(nint value) =>
        unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) =>
        unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    internal readonly record struct ActivationRegion(
        int Left,
        int Top,
        int Width,
        int Height,
        int ActivationX,
        int ActivationY,
        string ContainerId = "",
        string Title = "",
        int ContainerIndex = 0,
        ProductDesktopActivationRegionKind Kind =
            ProductDesktopActivationRegionKind.EnterInteraction,
        bool IsLocked = false,
        bool IsCollapsed = false)
    {
        internal bool Contains(int x, int y) =>
            x >= Left
            && y >= Top
            && x < checked(Left + Width)
            && y < checked(Top + Height);
    }

    private sealed record PendingMenuOpen(
        ActivationRegion Region,
        bool SourceAttested,
        bool IsInjected,
        bool IsAutoRepeat);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }

    private delegate nint WindowProcedure(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal WindowProcedure WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputMessageSource
    {
        internal uint DeviceType;
        internal uint OriginId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        internal int Left = left;
        internal int Top = top;
        internal int Right = right;
        internal int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal nint DeviceContext;
        internal int Erase;
        internal NativeRect Paint;
        internal int Restore;
        internal int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }

    private static class NativeMethods
    {
        internal const int Error = 0;
        internal const int NullRegion = 1;
        internal const int RgnOr = 2;
        internal const int GwlExStyle = -20;
        internal const uint GwOwner = 4;
        internal const uint WmEraseBackground = 0x0014;
        internal const uint WmPaint = 0x000F;
        internal const uint WmGetObject = 0x003D;
        internal const uint WmNcHitTest = 0x0084;
        internal const uint WmKeyDown = 0x0100;
        internal const uint WmSysKeyDown = 0x0104;
        internal const uint WmLButtonDown = 0x0201;
        internal const uint WmMouseActivate = 0x0021;
        internal const uint WmTimer = 0x0113;
        internal const uint WmOpenContainerMenu = 0x8001;
        internal const int HtClient = 1;
        internal const int MaNoActivate = 3;
        internal const int MaActivate = 1;
        internal const long WsPopup = unchecked((long)0x80000000);
        internal const long WsExTopmost = 0x00000008;
        internal const long WsExToolWindow = 0x00000080;
        internal const long WsExNoActivate = 0x08000000;
        internal const long WsExLayered = 0x00080000;
        internal const uint LwaAlpha = 0x00000002;
        internal const int SwHide = 0;
        internal const int SwShowNoActivate = 4;
        internal const int SwShow = 5;
        internal const int VkReturn = 0x0D;
        internal const int VkSpace = 0x20;
        internal const int VkEscape = 0x1B;
        internal const int VkLeft = 0x25;
        internal const int VkUp = 0x26;
        internal const int VkRight = 0x27;
        internal const int VkDown = 0x28;
        internal const int VkHome = 0x24;
        internal const int VkEnd = 0x23;
        internal const int VkShift = 0x10;
        internal const int VkControl = 0x11;
        internal const int VkMenu = 0x12;
        internal const uint ImoInjected = 2;
        internal const int Transparent = 1;
        internal const uint DtCenter = 0x00000001;
        internal const uint DtVCenter = 0x00000004;
        internal const uint DtSingleLine = 0x00000020;
        internal const uint MfString = 0x00000000;
        internal const uint MfGray = 0x00000001;
        internal const uint MfSeparator = 0x00000800;
        internal const uint TpmRightButton = 0x00000002;
        internal const uint TpmNoNotify = 0x00000080;
        internal const uint TpmReturnCommand = 0x00000100;
        internal const uint MenuRenameCommand = 41001;
        internal const uint MenuAppearanceCommand = 41002;
        internal const uint MenuSortCommand = 41003;
        internal const uint MenuDeleteCommand = 41004;
        internal const nint MenuEvidenceTimerId = 49004;
        internal static readonly nint ArrowCursor = new(32512);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool UnregisterClass(string className, nint instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(
            long extendedStyle,
            string className,
            string windowName,
            long style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint parameter);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProc(
            nint window,
            uint message,
            nint wordParameter,
            nint longParameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(
            nint window,
            uint message,
            nint wordParameter,
            nint longParameter);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll")]
        internal static extern bool UpdateWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetWindow(nint window, uint command);

        [DllImport("user32.dll")]
        internal static extern nint LoadCursor(nint instance, nint cursorName);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern nint SetWindowLongPtr(
            nint window,
            int index,
            nint value);

        [DllImport("user32.dll")]
        internal static extern nint SetFocus(nint window);

        [DllImport("user32.dll")]
        internal static extern nint GetFocus();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern short GetKeyState(int virtualKey);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetProp(
            nint window,
            string propertyName,
            nint value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetProp(nint window, string propertyName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint RemoveProp(nint window, string propertyName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetLayeredWindowAttributes(
            nint window,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateRectRgn(
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern int CombineRgn(
            nint destination,
            nint source1,
            nint source2,
            int mode);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(nint value);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowRgn(
            nint window,
            nint region,
            bool redraw);

        [DllImport("user32.dll")]
        internal static extern int GetWindowRgn(nint window, nint region);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCurrentInputMessageSource(
            ref InputMessageSource inputMessageSource);

        [DllImport("user32.dll")]
        internal static extern nint BeginPaint(
            nint window,
            out PaintStruct paint);

        [DllImport("user32.dll")]
        internal static extern bool EndPaint(
            nint window,
            ref PaintStruct paint);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateSolidBrush(uint color);

        [DllImport("user32.dll")]
        internal static extern int FillRect(
            nint deviceContext,
            ref NativeRect bounds,
            nint brush);

        [DllImport("gdi32.dll")]
        internal static extern int SetBkMode(nint deviceContext, int mode);

        [DllImport("gdi32.dll")]
        internal static extern uint SetTextColor(
            nint deviceContext,
            uint color);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int DrawText(
            nint deviceContext,
            string text,
            int characterCount,
            ref NativeRect bounds,
            uint format);

        [DllImport("user32.dll")]
        internal static extern nint CreatePopupMenu();

        [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AppendMenu(
            nint menu,
            uint flags,
            uint identifier,
            string text);

        [DllImport("user32.dll")]
        internal static extern uint TrackPopupMenuEx(
            nint menu,
            uint flags,
            int x,
            int y,
            nint window,
            nint parameters);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyMenu(nint menu);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(
            nint window,
            ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndMenu();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void TimerProcedure(
            nint window,
            uint message,
            nuint identifier,
            uint elapsedMilliseconds);

        [DllImport("user32.dll")]
        internal static extern nuint SetTimer(
            nint window,
            nint identifier,
            uint intervalMilliseconds,
            TimerProcedure? timerProcedure);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool KillTimer(
            nint window,
            nint identifier);

#if WINDOWS
        [DllImport("uiautomationcore.dll")]
        internal static extern nint UiaReturnRawElementProvider(
            nint window,
            nint wordParameter,
            nint longParameter,
            nint provider);
#endif
    }

#if WINDOWS
    internal sealed class ActivationUiaProvider
        : IRawElementProviderFragmentRoot
    {
        private readonly nint window;
        private readonly ProductDesktopHostDisplayProjection projection;
        private readonly ActivationUiaButtonProvider[] buttons;
        private readonly Func<ActivationRegion, bool> isAvailable;

        internal ActivationUiaProvider(
            nint window,
            ProductDesktopHostDisplayProjection projection,
            IReadOnlyList<ActivationRegion> regions,
            nint instanceMarker,
            Func<ActivationRegion, bool> isAvailable,
            Func<ActivationRegion, bool> invoke)
        {
            this.window = window;
            this.projection = projection;
            this.isAvailable = isAvailable;
            int marker = unchecked((int)instanceMarker.ToInt64());
            buttons = regions.Select((region, index) =>
                new ActivationUiaButtonProvider(
                    this,
                    region,
                    index,
                    marker,
                    isAvailable,
                    invoke)).ToArray();
        }

        public ProviderOptions ProviderOptions =>
            ProviderOptions.ServerSideProvider
                | ProviderOptions.UseComThreading;

        public IRawElementProviderSimple? HostRawElementProvider =>
            AutomationInteropProvider.HostProviderFromHandle(window);

        public Rect BoundingRectangle
        {
            get
            {
                Rect bounds = buttons[0].BoundingRectangle;
                foreach (ActivationUiaButtonProvider button in buttons.Skip(1))
                {
                    bounds.Union(button.BoundingRectangle);
                }
                return bounds;
            }
        }

        public IRawElementProviderFragmentRoot FragmentRoot => this;

        public object? GetPatternProvider(int patternId) => null;

        public object? GetPropertyValue(int propertyId) => propertyId switch
        {
            var id when id == AutomationElementIdentifiers.NameProperty.Id =>
                "桌面方格交互入口",
            var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                $"LongGrid.DesktopHost.Activation.{projection.DisplayId}",
            var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.Pane.Id,
            var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                buttons.Any(button => button.IsAvailable),
            var id when id == AutomationElementIdentifiers
                .IsKeyboardFocusableProperty.Id => false,
            _ => null,
        };

        public IRawElementProviderFragment? Navigate(
            NavigateDirection direction) => direction switch
            {
                NavigateDirection.FirstChild when buttons.Length > 0 => buttons[0],
                NavigateDirection.LastChild when buttons.Length > 0 => buttons[^1],
                _ => null,
            };

        public int[]? GetRuntimeId() => null;

        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus()
        {
        }

        public IRawElementProviderFragment? ElementProviderFromPoint(
            double x,
            double y)
        {
            var point = new Point(x, y);
            return buttons.FirstOrDefault(button =>
                button.BoundingRectangle.Contains(point));
        }

        public IRawElementProviderFragment? GetFocus() => null;

        internal IReadOnlyList<ActivationUiaButtonProvider> Buttons => buttons;

        internal Rect ScreenBounds(ActivationRegion region) => new(
            projection.WorkArea.Left + region.Left,
            projection.WorkArea.Top + region.Top,
            region.Width,
            region.Height);
    }

    internal sealed class ActivationUiaButtonProvider(
        ActivationUiaProvider root,
        ActivationRegion region,
        int index,
        int marker,
        Func<ActivationRegion, bool> isAvailable,
        Func<ActivationRegion, bool> invoke)
        : IRawElementProviderFragment, IInvokeProvider
    {
        public ProviderOptions ProviderOptions => root.ProviderOptions;

        internal bool IsAvailable => isAvailable(region);

        public IRawElementProviderSimple? HostRawElementProvider => null;

        public Rect BoundingRectangle => root.ScreenBounds(region);

        public IRawElementProviderFragmentRoot FragmentRoot => root;

        public object? GetPatternProvider(int patternId) =>
            patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;

        public object? GetPropertyValue(int propertyId) => propertyId switch
        {
            var id when id == AutomationElementIdentifiers.NameProperty.Id =>
                region.Kind switch
                {
                    ProductDesktopActivationRegionKind.EnterInteraction =>
                        string.IsNullOrWhiteSpace(region.Title)
                            ? "进入桌面方格交互"
                            : $"进入 {region.Title} 交互",
                    ProductDesktopActivationRegionKind.ToggleCollapsed =>
                        string.IsNullOrWhiteSpace(region.Title)
                            ? (region.IsCollapsed ? "展开桌面方格" : "折叠桌面方格")
                            : $"{(region.IsCollapsed ? "展开" : "折叠")} {region.Title}",
                    ProductDesktopActivationRegionKind.ToggleLocked =>
                        string.IsNullOrWhiteSpace(region.Title)
                            ? (region.IsLocked ? "解锁桌面方格" : "锁定桌面方格")
                            : $"{(region.IsLocked ? "解锁" : "锁定")} {region.Title}",
                    ProductDesktopActivationRegionKind.OpenMoreMenu =>
                        string.IsNullOrWhiteSpace(region.Title)
                            ? "更多桌面方格管理操作"
                            : $"更多 {region.Title} 管理操作",
                    _ => "桌面方格操作",
                },
            var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
                $"LongGrid.DesktopHost.ActivationButton.{index + 1}",
            var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
                ControlType.Button.Id,
            var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
                || id == AutomationElementIdentifiers.IsContentElementProperty.Id =>
                true,
            var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id =>
                IsAvailable,
            var id when id == AutomationElementIdentifiers
                .IsKeyboardFocusableProperty.Id => false,
            _ => null,
        };

        public IRawElementProviderFragment? Navigate(
            NavigateDirection direction) => direction switch
            {
                NavigateDirection.Parent => root,
                NavigateDirection.PreviousSibling when index > 0 =>
                    root.Buttons[index - 1],
                NavigateDirection.NextSibling
                    when index + 1 < root.Buttons.Count => root.Buttons[index + 1],
                _ => null,
            };

        public int[] GetRuntimeId() =>
        [
            AutomationInteropProvider.AppendRuntimeId,
            marker,
            5000 + index,
        ];

        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus()
        {
        }

        public void Invoke()
        {
            if (!IsAvailable || !invoke(region))
            {
                throw new ElementNotEnabledException();
            }
        }
    }
#endif
}
