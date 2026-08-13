namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionSystemSurfaceEventKind
{
    FocusLost,
    DesktopRevealRequested,
    FullScreenTransition,
    SessionUnavailable,
    RemoteSessionTransition,
    ExplorerRestarted,
    RecoveryCandidate,
}

public sealed record ProductDesktopInteractionSystemSurfaceEvent(
    ProductDesktopInteractionSystemSurfaceEventKind Kind,
    long Sequence,
    DateTimeOffset ObservedAtUtc)
{
    public bool RequiresHiddenSurface =>
        Kind != ProductDesktopInteractionSystemSurfaceEventKind.RecoveryCandidate;

    public ProductDesktopInteractionCancellationSignal ToCancellationSignal() =>
        Kind switch
        {
            ProductDesktopInteractionSystemSurfaceEventKind.FocusLost =>
                ProductDesktopInteractionCancellationSignal.FocusLost,
            ProductDesktopInteractionSystemSurfaceEventKind
                .DesktopRevealRequested =>
                ProductDesktopInteractionCancellationSignal
                    .DesktopRevealRequested,
            ProductDesktopInteractionSystemSurfaceEventKind
                .FullScreenTransition =>
                ProductDesktopInteractionCancellationSignal
                    .FullScreenTransition,
            ProductDesktopInteractionSystemSurfaceEventKind
                .SessionUnavailable =>
                ProductDesktopInteractionCancellationSignal
                    .SessionLockedOrDisconnected,
            ProductDesktopInteractionSystemSurfaceEventKind
                .RemoteSessionTransition =>
                ProductDesktopInteractionCancellationSignal
                    .RemoteSessionTransition,
            ProductDesktopInteractionSystemSurfaceEventKind
                .ExplorerRestarted =>
                ProductDesktopInteractionCancellationSignal.ExplorerRestarted,
            _ => throw new InvalidOperationException(
                "Recovery candidates are not cancellation signals."),
        };

    public bool IsValid =>
        Enum.IsDefined(Kind)
        && Sequence > 0
        && ObservedAtUtc != default;
}
