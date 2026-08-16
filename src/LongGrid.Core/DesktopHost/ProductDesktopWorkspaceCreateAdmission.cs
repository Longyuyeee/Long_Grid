using LongGrid.Core.Configuration;

namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopWorkspaceCreateInputKind
{
    PrimaryPointer,
    ContextMenu,
    KeyboardShortcut,
    AssistiveInvoke,
}

public sealed record ProductDesktopWorkspaceCreateRequest(
    ProductDesktopWorkspaceCreateInputKind Kind,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

public enum ProductDesktopWorkspaceCreateAdmissionStatus
{
    Ready,
    Invalid,
    UntrustedSource,
    Injected,
    AutoRepeat,
    StaleWorkspace,
    StaleTopology,
}

public sealed record ProductDesktopWorkspaceCreateAdmissionDecision(
    ProductDesktopWorkspaceCreateAdmissionStatus Status)
{
    public bool CanCreate =>
        Status == ProductDesktopWorkspaceCreateAdmissionStatus.Ready;
}

public static class ProductDesktopWorkspaceCreateAdmission
{
    public static ProductDesktopWorkspaceCreateAdmissionDecision Evaluate(
        ProductDesktopWorkspaceCreateRequest? request,
        long currentWorkspaceRevision,
        long currentTopologyGeneration)
    {
        if (request is null
            || !Enum.IsDefined(request.Kind)
            || string.IsNullOrWhiteSpace(request.DisplayId)
            || request.DisplayId.Length >
                ProductConfigurationLimits.MaximumDisplayKeyLength
            || request.WorkspaceRevision < 0
            || request.TopologyGeneration <= 0
            || currentWorkspaceRevision < 0
            || currentTopologyGeneration <= 0)
        {
            return new(ProductDesktopWorkspaceCreateAdmissionStatus.Invalid);
        }
        if (!request.SourceAttested)
        {
            return new(
                ProductDesktopWorkspaceCreateAdmissionStatus.UntrustedSource);
        }
        if (request.IsInjected)
        {
            return new(ProductDesktopWorkspaceCreateAdmissionStatus.Injected);
        }
        if (request.IsAutoRepeat)
        {
            return new(ProductDesktopWorkspaceCreateAdmissionStatus.AutoRepeat);
        }
        if (request.WorkspaceRevision != currentWorkspaceRevision)
        {
            return new(
                ProductDesktopWorkspaceCreateAdmissionStatus.StaleWorkspace);
        }
        if (request.TopologyGeneration != currentTopologyGeneration)
        {
            return new(
                ProductDesktopWorkspaceCreateAdmissionStatus.StaleTopology);
        }

        return new(ProductDesktopWorkspaceCreateAdmissionStatus.Ready);
    }
}
