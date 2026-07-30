using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

internal static class WindowBatchTransactionProbe
{
    private static readonly IReadOnlyDictionary<string, PixelRect>
        InitialBounds =
            new Dictionary<string, PixelRect>(StringComparer.Ordinal)
            {
                ["one"] = new(-900, -520, 240, 140),
                ["two"] = new(-610, -330, 280, 160),
            };

    private static readonly IReadOnlyDictionary<string, PixelRect>
        AppliedBounds =
            new Dictionary<string, PixelRect>(StringComparer.Ordinal)
            {
                ["one"] = new(-820, -460, 260, 150),
                ["two"] = new(-510, -270, 300, 180),
            };

    private static readonly IReadOnlyDictionary<string, PixelRect>
        SupersededBounds =
            new Dictionary<string, PixelRect>(StringComparer.Ordinal)
            {
                ["one"] = new(-720, -400, 270, 155),
                ["two"] = new(-400, -210, 310, 185),
            };

    private static readonly IReadOnlyDictionary<string, PixelRect>
        PartialFailureBounds =
            new Dictionary<string, PixelRect>(StringComparer.Ordinal)
            {
                ["one"] = new(-650, -350, 280, 165),
                ["two"] = new(-320, -150, 320, 190),
            };

    internal static WindowBatchTransactionReport Run(
        bool perMonitorV2Requested)
    {
        WarmUpWindowLifecycle();
        using Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        HiddenTransactionWindowSet? windows = null;
        ResourceSnapshot created = before;
        LayoutRecoveryTransactionResult applied;
        LayoutRecoveryTransactionResult idempotent;
        LayoutRecoveryTransactionResult generationRollback;
        LayoutRecoveryTransactionResult partialRollback;
        bool hiddenThroughout;
        bool foregroundPreserved;
        bool passiveStyles;
        bool topmostAbsent;
        bool negativeCoordinatesRoundTripped;
        int captureCalls;
        int nativeBatchCalls;
        int partialFailureInjections;
        bool partialMutationSucceeded;
        bool cleanupPassed;

        try
        {
            windows = HiddenTransactionWindowSet.Create(InitialBounds);
            created = ResourceSnapshot.Capture(process);
            hiddenThroughout = AreHidden(windows.Handles.Values);
            bool foregroundPreservedDuringProbe =
                foregroundBefore == NativeMethods.GetForegroundWindow();
            var successAdapter = new Win32WindowBatchAdapter(
                windows.Handles);
            long generation = 1;
            var successCoordinator =
                new LayoutRecoveryTransactionCoordinator(
                    () => generation,
                    successAdapter);
            applied = successCoordinator.Execute(
                CreateRequest(
                    generation,
                    InitialBounds,
                    AppliedBounds));
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreservedDuringProbe &=
                foregroundBefore == NativeMethods.GetForegroundWindow();
            idempotent = successCoordinator.Execute(
                CreateRequest(
                    generation,
                    AppliedBounds,
                    AppliedBounds));
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreservedDuringProbe &=
                foregroundBefore == NativeMethods.GetForegroundWindow();

            generation = 2;
            var supersededAdapter = new Win32WindowBatchAdapter(
                windows.Handles)
            {
                AfterSuccessfulNativeBatch = call =>
                {
                    if (call == 1)
                    {
                        generation = 3;
                    }
                },
            };
            var supersededCoordinator =
                new LayoutRecoveryTransactionCoordinator(
                    () => generation,
                    supersededAdapter);
            generationRollback = supersededCoordinator.Execute(
                CreateRequest(
                    generation,
                    AppliedBounds,
                    SupersededBounds));
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreservedDuringProbe &=
                foregroundBefore == NativeMethods.GetForegroundWindow();

            var partialAdapter = new Win32WindowBatchAdapter(
                windows.Handles)
            {
                InjectPartialFailureOnce = true,
            };
            var partialCoordinator =
                new LayoutRecoveryTransactionCoordinator(
                    () => generation,
                    partialAdapter);
            partialRollback = partialCoordinator.Execute(
                CreateRequest(
                    generation,
                    AppliedBounds,
                    PartialFailureBounds));
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreservedDuringProbe &=
                foregroundBefore == NativeMethods.GetForegroundWindow();

            LayoutRecoveryBoundsCapture final =
                partialAdapter.Capture(["one", "two"]);
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            passiveStyles = windows.Handles.Values.All(
                HasPassiveStyles);
            topmostAbsent = windows.Handles.Values.All(
                HasNoTopmostStyle);
            negativeCoordinatesRoundTripped =
                final.Succeeded
                && Matches(final.Bounds, AppliedBounds)
                && final.Bounds.Values.All(bounds =>
                    bounds.Left < 0 && bounds.Top < 0);
            captureCalls =
                successAdapter.CaptureCalls
                + supersededAdapter.CaptureCalls
                + partialAdapter.CaptureCalls;
            nativeBatchCalls =
                successAdapter.SuccessfulNativeBatchCalls
                + supersededAdapter.SuccessfulNativeBatchCalls
                + partialAdapter.SuccessfulNativeBatchCalls;
            partialFailureInjections =
                partialAdapter.PartialFailureInjections;
            partialMutationSucceeded =
                partialAdapter.PartialMutationSucceeded;
            foregroundPreserved =
                foregroundPreservedDuringProbe;
        }
        finally
        {
            windows?.Dispose();
            cleanupPassed = windows?.CleanupSucceeded ?? false;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        foregroundPreserved &=
            foregroundBefore == NativeMethods.GetForegroundWindow();
        cleanupPassed =
            cleanupPassed
            && after.UserObjects == before.UserObjects
            && after.GdiObjects == before.GdiObjects
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool passed =
            perMonitorV2Requested
            && applied.Status == LayoutRecoveryTransactionStatus.Applied
            && idempotent.Status
                == LayoutRecoveryTransactionStatus.NoChanges
            && generationRollback.Status
                == LayoutRecoveryTransactionStatus.RolledBack
            && generationRollback.Failure
                == LayoutRecoveryTransactionFailure.GenerationChanged
            && generationRollback.Rollback
                == LayoutRecoveryRollbackStatus.Succeeded
            && partialRollback.Status
                == LayoutRecoveryTransactionStatus.RolledBack
            && partialRollback.Failure
                == LayoutRecoveryTransactionFailure.ApplyFailed
            && partialRollback.Rollback
                == LayoutRecoveryRollbackStatus.Succeeded
            && hiddenThroughout
            && foregroundPreserved
            && passiveStyles
            && topmostAbsent
            && negativeCoordinatesRoundTripped
            && partialMutationSucceeded
            && cleanupPassed;

        return new WindowBatchTransactionReport(
            Probe: "P0-07b2b2b2a-win32-layout-batch-adapter",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            WindowCount: InitialBounds.Count,
            CaptureCalls: captureCalls,
            SuccessfulNativeBatchCalls: nativeBatchCalls,
            PartialFailureInjections: partialFailureInjections,
            PartialMutationSucceeded: partialMutationSucceeded,
            AppliedStatus: applied.Status,
            IdempotentStatus: idempotent.Status,
            GenerationRollbackStatus: generationRollback.Status,
            GenerationRollbackFailure: generationRollback.Failure,
            GenerationRollbackVerified:
                generationRollback.Rollback
                == LayoutRecoveryRollbackStatus.Succeeded,
            PartialFailureRollbackStatus: partialRollback.Status,
            PartialFailureRollbackFailure: partialRollback.Failure,
            PartialFailureRollbackVerified:
                partialRollback.Rollback
                == LayoutRecoveryRollbackStatus.Succeeded,
            HiddenThroughout: hiddenThroughout,
            ForegroundPreserved: foregroundPreserved,
            PassiveStylesPresent: passiveStyles,
            TopmostStyleAbsent: topmostAbsent,
            NegativeCoordinatesRoundTripped:
                negativeCoordinatesRoundTripped,
            DisplayStateChanged: false,
            ExternalWindowStateChanged: false,
            SyntheticFailureInjection: true,
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
                "Only hidden, same-thread, probe-owned top-level HWNDs were moved.",
                "The partial failure is injected after moving one hidden probe window; it is not an observed DeferWindowPos or EndDeferWindowPos failure.",
                "Window Region, Composition, UI Automation, cross-thread HWNDs, and visible rendering are not part of this probe.",
                "No display, DPI, rotation, projection, device, power, or RDP transition was induced.",
            ]);
    }

