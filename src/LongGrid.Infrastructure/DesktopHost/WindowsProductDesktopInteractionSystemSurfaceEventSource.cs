using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;
using Microsoft.Win32;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopSystemSurfaceSample(
    nint ShellWindow,
    nint ForegroundWindow,
    bool FullScreenStateKnown,
    bool FullScreenActive,
    bool RemoteSession);

internal sealed class ProductDesktopSystemSurfaceEventClassifier
{
    private const int StableSamplesRequired = 2;
    private bool initialized;
    private nint shellWindow;
    private bool desktopForeground;
    private bool fullScreenActive;
    private bool remoteSession;
    private bool sessionUnavailable;
    private bool powerSuspended;
    private bool recoveryPending;
    private int stableSafeSamples;

    internal IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind>
        Observe(ProductDesktopSystemSurfaceSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var events = new List<ProductDesktopInteractionSystemSurfaceEventKind>();
        bool shellUnavailable = sample.ShellWindow == nint.Zero;
        bool nextDesktopForeground = !shellUnavailable
            && sample.ForegroundWindow == sample.ShellWindow;
        bool nextFullScreen = !sample.FullScreenStateKnown
            || sample.FullScreenActive;

        if (!initialized)
        {
            initialized = true;
            shellWindow = sample.ShellWindow;
            desktopForeground = nextDesktopForeground;
            fullScreenActive = nextFullScreen;
            remoteSession = sample.RemoteSession;
            if (shellUnavailable)
            {
                AddUnsafe(
                    events,
                    ProductDesktopInteractionSystemSurfaceEventKind
                        .ExplorerRestarted);
            }

            if (nextDesktopForeground)
            {
                AddUnsafe(
                    events,
                    ProductDesktopInteractionSystemSurfaceEventKind
                        .DesktopRevealRequested);
            }

            if (nextFullScreen)
            {
                AddUnsafe(
                    events,
                    ProductDesktopInteractionSystemSurfaceEventKind
                        .FullScreenTransition);
            }

            return events.AsReadOnly();
        }

        if (sample.ShellWindow != shellWindow)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .ExplorerRestarted);
        }

        if (sample.RemoteSession != remoteSession)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RemoteSessionTransition);
        }

        if (nextDesktopForeground && !desktopForeground)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .DesktopRevealRequested);
        }

        if (nextFullScreen && !fullScreenActive)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .FullScreenTransition);
        }

        shellWindow = sample.ShellWindow;
        desktopForeground = nextDesktopForeground;
        fullScreenActive = nextFullScreen;
        remoteSession = sample.RemoteSession;
        bool safe = !shellUnavailable
            && !nextDesktopForeground
            && !nextFullScreen
            && !sessionUnavailable
            && !powerSuspended;
        if (recoveryPending && safe)
        {
            stableSafeSamples++;
            if (stableSafeSamples >= StableSamplesRequired)
            {
                recoveryPending = false;
                stableSafeSamples = 0;
                events.Add(
                    ProductDesktopInteractionSystemSurfaceEventKind
                        .RecoveryCandidate);
            }
        }
        else if (!safe)
        {
            stableSafeSamples = 0;
        }

        return events.AsReadOnly();
    }

    internal IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind>
        ObserveFocusLost()
    {
        var events = new List<ProductDesktopInteractionSystemSurfaceEventKind>();
        AddUnsafe(
            events,
            ProductDesktopInteractionSystemSurfaceEventKind.FocusLost);
        return events.AsReadOnly();
    }

    internal IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind>
        ObserveSessionAvailability(bool available, bool remoteTransition)
    {
        var events = new List<ProductDesktopInteractionSystemSurfaceEventKind>();
        sessionUnavailable = !available;
        if (remoteTransition)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RemoteSessionTransition);
        }

        if (!available)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .SessionUnavailable);
        }

        return events.AsReadOnly();
    }

    internal IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind>
        ObservePowerAvailability(bool available)
    {
        powerSuspended = !available;
        var events = new List<ProductDesktopInteractionSystemSurfaceEventKind>();
        if (!available)
        {
            AddUnsafe(
                events,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .SessionUnavailable);
        }

        return events.AsReadOnly();
    }

    private void AddUnsafe(
        List<ProductDesktopInteractionSystemSurfaceEventKind> events,
        ProductDesktopInteractionSystemSurfaceEventKind kind)
    {
        recoveryPending = true;
        stableSafeSamples = 0;
        events.Add(kind);
    }
}

