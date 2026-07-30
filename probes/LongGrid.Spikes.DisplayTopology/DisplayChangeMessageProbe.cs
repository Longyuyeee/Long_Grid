using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

internal sealed class DisplayChangeMessageProbe : IDisposable
{
    private const string WindowClassName =
        "LongGrid.Spikes.DisplayChangeMessageWindow";
    private const uint TimerIntervalMilliseconds = 50;
    private static readonly WindowProcedure WindowProcedure = WndProc;
    private static DisplayChangeMessageProbe? _active;

    private readonly TimeSpan _duration;
    private readonly Stopwatch _stopwatch = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly DisplayTopologyStabilizer _stabilizer = new();
    private readonly ConcurrentQueue<SnapshotCompletion> _completions = new();
    private readonly BlockingCollection<long> _snapshotRequests = new();
    private readonly Dictionary<DisplayChangeReason, int> _reasonCounts = [];
    private nint _window;
    private nint _instance;
    private bool _classRegistered;
    private bool _classUnregistered;
    private bool _wtsRegistrationSucceeded;
    private bool _wtsRegistered;
    private bool _wtsUnregistered;
    private bool _snapshotInFlight;
    private bool _stopRequested;
    private bool _messageLoopRunning;
    private Thread? _snapshotThread;
    private Exception? _fatalException;
    private int _timerTicks;
    private int _snapshotAttempts;
    private int _snapshotFailures;
    private int _staleSnapshots;
    private int _readyTransitions;
    private int _dpiSuggestedRectsApplied;
    private bool _disposed;

    private DisplayChangeMessageProbe(TimeSpan duration)
    {
        _duration = duration;
    }

    internal static DisplayChangeMessageProbeReport Run(int watchSeconds)
    {
        if (watchSeconds is < 2 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(watchSeconds));
        }

        if (_active is not null)
        {
            throw new InvalidOperationException(
                "Only one display message probe may run at a time.");
        }