    private static void WarmUpWindowLifecycle()
    {
        HiddenTransactionWindowSet windows =
            HiddenTransactionWindowSet.Create(InitialBounds);
        try
        {
            var adapter = new Win32WindowBatchAdapter(
                windows.Handles);
            if (!adapter.Capture(["one", "two"]).Succeeded)
            {
                throw new InvalidOperationException(
                    "The hidden transaction window warm-up failed.");
            }
        }
        finally
        {
            windows.Dispose();
        }

        if (!windows.CleanupSucceeded)
        {
            throw new InvalidOperationException(
                "The hidden transaction window warm-up leaked resources.");
        }
    }

    private static LayoutRecoveryTransactionRequest CreateRequest(
        long generation,
        IReadOnlyDictionary<string, PixelRect> current,
        IReadOnlyDictionary<string, PixelRect> proposed) =>
        new(
            generation,
            new LayoutRecoveryPlan(
                LayoutRecoveryStatus.Automatic,
                [],
                [],
                proposed
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                        new ContainerRecoveryPlacement(
                            pair.Key,
                            "probe",
                            "probe",
                            current[pair.Key],
                            pair.Value,
                            current[pair.Key] != pair.Value))
                    .ToArray()),
            ReviewApproved: true);

    private static bool HasPassiveStyles(nint window)
    {
        ulong style = unchecked(
            (ulong)NativeMethods.GetWindowLongPtr(
                window,
                NativeMethods.GwlExStyle).ToInt64());
        ulong required =
            NativeMethods.WsExToolWindow
            | NativeMethods.WsExNoActivate;
        return (style & required) == required;
    }

    private static bool HasNoTopmostStyle(nint window)
    {
        ulong style = unchecked(
            (ulong)NativeMethods.GetWindowLongPtr(
                window,
                NativeMethods.GwlExStyle).ToInt64());
        return (style & NativeMethods.WsExTopmost) == 0;
    }

    private static bool AreHidden(IEnumerable<nint> windows) =>
        windows.All(window =>
            !NativeMethods.IsWindowVisible(window));

    private static bool Matches(
        IReadOnlyDictionary<string, PixelRect> actual,
        IReadOnlyDictionary<string, PixelRect> expected) =>
        actual.Count == expected.Count
        && expected.All(pair =>
            actual.TryGetValue(pair.Key, out PixelRect bounds)
            && bounds == pair.Value);
}