public sealed class WindowsProductDesktopInteractionSystemSurfaceEventSource
    : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private readonly object gate = new();
    private readonly ProductDesktopSystemSurfaceEventClassifier classifier = new();
    private readonly IProductDesktopSystemSurfaceNativeSampler sampler;
    private readonly TimeSpan sampleDueTime;
    private readonly TimeSpan samplePeriod;
    private Timer? timer;
    private long sequence;
    private int sampleInProgress;
    private bool started;
    private bool disposed;

    public WindowsProductDesktopInteractionSystemSurfaceEventSource()
        : this(
            new WindowsProductDesktopSystemSurfaceNativeSampler(),
            TimeSpan.Zero,
            SampleInterval)
    {
    }

    internal WindowsProductDesktopInteractionSystemSurfaceEventSource(
        IProductDesktopSystemSurfaceNativeSampler sampler,
        TimeSpan? sampleDueTime = null,
        TimeSpan? samplePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        if (!sampler.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "System surface observation requires Windows.");
        }

        this.sampler = sampler;
        this.sampleDueTime = sampleDueTime ?? TimeSpan.Zero;
        this.samplePeriod = samplePeriod ?? SampleInterval;
    }

    public event EventHandler<ProductDesktopInteractionSystemSurfaceEvent>?
        SurfaceChanged;

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                return;
            }

            try
            {
                SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
                timer = new(Sample, null, sampleDueTime, samplePeriod);
                started = true;
            }
            catch
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                timer?.Dispose();
                timer = null;
                throw;
            }
        }
    }

    public void ReportFocusLost() => Classify(classifier.ObserveFocusLost);

    internal void SampleForEvidence() => Sample(state: null);

    internal void ReportSessionSwitchForEvidence(SessionSwitchReason reason) =>
        ObserveSessionSwitch(reason);

    internal void ReportPowerModeForEvidence(PowerModes mode) =>
        ObservePowerMode(mode);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (started)
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            }

            timer?.Dispose();
            timer = null;
        }
    }

    private void Sample(object? state)
    {
        if (Interlocked.Exchange(ref sampleInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            ProductDesktopSystemSurfaceSample sample = sampler.Read();
            Classify(() => classifier.Observe(sample));
        }
        catch (Exception exception) when (
            exception is not StackOverflowException)
        {
            Classify(() => classifier.ObserveSessionAvailability(
                available: false,
                remoteTransition: false));
        }
        finally
        {
            Volatile.Write(ref sampleInProgress, 0);
        }
    }

    private void SystemEvents_SessionSwitch(
        object sender,
        SessionSwitchEventArgs args) => ObserveSessionSwitch(args.Reason);

    private void ObserveSessionSwitch(SessionSwitchReason reason)
    {
        bool unavailable = reason is SessionSwitchReason.SessionLock
            or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect;
        bool available = reason is SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.SessionLogon
            or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect;
        if (reason == SessionSwitchReason.SessionRemoteControl)
        {
            Classify(() => classifier.ObserveSessionAvailability(
                available: true,
                remoteTransition: true));
            return;
        }

        if (!unavailable && !available)
        {
            return;
        }

        bool remote = reason is SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.RemoteDisconnect
            or SessionSwitchReason.SessionRemoteControl;
        Classify(() =>
            classifier.ObserveSessionAvailability(!unavailable, remote));
    }

    private void SystemEvents_PowerModeChanged(
        object sender,
        PowerModeChangedEventArgs args) => ObservePowerMode(args.Mode);

    private void ObservePowerMode(PowerModes mode)
    {
        if (mode == PowerModes.Suspend)
        {
            Classify(() =>
                classifier.ObservePowerAvailability(available: false));
        }
        else if (mode == PowerModes.Resume)
        {
            Classify(() =>
                classifier.ObservePowerAvailability(available: true));
        }
    }

    private void Classify(
        Func<IReadOnlyList<
            ProductDesktopInteractionSystemSurfaceEventKind>> operation)
    {
        IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> events;
        lock (gate)
        {
            if (disposed || !started)
            {
                return;
            }

            events = operation();
        }

        Publish(events);
    }

    private void Publish(
        IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> events)
    {
        foreach (ProductDesktopInteractionSystemSurfaceEventKind kind in events)
        {
            EventHandler<ProductDesktopInteractionSystemSurfaceEvent>? handler;
            ProductDesktopInteractionSystemSurfaceEvent value;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                handler = SurfaceChanged;
                value = new(
                    kind,
                    checked(++sequence),
                    DateTimeOffset.UtcNow);
            }

            try
            {
                handler?.Invoke(this, value);
            }
            catch (Exception exception) when (
                exception is not StackOverflowException)
            {
                // Native system callbacks must not tear down the process. The
                // next finite sample remains fail-closed and can recover.
            }
        }
    }
}

internal interface IProductDesktopSystemSurfaceNativeSampler
{
    bool IsSupported { get; }

    ProductDesktopSystemSurfaceSample Read();
}

internal sealed class WindowsProductDesktopSystemSurfaceNativeSampler
    : IProductDesktopSystemSurfaceNativeSampler
{
    private const int SmRemoteSession = 0x1000;

    public bool IsSupported => OperatingSystem.IsWindows();

    public ProductDesktopSystemSurfaceSample Read()
    {
        nint shell = NativeMethods.GetShellWindow();
        nint foreground = NativeMethods.GetForegroundWindow();
        int result = NativeMethods.SHQueryUserNotificationState(out
            QueryUserNotificationState notificationState);
        bool known = result >= 0;
        bool fullScreen = notificationState is
            QueryUserNotificationState.RunningDirect3dFullScreen
            or QueryUserNotificationState.PresentationMode;
        return new(
            shell,
            foreground,
            known,
            fullScreen,
            NativeMethods.GetSystemMetrics(SmRemoteSession) != 0);
    }

    private enum QueryUserNotificationState
    {
        NotPresent,
        Busy,
        RunningDirect3dFullScreen,
        PresentationMode,
        AcceptsNotifications,
        QuietTime,
        App,
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetShellWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("shell32.dll")]
        internal static extern int SHQueryUserNotificationState(
            out QueryUserNotificationState state);
    }
}
