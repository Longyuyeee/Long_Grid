using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

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
}
