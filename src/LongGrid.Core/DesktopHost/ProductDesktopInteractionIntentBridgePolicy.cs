namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionIntentBridgeFeatureStatus
{
    DisabledByInteractionPolicy,
    DisabledByIntentBridgePolicy,
    DisabledByManualSessionPolicy,
    EnabledForControlledManualSession,
}

public sealed record ProductDesktopInteractionIntentBridgeFeatureDecision(
    ProductDesktopInteractionIntentBridgeFeatureStatus Status)
{
    public bool IsEnabled =>
        Status == ProductDesktopInteractionIntentBridgeFeatureStatus
            .EnabledForControlledManualSession;
}

public static class ProductDesktopInteractionIntentBridgePolicy
{
    public const string EnvironmentVariableName =
        "LONGGRID_ENABLE_DESKTOP_INTENT_BRIDGE";
    public const string ManualSessionEnvironmentVariableName =
        "LONGGRID_ACKNOWLEDGE_DESKTOP_INTENT_SESSION";

    public static ProductDesktopInteractionIntentBridgeFeatureDecision Evaluate(
        ProductDesktopInteractionFeatureDecision interaction,
        string? bridgeValue,
        string? manualSessionValue)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        return new(
            !interaction.IsEnabled
                ? ProductDesktopInteractionIntentBridgeFeatureStatus
                    .DisabledByInteractionPolicy
                : !string.Equals(bridgeValue, "1", StringComparison.Ordinal)
                    ? ProductDesktopInteractionIntentBridgeFeatureStatus
                        .DisabledByIntentBridgePolicy
                    : !string.Equals(
                        manualSessionValue,
                        "1",
                        StringComparison.Ordinal)
                        ? ProductDesktopInteractionIntentBridgeFeatureStatus
                            .DisabledByManualSessionPolicy
                        : ProductDesktopInteractionIntentBridgeFeatureStatus
                            .EnabledForControlledManualSession);
    }
}
