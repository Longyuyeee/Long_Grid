namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopHostFeatureStatus
{
    DisabledBySafetyPolicy,
    EnabledForDevelopment,
}

public sealed record ProductDesktopHostFeatureDecision(
    ProductDesktopHostFeatureStatus Status)
{
    public bool IsEnabled =>
        Status == ProductDesktopHostFeatureStatus.EnabledForDevelopment;
}

public static class ProductDesktopHostFeaturePolicy
{
    public const string EnvironmentVariableName =
        "LONGGRID_ENABLE_DESKTOP_HOST";

    public static ProductDesktopHostFeatureDecision Evaluate(string? value) =>
        new(
            string.Equals(value, "1", StringComparison.Ordinal)
                ? ProductDesktopHostFeatureStatus.EnabledForDevelopment
                : ProductDesktopHostFeatureStatus.DisabledBySafetyPolicy);
}
