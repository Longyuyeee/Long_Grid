namespace LongGrid.Core.Configuration;

public static class ProductConfigurationDefaults
{
    public static ProductConfigurationDocument CreateEmpty() =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "default",
            Containers = Array.Empty<ContainerConfiguration>(),
        };
}