internal sealed class Win32WindowBatchAdapter(
    IReadOnlyDictionary<string, nint> handles)
    : ILayoutRecoveryWindowBatchAdapter
{
    private readonly Dictionary<string, nint> _handles =
        new Dictionary<string, nint>(
            handles,
            StringComparer.Ordinal);
    private bool _partialFailureInjected;

    internal bool InjectPartialFailureOnce { get; init; }

    internal Action<int>? AfterSuccessfulNativeBatch { get; init; }

    internal int CaptureCalls { get; private set; }

    internal int SuccessfulNativeBatchCalls { get; private set; }

    internal int PartialFailureInjections { get; private set; }

    internal bool PartialMutationSucceeded { get; private set; }

    public LayoutRecoveryBoundsCapture Capture(
        IReadOnlyList<string> containerIds)
    {
        CaptureCalls++;
        var bounds = new Dictionary<string, PixelRect>(
            StringComparer.Ordinal);
        foreach (string containerId in containerIds)
        {
            if (!_handles.TryGetValue(containerId, out nint window)
                || !NativeMethods.GetWindowRect(
                    window,
                    out NativeRect rectangle))
            {
                return LayoutRecoveryBoundsCapture.Failed;
            }

            var item = new PixelRect(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right - rectangle.Left,
                rectangle.Bottom - rectangle.Top);
            if (!item.HasArea)
            {
                return LayoutRecoveryBoundsCapture.Failed;
            }

            bounds.Add(containerId, item);
        }

        return new LayoutRecoveryBoundsCapture(true, bounds);
    }

    public bool Apply(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements)
    {
        if (InjectPartialFailureOnce && !_partialFailureInjected)
        {
            _partialFailureInjected = true;
            PartialFailureInjections++;
            LayoutRecoveryWindowPlacement first = placements[0];
            PartialMutationSucceeded =
                _handles.TryGetValue(
                first.ContainerId,
                out nint firstWindow)
                && NativeMethods.SetWindowPos(
                    firstWindow,
                    nint.Zero,
                    first.Bounds.Left,
                    first.Bounds.Top,
                    first.Bounds.Width,
                    first.Bounds.Height,
                    NativeMethods.SwpNoActivate
                    | NativeMethods.SwpNoZOrder
                    | NativeMethods.SwpNoOwnerZOrder);
            return false;
        }

        nint deferred = NativeMethods.BeginDeferWindowPos(
            placements.Count);
        if (deferred == nint.Zero)
        {
            return false;
        }

        foreach (LayoutRecoveryWindowPlacement placement in placements)
        {
            if (!_handles.TryGetValue(
                placement.ContainerId,
                out nint window))
            {
                return false;
            }

            deferred = NativeMethods.DeferWindowPos(
                deferred,
                window,
                nint.Zero,
                placement.Bounds.Left,
                placement.Bounds.Top,
                placement.Bounds.Width,
                placement.Bounds.Height,
                NativeMethods.SwpNoActivate
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoOwnerZOrder);
            if (deferred == nint.Zero)
            {
                return false;
            }
        }

        if (!NativeMethods.EndDeferWindowPos(deferred))
        {
            return false;
        }

        SuccessfulNativeBatchCalls++;
        AfterSuccessfulNativeBatch?.Invoke(
            SuccessfulNativeBatchCalls);
        return true;
    }
}

