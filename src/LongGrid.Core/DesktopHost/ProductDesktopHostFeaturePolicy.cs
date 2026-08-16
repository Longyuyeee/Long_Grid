namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopHostFeatureStatus
{
    DisabledBySafetyPolicy,
    DisabledByEmergencyPolicy,
    EnabledForProduct,
}

public sealed record ProductDesktopHostFeatureDecision(
    ProductDesktopHostFeatureStatus Status)
{
    public bool IsEnabled =>
        Status == ProductDesktopHostFeatureStatus.EnabledForProduct;
}

public static class ProductDesktopHostFeaturePolicy
{
    public const string EnvironmentVariableName =
        "LONGGRID_ENABLE_DESKTOP_HOST";
    public const string EmergencyDisableEnvironmentVariableName =
        "LONGGRID_DISABLE_DESKTOP_HOST";

    public static ProductDesktopHostFeatureDecision Evaluate(
        string? legacyEnableValue,
        string? emergencyDisableValue = null) =>
        new(
            string.Equals(emergencyDisableValue, "1", StringComparison.Ordinal)
                ? ProductDesktopHostFeatureStatus.DisabledByEmergencyPolicy
                : string.Equals(legacyEnableValue, "0", StringComparison.Ordinal)
                    ? ProductDesktopHostFeatureStatus.DisabledBySafetyPolicy
                    : ProductDesktopHostFeatureStatus.EnabledForProduct);
}
