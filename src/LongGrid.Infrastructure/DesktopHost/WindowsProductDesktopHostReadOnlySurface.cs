using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopPointerSelectionCommand(
    string ContainerId,
    ProductDesktopSelectionRequest Request);

internal static class ProductDesktopPointerSelectionAdapter
{
    internal static ProductDesktopPointerSelectionCommand? Map(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        int x,
        int y,
        bool control,
        bool shift)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (transaction?.IsExplicit != true)
        {
            return null;
        }

        string? targetId = transaction.Selection?.ContainerId;
        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                targetId,
                StringComparison.Ordinal));
        if (container is null || container.IsCollapsed)
        {
            return null;
        }

        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            projection,
            container);
        double scale = projection.EffectiveDpi / 96d;
        int headerHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int itemHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.GetItemHeightDip(container),
            scale);
        int index = (y - bounds.Top - headerHeight) / itemHeight;
        if (x < bounds.Left || x >= bounds.Right
            || y < bounds.Top + headerHeight
            || y >= bounds.Bottom
            || index < 0)
        {
            return null;
        }

        if (index >= container.ItemIds.Count)
        {
            return control || shift
                ? null
                : new(
                    container.ContainerId,
                    new(ProductDesktopSelectionAction.Clear));
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
            container.ContainerId,
            new(
                ProductDesktopSelectionAction.SelectItem,
                modifiers,
                container.ItemIds[index]));
    }
}

internal sealed class WindowsProductDesktopHostReadOnlySurfaceFactory
    : IProductDesktopHostReadOnlySurfaceFactory
{
    public IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden) =>
        WindowsProductDesktopHostReadOnlySurface.Create(
            projection,
            instanceMarker,
            startHidden);
}

internal sealed class WindowsProductDesktopHostReadOnlySurface
    : IProductDesktopHostReadOnlySurface
{
    private const int EmptyCreateHotKeyId = 0x4C47;
    private readonly string className;
    private readonly nint module;
    private readonly WindowProcedure windowProcedure;
    private ProductDesktopHostDisplayProjection projection;
    private readonly bool startHidden;
    private Func<ProductDesktopInteractionSurfaceTransactionSnapshot?>
        selectionSnapshot = static () => null;
    private Func<string, ProductDesktopSelectionRequest, bool>
        applySelection = static (_, _) => false;
    private Func<ProductDesktopWorkspaceCreateInput, bool>
        requestWorkspaceCreate = static _ => false;
    private Func<ProductDesktopContainerLayoutSurfaceInput, bool>
        requestContainerLayout = static _ => false;
    private Func<ProductDesktopContainerHeaderSurfaceInput, bool>
        requestContainerHeaderCommand = static _ => false;
    private Func<ProductDesktopItemViewportSurfaceInput, bool>
        requestItemViewport = static _ => false;
    private Func<ProductDesktopItemOpenSurfaceInput, bool>
        requestItemOpen = static _ => false;
    private Func<object, string, bool> requestExplorerReferenceDrop =
        static (_, _) => false;
    private Func<ProductDesktopReferenceReassignmentSurfaceInput, bool>
        requestReferenceReassignment = static _ => false;
    private ProductDesktopItemOpenFeedback? itemOpenFeedback;
    private bool openItemsWithSingleClick;
    private ActiveContainerLayout? activeContainerLayout;
    private ProductDesktopMarqueeSelectionSession? activeMarqueeSelection;
    private ProductDesktopReferenceReassignmentSession?
        activeReferenceReassignment;
    private ProductDesktopHostReadOnlyProjection? containerLayoutPreview;
    private string? containerLayoutKeyboardFocusId;
    private NativePoint workspaceCreateDragStart;
    private NativePoint workspaceCreateDragCurrent;
    private bool workspaceCreateDragActive;
    private string? hoveredHeaderContainerId;
    private string? hoveredDropContainerId;
    private bool trackingMouseLeave;
    private volatile ProductDesktopInteractionSurfaceMode mode;
    private bool workspaceCreateHotKeyRegistered;
    private WindowsProductDesktopHostDropTarget? explorerDropTarget;
#if WINDOWS
    private WindowsProductDesktopHostUiaRootProvider? uiaProvider;
#endif
    private bool disposed;

    private WindowsProductDesktopHostReadOnlySurface(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DesktopHost read-only surface requires Windows.");
        }

        this.projection = projection;
        this.startHidden = startHidden;
        mode = startHidden
            ? ProductDesktopInteractionSurfaceMode.Hidden
            : ProductDesktopInteractionSurfaceMode.Passive;
        InstanceMarker = instanceMarker != nint.Zero
            ? instanceMarker
            : throw new ArgumentOutOfRangeException(nameof(instanceMarker));
        module = NativeMethods.GetModuleHandle(null);
        if (module == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        className = $"LongGrid.ReadOnlySurface.{Guid.NewGuid():N}";
        windowProcedure = WindowProc;
    }

    public nint Handle { get; private set; }

    public nint InstanceMarker { get; }

    public uint ProcessId { get; private set; }

    public uint ThreadId { get; private set; }

    public bool ReadOnlyAccessibilityAttested => uiaProvider is not null;

    internal bool WorkspaceKeyboardCreateAvailable =>
        workspaceCreateHotKeyRegistered;

    public bool PassiveWindowContractAttested =>
        !disposed
        && Handle != nint.Zero
        && mode == ProductDesktopInteractionSurfaceMode.Passive
        && NativeMethods.IsWindowVisible(Handle)
        && AttestStableWindowPolicy()
        && AttestWindowRegion(expectEmpty: false);

    public bool ExplicitWindowContractAttested =>
        !disposed
        && Handle != nint.Zero
        && mode == ProductDesktopInteractionSurfaceMode.Explicit
        && NativeMethods.IsWindowVisible(Handle)
        && AttestStableWindowPolicy()
        && AttestWindowRegion(expectEmpty: false)
        && uiaProvider is not null;

    public bool HiddenWindowContractAttested =>
        !disposed
        && Handle != nint.Zero
        && mode == ProductDesktopInteractionSurfaceMode.Hidden
        && !NativeMethods.IsWindowVisible(Handle)
        && AttestStableWindowPolicy()
        && AttestWindowRegion(expectEmpty: true);

    internal static WindowsProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var surface = new WindowsProductDesktopHostReadOnlySurface(
            projection,
            instanceMarker,
            startHidden);
        try
        {
            surface.CreateNativeWindow();
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private void CreateNativeWindow()
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = NativeMethods.CsDoubleClicks,
            WindowProcedure = windowProcedure,
            Instance = module,
            ClassName = className,
            Cursor = NativeMethods.LoadCursor(
                nint.Zero,
                NativeMethods.ArrowCursor),
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        PixelRect workArea = projection.WorkArea;

        uint extendedStyle = NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered
                | NativeMethods.WsExNoActivate;
        Handle = NativeMethods.CreateWindowEx(
            extendedStyle,
            className,
            "Long方格桌面只读宿主",
            NativeMethods.WsPopup,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            nint.Zero,
            nint.Zero,
            module,
            nint.Zero);
        if (Handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

#if WINDOWS
        uiaProvider = CreateUiaProvider();
#endif

        ThreadId = NativeMethods.GetWindowThreadProcessId(
            Handle,
            out uint processId);
        ProcessId = processId;
        if (ThreadId == 0
            || ProcessId == 0
            || !NativeMethods.SetProp(
                Handle,
                WindowsProductDesktopHostWindowInspector.InstanceMarkerProperty,
                InstanceMarker))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!NativeMethods.SetLayeredWindowAttributes(
                Handle,
                0,
                byte.MaxValue,
                NativeMethods.LwaAlpha))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        int cornerPreference = NativeMethods.DwmWindowCornerPreferenceRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        bool applied = startHidden ? ApplyHidden() : ApplyPassive();
        if (!applied)
        {
            throw new InvalidOperationException(
                "DesktopHost surface failed its initial mode attestation.");
        }

        explorerDropTarget = WindowsProductDesktopHostDropTarget.TryRegister(
            Handle,
            ResolveExplorerDropTarget,
            (dataObject, containerId) =>
                requestExplorerReferenceDrop(dataObject, containerId),
            ApplyExplorerDropHover);
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
                when longParameter.ToInt64() ==
                    System.Windows.Automation.Provider.AutomationInteropProvider.RootObjectId
                    && uiaProvider is not null:
                return System.Windows.Automation.Provider.AutomationInteropProvider
                    .ReturnRawElementProvider(
                        window,
                        wordParameter,
                        longParameter,
                        uiaProvider);
#endif
            case NativeMethods.WmEraseBackground:
                return new nint(1);
            case NativeMethods.WmNcHitTest:
                return new nint(ResolveHitTest(longParameter));
            case NativeMethods.WmMouseActivate:
                return new nint(NativeMethods.MaNoActivate);
            case NativeMethods.WmLButtonDown:
                if (!TryStartReferenceReassignment(
                        window,
                        wordParameter,
                        longParameter)
                    && !TryStartContainerLayout(window, wordParameter, longParameter)
                    && !HandleWorkspaceCreatePress(longParameter))
                {
                    if (!TryStartMarqueeSelection(
                            window,
                            wordParameter,
                            longParameter)
                        && !TryStartWorkspaceCreateDrag(
                            window,
                            wordParameter,
                            longParameter))
                    {
                        HandlePrimaryPointerPress(wordParameter, longParameter);
                    }
                }
                return nint.Zero;
            case NativeMethods.WmLButtonDoubleClick:
                if (!HandleHeaderDoubleClick(longParameter))
                {
                    _ = HandleItemDoubleClick(wordParameter, longParameter);
                }
                return nint.Zero;
            case NativeMethods.WmMouseMove:
                UpdateHeaderHover(window, longParameter);
                if (activeReferenceReassignment is not null)
                {
                    UpdateReferenceReassignment(longParameter);
                }
                else if (activeContainerLayout is not null)
                {
                    UpdateContainerLayout(longParameter);
                }
                else if (activeMarqueeSelection is not null)
                {
                    UpdateMarqueeSelection(longParameter);
                }
                else
                {
                    UpdateWorkspaceCreateDrag(longParameter);
                }
                return nint.Zero;
            case NativeMethods.WmMouseLeave:
                trackingMouseLeave = false;
                if (hoveredHeaderContainerId is not null)
                {
                    hoveredHeaderContainerId = null;
                    _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
                }
                return nint.Zero;
            case NativeMethods.WmMouseWheel:
                _ = HandleItemViewportWheel(wordParameter, longParameter);
                return nint.Zero;
            case NativeMethods.WmLButtonUp:
                if (activeReferenceReassignment is not null)
                {
                    CompleteReferenceReassignment(window, longParameter);
                }
                else if (activeContainerLayout is not null)
                {
                    CompleteContainerLayout(window, longParameter);
                }
                else if (activeMarqueeSelection is not null)
                {
                    CompleteMarqueeSelection(window, longParameter);
                }
                else
                {
                    CompleteWorkspaceCreateDrag(window, longParameter);
                }
                return nint.Zero;
            case NativeMethods.WmCancelMode:
                CancelContainerLayout(
                    window,
                    ProductDesktopContainerLayoutCancellationReason.CancelMode);
                CancelMarqueeSelection(window);
                CancelWorkspaceCreateDrag(window);
                CancelReferenceReassignment(window);
                return nint.Zero;
            case NativeMethods.WmCaptureChanged:
                CancelContainerLayout(
                    window,
                    ProductDesktopContainerLayoutCancellationReason.CaptureLost);
                CancelMarqueeSelection(window);
                CancelWorkspaceCreateDrag(window);
                CancelReferenceReassignment(window);
                return nint.Zero;
            case NativeMethods.WmKeyDown
                when wordParameter.ToInt64() == NativeMethods.VkEscape:
                CancelContainerLayout(
                    window,
                    ProductDesktopContainerLayoutCancellationReason.EscapePressed);
                CancelMarqueeSelection(window);
                CancelWorkspaceCreateDrag(window);
                CancelReferenceReassignment(window);
                return nint.Zero;
            case NativeMethods.WmRButtonUp:
                if (!HandleItemOpenFeedbackContextMenu(window, longParameter))
                {
                    _ = HandleWorkspaceCreateContextMenu(window, longParameter);
                }
                return nint.Zero;
            case NativeMethods.WmHotKey
                when wordParameter.ToInt64() == EmptyCreateHotKeyId:
                _ = SubmitWorkspaceCreateInput(
                    ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
                    isAutoRepeat: false);
                return nint.Zero;
            case NativeMethods.WmPaint:
                Paint(window);
                return nint.Zero;
            default:
                return NativeMethods.DefWindowProc(
                    window,
                    message,
                    wordParameter,
                    longParameter);
        }
    }

    public void BindSelection(
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?> snapshot,
        Func<string, ProductDesktopSelectionRequest, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(apply);
        selectionSnapshot = snapshot;
        applySelection = apply;
    }

    public void BindWorkspaceCreate(
        Func<ProductDesktopWorkspaceCreateInput, bool> requestCreate)
    {
        ArgumentNullException.ThrowIfNull(requestCreate);
        requestWorkspaceCreate = requestCreate;
    }

    public void BindExplorerReferenceDrop(
        Func<object, string, bool> requestDrop)
    {
        ArgumentNullException.ThrowIfNull(requestDrop);
        requestExplorerReferenceDrop = requestDrop;
    }

    public void BindReferenceReassignment(
        Func<ProductDesktopReferenceReassignmentSurfaceInput, bool> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        requestReferenceReassignment = request;
    }

    public void BindContainerLayout(
        Func<ProductDesktopContainerLayoutSurfaceInput, bool> requestLayout)
    {
        ArgumentNullException.ThrowIfNull(requestLayout);
        requestContainerLayout = requestLayout;
    }

    public void BindContainerHeaderCommand(
        Func<ProductDesktopContainerHeaderSurfaceInput, bool> requestCommand)
    {
        ArgumentNullException.ThrowIfNull(requestCommand);
        requestContainerHeaderCommand = requestCommand;
    }

    public void BindItemViewport(
        Func<ProductDesktopItemViewportSurfaceInput, bool> requestViewport)
    {
        ArgumentNullException.ThrowIfNull(requestViewport);
        requestItemViewport = requestViewport;
    }

    public void BindItemOpen(
        Func<ProductDesktopItemOpenSurfaceInput, bool> requestOpen)
    {
        ArgumentNullException.ThrowIfNull(requestOpen);
        requestItemOpen = requestOpen;
    }

    public bool ApplyItemOpenFeedback(ProductDesktopItemOpenFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        if (disposed
            || Handle == nint.Zero
            || !Enum.IsDefined(feedback.Status)
            || string.IsNullOrWhiteSpace(feedback.Message)
            || feedback.Message.Length > 160
            || !projection.Containers.Any(container =>
                string.Equals(
                    container.ContainerId,
                    feedback.ContainerId,
                    StringComparison.Ordinal)
                && container.ItemIds.Contains(
                    feedback.ItemId,
                    StringComparer.Ordinal)))
        {
            return false;
        }
        string? previous = itemOpenFeedback is { } old
            && string.Equals(old.ContainerId, feedback.ContainerId,
                StringComparison.Ordinal)
            && string.Equals(old.ItemId, feedback.ItemId,
                StringComparison.Ordinal)
                ? old.Message
                : null;
        itemOpenFeedback = feedback;
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
#if WINDOWS
        uiaProvider?.PublishItemOpenFeedback(
            feedback.ContainerId,
            feedback.ItemId,
            previous,
            feedback.Message);
#endif
        return true;
    }

    public bool ApplyItemOpenPolicy(bool singleClickEnabled)
    {
        if (disposed || Handle == nint.Zero)
        {
            return false;
        }
        openItemsWithSingleClick = singleClickEnabled;
        return true;
    }

    public bool ApplyPresentation(
        ProductDesktopHostDisplayProjection nextProjection)
    {
        ArgumentNullException.ThrowIfNull(nextProjection);
        if (disposed
            || Handle == nint.Zero
            || !string.Equals(
                projection.DisplayId,
                nextProjection.DisplayId,
                StringComparison.Ordinal)
            || projection.WorkArea != nextProjection.WorkArea
            || projection.EffectiveDpi != nextProjection.EffectiveDpi
            || projection.Containers.Select(container => container.ContainerId)
                .SequenceEqual(
                    nextProjection.Containers.Select(container =>
                        container.ContainerId),
                    StringComparer.Ordinal) is false)
        {
            return false;
        }
        CancelMarqueeSelection(Handle);
        CancelReferenceReassignment(Handle);
        projection = nextProjection;
        if (itemOpenFeedback is { } feedback
            && !projection.Containers.Any(container =>
                string.Equals(container.ContainerId, feedback.ContainerId,
                    StringComparison.Ordinal)
                && container.ItemIds.Contains(feedback.ItemId,
                    StringComparer.Ordinal)))
        {
            itemOpenFeedback = null;
        }
#if WINDOWS
        uiaProvider = CreateUiaProvider();
#endif
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        return true;
    }

