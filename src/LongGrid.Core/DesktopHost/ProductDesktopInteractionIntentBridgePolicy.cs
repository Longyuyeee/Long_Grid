namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionIntentBridgeFeatureStatus
{
    DisabledByInteractionPolicy,
    DisabledByIntentBridgePolicy,
    DisabledByManualSessionPolicy,
    EnabledForControlledManualSession,
    EnabledForProduct,
}

public sealed record ProductDesktopInteractionIntentBridgeFeatureDecision(
    ProductDesktopInteractionIntentBridgeFeatureStatus Status)
{
    public bool IsEnabled =>
        Status is ProductDesktopInteractionIntentBridgeFeatureStatus
            .EnabledForControlledManualSession
            or ProductDesktopInteractionIntentBridgeFeatureStatus.EnabledForProduct;
}

public static class ProductDesktopInteractionIntentBridgePolicy
{
    public static ProductDesktopInteractionIntentBridgeFeatureDecision EvaluateForProduct(
        ProductDesktopInteractionFeatureDecision interaction,
        string? bridgeValue,
        string? manualSessionValue)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        return interaction.Status == ProductDesktopInteractionFeatureStatus.EnabledForProduct
            && bridgeValue is null && manualSessionValue is null
            ? new(ProductDesktopInteractionIntentBridgeFeatureStatus.EnabledForProduct)
            : Evaluate(interaction, bridgeValue, manualSessionValue);
    }

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
