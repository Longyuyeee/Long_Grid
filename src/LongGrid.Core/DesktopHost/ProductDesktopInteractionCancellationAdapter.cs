namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionCancellationSignal
{
    EscapePressed,
    FocusLost,
    DesktopRevealRequested,
    FullScreenTransition,
    SessionLockedOrDisconnected,
    RemoteSessionTransition,
    ExplorerRestarted,
    ApplicationShutdown,
    EvidenceChanged,
    LeaseTimerElapsed,
}

public sealed class ProductDesktopInteractionCancellationAdapter
{
    private readonly ProductDesktopInteractionAdmissionController controller;

    public ProductDesktopInteractionCancellationAdapter(
        ProductDesktopInteractionAdmissionController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        this.controller = controller;
    }

    public ProductDesktopInteractionSnapshot Handle(
        ProductDesktopInteractionCancellationSignal signal,
        DateTimeOffset nowUtc,
        ProductDesktopInteractionEvidence? evidence = null)
    {
        if (signal is ProductDesktopInteractionCancellationSignal
                .EvidenceChanged
            or ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            return controller.Revalidate(evidence, nowUtc);
        }

        if (evidence is not null)
        {
            throw new ArgumentException(
                "Direct cancellation signals do not accept unrelated evidence.",
                nameof(evidence));
        }

        return controller.CancelForSystemTransition(Map(signal));
    }

    private static ProductDesktopInteractionCancellationReason Map(
        ProductDesktopInteractionCancellationSignal signal) =>
        signal switch
        {
            ProductDesktopInteractionCancellationSignal.EscapePressed =>
                ProductDesktopInteractionCancellationReason.EscapePressed,
            ProductDesktopInteractionCancellationSignal.FocusLost =>
                ProductDesktopInteractionCancellationReason.FocusLost,
            ProductDesktopInteractionCancellationSignal
                .DesktopRevealRequested =>
                ProductDesktopInteractionCancellationReason
                    .DesktopRevealRequested,
            ProductDesktopInteractionCancellationSignal.FullScreenTransition =>
                ProductDesktopInteractionCancellationReason
                    .FullScreenTransition,
            ProductDesktopInteractionCancellationSignal
                .SessionLockedOrDisconnected =>
                ProductDesktopInteractionCancellationReason
                    .SessionUnavailable,
            ProductDesktopInteractionCancellationSignal
                .RemoteSessionTransition =>
                ProductDesktopInteractionCancellationReason
                    .RemoteSessionTransition,
            ProductDesktopInteractionCancellationSignal.ExplorerRestarted =>
                ProductDesktopInteractionCancellationReason.ExplorerRestarted,
            ProductDesktopInteractionCancellationSignal.ApplicationShutdown =>
                ProductDesktopInteractionCancellationReason
                    .ApplicationShutdown,
            _ => throw new ArgumentOutOfRangeException(
                nameof(signal),
                "The cancellation signal is not supported."),
        };
}
