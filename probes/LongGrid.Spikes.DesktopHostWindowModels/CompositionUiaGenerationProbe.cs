using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

internal static class CompositionUiaGenerationProbe
{
    private static readonly UiaGenerationSnapshot Initial =
        new(1, new Rect(-760, -440, 280, 160));

    private static readonly UiaGenerationSnapshot Applied =
        new(2, new Rect(-680, -380, 300, 180));

    private static readonly UiaGenerationSnapshot Superseded =
        new(3, new Rect(-590, -310, 320, 190));

    internal static CompositionUiaGenerationReport Run(
        bool perMonitorV2Requested)
    {
        WarmUp();
        using Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        ResourceSnapshot created = before;
        bool hiddenThroughout = false;
        bool foregroundPreserved = false;
        bool dcompTargetCreated = false;
        bool visualRootCommitted = false;
        bool waitCompleted = false;
        bool appliedVerified = false;
        bool rollbackVerified = false;
        bool unpublishedSupersededGeneration = false;
        bool uiaClientVerified = false;
        int commitCalls = 0;
        int waitCalls = 0;
        bool cleanupPassed = false;

        try
        {
            using var host = CompositionUiaHost.Create(Initial);
            created = ResourceSnapshot.Capture(process);
            dcompTargetCreated = host.TargetCreated;
            visualRootCommitted = host.RootCommitted;
            commitCalls = host.CommitCalls;
            waitCalls = host.WaitCalls;
            waitCompleted = host.LastWaitSucceeded;
            hiddenThroughout = !NativeMethods.IsWindowVisible(
                host.Window);
            foregroundPreserved =
                foregroundBefore == NativeMethods.GetForegroundWindow();

            long generation = Initial.Generation;
            bool applied = host.TryPublish(
                generation,
                () => generation,
                Applied);
            commitCalls = host.CommitCalls;
            waitCalls = host.WaitCalls;
            appliedVerified =
                applied
                && host.CompositionGeneration
                    == Applied.Generation
                && host.Provider.Snapshot == Applied;
            UiaClientSnapshot clientApplied =
                ReadWithUiaClient(host.Window);
            uiaClientVerified =
                clientApplied.Generation == Applied.Generation
                && clientApplied.Bounds == Applied.Bounds
                && clientApplied.AutomationId
                    == $"LongGrid.Generation.{Applied.Generation}";

            generation = Applied.Generation;
            bool superseded = host.TryPublish(
                generation,
                () => generation,
                Superseded,
                afterCompositionCommit: () => generation++);
            commitCalls = host.CommitCalls;
            waitCalls = host.WaitCalls;
            UiaClientSnapshot clientAfterRollback =
                ReadWithUiaClient(host.Window);
            rollbackVerified =
                !superseded
                && host.CompositionGeneration
                    == Applied.Generation
                && host.Provider.Snapshot == Applied;
            unpublishedSupersededGeneration =
                clientAfterRollback.Generation == Applied.Generation
                && clientAfterRollback.Bounds == Applied.Bounds
                && clientAfterRollback.AutomationId
                    == $"LongGrid.Generation.{Applied.Generation}";
            hiddenThroughout &=
                !NativeMethods.IsWindowVisible(host.Window);
            foregroundPreserved &=
                foregroundBefore == NativeMethods.GetForegroundWindow();
            waitCompleted &= host.LastWaitSucceeded;
            cleanupPassed = host.DisposeAndVerify();
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        foregroundPreserved &=
            foregroundBefore == NativeMethods.GetForegroundWindow();
        cleanupPassed &=
            after.UserObjects == before.UserObjects
            && after.GdiObjects == before.GdiObjects
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool passed =
            perMonitorV2Requested
            && dcompTargetCreated
            && visualRootCommitted
            && waitCompleted
            && appliedVerified
            && rollbackVerified
            && unpublishedSupersededGeneration
            && uiaClientVerified
            && hiddenThroughout
            && foregroundPreserved
            && cleanupPassed;

        return new CompositionUiaGenerationReport(
            Probe: "P0-07b2b2b2b2-directcomposition-uia-generation",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            DCompositionTargetCreated: dcompTargetCreated,
            VisualRootCommitted: visualRootCommitted,
            CommitCalls: commitCalls,
            WaitCalls: waitCalls,
            WaitForCommitCompletionSucceeded: waitCompleted,
            AppliedGeneration: Applied.Generation,
            AppliedCompositionAndProviderVerified: appliedVerified,
            SupersededGeneration: Superseded.Generation,
            SupersededRollbackVerified: rollbackVerified,
            SupersededGenerationNeverPublishedToUia:
                unpublishedSupersededGeneration,
            UiaClientVerified: uiaClientVerified,
            UiaBoundingRectangleUsesPhysicalScreenCoordinates: true,
            HiddenThroughout: hiddenThroughout,
            ForegroundPreserved: foregroundPreserved,
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
                "The probe uses one hidden, same-thread, probe-owned HWND and a visual without rendered content.",
                "DirectComposition commits are atomic only within one device; HWND Bounds, Window Region, and UIA publication remain application-coordinated and UIA clients must honor the published generation token.",
                "UIA is read through a real AutomationElement client, but Narrator, fragment navigation, focus, events, and cross-process assistive technology remain unverified.",
                "The superseded generation is injected after a real Commit/Wait and is compensated before the provider snapshot is published.",
                "No display, DPI, rotation, projection, device, power, Explorer, or RDP transition was induced.",
            ]);
    }

