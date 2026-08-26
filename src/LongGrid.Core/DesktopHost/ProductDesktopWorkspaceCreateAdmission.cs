using LongGrid.Core.Configuration;

namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopWorkspaceCreateInputKind
{
    PrimaryPointer,
    PointerDrag,
    ContextMenu,
    KeyboardShortcut,
    AssistiveInvoke,
    SelectedReferences,
    ExplorerContextMenu,
}

public sealed record ProductDesktopWorkspaceCreateRequest(
    ProductDesktopWorkspaceCreateInputKind Kind,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat,
    PixelRect? RequestedBoundsPixels = null,
    ProductWorkspaceSelectedReferenceCreateSnapshot? SelectedReferences = null);

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
            || currentTopologyGeneration <= 0
            || (request.Kind == ProductDesktopWorkspaceCreateInputKind.PointerDrag
                ? request.RequestedBoundsPixels is not PixelRect requested
                    || !HasSafeArea(requested)
                : request.RequestedBoundsPixels is not null)
            || (request.Kind ==
                    ProductDesktopWorkspaceCreateInputKind.SelectedReferences
                ? !ProductWorkspaceSelectedReferenceCreateSnapshots.HasValidShape(
                    request.SelectedReferences)
                : request.SelectedReferences is not null))
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

    private static bool HasSafeArea(PixelRect bounds) =>
        bounds.HasArea
        && (long)bounds.Left + bounds.Width <= int.MaxValue
        && (long)bounds.Top + bounds.Height <= int.MaxValue;
}
