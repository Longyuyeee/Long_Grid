namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopContainerMenuAction
{
    OpenRename,
    OpenAppearance,
    OpenSort,
    DeleteContainerConfiguration,
}

public sealed record ProductDesktopContainerMenuRequest(
    ProductDesktopContainerMenuAction Action,
    string ContainerId,
    string DisplayId,
    long ExpectedWorkspaceRevision,
    long ExpectedTopologyGeneration,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

public sealed record ProductDesktopContainerMenuAvailability(
    bool CanOpenRename,
    bool CanOpenAppearance,
    bool CanOpenSort,
    bool CanDeleteContainerConfiguration)
{
    public static ProductDesktopContainerMenuAvailability Unavailable { get; } =
        new(false, false, false, false);
}
