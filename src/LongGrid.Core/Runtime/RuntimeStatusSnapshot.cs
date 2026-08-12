namespace LongGrid.Core.Runtime;

public sealed record RuntimeStatusSnapshot
{
    private RuntimeStatusSnapshot(
        RuntimeMode mode,
        RuntimeCapabilityState desktopCatalog,
        RuntimeCapabilityState fileOperations,
        RuntimeCapabilityState desktopHost)
    {
        Mode = mode;
        DesktopCatalog = desktopCatalog;
        FileOperations = fileOperations;
        DesktopHost = desktopHost;
    }

    public RuntimeMode Mode { get; }

    public bool IsDevelopmentReadOnly => Mode == RuntimeMode.DevelopmentReadOnly;

    public RuntimeCapabilityState DesktopCatalog { get; }

    public RuntimeCapabilityState FileOperations { get; }

    public RuntimeCapabilityState DesktopHost { get; }

    public bool HasExternalConnection =>
        DesktopCatalog == RuntimeCapabilityState.ConnectedReadOnly ||
        DesktopHost == RuntimeCapabilityState.ConnectedReadOnly;

    public bool AllowsFileOperations =>
        FileOperations != RuntimeCapabilityState.DisabledBySafetyPolicy;

    public static RuntimeStatusSnapshot CreateDevelopmentReadOnly(
        bool desktopCatalogConnected = false,
        bool desktopHostFeatureEnabled = false,
        bool desktopHostConnected = false) =>
        new(
            RuntimeMode.DevelopmentReadOnly,
            desktopCatalogConnected
                ? RuntimeCapabilityState.ConnectedReadOnly
                : RuntimeCapabilityState.Disconnected,
            RuntimeCapabilityState.DisabledBySafetyPolicy,
            desktopHostFeatureEnabled
                ? desktopHostConnected
                    ? RuntimeCapabilityState.ConnectedReadOnly
                    : RuntimeCapabilityState.Disconnected
                : RuntimeCapabilityState.DisabledBySafetyPolicy);
}