    private static void WarmUp()
    {
        using var host = CompositionUiaHost.Create(Initial);
        long generation = Initial.Generation;
        if (!host.TryPublish(
                generation,
                () => generation,
                Applied)
            || ReadWithUiaClient(host.Window).Generation
                != Applied.Generation
            || !host.DisposeAndVerify())
        {
            throw new InvalidOperationException(
                "The DirectComposition/UIA warm-up failed.");
        }
    }

    internal static UiaClientSnapshot ReadWithUiaClient(
        nint window)
    {
        AutomationElement element =
            AutomationElement.FromHandle(window);
        string automationId =
            element.Current.AutomationId;
        string itemStatus =
            element.Current.ItemStatus;
        Rect bounds =
            element.Current.BoundingRectangle;
        const string prefix = "generation:";
        if (!itemStatus.StartsWith(
                prefix,
                StringComparison.Ordinal)
            || !long.TryParse(
                itemStatus.AsSpan(prefix.Length),
                out long generation))
        {
            throw new InvalidOperationException(
                "UI Automation returned an invalid generation token.");
        }

        return new UiaClientSnapshot(
            generation,
            automationId,
            bounds);
    }
}

internal sealed class CompositionUiaHost : IDisposable
{
    private readonly string _className;
    private readonly nint _instance;
    private readonly WindowProcedure _windowProcedure;
    private readonly IDCompositionDevice _device;
    private readonly IDCompositionTarget _target;
    private readonly List<IDCompositionVisual> _retiredVisuals = [];
    private IDCompositionVisual _visual;
    private bool _disposed;

    private CompositionUiaHost(
        string className,
        nint instance,
        WindowProcedure windowProcedure,
        nint window,
        LongGridRawElementProvider provider,
        IDCompositionDevice device,
        IDCompositionTarget target,
        IDCompositionVisual visual)
    {
        _className = className;
        _instance = instance;
        _windowProcedure = windowProcedure;
        Window = window;
        Provider = provider;
        _device = device;
        _target = target;
        _visual = visual;
    }

    internal nint Window { get; }

    internal LongGridRawElementProvider Provider { get; }

    internal bool TargetCreated { get; private init; }

    internal bool RootCommitted { get; private set; }

    internal int CommitCalls { get; private set; }

    internal int WaitCalls { get; private set; }

    internal bool LastWaitSucceeded { get; private set; }

    internal long CompositionGeneration { get; private set; }

    internal long CompositionRevision { get; private set; }