        using var probe = new DisplayChangeMessageProbe(
            TimeSpan.FromSeconds(watchSeconds));
        WarmUpNativeLifecycle();
        return probe.RunCore();
    }

    private static void WarmUpNativeLifecycle()
    {
        using var warmup =
            new DisplayChangeMessageProbe(TimeSpan.Zero);
        _active = warmup;
        try
        {
            warmup.CreateMessageWindow();
            NativeMethods.PeekMessage(
                out _,
                nint.Zero,
                0,
                0,
                NativeMethods.PmNoRemove);
            if (!NativeMethods.PostMessage(
                warmup._window,
                NativeMethods.WmAppWarmup,
                0,
                nint.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            int result = NativeMethods.GetMessage(
                out WindowMessage message,
                nint.Zero,
                0,
                0);
            if (result <= 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
        finally
        {
            warmup.Cleanup();
            _active = null;
        }
    }

    private DisplayChangeMessageProbeReport RunCore()
    {
        using Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        _active = this;

        try
        {
            CreateMessageWindow();
            StartSnapshotWorker();
            _stopwatch.Start();
            RecordChange(DisplayChangeReason.Startup);
            _messageLoopRunning = true;

            int messageResult;
            while ((messageResult = NativeMethods.GetMessage(
                out WindowMessage message,
                nint.Zero,
                0,
                0)) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }

            _messageLoopRunning = false;
            if (messageResult < 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (_fatalException is not null)
            {
                throw new InvalidOperationException(
                    "The display message window failed.",
                    _fatalException);
            }
        }
        finally
        {
            Cleanup();
            _active = null;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        DisplayTopologyStabilizationResult final = _stabilizer.Current;
        bool passed = _classRegistered
            && _classUnregistered
            && _wtsRegistrationSucceeded
            && _wtsUnregistered
            && final.State == DisplayTopologyStabilizationState.Ready
            && _snapshotAttempts >= 2
            && _snapshotFailures == 0
            && _staleSnapshots == 0
            && after.UserObjects == before.UserObjects
            && after.GdiObjects == before.GdiObjects
            && after.ProcessHandles <= before.ProcessHandles + 2;

        return new DisplayChangeMessageProbeReport(
            Probe: "P0-07b2b2a-display-message-window",
            TimestampUtc: _startedAtUtc,
            DurationMilliseconds: _stopwatch.Elapsed.TotalMilliseconds,
            FinalState: final.State,
            FinalGeneration: final.Generation,
            FinalReasons: final.Reasons,
            WtsRegistrationSucceeded: _wtsRegistrationSucceeded,
            WtsUnregistrationSucceeded: _wtsUnregistered,
            WindowClassRegistered: _classRegistered,
            WindowClassUnregistered: _classUnregistered,
            TimerTicks: _timerTicks,
            SnapshotAttempts: _snapshotAttempts,
            SnapshotFailures: _snapshotFailures,
            StaleSnapshots: _staleSnapshots,
            ReadyTransitions: _readyTransitions,
            DpiSuggestedRectsApplied: _dpiSuggestedRectsApplied,
            ObservedReasonCounts: _reasonCounts
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            UserObjectsBefore: before.UserObjects,
            UserObjectsAfter: after.UserObjects,
            GdiObjectsBefore: before.GdiObjects,
            GdiObjectsAfter: after.GdiObjects,
            ProcessHandlesBefore: before.ProcessHandles,
            ProcessHandlesAfter: after.ProcessHandles,
            Result: passed ? "Conditional Pass" : "Fail",
            Privacy:
            [
                "No monitor name, device path, adapter/target ID, topology fingerprint, window title, or session ID is printed.",
                "The message window is hidden, non-activating, not topmost, and never shown.",
                "CCD and DPI sampling runs outside WindowProc and returns through a private WM_APP message.",
            ],
            Limitations:
            [
                "The current session was observed without inducing a display, DPI, device, power, or session transition.",
                "Real rotation, scaling, attach/detach, projection, sleep, RDP, and rollback remain controlled-lab gates.",
                "SetTimer is approximate; the observed timing is a correctness smoke test, not a performance budget.",
            ]);
    }

    private void CreateMessageWindow()
    {
        _instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            Size = checked((uint)Marshal.SizeOf<WindowClassEx>()),
            WindowProcedure = WindowProcedure,
            Instance = _instance,
            ClassName = WindowClassName,
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _classRegistered = true;
        _window = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate,
            WindowClassName,
            string.Empty,
            NativeMethods.WsPopup,
            0,
            0,
            1,
            1,
            nint.Zero,
            nint.Zero,
            _instance,
            nint.Zero);
        if (_window == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _wtsRegistered =
            NativeMethods.WTSRegisterSessionNotification(
                _window,
                NativeMethods.NotifyForThisSession);
        if (!_wtsRegistered)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _wtsRegistrationSucceeded = true;
        if (NativeMethods.SetTimer(
            _window,
            NativeMethods.MessageTimerId,
            TimerIntervalMilliseconds,
            nint.Zero) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static nint WndProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        DisplayChangeMessageProbe? probe = _active;
        if (probe is null)
        {
            return NativeMethods.DefWindowProc(
                window,
                message,
                wParam,
                lParam);
        }

        try
        {
            return probe.HandleMessage(
                window,
                message,
                wParam,
                lParam);
        }
        catch (Exception exception)
        {
            probe._fatalException = exception;
            probe.CloseWindow();
            return nint.Zero;
        }
    }

    private nint HandleMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmDisplayChange:
                RecordChange(DisplayChangeReason.DisplayConfiguration);
                return nint.Zero;
            case NativeMethods.WmDpiChanged:
                ApplySuggestedDpiRectangle(window, lParam);
                RecordChange(DisplayChangeReason.Dpi);
                return nint.Zero;
            case NativeMethods.WmDeviceChange:
                RecordChange(DisplayChangeReason.Device);
                return new nint(1);
            case NativeMethods.WmPowerBroadcast:
                HandlePowerChange(unchecked((uint)wParam));
                return new nint(1);
            case NativeMethods.WmWtsSessionChange:
                HandleSessionChange(unchecked((uint)wParam));
                return nint.Zero;
            case NativeMethods.WmTimer:
                if (wParam == NativeMethods.MessageTimerId)
                {
                    OnTimer();
                    return nint.Zero;
                }

                break;
            case NativeMethods.WmAppSnapshotCompleted:
                OnSnapshotCompleted();
                return nint.Zero;
            case NativeMethods.WmDestroy:
                if (_messageLoopRunning)
                {
                    NativeMethods.PostQuitMessage(0);
                }

                return nint.Zero;
        }

        return NativeMethods.DefWindowProc(
            window,
            message,
            wParam,
            lParam);
    }

    private void OnTimer()
    {
        _timerTicks++;
        if (_stopwatch.Elapsed >= _duration)
        {
            _stopRequested = true;
        }

        if (_stopRequested)
        {
            if (!_snapshotInFlight)
            {
                CloseWindow();
            }

            return;
        }

        DisplayTopologyStabilizationResult current = _stabilizer.Current;
        if (current.State is not (
            DisplayTopologyStabilizationState.WaitingQuietPeriod
            or DisplayTopologyStabilizationState.Sampling)
            || current.NextActionAt is null
            || Now < current.NextActionAt.Value
            || _snapshotInFlight)
        {
            return;
        }

        StartSnapshot(current.Generation);
    }

    private void StartSnapshot(long generation)
    {
        _snapshotInFlight = true;
        _snapshotAttempts++;
        _snapshotRequests.Add(generation);
    }

    private void StartSnapshotWorker()
    {
        _snapshotThread = new Thread(SnapshotWorker)
        {
            IsBackground = true,
            Name = "LongGrid.DisplaySnapshotProbe",
        };
        _snapshotThread.Start();
    }

    private void SnapshotWorker()
    {
        foreach (long generation in _snapshotRequests.GetConsumingEnumerable())
        {
            try
            {
                CombinedDisplaySnapshot snapshot = Program.CaptureSnapshot();
                string fingerprint = DisplayTopologyFingerprint.Compute(
                    snapshot.Displays.Displays);
                _completions.Enqueue(
                    new SnapshotCompletion(
                        generation,
                        fingerprint,
                        null));
            }
            catch (Exception exception)
            {
                _completions.Enqueue(
                    new SnapshotCompletion(
                        generation,
                        null,
                        exception));
            }
            finally
            {
                if (!NativeMethods.PostMessage(
                    _window,
                    NativeMethods.WmAppSnapshotCompleted,
                    0,
                    nint.Zero))
                {
                    _fatalException = new Win32Exception(
                        Marshal.GetLastWin32Error());
                }
            }
        }
    }

    private void OnSnapshotCompleted()
    {
        while (_completions.TryDequeue(out SnapshotCompletion? completion))
        {
            _snapshotInFlight = false;
            if (completion.Error is not null)
            {
                _snapshotFailures++;
                continue;
            }

            if (completion.Generation != _stabilizer.Current.Generation)
            {
                _staleSnapshots++;
                continue;
            }

            DisplayTopologyStabilizationState before =
                _stabilizer.Current.State;
            DisplayTopologyStabilizationResult result =
                _stabilizer.ObserveTopology(
                    completion.Fingerprint!,
                    Now);
            if (before != DisplayTopologyStabilizationState.Ready
                && result.State == DisplayTopologyStabilizationState.Ready)
            {
                _readyTransitions++;
            }
        }

        if (_stopRequested && !_snapshotInFlight)
        {
            CloseWindow();
        }
    }

    private void RecordChange(DisplayChangeReason reason)
    {
        CountReason(reason);
        _stabilizer.RecordChange(reason, Now);
    }

    private void HandlePowerChange(uint eventType)
    {
        switch (eventType)
        {
            case NativeMethods.PbtApmSuspend:
                CountReason(DisplayChangeReason.PowerSuspend);
                _stabilizer.Pause(
                    DisplayChangeReason.PowerSuspend,
                    Now);
                break;
            case NativeMethods.PbtApmResumeAutomatic:
            case NativeMethods.PbtApmResumeSuspend:
                CountReason(DisplayChangeReason.PowerResume);
                _stabilizer.Resume(
                    DisplayChangeReason.PowerResume,
                    Now);
                break;
        }
    }

    private void HandleSessionChange(uint eventType)
    {
        switch (eventType)
        {
            case NativeMethods.WtsConsoleDisconnect:
            case NativeMethods.WtsRemoteDisconnect:
            case NativeMethods.WtsSessionLock:
                CountReason(DisplayChangeReason.SessionUnavailable);
                _stabilizer.Pause(
                    DisplayChangeReason.SessionUnavailable,
                    Now);
                break;
            case NativeMethods.WtsConsoleConnect:
            case NativeMethods.WtsRemoteConnect:
            case NativeMethods.WtsSessionUnlock:
            case NativeMethods.WtsSessionDesktopReady:
                CountReason(DisplayChangeReason.SessionAvailable);
                _stabilizer.Resume(
                    DisplayChangeReason.SessionAvailable,
                    Now);
                break;
        }
    }

    private void ApplySuggestedDpiRectangle(nint window, nint lParam)
    {
        if (lParam == nint.Zero)
        {
            return;
        }

        NativeRect rectangle = Marshal.PtrToStructure<NativeRect>(lParam);
        if (!NativeMethods.SetWindowPos(
            window,
            nint.Zero,
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _dpiSuggestedRectsApplied++;
    }

    private void CountReason(DisplayChangeReason reason)
    {
        _reasonCounts.TryGetValue(reason, out int count);
        _reasonCounts[reason] = checked(count + 1);
    }

    private DateTimeOffset Now =>
        _startedAtUtc + _stopwatch.Elapsed;

    private void Cleanup()
    {
        CloseWindow();
        if (!_snapshotRequests.IsAddingCompleted)
        {
            _snapshotRequests.CompleteAdding();
        }

        if (_snapshotThread is not null
            && !_snapshotThread.Join(TimeSpan.FromSeconds(5)))
        {
            _fatalException ??= new TimeoutException(
                "The display snapshot worker did not stop.");
        }

        if (_classRegistered && !_classUnregistered)
        {
            _classUnregistered = NativeMethods.UnregisterClass(
                WindowClassName,
                _instance);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cleanup();
        _snapshotRequests.Dispose();
        _disposed = true;
    }

    private void CloseWindow()
    {
        if (_window == nint.Zero)
        {
            return;
        }

        NativeMethods.KillTimer(
            _window,
            NativeMethods.MessageTimerId);
        if (_wtsRegistered)
        {
            _wtsUnregistered =
                NativeMethods.WTSUnRegisterSessionNotification(
                    _window);
            _wtsRegistered = false;
        }

        nint window = _window;
        _window = nint.Zero;
        if (!NativeMethods.DestroyWindow(window)
            && Marshal.GetLastWin32Error() != 0)
        {
            if (_fatalException is null)
            {
                _fatalException = new Win32Exception(
                    Marshal.GetLastWin32Error());
            }
        }
    }
}

internal sealed record SnapshotCompletion(
    long Generation,
    string? Fingerprint,
    Exception? Error);

internal sealed record DisplayChangeMessageProbeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    double DurationMilliseconds,
    DisplayTopologyStabilizationState FinalState,
    long FinalGeneration,
    DisplayChangeReason FinalReasons,
    bool WtsRegistrationSucceeded,
    bool WtsUnregistrationSucceeded,
    bool WindowClassRegistered,
    bool WindowClassUnregistered,
    int TimerTicks,
    int SnapshotAttempts,
    int SnapshotFailures,
    int StaleSnapshots,
    int ReadyTransitions,
    int DpiSuggestedRectsApplied,
    IReadOnlyDictionary<DisplayChangeReason, int> ObservedReasonCounts,
    uint UserObjectsBefore,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesAfter,
    string Result,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);
