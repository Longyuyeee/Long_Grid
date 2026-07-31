using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongGrid.Spikes.ConfigurationPersistence;

public sealed class ProbeConfigurationDocument
{
    public int SchemaVersion { get; init; }

    public required string ProfileId { get; init; }

    public required IReadOnlyList<ProbeContainer> Containers { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed class ProbeContainer
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal static class ProbeConfigurationValidation
{
    public static ConfigurationValidationFailure Validate(ProbeConfigurationDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            return ConfigurationValidationFailure.UnsupportedSchema;
        }

        if (string.IsNullOrWhiteSpace(document.ProfileId) || document.Containers is null)
        {
            return ConfigurationValidationFailure.InvalidDocument;
        }

        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (ProbeContainer container in document.Containers)
        {
            if (string.IsNullOrWhiteSpace(container.Id)
                || string.IsNullOrWhiteSpace(container.Name)
                || !ids.Add(container.Id))
            {
                return ConfigurationValidationFailure.InvalidDocument;
            }
        }

        return ConfigurationValidationFailure.None;
    }
}