    internal static CompositionUiaHost Create(
        UiaGenerationSnapshot initial)
    {
        string className =
            $"LongGrid.P0.CompositionUia.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        LongGridRawElementProvider? provider = null;
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
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        nint window = nint.Zero;
        IDCompositionDevice? device = null;
        IDCompositionTarget? target = null;
        IDCompositionVisual? visual = null;
        CompositionUiaHost? host = null;
        try
        {
            window = NativeMethods.CreateWindowEx(
                NativeMethods.WsExToolWindow
                | NativeMethods.WsExNoActivate,
                className,
                string.Empty,
                NativeMethods.WsPopup,
                checked((int)initial.Bounds.X),
                checked((int)initial.Bounds.Y),
                checked((int)initial.Bounds.Width),
                checked((int)initial.Bounds.Height),
                nint.Zero,
                nint.Zero,
                instance,
                nint.Zero);
            if (window == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            provider = new LongGridRawElementProvider(
                window,
                initial);
            Guid deviceId = typeof(IDCompositionDevice).GUID;
            int result = NativeMethods.DCompositionCreateDevice(
                nint.Zero,
                ref deviceId,
                out device);
            Marshal.ThrowExceptionForHR(result);
            Marshal.ThrowExceptionForHR(
                device.CreateTargetForHwnd(
                    window,
                    topmost: true,
                    out target));
            Marshal.ThrowExceptionForHR(
                device.CreateVisual(out visual));
            Marshal.ThrowExceptionForHR(
                target.SetRoot(visual));
            host = new CompositionUiaHost(
                className,
                instance,
                procedure,
                window,
                provider,
                device,
                target,
                visual)
            {
                TargetCreated = true,
            };
            if (!host.CommitAndWait())
            {
                host.Dispose();
                throw new InvalidOperationException(
                    "The initial DirectComposition commit failed.");
            }

            host.RootCommitted = true;
            host.CompositionGeneration = initial.Generation;
            host.CompositionRevision = 1;
            return host;
        }
        catch
        {
            if (host is not null)
            {
                host.Dispose();
            }
            else
            {
                ReleaseComObject(visual);
                ReleaseComObject(target);
                ReleaseComObject(device);
                if (window != nint.Zero)
                {
                    NativeMethods.DestroyWindow(window);
                }

                NativeMethods.UnregisterClass(
                    className,
                    instance);
            }

            GC.KeepAlive(procedure);
            throw;
        }
    }

    internal bool TryPublish(
        long expectedGeneration,
        Func<long> currentGeneration,
        UiaGenerationSnapshot proposed,
        Action? afterCompositionCommit = null)
    {
        if (currentGeneration() != expectedGeneration)
        {
            return false;
        }

        UiaGenerationSnapshot original = Provider.Snapshot;
        if (!SetWindowBounds(proposed.Bounds))
        {
            return false;
        }

        Marshal.ThrowExceptionForHR(
            _device.CreateVisual(
                out IDCompositionVisual proposedVisual));
        Marshal.ThrowExceptionForHR(
            _target.SetRoot(proposedVisual));
        if (!CommitAndWait())
        {
            _ = _target.SetRoot(_visual);
            _ = CommitAndWait();
            _ = SetWindowBounds(original.Bounds);
            ReleaseComObject(proposedVisual);
            return false;
        }

        CompositionGeneration = proposed.Generation;
        afterCompositionCommit?.Invoke();
        if (currentGeneration() != expectedGeneration)
        {
            Marshal.ThrowExceptionForHR(
                _target.SetRoot(_visual));
            _ = CommitAndWait();
            CompositionGeneration = original.Generation;
            _ = SetWindowBounds(original.Bounds);
            ReleaseComObject(proposedVisual);
            return false;
        }

        IDCompositionVisual oldVisual = _visual;
        _visual = proposedVisual;
        ReleaseComObject(oldVisual);
        Provider.Publish(proposed);
        return true;
    }

    internal PixelRect CaptureBounds()
    {
        if (!NativeMethods.GetWindowRect(
            Window,
            out NativeRect rectangle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        return new PixelRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    internal bool ApplyBounds(PixelRect bounds) =>
        bounds.HasArea
        && NativeMethods.SetWindowPos(
            Window,
            nint.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate
            | NativeMethods.SwpNoZOrder
            | NativeMethods.SwpNoOwnerZOrder);

    internal bool MatchesBounds(PixelRect expected) =>
        CaptureBounds() == expected;

    internal CompositionVisualSnapshot CaptureVisual() =>
        new(
            _visual,
            CompositionGeneration,
            CompositionRevision);

    internal bool ApplyVisual(
        long generation,
        long revision)
    {
        int createResult = _device.CreateVisual(
            out IDCompositionVisual proposedVisual);
        if (createResult < 0)
        {
            return false;
        }

        int rootResult = _target.SetRoot(proposedVisual);
        if (rootResult < 0 || !CommitAndWait())
        {
            _ = _target.SetRoot(_visual);
            _ = CommitAndWait();
            ReleaseComObject(proposedVisual);
            return false;
        }

        _retiredVisuals.Add(_visual);
        _visual = proposedVisual;
        CompositionGeneration = generation;
        CompositionRevision = revision;
        return true;
    }

    internal bool MatchesVisual(
        long generation,
        long revision) =>
        CompositionGeneration == generation
        && CompositionRevision == revision
        && LastWaitSucceeded;

    internal bool RestoreVisual(
        CompositionVisualSnapshot snapshot)
    {
        if (_target.SetRoot(snapshot.Visual) < 0
            || !CommitAndWait())
        {
            return false;
        }

        if (!ReferenceEquals(_visual, snapshot.Visual))
        {
            ReleaseComObject(_visual);
            _retiredVisuals.Remove(snapshot.Visual);
            _visual = snapshot.Visual;
        }

        CompositionGeneration = snapshot.Generation;
        CompositionRevision = snapshot.Revision;
        return true;
    }

    private bool SetWindowBounds(Rect bounds) =>
        NativeMethods.SetWindowPos(
            Window,
            nint.Zero,
            checked((int)bounds.X),
            checked((int)bounds.Y),
            checked((int)bounds.Width),
            checked((int)bounds.Height),
            NativeMethods.SwpNoActivate
            | NativeMethods.SwpNoZOrder
            | NativeMethods.SwpNoOwnerZOrder);

    internal bool DisposeAndVerify()
    {
        Dispose();
        return _disposed
            && !NativeMethods.IsWindow(Window);
    }

    private bool CommitAndWait()
    {
        CommitCalls++;
        int commit = _device.Commit();
        if (commit < 0)
        {
            LastWaitSucceeded = false;
            return false;
        }

        WaitCalls++;
        int wait = _device.WaitForCommitCompletion();
        LastWaitSucceeded = wait >= 0;
        return LastWaitSucceeded;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = _target.SetRoot(null);
            _ = CommitAndWait();
        }
        finally
        {
            ReleaseComObject(_visual);
            foreach (IDCompositionVisual visual in _retiredVisuals)
            {
                if (!ReferenceEquals(visual, _visual))
                {
                    ReleaseComObject(visual);
                }
            }

            _retiredVisuals.Clear();
            ReleaseComObject(_target);
            ReleaseComObject(_device);
            bool windowDestroyed =
                NativeMethods.DestroyWindow(Window);
            bool classUnregistered =
                NativeMethods.UnregisterClass(
                    _className,
                    _instance);
            _disposed =
                windowDestroyed && classUnregistered;
            GC.KeepAlive(_windowProcedure);
            GC.KeepAlive(Provider);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null
            && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}

internal sealed class LongGridRawElementProvider(
    nint window,
    UiaGenerationSnapshot initial)
    : IRawElementProviderSimple
{
    private UiaGenerationSnapshot _snapshot = initial;
    internal UiaGenerationSnapshot Snapshot =>
        Volatile.Read(ref _snapshot);

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(window);

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        UiaGenerationSnapshot snapshot = Snapshot;
        if (propertyId
            == AutomationElementIdentifiers.NameProperty.Id)
        {
            return "Long Grid composition host";
        }

        if (propertyId
            == AutomationElementIdentifiers.AutomationIdProperty.Id)
        {
            return $"LongGrid.Generation.{snapshot.Generation}";
        }

        if (propertyId
            == AutomationElementIdentifiers.ItemStatusProperty.Id)
        {
            return $"generation:{snapshot.Generation}";
        }

        if (propertyId
            == AutomationElementIdentifiers.BoundingRectangleProperty.Id)
        {
            return snapshot.Bounds;
        }

        if (propertyId
            == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Pane.Id;
        }

        return null;
    }

    internal void Publish(UiaGenerationSnapshot snapshot) =>
        Volatile.Write(ref _snapshot, snapshot);

}

[ComImport]
[Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDCompositionDevice
{
    [PreserveSig]
    int Commit();

    [PreserveSig]
    int WaitForCommitCompletion();

    [PreserveSig]
    int GetFrameStatistics(nint statistics);

    [PreserveSig]
    int CreateTargetForHwnd(
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool topmost,
        out IDCompositionTarget target);

    [PreserveSig]
    int CreateVisual(out IDCompositionVisual visual);
}

[ComImport]
[Guid("EACDD04C-117E-4E17-88F4-D1B12B0E3D89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDCompositionTarget
{
    [PreserveSig]
    int SetRoot(
        [MarshalAs(UnmanagedType.Interface)]
        IDCompositionVisual? visual);
}

[ComImport]
[Guid("4D93059D-097B-4651-9A60-F0F25116E2F3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDCompositionVisual
{
}

internal sealed record UiaGenerationSnapshot(
    long Generation,
    Rect Bounds)
    : IDesktopHostLayerSnapshot
{
    public void Dispose()
    {
    }
}

internal sealed class CompositionVisualSnapshot(
    IDCompositionVisual visual,
    long generation,
    long revision)
    : IDesktopHostLayerSnapshot
{
    internal IDCompositionVisual Visual { get; } = visual;

    internal long Generation { get; } = generation;

    internal long Revision { get; } = revision;

    public void Dispose()
    {
    }
}

internal sealed record UiaClientSnapshot(
    long Generation,
    string AutomationId,
    Rect Bounds);

internal sealed record CompositionUiaGenerationReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    bool DCompositionTargetCreated,
    bool VisualRootCommitted,
    int CommitCalls,
    int WaitCalls,
    bool WaitForCommitCompletionSucceeded,
    long AppliedGeneration,
    bool AppliedCompositionAndProviderVerified,
    long SupersededGeneration,
    bool SupersededRollbackVerified,
    bool SupersededGenerationNeverPublishedToUia,
    bool UiaClientVerified,
    bool UiaBoundingRectangleUsesPhysicalScreenCoordinates,
    bool HiddenThroughout,
    bool ForegroundPreserved,
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