internal sealed class HiddenTransactionWindowSet : IDisposable
{
    private readonly string _className;
    private readonly nint _instance;
    private bool _disposed;

    private HiddenTransactionWindowSet(
        string className,
        nint instance,
        IReadOnlyDictionary<string, nint> handles)
    {
        _className = className;
        _instance = instance;
        Handles = handles;
    }

    internal IReadOnlyDictionary<string, nint> Handles { get; }

    internal bool CleanupSucceeded { get; private set; }

    internal static HiddenTransactionWindowSet Create(
        IReadOnlyDictionary<string, PixelRect> bounds)
    {
        string className =
            $"LongGrid.P0.LayoutTransaction.{Guid.NewGuid():N}";
        nint instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = checked((uint)Marshal.SizeOf<WindowClass>()),
            Instance = instance,
            WindowProcedure = WindowModelProbeWindowProcedure.Instance,
            ClassName = className,
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var handles = new Dictionary<string, nint>(
            StringComparer.Ordinal);
        var result = new HiddenTransactionWindowSet(
            className,
            instance,
            handles);
        try
        {
            foreach (KeyValuePair<string, PixelRect> pair in bounds)
            {
                PixelRect item = pair.Value;
                nint window = NativeMethods.CreateWindowEx(
                    NativeMethods.WsExToolWindow
                    | NativeMethods.WsExNoActivate,
                    className,
                    string.Empty,
                    NativeMethods.WsPopup,
                    item.Left,
                    item.Top,
                    item.Width,
                    item.Height,
                    nint.Zero,
                    nint.Zero,
                    instance,
                    nint.Zero);
                if (window == nint.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error());
                }

                handles.Add(pair.Key, window);
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

        bool windowsDestroyed = true;
        foreach (nint window in Handles.Values.Reverse())
        {
            windowsDestroyed &=
                NativeMethods.DestroyWindow(window);
        }

        bool classUnregistered =
            NativeMethods.UnregisterClass(
                _className,
                _instance);
        CleanupSucceeded =
            windowsDestroyed && classUnregistered;
        _disposed = true;
    }
}

internal sealed record WindowBatchTransactionReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int WindowCount,
    int CaptureCalls,
    int SuccessfulNativeBatchCalls,
    int PartialFailureInjections,
    bool PartialMutationSucceeded,
    LayoutRecoveryTransactionStatus AppliedStatus,
    LayoutRecoveryTransactionStatus IdempotentStatus,
    LayoutRecoveryTransactionStatus GenerationRollbackStatus,
    LayoutRecoveryTransactionFailure GenerationRollbackFailure,
    bool GenerationRollbackVerified,
    LayoutRecoveryTransactionStatus PartialFailureRollbackStatus,
    LayoutRecoveryTransactionFailure PartialFailureRollbackFailure,
    bool PartialFailureRollbackVerified,
    bool HiddenThroughout,
    bool ForegroundPreserved,
    bool PassiveStylesPresent,
    bool TopmostStyleAbsent,
    bool NegativeCoordinatesRoundTripped,
    bool DisplayStateChanged,
    bool ExternalWindowStateChanged,
    bool SyntheticFailureInjection,
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
