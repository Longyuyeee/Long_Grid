namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionInputForwardingFeatureStatus
{
    DisabledByIntentBridgePolicy,
    DisabledByInputForwardingPolicy,
    DisabledByManualSessionPolicy,
    EnabledForControlledManualSession,
    EnabledForProduct,
}

public sealed record ProductDesktopInteractionInputForwardingFeatureDecision(
    ProductDesktopInteractionInputForwardingFeatureStatus Status)
{
    public bool IsEnabled =>
        Status is ProductDesktopInteractionInputForwardingFeatureStatus
            .EnabledForControlledManualSession
            or ProductDesktopInteractionInputForwardingFeatureStatus.EnabledForProduct;
}

public static class ProductDesktopInteractionInputForwardingPolicy
{
    public static ProductDesktopInteractionInputForwardingFeatureDecision EvaluateForProduct(
        ProductDesktopInteractionIntentBridgeFeatureDecision intentBridge,
        string? forwardingValue,
        string? manualSessionValue)
    {
        ArgumentNullException.ThrowIfNull(intentBridge);
        return intentBridge.Status == ProductDesktopInteractionIntentBridgeFeatureStatus.EnabledForProduct
            && forwardingValue is null && manualSessionValue is null
            ? new(ProductDesktopInteractionInputForwardingFeatureStatus.EnabledForProduct)
            : Evaluate(intentBridge, forwardingValue, manualSessionValue);
    }

    public const string EnvironmentVariableName =
        "LONGGRID_ENABLE_DESKTOP_INPUT_FORWARDING";
    public const string ManualSessionEnvironmentVariableName =
        "LONGGRID_ACKNOWLEDGE_DESKTOP_INPUT_FORWARDING_SESSION";

    public static ProductDesktopInteractionInputForwardingFeatureDecision
        Evaluate(
            ProductDesktopInteractionIntentBridgeFeatureDecision intentBridge,
            string? forwardingValue,
            string? manualSessionValue)
    {
        ArgumentNullException.ThrowIfNull(intentBridge);
        return new(
            !intentBridge.IsEnabled
                ? ProductDesktopInteractionInputForwardingFeatureStatus
                    .DisabledByIntentBridgePolicy
                : !string.Equals(
                    forwardingValue,
                    "1",
                    StringComparison.Ordinal)
                    ? ProductDesktopInteractionInputForwardingFeatureStatus
                        .DisabledByInputForwardingPolicy
                    : !string.Equals(
                        manualSessionValue,
                        "1",
                        StringComparison.Ordinal)
                        ? ProductDesktopInteractionInputForwardingFeatureStatus
                            .DisabledByManualSessionPolicy
                        : ProductDesktopInteractionInputForwardingFeatureStatus
                            .EnabledForControlledManualSession);
    }
}
