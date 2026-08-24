namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopFirstHostReadiness
{
    AwaitingHost,
    AwaitingWorkspace,
    SuspendedSystemSurface,
    Ready,
    DisabledByUser,
    DisabledBySafetyPolicy,
    SuspendedUnsafeTopology,
    Faulted,
}

public enum ProductDesktopFirstStartupAction
{
    KeepControlCenterHidden,
    ActivateControlCenter,
}

public enum ProductDesktopFirstStartupReason
{
    DesktopReady,
    EmptyWorkspaceReady,
    SystemSurfaceSuspended,
    EvidenceSession,
    RedirectedActivation,
    BoxesDisabled,
    ConfigurationRequiresAttention,
    DesktopHostUnavailable,
}

public sealed record ProductDesktopFirstStartupRequest(
    bool EvidenceSession,
    bool RedirectedActivationPending,
    bool BoxesEnabled,
    bool ConfigurationRequiresAttention,
    ProductDesktopFirstHostReadiness HostReadiness);

public sealed record ProductDesktopFirstStartupDecision(
    ProductDesktopFirstStartupAction Action,
    ProductDesktopFirstStartupReason Reason)
{
    public bool ActivateControlCenter =>
        Action == ProductDesktopFirstStartupAction.ActivateControlCenter;
}

public static class ProductDesktopFirstStartupPolicy
{
    public static ProductDesktopFirstStartupDecision Evaluate(
        ProductDesktopFirstStartupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EvidenceSession)
        {
            return Activate(ProductDesktopFirstStartupReason.EvidenceSession);
        }
        if (request.RedirectedActivationPending)
        {
            return Activate(ProductDesktopFirstStartupReason.RedirectedActivation);
        }
        if (!request.BoxesEnabled)
        {
            return Activate(ProductDesktopFirstStartupReason.BoxesDisabled);
        }
        if (request.ConfigurationRequiresAttention)
        {
            return Activate(
                ProductDesktopFirstStartupReason.ConfigurationRequiresAttention);
        }

        return request.HostReadiness switch
        {
            ProductDesktopFirstHostReadiness.Ready =>
                KeepHidden(ProductDesktopFirstStartupReason.DesktopReady),
            ProductDesktopFirstHostReadiness.AwaitingWorkspace =>
                KeepHidden(ProductDesktopFirstStartupReason.EmptyWorkspaceReady),
            ProductDesktopFirstHostReadiness.SuspendedSystemSurface =>
                KeepHidden(ProductDesktopFirstStartupReason.SystemSurfaceSuspended),
            _ => Activate(ProductDesktopFirstStartupReason.DesktopHostUnavailable),
        };
    }

    private static ProductDesktopFirstStartupDecision Activate(
        ProductDesktopFirstStartupReason reason) =>
        new(ProductDesktopFirstStartupAction.ActivateControlCenter, reason);

    private static ProductDesktopFirstStartupDecision KeepHidden(
        ProductDesktopFirstStartupReason reason) =>
        new(ProductDesktopFirstStartupAction.KeepControlCenterHidden, reason);
}