#if WINDOWS
    private WindowsProductDesktopHostUiaRootProvider CreateUiaProvider() => new(
        Handle,
        projection,
        InstanceMarker,
        () => mode == ProductDesktopInteractionSurfaceMode.Explicit,
        () => selectionSnapshot(),
        (containerId, request) =>
        {
            bool applied = applySelection(containerId, request);
            if (applied)
            {
                RefreshSelection();
            }
            return applied;
        },
        () => requestWorkspaceCreate(new(
            ProductDesktopWorkspaceCreateInputKind.AssistiveInvoke,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false)),
        () => workspaceCreateHotKeyRegistered,
        (containerId, itemId) => requestItemOpen(new(
            containerId,
            itemId,
            ProductDesktopItemOpenSource.AssistiveInvoke,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false)),
        (containerId, itemId) => itemOpenFeedback is { } feedback
            && string.Equals(feedback.ContainerId, containerId,
                StringComparison.Ordinal)
            && string.Equals(feedback.ItemId, itemId,
                StringComparison.Ordinal)
                ? feedback.Message
                : null);
#endif

    internal bool SubmitItemViewportWheelForEvidence(
        string containerId,
        int wheelDelta,
        bool sourceAttested = true,
        bool isInjected = false) =>
        requestItemViewport(new(
            containerId,
            wheelDelta,
            sourceAttested,
            isInjected));

    internal nint DispatchWindowMessageForEvidence(
        uint message,
        nint wordParameter,
        nint longParameter) =>
        WindowProc(Handle, message, wordParameter, longParameter);

    internal bool SubmitPrimaryPointerForEvidence(
        int x,
        int y,
        bool control = false,
        bool shift = false,
        bool sourceAttested = true,
        bool isInjected = false) => HandlePrimaryPointerPressCore(
            x,
            y,
            control,
            shift,
            sourceAttested,
            isInjected);

    internal bool BeginReferenceReassignmentForEvidence(
        int x,
        int y,
        bool sourceAttested = true,
        bool isInjected = false) => TryStartReferenceReassignmentCore(
            Handle,
            x,
            y,
            sourceAttested,
            isInjected);

    internal bool UpdateReferenceReassignmentForEvidence(int x, int y) =>
        UpdateReferenceReassignmentCore(x, y);

    internal bool CompleteReferenceReassignmentForEvidence(int x, int y) =>
        CompleteReferenceReassignmentCore(Handle, x, y);

    internal void CancelReferenceReassignmentForEvidence() =>
        CancelReferenceReassignment(Handle);

    internal bool BeginMarqueeSelectionForEvidence(
        int x,
        int y,
        bool control = false,
        bool shift = false,
        bool sourceAttested = true,
        bool isInjected = false) => TryStartMarqueeSelectionCore(
            Handle,
            x,
            y,
            control,
            shift,
            sourceAttested,
            isInjected);

    internal bool UpdateMarqueeSelectionForEvidence(int x, int y) =>
        UpdateMarqueeSelectionCore(x, y);

    internal bool CompleteMarqueeSelectionForEvidence(int x, int y) =>
        CompleteMarqueeSelectionCore(Handle, x, y);

    internal void CancelMarqueeSelectionForEvidence() =>
        CancelMarqueeSelection(Handle);

    internal PixelRect? GetMarqueeBoundsForEvidence() =>
        activeMarqueeSelection is { } session
            ? ProductDesktopMarqueeSelectionAdapter.GetBounds(session)
            : null;

    internal bool SubmitItemOpenFeedbackActionForEvidence(
        ProductDesktopItemOpenSource source,
        bool sourceAttested = true,
        bool isInjected = false) => SubmitItemOpenFeedbackAction(
            source,
            sourceAttested,
            isInjected);

    public bool ApplyContainerLayoutPreview(
        string containerId,
        ProductContainerPlacementState? placement)
    {
        if (disposed || Handle == nint.Zero || string.IsNullOrWhiteSpace(containerId))
        {
            return false;
        }

        if (placement is null)
        {
            if (containerLayoutPreview is not null && !string.Equals(
                containerLayoutPreview.ContainerId,
                containerId,
                StringComparison.Ordinal))
            {
                return false;
            }
            containerLayoutPreview = null;
            _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
            return true;
        }

        ProductDesktopHostReadOnlyProjection[] matches = projection.Containers
            .Where(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        return ApplyContainerLayoutPreview(matches[0], placement);
    }

    public bool ApplyContainerLayoutPreview(
        ProductDesktopHostReadOnlyProjection source,
        ProductContainerPlacementState placement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placement);
        if (disposed
            || Handle == nint.Zero
            || string.IsNullOrWhiteSpace(source.ContainerId)
            || !string.Equals(
                placement.DisplayKey,
                projection.DisplayId,
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            ProductDesktopHostReadOnlyProjection candidate =
                ProductDesktopHostReadOnlyProjection.Create(
                source.ContainerId,
                source.Title,
                source.ItemNames,
                source.Color,
                source.Opacity,
                source.IsCollapsed,
                placement.XDip,
                placement.YDip,
                placement.WidthDip,
                placement.HeightDip,
                source.IsLocked,
                source.ItemIds,
                source.TotalItemCount,
                source.ItemVisuals,
                source.TitleVisibility,
                source.TitleDoubleClickAction);
            _ = ProductDesktopHostSurfaceLayout.GetContainerBounds(
                projection,
                candidate);
            containerLayoutPreview = candidate;
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException)
        {
            return false;
        }

        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        return true;
    }

    public bool ApplyContainerLayoutKeyboardFocus(string? containerId)
    {
        if (disposed || Handle == nint.Zero)
        {
            return false;
        }
        if (containerId is not null
            && (mode != ProductDesktopInteractionSurfaceMode.Explicit
                || projection.Containers.Count(candidate =>
                    !candidate.IsLocked
                    && string.Equals(
                        candidate.ContainerId,
                        containerId,
                        StringComparison.Ordinal)) != 1))
        {
            return false;
        }

        containerLayoutKeyboardFocusId = containerId;
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        return true;
    }

    internal PixelRect? GetContainerLayoutBoundsForEvidence(string containerId)
    {
        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal))
            ?? (containerLayoutPreview is not null && string.Equals(
                containerLayoutPreview.ContainerId,
                containerId,
                StringComparison.Ordinal)
                    ? containerLayoutPreview
                    : null);
        return container is null
            ? null
            : ToPixelRect(GetContainerBounds(container));
    }

    internal ProductDesktopContainerHeaderPresentation?
        GetContainerHeaderPresentationForEvidence(string containerId) =>
        projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal))
            ?.Header;

    internal bool IsContainerHeaderVisibleForEvidence(string containerId) =>
        projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal)) is { } container
        && IsHeaderVisible(container);

    internal bool IsSystemTypeIconAvailableForEvidence(
        string containerId,
        int itemIndex)
    {
        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal));
        return container is not null
            && itemIndex >= 0
            && itemIndex < container.ItemVisuals.Count
            && TryAcquireSystemIcon(container.ItemVisuals[itemIndex], out nint icon)
            && DestroyAcquiredIcon(icon);
    }

    internal ProductDesktopItemVisualPresentation?
        GetItemVisualForEvidence(string containerId, int itemIndex)
    {
        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate => string.Equals(
                candidate.ContainerId,
                containerId,
                StringComparison.Ordinal));
        return container is not null
            && itemIndex >= 0
            && itemIndex < container.ItemVisuals.Count
                ? container.ItemVisuals[itemIndex]
                : null;
    }

    internal int GetSystemTypeIconSizeForEvidence() =>
        ToPixels(20, projection.EffectiveDpi / 96d);

    internal int DrawThumbnailFrameForEvidence(
        ProductDesktopThumbnailFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        nint deviceContext = NativeMethods.GetDC(Handle);
        if (deviceContext == nint.Zero)
        {
            return 0;
        }
        try
        {
            return DrawThumbnailFrame(
                deviceContext,
                frame,
                0,
                0,
                Math.Max(1, GetSystemTypeIconSizeForEvidence()));
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(Handle, deviceContext);
        }
    }

    internal uint DrawThumbnailFrameAndReadCenterForEvidence(
        ProductDesktopThumbnailFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        nint deviceContext = NativeMethods.GetDC(Handle);
        if (deviceContext == nint.Zero)
        {
            return uint.MaxValue;
        }
        try
        {
            int size = Math.Max(1, GetSystemTypeIconSizeForEvidence());
            int lines = DrawThumbnailFrame(
                deviceContext,
                frame,
                0,
                0,
                size);
            return lines > 0
                ? NativeMethods.GetPixel(
                    deviceContext,
                    Math.Max(0, size / 2),
                    Math.Max(0, size / 2))
                : uint.MaxValue;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(Handle, deviceContext);
        }
    }

    internal bool SubmitContainerLayoutInput(
        ProductDesktopContainerLayoutSurfaceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return requestContainerLayout(input);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private bool TryStartContainerLayout(
        nint window,
        nint wordParameter,
        nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || activeContainerLayout is not null
            || activeMarqueeSelection is not null
            || workspaceCreateDragActive)
        {
            return false;
        }

        int x = SignedLowWord(longParameter);
        int y = SignedHighWord(longParameter);
        ProductDesktopContainerLayoutHitResult hit =
            ProductDesktopContainerLayoutHitTestAdapter.HitTest(projection, x, y);
        if (!hit.IsHit)
        {
            return false;
        }

        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source)
            || source.OriginId == NativeMethods.ImoInjected)
        {
            return false;
        }

        bool shift = (wordParameter.ToInt64() & NativeMethods.MkShift) != 0;
        NativePoint startScreen = ToScreenPoint(x, y);
        var active = new ActiveContainerLayout(
            hit.ContainerId!,
            hit.Kind!.Value,
            new(x, y),
            shift);
        if (!SubmitContainerLayoutInput(new(
                ProductDesktopContainerLayoutInputPhase.Begin,
                active.Kind,
                active.ContainerId,
                0,
                0,
                SnapEnabled: true,
                shift,
                ProductDesktopContainerLayoutCancellationReason.None,
                startScreen.X,
                startScreen.Y)))
        {
            return false;
        }

        activeContainerLayout = active;
        _ = NativeMethods.SetCapture(window);
        if (NativeMethods.GetCapture() == window)
        {
            return true;
        }

        CancelContainerLayout(
            window,
            ProductDesktopContainerLayoutCancellationReason.CaptureLost);
        return false;
    }

    private bool UpdateContainerLayout(nint longParameter)
    {
        if (activeContainerLayout is not { } active)
        {
            return false;
        }

        double scale = projection.EffectiveDpi / 96d;
        int currentX = SignedLowWord(longParameter);
        int currentY = SignedHighWord(longParameter);
        NativePoint currentScreen = ToScreenPoint(currentX, currentY);
        bool accepted = SubmitContainerLayoutInput(new(
            ProductDesktopContainerLayoutInputPhase.Update,
            active.Kind,
            active.ContainerId,
            (currentX - active.Start.X) / scale,
            (currentY - active.Start.Y) / scale,
            SnapEnabled: true,
            active.ShiftPressed,
            ProductDesktopContainerLayoutCancellationReason.None,
            currentScreen.X,
            currentScreen.Y));
        if (!accepted)
        {
            CancelContainerLayout(
                Handle,
                ProductDesktopContainerLayoutCancellationReason.HostInvalidated);
        }
        return accepted;
    }

    private void CompleteContainerLayout(nint window, nint longParameter)
    {
        if (activeContainerLayout is not { } active)
        {
            return;
        }

        if (!UpdateContainerLayout(longParameter))
        {
            return;
        }
        double scale = projection.EffectiveDpi / 96d;
        int currentX = SignedLowWord(longParameter);
        int currentY = SignedHighWord(longParameter);
        NativePoint currentScreen = ToScreenPoint(currentX, currentY);
        activeContainerLayout = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = SubmitContainerLayoutInput(new(
            ProductDesktopContainerLayoutInputPhase.Complete,
            active.Kind,
            active.ContainerId,
            (currentX - active.Start.X) / scale,
            (currentY - active.Start.Y) / scale,
            SnapEnabled: true,
            active.ShiftPressed,
            ProductDesktopContainerLayoutCancellationReason.None,
            currentScreen.X,
            currentScreen.Y));
    }

    private void CancelContainerLayout(
        nint window,
        ProductDesktopContainerLayoutCancellationReason reason)
    {
        if (activeContainerLayout is not { } active)
        {
            return;
        }

        activeContainerLayout = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = SubmitContainerLayoutInput(new(
            ProductDesktopContainerLayoutInputPhase.Cancel,
            active.Kind,
            active.ContainerId,
            0,
            0,
            SnapEnabled: true,
            active.ShiftPressed,
            reason));
    }

    public void RefreshSelection()
    {
        if (!disposed && Handle != nint.Zero)
        {
            _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
#if WINDOWS
            uiaProvider?.PublishSelectionChanges();
#endif
        }
    }

    private bool TryStartReferenceReassignment(
        nint window,
        nint wordParameter,
        nint longParameter)
    {
        if ((wordParameter.ToInt64()
                & (NativeMethods.MkControl | NativeMethods.MkShift)) != 0)
        {
            return false;
        }
        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source))
        {
            return false;
        }
        return TryStartReferenceReassignmentCore(
            window,
            SignedLowWord(longParameter),
            SignedHighWord(longParameter),
            sourceAttested: true,
            isInjected: source.OriginId == NativeMethods.ImoInjected);
    }

    private bool TryStartReferenceReassignmentCore(
        nint window,
        int x,
        int y,
        bool sourceAttested,
        bool isInjected)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || activeReferenceReassignment is not null
            || activeContainerLayout is not null
            || activeMarqueeSelection is not null
            || workspaceCreateDragActive
            || !sourceAttested
            || isInjected)
        {
            return false;
        }

        ProductDesktopReferenceReassignmentSession? session =
            ProductDesktopReferenceReassignmentAdapter.TryStart(
                projection,
                selectionSnapshot(),
                x,
                y);
        if (session is null)
        {
            return false;
        }

        activeReferenceReassignment = session;
        _ = NativeMethods.SetCapture(window);
        if (NativeMethods.GetCapture() == window)
        {
            return true;
        }
        activeReferenceReassignment = null;
        return false;
    }

    private void UpdateReferenceReassignment(nint longParameter) =>
        _ = UpdateReferenceReassignmentCore(
            SignedLowWord(longParameter),
            SignedHighWord(longParameter));

    private bool UpdateReferenceReassignmentCore(int x, int y)
    {
        if (activeReferenceReassignment is not { } session)
        {
            return false;
        }
        ProductDesktopReferenceReassignmentSession? updated =
            ProductDesktopReferenceReassignmentAdapter.TryUpdate(
                projection,
                selectionSnapshot(),
                session,
                x,
                y);
        if (updated is null)
        {
            CancelReferenceReassignment(Handle);
            return false;
        }
        activeReferenceReassignment = updated;
        ApplyExplorerDropHover(updated.HoveredTargetContainerId);
        return true;
    }

    private bool CompleteReferenceReassignment(
        nint window,
        nint longParameter) => CompleteReferenceReassignmentCore(
            window,
            SignedLowWord(longParameter),
            SignedHighWord(longParameter));

    private bool CompleteReferenceReassignmentCore(
        nint window,
        int x,
        int y)
    {
        if (activeReferenceReassignment is not { } session)
        {
            return false;
        }
        ProductDesktopReferenceReassignmentSession? updated =
            ProductDesktopReferenceReassignmentAdapter.TryUpdate(
                projection,
                selectionSnapshot(),
                session,
                x,
                y);
        ProductDesktopReferenceReassignmentSurfaceInput? input =
            updated is null
                ? null
                : ProductDesktopReferenceReassignmentAdapter.TryComplete(
                    projection,
                    selectionSnapshot(),
                    updated,
                    x,
                    y);
        bool restoreClick = updated is { DragThresholdReached: false };
        activeReferenceReassignment = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        ApplyExplorerDropHover(null);
        if (input is null)
        {
            return restoreClick
                && HandlePrimaryPointerPressCore(
                    session.StartX,
                    session.StartY,
                    control: false,
                    shift: false,
                    sourceAttested: true,
                    isInjected: false);
        }
        try
        {
            return requestReferenceReassignment(input);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private void CancelReferenceReassignment(nint window)
    {
        if (activeReferenceReassignment is null)
        {
            return;
        }
        activeReferenceReassignment = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        ApplyExplorerDropHover(null);
    }

    private void HandlePrimaryPointerPress(nint wordParameter, nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit)
        {
            return;
        }

        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source)
            || source.OriginId == NativeMethods.ImoInjected)
        {
            return;
        }

        long flags = wordParameter.ToInt64();
        _ = HandlePrimaryPointerPressCore(
            SignedLowWord(longParameter),
            SignedHighWord(longParameter),
            control: (flags & NativeMethods.MkControl) != 0,
            shift: (flags & NativeMethods.MkShift) != 0,
            sourceAttested: true,
            isInjected: false);
    }

    private bool HandlePrimaryPointerPressCore(
        int x,
        int y,
        bool control,
        bool shift,
        bool sourceAttested,
        bool isInjected)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || !sourceAttested
            || isInjected)
        {
            return false;
        }
        ProductDesktopPointerSelectionCommand? command =
            ProductDesktopPointerSelectionAdapter.Map(
                projection,
                selectionSnapshot(),
                x,
                y,
                control,
                shift);
        if (command is null)
        {
            return false;
        }
        bool selected = applySelection(command.ContainerId, command.Request);
        if (!openItemsWithSingleClick
            || control
            || shift
            || command.Request is not
            {
                Action: ProductDesktopSelectionAction.SelectItem,
                ItemId: { } itemId,
            })
        {
            return selected;
        }
        return requestItemOpen(new(
            command.ContainerId,
            itemId,
            ProductDesktopItemOpenSource.PointerSingleClick,
            sourceAttested,
            isInjected,
            IsAutoRepeat: false));
    }

    private bool HandleItemDoubleClick(
        nint wordParameter,
        nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || openItemsWithSingleClick
            || (wordParameter.ToInt64()
                & (NativeMethods.MkControl | NativeMethods.MkShift)) != 0)
        {
            return false;
        }
        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source)
            || source.OriginId == NativeMethods.ImoInjected)
        {
            return false;
        }
        ProductDesktopPointerSelectionCommand? hit =
            ProductDesktopPointerSelectionAdapter.Map(
                projection,
                selectionSnapshot(),
                SignedLowWord(longParameter),
                SignedHighWord(longParameter),
                control: false,
                shift: false);
        if (hit?.Request is not
            {
                Action: ProductDesktopSelectionAction.SelectItem,
                ItemId: { } itemId
            })
        {
            return false;
        }
        if (!applySelection(hit.ContainerId, hit.Request))
        {
            return false;
        }
        return requestItemOpen(new(
            hit.ContainerId,
            itemId,
            ProductDesktopItemOpenSource.PointerDoubleClick,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false));
    }

    private bool TryStartWorkspaceCreateDrag(
        nint window,
        nint wordParameter,
        nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || activeContainerLayout is not null
            || activeMarqueeSelection is not null
            || workspaceCreateDragActive
            || (wordParameter.ToInt64()
                & (NativeMethods.MkControl | NativeMethods.MkShift)) != 0)
        {
            return false;
        }

        int x = SignedLowWord(longParameter);
        int y = SignedHighWord(longParameter);
        if (projection.Containers.Any(container => Contains(
            ProductDesktopHostSurfaceLayout.GetContainerBounds(
                projection,
                container),
            x,
            y)))
        {
            return false;
        }

        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source)
            || source.OriginId == NativeMethods.ImoInjected)
        {
            return false;
        }

        workspaceCreateDragStart = new(x, y);
        workspaceCreateDragCurrent = workspaceCreateDragStart;
        workspaceCreateDragActive = true;
        _ = NativeMethods.SetCapture(window);
        if (NativeMethods.GetCapture() != window)
        {
            workspaceCreateDragActive = false;
            return false;
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
        return true;
    }

    private bool TryStartMarqueeSelection(
        nint window,
        nint wordParameter,
        nint longParameter)
    {
        InputMessageSource source = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref source))
        {
            return false;
        }

        long flags = wordParameter.ToInt64();
        return TryStartMarqueeSelectionCore(
            window,
            SignedLowWord(longParameter),
            SignedHighWord(longParameter),
            (flags & NativeMethods.MkControl) != 0,
            (flags & NativeMethods.MkShift) != 0,
            sourceAttested: true,
            isInjected: source.OriginId == NativeMethods.ImoInjected);
    }

    private bool TryStartMarqueeSelectionCore(
        nint window,
        int x,
        int y,
        bool control,
        bool shift,
        bool sourceAttested,
        bool isInjected)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || activeContainerLayout is not null
            || activeMarqueeSelection is not null
            || workspaceCreateDragActive
            || !sourceAttested
            || isInjected)
        {
            return false;
        }

        ProductDesktopMarqueeSelectionSession? session =
            ProductDesktopMarqueeSelectionAdapter.TryStart(
                projection,
                selectionSnapshot(),
                x,
                y,
                control,
                shift);
        if (session is null)
        {
            return false;
        }

        activeMarqueeSelection = session;
        _ = NativeMethods.SetCapture(window);
        if (NativeMethods.GetCapture() != window)
        {
            activeMarqueeSelection = null;
            return false;
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
        return true;
    }

    private void UpdateMarqueeSelection(nint longParameter) =>
        _ = UpdateMarqueeSelectionCore(
            SignedLowWord(longParameter),
            SignedHighWord(longParameter));

    private bool UpdateMarqueeSelectionCore(int x, int y)
    {
        if (activeMarqueeSelection is not { } session)
        {
            return false;
        }

        ProductDesktopMarqueeSelectionSession? updated =
            ProductDesktopMarqueeSelectionAdapter.TryUpdate(
                projection,
                selectionSnapshot(),
                session,
                x,
                y);
        if (updated is null)
        {
            CancelMarqueeSelection(Handle);
            return false;
        }

        activeMarqueeSelection = updated;
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        return true;
    }

    private void CompleteMarqueeSelection(nint window, nint longParameter) =>
        _ = CompleteMarqueeSelectionCore(
            window,
            SignedLowWord(longParameter),
            SignedHighWord(longParameter));

    private bool CompleteMarqueeSelectionCore(
        nint window,
        int x,
        int y)
    {
        if (activeMarqueeSelection is not { } session)
        {
            return false;
        }

        ProductDesktopMarqueeSelectionCommand? command =
            ProductDesktopMarqueeSelectionAdapter.TryComplete(
                projection,
                selectionSnapshot(),
                session,
                x,
                y);
        activeMarqueeSelection = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
        return command is not null
            && applySelection(command.ContainerId, command.Request);
    }

    private void CancelMarqueeSelection(nint window)
    {
        if (activeMarqueeSelection is null)
        {
            return;
        }

        activeMarqueeSelection = null;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
    }

    private void UpdateWorkspaceCreateDrag(nint longParameter)
    {
        if (!workspaceCreateDragActive)
        {
            return;
        }

        workspaceCreateDragCurrent = new(
            Math.Clamp(SignedLowWord(longParameter), 0, projection.WorkArea.Width),
            Math.Clamp(SignedHighWord(longParameter), 0, projection.WorkArea.Height));
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
    }

    private void CompleteWorkspaceCreateDrag(nint window, nint longParameter)
    {
        if (!workspaceCreateDragActive)
        {
            return;
        }

        UpdateWorkspaceCreateDrag(longParameter);
        PixelRect clientBounds = CurrentWorkspaceCreateDragBounds();
        workspaceCreateDragActive = false;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
        if (!clientBounds.HasArea)
        {
            return;
        }

        _ = SubmitWorkspaceCreateDragInput(
            clientBounds.OffsetBy(
                projection.WorkArea.Left,
                projection.WorkArea.Top),
            sourceAttested: true,
            isInjected: false);
    }

    private void CancelWorkspaceCreateDrag(nint window)
    {
        if (!workspaceCreateDragActive)
        {
            return;
        }

        workspaceCreateDragActive = false;
        if (NativeMethods.GetCapture() == window)
        {
            _ = NativeMethods.ReleaseCapture();
        }
        _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
    }

    private PixelRect CurrentWorkspaceCreateDragBounds()
    {
        int left = Math.Min(workspaceCreateDragStart.X, workspaceCreateDragCurrent.X);
        int top = Math.Min(workspaceCreateDragStart.Y, workspaceCreateDragCurrent.Y);
        int right = Math.Max(workspaceCreateDragStart.X, workspaceCreateDragCurrent.X);
        int bottom = Math.Max(workspaceCreateDragStart.Y, workspaceCreateDragCurrent.Y);
        return new(left, top, right - left, bottom - top);
    }

    private static int SignedLowWord(nint value) =>
        unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) =>
        unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    private NativePoint ToScreenPoint(int clientX, int clientY) => new(
        checked(projection.WorkArea.Left + clientX),
        checked(projection.WorkArea.Top + clientY));

    private bool HandleItemViewportWheel(
        nint wordParameter,
        nint longParameter)
    {
        if (mode == ProductDesktopInteractionSurfaceMode.Hidden)
        {
            return false;
        }
        int wheelDelta = SignedHighWord(wordParameter);
        int x = SignedLowWord(longParameter) - projection.WorkArea.Left;
        int y = SignedHighWord(longParameter) - projection.WorkArea.Top;
        ProductDesktopHostReadOnlyProjection? container = projection.Containers
            .SingleOrDefault(candidate =>
                !candidate.IsCollapsed
                && candidate.TotalItemCount >
                    ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(
                        candidate.ContentDensity)
                && Contains(
                    ProductDesktopHostSurfaceLayout.GetContainerBounds(
                        projection,
                        candidate),
                    x,
                    y));
        if (container is null || wheelDelta == 0)
        {
            return false;
        }
        InputMessageSource source = default;
        bool observed = NativeMethods.GetCurrentInputMessageSource(ref source);
        try
        {
            return requestItemViewport(new(
                container.ContainerId,
                wheelDelta,
                SourceAttested: observed,
                IsInjected: !observed
                    || source.OriginId == NativeMethods.ImoInjected));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private int ResolveHitTest(nint longParameter)
    {
        if (mode == ProductDesktopInteractionSurfaceMode.Explicit)
        {
            return NativeMethods.HtClient;
        }
        if (mode != ProductDesktopInteractionSurfaceMode.Passive)
        {
            return NativeMethods.HtTransparent;
        }

        int x = SignedLowWord(longParameter) - projection.WorkArea.Left;
        int y = SignedHighWord(longParameter) - projection.WorkArea.Top;
        PixelRect? create =
            ProductDesktopHostSurfaceLayout.GetWorkspaceCreateButtonBounds(
                projection);
        if (create is not null && Contains(create.Value, x, y))
        {
            return NativeMethods.HtClient;
        }
        return FindHeaderAt(x, y) is not null
            ? NativeMethods.HtClient
            : NativeMethods.HtTransparent;
    }

    private ProductDesktopHostReadOnlyProjection? FindHeaderAt(int x, int y) =>
        projection.Containers.SingleOrDefault(container =>
            Contains(GetHeaderInteractionBounds(container), x, y));

    private PixelRect GetHeaderInteractionBounds(
        ProductDesktopHostReadOnlyProjection container)
    {
        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            projection,
            container);
        double scale = projection.EffectiveDpi / 96d;
        int headerHeight = ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int controlsWidth = ToPixels(140, scale);
        return new(
            bounds.Left,
            bounds.Top,
            Math.Max(0, bounds.Width - controlsWidth),
            Math.Min(bounds.Height, headerHeight));
    }

    private bool IsHeaderVisible(ProductDesktopHostReadOnlyProjection container) =>
        container.TitleVisibility switch
        {
            ProductContainerTitleVisibilityPolicy.Always => true,
            ProductContainerTitleVisibilityPolicy.Hover => string.Equals(
                hoveredHeaderContainerId,
                container.ContainerId,
                StringComparison.Ordinal),
            ProductContainerTitleVisibilityPolicy.Hidden => false,
            _ => false,
        };

    private void UpdateHeaderHover(nint window, nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Passive)
        {
            return;
        }
        if (!trackingMouseLeave)
        {
            var tracking = new TrackMouseEvent
            {
                Size = (uint)Marshal.SizeOf<TrackMouseEvent>(),
                Flags = NativeMethods.TmeLeave,
                Window = window,
            };
            trackingMouseLeave = NativeMethods.TrackMouseEvent(ref tracking);
        }

        string? next = FindHeaderAt(
            SignedLowWord(longParameter),
            SignedHighWord(longParameter))?.ContainerId;
        if (!string.Equals(
            hoveredHeaderContainerId,
            next,
            StringComparison.Ordinal))
        {
            hoveredHeaderContainerId = next;
            _ = NativeMethods.InvalidateRect(window, nint.Zero, erase: false);
        }
    }

    private bool HandleHeaderDoubleClick(nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Passive)
        {
            return false;
        }
        ProductDesktopHostReadOnlyProjection? container = FindHeaderAt(
            SignedLowWord(longParameter),
            SignedHighWord(longParameter));
        if (container is null
            || container.IsLocked
            || container.TitleDoubleClickAction !=
                ProductContainerTitleDoubleClickAction.ToggleCollapsed)
        {
            return false;
        }

        InputMessageSource source = default;
        bool observed = NativeMethods.GetCurrentInputMessageSource(ref source);
        try
        {
            return requestContainerHeaderCommand(new(
                ProductDesktopContainerHeaderCommandKind.ToggleCollapsed,
                container.ContainerId,
                SourceAttested: observed,
                IsInjected: !observed
                    || source.OriginId == NativeMethods.ImoInjected,
                IsAutoRepeat: false));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private bool HandleWorkspaceCreatePress(nint longParameter)
    {
        PixelRect? create =
            ProductDesktopHostSurfaceLayout.GetWorkspaceCreateButtonBounds(
                projection);
        if (mode != ProductDesktopInteractionSurfaceMode.Passive
            || create is null
            || !Contains(
                create.Value,
                SignedLowWord(longParameter),
                SignedHighWord(longParameter)))
        {
            return false;
        }

        return SubmitWorkspaceCreateInput(
            ProductDesktopWorkspaceCreateInputKind.PrimaryPointer,
            isAutoRepeat: false);
    }

    private bool HandleWorkspaceCreateContextMenu(
        nint window,
        nint longParameter)
    {
        PixelRect? create =
            ProductDesktopHostSurfaceLayout.GetWorkspaceCreateButtonBounds(
                projection);
        if (mode != ProductDesktopInteractionSurfaceMode.Passive
            || create is null
            || !Contains(
                create.Value,
                SignedLowWord(longParameter),
                SignedHighWord(longParameter)))
        {
            return false;
        }

        InputMessageSource source = default;
        bool observed = NativeMethods.GetCurrentInputMessageSource(ref source);
        nint foreground = NativeMethods.GetForegroundWindow();
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return false;
        }

        try
        {
            if (!NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MfString,
                    NativeMethods.EmptyCreateMenuCommand,
                    projection.WorkspaceIsEmpty
                        ? "创建第一个方格"
                        : "新建方格"))
            {
                return false;
            }

            NativePoint point = new(
                SignedLowWord(longParameter),
                SignedHighWord(longParameter));
            if (!NativeMethods.ClientToScreen(window, ref point))
            {
                return false;
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
            return command == NativeMethods.EmptyCreateMenuCommand
                && NativeMethods.GetForegroundWindow() == foreground
                && foreground != window
                && SubmitWorkspaceCreateInput(
                    ProductDesktopWorkspaceCreateInputKind.ContextMenu,
                    sourceAttested: observed,
                    isInjected: !observed
                        || source.OriginId == NativeMethods.ImoInjected,
                    isAutoRepeat: false);
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    private bool HandleItemOpenFeedbackContextMenu(
        nint window,
        nint longParameter)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || itemOpenFeedback is not { } feedback
            || (!feedback.CanRetry && !feedback.CanLocateInExplorer))
        {
            return false;
        }
        ProductDesktopPointerSelectionCommand? hit =
            ProductDesktopPointerSelectionAdapter.Map(
                projection,
                selectionSnapshot(),
                SignedLowWord(longParameter),
                SignedHighWord(longParameter),
                control: false,
                shift: false);
        if (hit?.Request is not
            {
                Action: ProductDesktopSelectionAction.SelectItem,
                ItemId: { } itemId,
            }
            || !string.Equals(hit.ContainerId, feedback.ContainerId,
                StringComparison.Ordinal)
            || !string.Equals(itemId, feedback.ItemId,
                StringComparison.Ordinal))
        {
            return false;
        }
        InputMessageSource inputSource = default;
        if (!NativeMethods.GetCurrentInputMessageSource(ref inputSource)
            || inputSource.OriginId == NativeMethods.ImoInjected
            || !applySelection(hit.ContainerId, hit.Request))
        {
            return true;
        }
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return true;
        }
        try
        {
            if (feedback.CanRetry
                && !NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MfString,
                    NativeMethods.ItemOpenRetryMenuCommand,
                    "重新验证并重试"))
            {
                return true;
            }
            if (feedback.CanLocateInExplorer
                && !NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MfString,
                    NativeMethods.ItemOpenLocateMenuCommand,
                    "在资源管理器中定位"))
            {
                return true;
            }
            NativePoint point = new(
                SignedLowWord(longParameter),
                SignedHighWord(longParameter));
            if (!NativeMethods.ClientToScreen(window, ref point))
            {
                return true;
            }
            nint foreground = NativeMethods.GetForegroundWindow();
            uint command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton
                    | NativeMethods.TpmNoNotify
                    | NativeMethods.TpmReturnCommand,
                point.X,
                point.Y,
                window,
                nint.Zero);
            if (NativeMethods.GetForegroundWindow() != foreground)
            {
                return true;
            }
            return command switch
            {
                NativeMethods.ItemOpenRetryMenuCommand =>
                    SubmitItemOpenFeedbackAction(
                        ProductDesktopItemOpenSource.FeedbackRetry,
                        sourceAttested: true,
                        isInjected: false),
                NativeMethods.ItemOpenLocateMenuCommand =>
                    SubmitItemOpenFeedbackAction(
                        ProductDesktopItemOpenSource.FeedbackLocateInExplorer,
                        sourceAttested: true,
                        isInjected: false),
                _ => true,
            };
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    private bool SubmitItemOpenFeedbackAction(
        ProductDesktopItemOpenSource source,
        bool sourceAttested,
        bool isInjected)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || !sourceAttested
            || isInjected
            || itemOpenFeedback is not { } feedback
            || (source == ProductDesktopItemOpenSource.FeedbackRetry
                && !feedback.CanRetry)
            || (source == ProductDesktopItemOpenSource.FeedbackLocateInExplorer
                && !feedback.CanLocateInExplorer)
            || source is not (
                ProductDesktopItemOpenSource.FeedbackRetry
                    or ProductDesktopItemOpenSource.FeedbackLocateInExplorer))
        {
            return false;
        }
        return requestItemOpen(new(
            feedback.ContainerId,
            feedback.ItemId,
            source,
            sourceAttested,
            isInjected,
            IsAutoRepeat: false));
    }

    internal bool SubmitWorkspaceCreateInput(
        ProductDesktopWorkspaceCreateInputKind kind,
        bool isAutoRepeat)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Passive
            || kind is not (
                ProductDesktopWorkspaceCreateInputKind.PrimaryPointer
                or ProductDesktopWorkspaceCreateInputKind.ContextMenu
                or ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut))
        {
            return false;
        }

        InputMessageSource source = default;
        bool observed = NativeMethods.GetCurrentInputMessageSource(ref source);
        return SubmitWorkspaceCreateInput(
            kind,
            sourceAttested: observed,
            isInjected: !observed
                || source.OriginId == NativeMethods.ImoInjected,
            isAutoRepeat);
    }

    internal bool SubmitWorkspaceCreateInput(
        ProductDesktopWorkspaceCreateInputKind kind,
        bool sourceAttested,
        bool isInjected,
        bool isAutoRepeat)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Passive
            || kind is not (
                ProductDesktopWorkspaceCreateInputKind.PrimaryPointer
                or ProductDesktopWorkspaceCreateInputKind.ContextMenu
                or ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut))
        {
            return false;
        }

        return requestWorkspaceCreate(new(
            kind,
            sourceAttested,
            isInjected,
            isAutoRepeat));
    }

    internal bool SubmitWorkspaceCreateDragInput(
        PixelRect requestedBoundsPixels,
        bool sourceAttested,
        bool isInjected)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Explicit
            || !requestedBoundsPixels.HasArea)
        {
            return false;
        }

        return requestWorkspaceCreate(new(
            ProductDesktopWorkspaceCreateInputKind.PointerDrag,
            sourceAttested,
            isInjected,
            IsAutoRepeat: false,
            requestedBoundsPixels));
    }

    private static bool Contains(PixelRect bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right
        && y >= bounds.Top && y < bounds.Bottom;

    private void Paint(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(
            window,
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
            _ = NativeMethods.SetTextColor(deviceContext, 0x00FFFFFF);
            nint previousFont = NativeMethods.SelectObject(
                deviceContext,
                NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont));
            try
            {
                if (projection.WorkspaceIsEmpty)
                {
                    DrawEmptyWorkspace(deviceContext);
                }
                else
                {
                    foreach (ProductDesktopHostReadOnlyProjection container
                        in projection.Containers)
                    {
                        DrawContainer(deviceContext, container);
                    }
                    if (containerLayoutPreview is not null
                        && !projection.Containers.Any(container => string.Equals(
                            container.ContainerId,
                            containerLayoutPreview.ContainerId,
                            StringComparison.Ordinal)))
                    {
                        DrawContainer(deviceContext, containerLayoutPreview);
                    }
                    DrawContinuedWorkspaceCreate(deviceContext);
                }
                if (workspaceCreateDragActive)
                {
                    PixelRect bounds = CurrentWorkspaceCreateDragBounds();
                    NativeRect outline = new(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right,
                        bounds.Bottom);
                    _ = NativeMethods.DrawFocusRect(deviceContext, ref outline);
                }
                if (activeMarqueeSelection is { } marquee)
                {
                    PixelRect bounds =
                        ProductDesktopMarqueeSelectionAdapter.GetBounds(marquee);
                    NativeRect outline = new(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right,
                        bounds.Bottom);
                    _ = NativeMethods.DrawFocusRect(deviceContext, ref outline);
                }
            }
            finally
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void DrawEmptyWorkspace(nint deviceContext)
    {
        PixelRect card = ProductDesktopHostSurfaceLayout.GetEmptyCardBounds(
            projection);
        PixelRect button =
            ProductDesktopHostSurfaceLayout.GetEmptyCreateButtonBounds(projection);
        NativeRect cardBounds = new(card.Left, card.Top, card.Right, card.Bottom);
        NativeRect buttonBounds = new(
            button.Left,
            button.Top,
            button.Right,
            button.Bottom);
        nint cardBrush = NativeMethods.CreateSolidBrush(0x003B3028);
        nint buttonBrush = NativeMethods.CreateSolidBrush(0x00D67524);
        nint borderPen = NativeMethods.CreatePen(NativeMethods.PsSolid, 1, 0x006F6259);
        try
        {
            nint previousBrush = NativeMethods.SelectObject(deviceContext, cardBrush);
            nint previousPen = NativeMethods.SelectObject(deviceContext, borderPen);
            _ = NativeMethods.Rectangle(
                deviceContext,
                cardBounds.Left,
                cardBounds.Top,
                cardBounds.Right,
                cardBounds.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, buttonBrush);
            _ = NativeMethods.Rectangle(
                deviceContext,
                buttonBounds.Left,
                buttonBounds.Top,
                buttonBounds.Right,
                buttonBounds.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);

            _ = NativeMethods.SetTextColor(deviceContext, 0x00FFFFFF);
            double scale = projection.EffectiveDpi / 96d;
            int padding = ProductDesktopHostSurfaceLayout.ToPixels(20, scale);
            DrawText(
                deviceContext,
                "桌面还没有方格",
                new(card.Left + padding, card.Top + padding,
                    card.Right - padding, card.Top + padding + 36),
                NativeMethods.DtCenter | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);
            DrawText(
                deviceContext,
                workspaceCreateHotKeyRegistered
                    ? "右键按钮或按 Ctrl+Alt+N；不会移动桌面文件"
                    : "快捷键冲突时仍可点击或右键；不会移动桌面文件",
                new(card.Left + padding, card.Top + padding + 38,
                    card.Right - padding, button.Top - 8),
                NativeMethods.DtCenter | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine | NativeMethods.DtEndEllipsis);
            DrawText(
                deviceContext,
                "创建第一个方格 · 可右键",
                buttonBounds,
                NativeMethods.DtCenter | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(borderPen);
            _ = NativeMethods.DeleteObject(buttonBrush);
            _ = NativeMethods.DeleteObject(cardBrush);
        }
    }

    private void DrawContinuedWorkspaceCreate(nint deviceContext)
    {
        if (mode != ProductDesktopInteractionSurfaceMode.Passive)
        {
            return;
        }

        PixelRect? button =
            ProductDesktopHostSurfaceLayout.GetContinuedCreateButtonBounds(
                projection);
        if (button is null)
        {
            return;
        }

        NativeRect bounds = new(
            button.Value.Left,
            button.Value.Top,
            button.Value.Right,
            button.Value.Bottom);
        nint brush = NativeMethods.CreateSolidBrush(0x00D67524);
        nint pen = NativeMethods.CreatePen(NativeMethods.PsSolid, 1, 0x006F6259);
        try
        {
            nint previousBrush = NativeMethods.SelectObject(deviceContext, brush);
            nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
            _ = NativeMethods.Rectangle(
                deviceContext,
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
            _ = NativeMethods.DrawText(
                deviceContext,
                "+ 新建方格",
                -1,
                ref bounds,
                NativeMethods.DtCenter
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(pen);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private void DrawContainer(
        nint deviceContext,
        ProductDesktopHostReadOnlyProjection container)
    {
        container = ResolveVisualContainer(container);
        NativeRect bounds = GetContainerBounds(container);
        double scale = projection.EffectiveDpi / 96d;
        int headerHeight = ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int horizontalPadding = ToPixels(18, scale);
        uint background = BlendWithDesktop(
            ParseColor(container.Color),
            container.Opacity);
        nint backgroundBrush = NativeMethods.CreateSolidBrush(background);
        nint borderPen = NativeMethods.CreatePen(
            NativeMethods.PsSolid,
            1,
            Lighten(background));
        try
        {
            nint previousBrush = NativeMethods.SelectObject(
                deviceContext,
                backgroundBrush);
            nint previousPen = NativeMethods.SelectObject(
                deviceContext,
                borderPen);
            _ = NativeMethods.Rectangle(
                deviceContext,
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom);
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);

            if (container.SearchHighlighted)
            {
                NativeRect searchOutline = new(
                    bounds.Left + 1,
                    bounds.Top + 1,
                    bounds.Right - 1,
                    bounds.Bottom - 1);
                _ = NativeMethods.DrawFocusRect(
                    deviceContext,
                    ref searchOutline);
            }

            if (containerLayoutPreview is not null && string.Equals(
                containerLayoutPreview.ContainerId,
                container.ContainerId,
                StringComparison.Ordinal))
            {
                NativeRect previewOutline = bounds;
                _ = NativeMethods.DrawFocusRect(deviceContext, ref previewOutline);
            }

            if (string.Equals(
                hoveredDropContainerId,
                container.ContainerId,
                StringComparison.Ordinal))
            {
                NativeRect dropOutline = new(
                    bounds.Left + 2,
                    bounds.Top + 2,
                    bounds.Right - 2,
                    bounds.Bottom - 2);
                _ = NativeMethods.DrawFocusRect(deviceContext, ref dropOutline);
            }

            if (string.Equals(
                containerLayoutKeyboardFocusId,
                container.ContainerId,
                StringComparison.Ordinal))
            {
                NativeRect headerFocus = new(
                    bounds.Left + ToPixels(4, scale),
                    bounds.Top + ToPixels(4, scale),
                    bounds.Right - ToPixels(4, scale),
                    Math.Min(
                        bounds.Bottom,
                        bounds.Top + headerHeight - ToPixels(4, scale)));
                _ = NativeMethods.DrawFocusRect(deviceContext, ref headerFocus);
            }

            ProductDesktopContainerHeaderPresentation header = container.Header;
            if (IsHeaderVisible(container))
            {
                int controlsWidth = ToPixels(140, scale);
                DrawText(
                    deviceContext,
                    header.VisualTitle,
                    new(
                        bounds.Left + horizontalPadding,
                        bounds.Top + ToPixels(4, scale),
                        Math.Max(
                            bounds.Left + horizontalPadding,
                            bounds.Right - controlsWidth),
                        bounds.Top + ToPixels(27, scale)),
                    NativeMethods.DtLeft
                        | NativeMethods.DtVCenter
                        | NativeMethods.DtSingleLine
                        | NativeMethods.DtEndEllipsis);
                DrawText(
                    deviceContext,
                    header.VisualStatus,
                    new(
                        bounds.Left + horizontalPadding,
                        bounds.Top + ToPixels(27, scale),
                        Math.Max(
                            bounds.Left + horizontalPadding,
                            bounds.Right - controlsWidth),
                        bounds.Top + ToPixels(50, scale)),
                    NativeMethods.DtLeft
                        | NativeMethods.DtVCenter
                        | NativeMethods.DtSingleLine
                        | NativeMethods.DtEndEllipsis);
            }
            if (container.IsCollapsed)
            {
                return;
            }

            DrawItems(deviceContext, container, bounds, scale, headerHeight);
        }
        finally
        {
            if (borderPen != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(borderPen);
            }

            if (backgroundBrush != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(backgroundBrush);
            }
        }
    }

    private void DrawItems(
        nint deviceContext,
        ProductDesktopHostReadOnlyProjection container,
        NativeRect bounds,
        double scale,
        int headerHeight)
    {
        IReadOnlyList<string> items = container.ItemNames.Count == 0
            ? ["空方格 · 只读预览"]
            : container.ItemNames;
        int itemHeight = ToPixels(
            ProductDesktopHostSurfaceLayout.GetItemHeightDip(container),
            scale);
        int horizontalPadding = ToPixels(18, scale);
        int iconSize = ToPixels(20, scale);
        int top = bounds.Top + headerHeight;
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
            selectionSnapshot();
        var selected = transaction?.Accessibility.SelectedItemIds.ToHashSet(
            StringComparer.Ordinal) ?? [];
        bool activeContainer = transaction?.IsExplicit == true
            && string.Equals(
                transaction.Selection?.ContainerId,
                container.ContainerId,
                StringComparison.Ordinal);
        for (int index = 0; index < items.Count; index++)
        {
            if (top + itemHeight > bounds.Bottom)
            {
                break;
            }

            string item = items[index];
            string? itemId = index < container.ItemIds.Count
                ? container.ItemIds[index]
                : null;
            NativeRect itemBounds = new(
                bounds.Left + ToPixels(6, scale),
                top,
                bounds.Right - ToPixels(6, scale),
                top + itemHeight);
            if (activeContainer && itemId is not null
                && selected.Contains(itemId))
            {
                nint selectionBrush = NativeMethods.CreateSolidBrush(0x00D67524);
                try
                {
                    _ = NativeMethods.FillRect(
                        deviceContext,
                        ref itemBounds,
                        selectionBrush);
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(selectionBrush);
                }
            }
            if (itemId is not null
                && string.Equals(
                    container.SearchHighlightedItemId,
                    itemId,
                    StringComparison.Ordinal))
            {
                nint searchBrush = NativeMethods.CreateSolidBrush(0x00D67524);
                try
                {
                    _ = NativeMethods.FillRect(
                        deviceContext,
                        ref itemBounds,
                        searchBrush);
                    _ = NativeMethods.DrawFocusRect(
                        deviceContext,
                        ref itemBounds);
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(searchBrush);
                }
            }
            if (activeContainer && itemId is not null
                && string.Equals(
                    transaction?.Selection?.FocusedItemId,
                    itemId,
                    StringComparison.Ordinal))
            {
                _ = NativeMethods.DrawFocusRect(deviceContext, ref itemBounds);
            }

            ProductDesktopItemVisualPresentation? visual =
                container.ItemNames.Count == 0
                    ? null
                    : container.ItemVisuals[index];
            int iconX = bounds.Left + horizontalPadding;
            int iconY = top + Math.Max(0, (itemHeight - iconSize) / 2);
            bool thumbnailDrawn = visual is
            {
                Status: ProductDesktopItemVisualStatus.ReadyThumbnail,
                Thumbnail: { } thumbnail,
            }
                && DrawThumbnailFrame(
                    deviceContext,
                    thumbnail,
                    iconX,
                    iconY,
                    iconSize) > 0;
            if (!thumbnailDrawn
                && visual is not null
                && TryAcquireSystemIcon(visual, out nint icon))
            {
                try
                {
                    _ = NativeMethods.DrawIconEx(
                        deviceContext,
                        iconX,
                        iconY,
                        icon,
                        iconSize,
                        iconSize,
                        0,
                        nint.Zero,
                        NativeMethods.DiNormal);
                }
                finally
                {
                    _ = NativeMethods.DestroyIcon(icon);
                }
            }

            string label = container.ItemNames.Count == 0
                ? item
                : VisualLabel(visual!, item);
            if (itemId is not null
                && itemOpenFeedback is { } feedback
                && string.Equals(feedback.ContainerId, container.ContainerId,
                    StringComparison.Ordinal)
                && string.Equals(feedback.ItemId, itemId,
                    StringComparison.Ordinal))
            {
                label = $"{feedback.Message} · {label}";
            }
            DrawText(
                deviceContext,
                label,
                new(
                    bounds.Left + horizontalPadding
                        + (visual is null
                            ? 0
                            : iconSize + ToPixels(8, scale)),
                    top,
                    Math.Max(
                        bounds.Left + horizontalPadding,
                        bounds.Right - horizontalPadding),
                    top + itemHeight),
                NativeMethods.DtLeft
                    | NativeMethods.DtVCenter
                    | NativeMethods.DtSingleLine
                    | NativeMethods.DtEndEllipsis);
            top += itemHeight;
        }
    }

    private static int DrawThumbnailFrame(
        nint deviceContext,
        ProductDesktopThumbnailFrame frame,
        int x,
        int y,
        int size)
    {
        var header = new NativeBitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<NativeBitmapInfoHeader>(),
            Width = frame.Width,
            Height = -frame.Height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
            SizeImage = (uint)frame.Bgra32Pixels.Length,
        };
        GCHandle pixels = GCHandle.Alloc(
            frame.Bgra32Pixels,
            GCHandleType.Pinned);
        try
        {
            return NativeMethods.StretchDIBits(
                deviceContext,
                x,
                y,
                size,
                size,
                0,
                0,
                frame.Width,
                frame.Height,
                pixels.AddrOfPinnedObject(),
                ref header,
                NativeMethods.DibRgbColors,
                NativeMethods.SourceCopy);
        }
        finally
        {
            pixels.Free();
        }
    }

    private static string VisualLabel(
        ProductDesktopItemVisualPresentation visual,
        string name) => visual.Status switch
        {
            ProductDesktopItemVisualStatus.ReadyTypeIcon => name,
            ProductDesktopItemVisualStatus.LoadingThumbnail => $"加载中 · {name}",
            ProductDesktopItemVisualStatus.ReadyThumbnail => name,
            ProductDesktopItemVisualStatus.Offline => $"离线 · {name}",
            ProductDesktopItemVisualStatus.TargetChanged => $"类型变化 · {name}",
            ProductDesktopItemVisualStatus.Ambiguous => $"待确认 · {name}",
            ProductDesktopItemVisualStatus.Unsupported => $"不支持 · {name}",
            ProductDesktopItemVisualStatus.AccessDenied => $"无权限 · {name}",
            ProductDesktopItemVisualStatus.FailedFallback => $"已回退 · {name}",
            _ => name,
        };

    private static bool TryAcquireSystemIcon(
        ProductDesktopItemVisualPresentation visual,
        out nint icon)
    {
        var info = new ShellStockIconInfo
        {
            Size = (uint)Marshal.SizeOf<ShellStockIconInfo>(),
            Path = string.Empty,
        };
        uint stockId = visual.Status is ProductDesktopItemVisualStatus.Offline
            or ProductDesktopItemVisualStatus.TargetChanged
            or ProductDesktopItemVisualStatus.Ambiguous
            or ProductDesktopItemVisualStatus.Unsupported
            or ProductDesktopItemVisualStatus.AccessDenied
            ? NativeMethods.StockIconWarning
            : visual.TypeIcon switch
            {
                ProductDesktopItemTypeIconKind.Folder =>
                    NativeMethods.StockIconFolder,
                ProductDesktopItemTypeIconKind.Shortcut =>
                    NativeMethods.StockIconLink,
                ProductDesktopItemTypeIconKind.Url =>
                    NativeMethods.StockIconWorld,
                _ => NativeMethods.StockIconDocument,
            };
        int result = NativeMethods.SHGetStockIconInfo(
            stockId,
            NativeMethods.ShellStockIconFlagIcon
                | NativeMethods.ShellStockIconFlagSmallIcon,
            ref info);
        icon = info.Icon;
        return result >= 0 && icon != nint.Zero;
    }

    private static bool DestroyAcquiredIcon(nint icon) =>
        icon != nint.Zero && NativeMethods.DestroyIcon(icon);

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeRect bounds,
        uint format) =>
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            -1,
            ref bounds,
            format);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CancelContainerLayout(
            Handle,
            ProductDesktopContainerLayoutCancellationReason.HostInvalidated);
        CancelMarqueeSelection(Handle);
        CancelWorkspaceCreateDrag(Handle);
        containerLayoutPreview = null;
        containerLayoutKeyboardFocusId = null;
        disposed = true;
        if (Handle != nint.Zero)
        {
            explorerDropTarget?.Dispose();
            explorerDropTarget = null;
            ReleaseWorkspaceCreateHotKey();
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

    internal bool ExplorerDropTargetRegistered =>
        explorerDropTarget?.IsRegistered == true;

    internal uint DispatchExplorerDragEnterForEvidence(
        object dataObject,
        int screenX,
        int screenY,
        uint allowedEffects) =>
        explorerDropTarget?.DispatchDragEnterForEvidence(
            dataObject,
            screenX,
            screenY,
            allowedEffects) ?? WindowsProductDesktopHostDropTarget.EffectNone;

    internal uint DispatchExplorerDropForEvidence(
        object dataObject,
        int screenX,
        int screenY,
        uint allowedEffects) =>
        explorerDropTarget?.DispatchDropForEvidence(
            dataObject,
            screenX,
            screenY,
            allowedEffects) ?? WindowsProductDesktopHostDropTarget.EffectNone;

    private string? ResolveExplorerDropTarget(int x, int y)
    {
        if (disposed || mode == ProductDesktopInteractionSurfaceMode.Hidden)
        {
            return null;
        }

        ProductDesktopHostReadOnlyProjection? target = projection.Containers
            .LastOrDefault(container =>
            {
                NativeRect bounds = GetContainerBounds(container);
                return !container.IsLocked
                    && x >= bounds.Left
                    && x < bounds.Right
                    && y >= bounds.Top
                    && y < bounds.Bottom;
            });
        return target?.ContainerId;
    }

    private void ApplyExplorerDropHover(string? containerId)
    {
        if (string.Equals(
            hoveredDropContainerId,
            containerId,
            StringComparison.Ordinal))
        {
            return;
        }

        hoveredDropContainerId = containerId;
        if (!disposed && Handle != nint.Zero)
        {
            _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        }
    }

    public bool ApplyPassive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelContainerLayout(
            Handle,
            ProductDesktopContainerLayoutCancellationReason.HostInvalidated);
        containerLayoutPreview = null;
        containerLayoutKeyboardFocusId = null;
        CancelMarqueeSelection(Handle);
        CancelWorkspaceCreateDrag(Handle);
        mode = ProductDesktopInteractionSurfaceMode.Passive;
        TryRegisterWorkspaceCreateHotKey();
        ApplyWindowRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        _ = NativeMethods.UpdateWindow(Handle);
        return PassiveWindowContractAttested;
    }

    public bool ApplyExplicit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        mode = ProductDesktopInteractionSurfaceMode.Explicit;
        ReleaseWorkspaceCreateHotKey();
        ApplyWindowRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        _ = NativeMethods.InvalidateRect(Handle, nint.Zero, erase: false);
        _ = NativeMethods.UpdateWindow(Handle);
        return ExplicitWindowContractAttested;
    }

    public bool ApplyHidden()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        _ = explorerDropTarget?.DragLeave();
        CancelContainerLayout(
            Handle,
            ProductDesktopContainerLayoutCancellationReason.HostInvalidated);
        containerLayoutPreview = null;
        containerLayoutKeyboardFocusId = null;
        CancelMarqueeSelection(Handle);
        CancelWorkspaceCreateDrag(Handle);
        mode = ProductDesktopInteractionSurfaceMode.Hidden;
        ReleaseWorkspaceCreateHotKey();
        ApplyEmptyWindowRegion();
        _ = NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        return HiddenWindowContractAttested;
    }

    private void TryRegisterWorkspaceCreateHotKey()
    {
        if (Handle != nint.Zero
            && projection.IsPrimary
            && !workspaceCreateHotKeyRegistered)
        {
            workspaceCreateHotKeyRegistered = NativeMethods.RegisterHotKey(
                Handle,
                EmptyCreateHotKeyId,
                NativeMethods.ModAlt
                    | NativeMethods.ModControl
                    | NativeMethods.ModNoRepeat,
                NativeMethods.VkN);
        }
    }

    private void ReleaseWorkspaceCreateHotKey()
    {
        if (Handle != nint.Zero && workspaceCreateHotKeyRegistered)
        {
            _ = NativeMethods.UnregisterHotKey(Handle, EmptyCreateHotKeyId);
            workspaceCreateHotKeyRegistered = false;
        }
    }

    private void ApplyWindowRegion()
    {
        nint combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (combined == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        bool transferred = false;
        try
        {
            if (mode == ProductDesktopInteractionSurfaceMode.Explicit)
            {
                nint full = NativeMethods.CreateRectRgn(
                    0,
                    0,
                    projection.WorkArea.Width,
                    projection.WorkArea.Height);
                if (full == nint.Zero
                    || NativeMethods.CombineRgn(
                        combined,
                        combined,
                        full,
                        NativeMethods.RgnOr) == NativeMethods.Error)
                {
                    if (full != nint.Zero)
                    {
                        _ = NativeMethods.DeleteObject(full);
                    }
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                _ = NativeMethods.DeleteObject(full);
            }
            if (projection.WorkspaceIsEmpty)
            {
                PixelRect emptyCard =
                    ProductDesktopHostSurfaceLayout.GetEmptyCardBounds(projection);
                nint card = NativeMethods.CreateRectRgn(
                    emptyCard.Left,
                    emptyCard.Top,
                    emptyCard.Right,
                    emptyCard.Bottom);
                if (card == nint.Zero
                    || NativeMethods.CombineRgn(
                        combined,
                        combined,
                        card,
                        NativeMethods.RgnOr) == NativeMethods.Error)
                {
                    if (card != nint.Zero)
                    {
                        _ = NativeMethods.DeleteObject(card);
                    }
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                _ = NativeMethods.DeleteObject(card);
            }
            else if (mode == ProductDesktopInteractionSurfaceMode.Passive
                && ProductDesktopHostSurfaceLayout
                .GetContinuedCreateButtonBounds(projection) is { } create)
            {
                nint button = NativeMethods.CreateRectRgn(
                    create.Left,
                    create.Top,
                    create.Right,
                    create.Bottom);
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
            foreach (ProductDesktopHostReadOnlyProjection container
                in projection.Containers)
            {
                NativeRect bounds = GetContainerBounds(container);
                nint card = NativeMethods.CreateRectRgn(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right,
                    bounds.Bottom);
                if (card == nint.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (NativeMethods.CombineRgn(
                            combined,
                            combined,
                            card,
                            NativeMethods.RgnOr) == NativeMethods.Error)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    _ = NativeMethods.DeleteObject(card);
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

    private void ApplyEmptyWindowRegion()
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

    private bool AttestStableWindowPolicy()
    {
        nint extendedStyle = NativeMethods.GetWindowLongPtr(
            Handle,
            NativeMethods.GwlExStyle);
        long style = extendedStyle.ToInt64();
        long required = NativeMethods.WsExToolWindow
            | NativeMethods.WsExLayered
            | NativeMethods.WsExNoActivate;
        return (style & required) == required
            && (style & NativeMethods.WsExTransparent) == 0
            && (style & NativeMethods.WsExTopmost) == 0
            && NativeMethods.GetWindow(Handle, NativeMethods.GwOwner) == nint.Zero
            && NativeMethods.GetForegroundWindow() != Handle;
    }

    private bool AttestWindowRegion(bool expectEmpty)
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
                : result is not (NativeMethods.Error
                    or NativeMethods.NullRegion);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    private NativeRect GetContainerBounds(
        ProductDesktopHostReadOnlyProjection container)
    {
        container = ResolveVisualContainer(container);
        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            projection,
            container);
        return new(
            bounds.Left,
            bounds.Top,
            checked(bounds.Left + bounds.Width),
            checked(bounds.Top + bounds.Height));
    }

    private ProductDesktopHostReadOnlyProjection ResolveVisualContainer(
        ProductDesktopHostReadOnlyProjection container) =>
        containerLayoutPreview is not null && string.Equals(
            containerLayoutPreview.ContainerId,
            container.ContainerId,
            StringComparison.Ordinal)
            ? containerLayoutPreview
            : container;

    private static PixelRect ToPixelRect(NativeRect bounds) => new(
        bounds.Left,
        bounds.Top,
        checked(bounds.Right - bounds.Left),
        checked(bounds.Bottom - bounds.Top));

    private static int ToPixels(double value, double scale) =>
        checked((int)Math.Round(value * scale));

    private static uint ParseColor(string value)
    {
        byte red = byte.Parse(
            value.AsSpan(1, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        byte green = byte.Parse(
            value.AsSpan(3, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        byte blue = byte.Parse(
            value.AsSpan(5, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        return (uint)(red | (green << 8) | (blue << 16));
    }

    private static uint Lighten(uint color)
    {
        uint red = Math.Min(255, (color & 0xFF) + 48);
        uint green = Math.Min(255, ((color >> 8) & 0xFF) + 48);
        uint blue = Math.Min(255, ((color >> 16) & 0xFF) + 48);
        return red | (green << 8) | (blue << 16);
    }

    private static uint BlendWithDesktop(uint color, double opacity)
    {
        const uint desktopChannel = 28;
        uint red = BlendChannel(color & 0xFF, desktopChannel, opacity);
        uint green = BlendChannel((color >> 8) & 0xFF, desktopChannel, opacity);
        uint blue = BlendChannel((color >> 16) & 0xFF, desktopChannel, opacity);
        return red | (green << 8) | (blue << 16);
    }

    private static uint BlendChannel(
        uint foreground,
        uint background,
        double opacity) =>
        (uint)Math.Clamp(
            (int)Math.Round((foreground * opacity) + (background * (1 - opacity))),
            0,
            255);

    private sealed record ActiveContainerLayout(
        string ContainerId,
        ProductWorkspaceContainerLayoutGestureKind Kind,
        NativePoint Start,
        bool ShiftPressed);

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
    private readonly struct NativeRect
    {
        internal NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal readonly int Left;
        internal readonly int Top;
        internal readonly int Right;
        internal readonly int Bottom;

        internal int Width => checked(Right - Left);

        internal int Height => checked(Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal nint DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Erase;
        internal NativeRect PaintRectangle;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEvent
    {
        internal uint Size;
        internal uint Flags;
        internal nint Window;
        internal uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputMessageSource
    {
        internal uint DeviceType;
        internal uint OriginId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellStockIconInfo
    {
        internal uint Size;
        internal nint Icon;
        internal int SystemImageIndex;
        internal int IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal int X;
        internal int Y;
    }

    private static class NativeMethods
    {
        internal const uint WsPopup = 0x80000000;
        internal const uint CsDoubleClicks = 0x0008;
        internal const uint WsExToolWindow = 0x00000080;
        internal const uint WsExTransparent = 0x00000020;
        internal const uint WsExLayered = 0x00080000;
        internal const uint WsExNoActivate = 0x08000000;
        internal const uint WsExTopmost = 0x00000008;
        internal const uint LwaAlpha = 0x00000002;
        internal const int SwHide = 0;
        internal const int SwShowNoActivate = 4;
        internal const uint WmPaint = 0x000F;
        internal const uint WmEraseBackground = 0x0014;
        internal const uint WmNcHitTest = 0x0084;
        internal const uint WmMouseActivate = 0x0021;
        internal const uint WmGetObject = 0x003D;
        internal const uint WmKeyDown = 0x0100;
        internal const uint WmLButtonDown = 0x0201;
        internal const uint WmLButtonDoubleClick = 0x0203;
        internal const uint WmLButtonUp = 0x0202;
        internal const uint WmMouseMove = 0x0200;
        internal const uint WmMouseWheel = 0x020A;
        internal const uint WmMouseLeave = 0x02A3;
        internal const uint TmeLeave = 0x00000002;
        internal const int VkEscape = 0x1B;
        internal const uint WmRButtonUp = 0x0205;
        internal const uint WmCaptureChanged = 0x0215;
        internal const uint WmCancelMode = 0x001F;
        internal const uint WmHotKey = 0x0312;
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModNoRepeat = 0x4000;
        internal const uint VkN = 0x4E;
        internal const uint MfString = 0x0000;
        internal const uint TpmRightButton = 0x0002;
        internal const uint TpmNoNotify = 0x0080;
        internal const uint TpmReturnCommand = 0x0100;
        internal const uint EmptyCreateMenuCommand = 1;
        internal const uint ItemOpenRetryMenuCommand = 2;
        internal const uint ItemOpenLocateMenuCommand = 3;
        internal const long MkShift = 0x0004;
        internal const long MkControl = 0x0008;
        internal const uint ImoInjected = 2;
        internal const int HtTransparent = -1;
        internal const int HtClient = 1;
        internal const int MaNoActivate = 3;
        internal const int TransparentBackground = 1;
        internal const int DefaultGuiFont = 17;
        internal const int PsSolid = 0;
        internal const uint DtLeft = 0x0000;
        internal const uint DtVCenter = 0x0004;
        internal const uint DtSingleLine = 0x0020;
        internal const uint DtEndEllipsis = 0x8000;
        internal const uint DiNormal = 0x0003;
        internal const uint ShellStockIconFlagIcon = 0x00000100;
        internal const uint ShellStockIconFlagSmallIcon = 0x00000001;
        internal const uint StockIconDocument = 0;
        internal const uint StockIconFolder = 3;
        internal const uint StockIconWorld = 13;
        internal const uint StockIconLink = 29;
        internal const uint StockIconWarning = 78;
        internal const uint DibRgbColors = 0;
        internal const uint SourceCopy = 0x00CC0020;
        internal const uint DtCenter = 0x0001;
        internal const int RgnOr = 2;
        internal const int NullRegion = 1;
        internal const int Error = 0;
        internal const int DwmWindowCornerPreference = 33;
        internal const int DwmWindowCornerPreferenceRound = 2;
        internal const int GwlExStyle = -20;
        internal const uint GwOwner = 4;
        internal static readonly nint ArrowCursor = new(32512);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll")]
        internal static extern nint GetWindow(nint window, uint command);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern nint SetCapture(nint window);

        [DllImport("user32.dll")]
        internal static extern nint GetCapture();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClass(string className, nint instance);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
        internal static extern nint DefWindowProc(
            nint window,
            uint message,
            nint wordParameter,
            nint longParameter);

        [DllImport("user32.dll")]
        internal static extern nint LoadCursor(nint instance, nint cursorName);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            nint window,
            out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProp(
            nint window,
            string name,
            nint value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint RemoveProp(nint window, string name);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetLayeredWindowAttributes(
            nint window,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint window, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InvalidateRect(
            nint window,
            nint rectangle,
            [MarshalAs(UnmanagedType.Bool)] bool erase);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCurrentInputMessageSource(
            ref InputMessageSource inputMessageSource);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TrackMouseEvent(ref TrackMouseEvent trackEvent);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(
            nint window,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(nint window, int id);

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

        [DllImport("gdi32.dll")]
        internal static extern nint CreateRectRgn(
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("gdi32.dll")]
        internal static extern int CombineRgn(
            nint destination,
            nint source1,
            nint source2,
            int mode);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowRgn(
            nint window,
            nint region,
            [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("user32.dll")]
        internal static extern int GetWindowRgn(nint window, nint region);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(
            nint window,
            out NativeRect rectangle);

        [DllImport("user32.dll")]
        internal static extern nint BeginPaint(
            nint window,
            out PaintStruct paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndPaint(
            nint window,
            ref PaintStruct paint);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        internal static extern nint CreatePen(int style, int width, uint color);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint deviceContext, nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Rectangle(
            nint deviceContext,
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("gdi32.dll")]
        internal static extern int SetBkMode(nint deviceContext, int mode);

        [DllImport("gdi32.dll")]
        internal static extern uint SetTextColor(nint deviceContext, uint color);

        [DllImport("gdi32.dll")]
        internal static extern int StretchDIBits(
            nint deviceContext,
            int destinationX,
            int destinationY,
            int destinationWidth,
            int destinationHeight,
            int sourceX,
            int sourceY,
            int sourceWidth,
            int sourceHeight,
            nint bits,
            ref NativeBitmapInfoHeader bitmapInfo,
            uint usage,
            uint rasterOperation);

        [DllImport("gdi32.dll")]
        internal static extern uint GetPixel(
            nint deviceContext,
            int x,
            int y);

        [DllImport("user32.dll")]
        internal static extern nint GetDC(nint window);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint window, nint deviceContext);

        [DllImport("gdi32.dll")]
        internal static extern nint GetStockObject(int objectIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int DrawText(
            nint deviceContext,
            string text,
            int characterCount,
            ref NativeRect rectangle,
            uint format);

        [DllImport("shell32.dll")]
        internal static extern int SHGetStockIconInfo(
            uint stockIconId,
            uint flags,
            ref ShellStockIconInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DrawIconEx(
            nint deviceContext,
            int x,
            int y,
            nint icon,
            int width,
            int height,
            uint animationStep,
            nint flickerFreeBrush,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(nint icon);

        [DllImport("user32.dll")]
        internal static extern int FillRect(
            nint deviceContext,
            ref NativeRect bounds,
            nint brush);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DrawFocusRect(
            nint deviceContext,
            ref NativeRect bounds);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(
            nint window,
            int attribute,
            ref int value,
            int size);

#if WINDOWS
        [DllImport("uiautomationcore.dll")]
        internal static extern nint UiaReturnRawElementProvider(
            nint window,
            nint wordParameter,
            nint longParameter,
            nint provider);
#endif
    }
}
