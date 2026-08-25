using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using Microsoft.Win32;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopSystemSurfaceEventClassifierTests
{
    private static readonly ProductDesktopSystemSurfaceSample SafeSample = new(
        ShellWindow: new nint(10),
        ForegroundWindow: new nint(20),
        FullScreenStateKnown: true,
        FullScreenActive: false,
        RemoteSession: false);

    [Fact]
    public void DesktopRevealRequiresTwoStableSamplesBeforeRecovery()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();
        Assert.Empty(classifier.Observe(SafeSample));

        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind
                .DesktopRevealRequested],
            classifier.Observe(SafeSample with
            {
                ForegroundWindow = SafeSample.ShellWindow,
            }));
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate],
            classifier.Observe(SafeSample));
        Assert.Empty(classifier.Observe(SafeSample));
    }

    [Fact]
    public void UnknownFullScreenStateFailsClosed()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();

        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind
                .FullScreenTransition],
            classifier.Observe(SafeSample with
            {
                FullScreenStateKnown = false,
            }));
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate],
            classifier.Observe(SafeSample));
    }

    [Fact]
    public void ShellAndRemoteChangesAreFiniteAuditableSignals()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();
        Assert.Empty(classifier.Observe(SafeSample));

        IReadOnlyList<ProductDesktopInteractionSystemSurfaceEventKind> events =
            classifier.Observe(SafeSample with
            {
                ShellWindow = new nint(30),
                RemoteSession = true,
            });

        Assert.Equal(
            [
                ProductDesktopInteractionSystemSurfaceEventKind
                    .ExplorerRestarted,
                ProductDesktopInteractionSystemSurfaceEventKind
                    .RemoteSessionTransition,
            ],
            events);
    }

    [Fact]
    public void SessionAndPowerRemainHiddenUntilAvailabilityAndStableSamples()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.SessionUnavailable],
            classifier.ObserveSessionAvailability(
                available: false,
                remoteTransition: false));
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Empty(classifier.ObserveSessionAvailability(
            available: true,
            remoteTransition: false));
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate],
            classifier.Observe(SafeSample));

        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.SessionUnavailable],
            classifier.ObservePowerAvailability(available: false));
        Assert.Empty(classifier.ObservePowerAvailability(available: true));
    }

    [Fact]
    public void FocusLossAlwaysProducesFailClosedSignal()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();

        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.FocusLost],
            classifier.ObserveFocusLost());
    }

    [Fact]
    public void InitialUnavailableShellAndCombinedSessionSignalsFailClosed()
    {
        var classifier = new ProductDesktopSystemSurfaceEventClassifier();

        Assert.Equal(
            [
                ProductDesktopInteractionSystemSurfaceEventKind.ExplorerRestarted,
                ProductDesktopInteractionSystemSurfaceEventKind.FullScreenTransition,
            ],
            classifier.Observe(new(
                ShellWindow: nint.Zero,
                ForegroundWindow: nint.Zero,
                FullScreenStateKnown: false,
                FullScreenActive: false,
                RemoteSession: true)));
        Assert.Equal(
            [
                ProductDesktopInteractionSystemSurfaceEventKind.RemoteSessionTransition,
                ProductDesktopInteractionSystemSurfaceEventKind.SessionUnavailable,
            ],
            classifier.ObserveSessionAvailability(
                available: false,
                remoteTransition: true));
        Assert.Equal(
            [
                ProductDesktopInteractionSystemSurfaceEventKind.ExplorerRestarted,
                ProductDesktopInteractionSystemSurfaceEventKind.RemoteSessionTransition,
            ],
            classifier.Observe(SafeSample));
        Assert.Empty(classifier.ObserveSessionAvailability(
            available: true,
            remoteTransition: false));
        Assert.Empty(classifier.Observe(SafeSample));
        Assert.Equal(
            [ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate],
            classifier.Observe(SafeSample));
    }

    [Theory]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind.FocusLost,
        ProductDesktopInteractionCancellationSignal.FocusLost)]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind
            .DesktopRevealRequested,
        ProductDesktopInteractionCancellationSignal.DesktopRevealRequested)]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind
            .FullScreenTransition,
        ProductDesktopInteractionCancellationSignal.FullScreenTransition)]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind
            .SessionUnavailable,
        ProductDesktopInteractionCancellationSignal.SessionLockedOrDisconnected)]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind
            .RemoteSessionTransition,
        ProductDesktopInteractionCancellationSignal.RemoteSessionTransition)]
    [InlineData(ProductDesktopInteractionSystemSurfaceEventKind
            .ExplorerRestarted,
        ProductDesktopInteractionCancellationSignal.ExplorerRestarted)]
    public void FiniteEventsMapToExistingCancellationContract(
        ProductDesktopInteractionSystemSurfaceEventKind kind,
        ProductDesktopInteractionCancellationSignal signal)
    {
        var systemEvent = new ProductDesktopInteractionSystemSurfaceEvent(
            kind,
            1,
            DateTimeOffset.UtcNow);

        Assert.True(systemEvent.IsValid);
        Assert.Equal(signal, systemEvent.ToCancellationSignal());
    }

    [Fact]
    public void RecoveryCannotBeMisusedAsCancellationSignal()
    {
        var systemEvent = new ProductDesktopInteractionSystemSurfaceEvent(
            ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate,
            1,
            DateTimeOffset.UtcNow);

        Assert.False(systemEvent.RequiresHiddenSurface);
        Assert.Throws<InvalidOperationException>(
            () => { _ = systemEvent.ToCancellationSignal(); });
    }

    [Fact]
    public void EventSourcePublishesDeterministicSamplesAndFiniteRecovery()
    {
        var sampler = new QueueSystemSurfaceSampler(
            SafeSample,
            SafeSample with { ForegroundWindow = SafeSample.ShellWindow },
            SafeSample,
            SafeSample);
        using var source = new WindowsProductDesktopInteractionSystemSurfaceEventSource(
            sampler,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        var actual = new List<ProductDesktopInteractionSystemSurfaceEvent>();
        source.SurfaceChanged += (_, value) => actual.Add(value);
        source.Start();

        source.SampleForEvidence();
        source.SampleForEvidence();
        source.SampleForEvidence();
        source.SampleForEvidence();

        Assert.Equal(
            [
                ProductDesktopInteractionSystemSurfaceEventKind.DesktopRevealRequested,
                ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate,
            ],
            actual.Select(value => value.Kind));
        Assert.Equal([1L, 2L], actual.Select(value => value.Sequence));
        Assert.All(actual, value => Assert.True(value.ObservedAtUtc <= DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EventSourceMapsSessionPowerAndFocusWithoutPublishingBeforeStart()
    {
        using var source = new WindowsProductDesktopInteractionSystemSurfaceEventSource(
            new QueueSystemSurfaceSampler(SafeSample),
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        var actual = new List<ProductDesktopInteractionSystemSurfaceEventKind>();
        source.SurfaceChanged += (_, value) => actual.Add(value.Kind);

        source.ReportFocusLost();
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionLock);
        source.ReportPowerModeForEvidence(PowerModes.Suspend);
        Assert.Empty(actual);

        source.Start();
        source.Start();
        source.ReportFocusLost();
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionRemoteControl);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionLock);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.RemoteDisconnect);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionUnlock);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.RemoteConnect);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionLogon);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.ConsoleDisconnect);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.ConsoleConnect);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionLogoff);
        source.ReportSessionSwitchForEvidence(SessionSwitchReason.SessionRemoteControl);
        source.ReportPowerModeForEvidence(PowerModes.Suspend);
        source.ReportPowerModeForEvidence(PowerModes.Resume);
        source.ReportPowerModeForEvidence(PowerModes.StatusChange);

        Assert.Contains(ProductDesktopInteractionSystemSurfaceEventKind.FocusLost, actual);
        Assert.Contains(ProductDesktopInteractionSystemSurfaceEventKind.RemoteSessionTransition, actual);
        Assert.Contains(ProductDesktopInteractionSystemSurfaceEventKind.SessionUnavailable, actual);
    }

    [Fact]
    public void EventSourceConvertsSamplerFailureAndSubscriberFailureToFiniteSignal()
    {
        using var source = new WindowsProductDesktopInteractionSystemSurfaceEventSource(
            new ThrowingSystemSurfaceSampler(),
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        int delivered = 0;
        EventHandler<ProductDesktopInteractionSystemSurfaceEvent> throwing =
            (_, _) => throw new InvalidOperationException("subscriber");
        source.SurfaceChanged += throwing;
        source.Start();

        source.SampleForEvidence();
        source.SurfaceChanged -= throwing;
        source.SurfaceChanged += (_, _) => delivered++;
        source.ReportFocusLost();

        Assert.Equal(1, delivered);
        source.Dispose();
        source.Dispose();
        source.ReportFocusLost();
        Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Fact]
    public void EventSourceRejectsUnsupportedOrMissingSampler()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WindowsProductDesktopInteractionSystemSurfaceEventSource(null!));
        Assert.Throws<PlatformNotSupportedException>(() =>
            new WindowsProductDesktopInteractionSystemSurfaceEventSource(
                new UnsupportedSystemSurfaceSampler()));
    }

    [Fact]
    public void NativeSamplerReadsFiniteWindowsSystemState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sampler = new WindowsProductDesktopSystemSurfaceNativeSampler();
        ProductDesktopSystemSurfaceSample actual = sampler.Read();

        Assert.True(sampler.IsSupported);
        Assert.True(actual.ShellWindow == nint.Zero
            || actual.ShellWindow != nint.Zero);
        Assert.True(actual.ForegroundWindow == nint.Zero
            || actual.ForegroundWindow != nint.Zero);
    }

    private sealed class QueueSystemSurfaceSampler(
        params ProductDesktopSystemSurfaceSample[] samples)
        : IProductDesktopSystemSurfaceNativeSampler
    {
        private readonly Queue<ProductDesktopSystemSurfaceSample> samples = new(samples);

        public bool IsSupported => true;

        public ProductDesktopSystemSurfaceSample Read() => samples.Dequeue();
    }

    private sealed class ThrowingSystemSurfaceSampler
        : IProductDesktopSystemSurfaceNativeSampler
    {
        public bool IsSupported => true;

        public ProductDesktopSystemSurfaceSample Read() =>
            throw new InvalidOperationException("sample");
    }

    private sealed class UnsupportedSystemSurfaceSampler
        : IProductDesktopSystemSurfaceNativeSampler
    {
        public bool IsSupported => false;

        public ProductDesktopSystemSurfaceSample Read() => SafeSample;
    }
}
