namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopContainerHeaderCommandKind
{
    ToggleCollapsed,
    ToggleLocked,
}

public sealed record ProductDesktopContainerHeaderCommandRequest(
    ProductDesktopContainerHeaderCommandKind Kind,
    string ContainerId,
    string DisplayId,
    long ExpectedWorkspaceRevision,
    long ExpectedTopologyGeneration,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);
