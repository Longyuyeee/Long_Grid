using System.Text.Json.Serialization;

namespace LongGrid.Core.Configuration;

public sealed record ProductBoxesSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("boxesEnabled")]
    public bool BoxesEnabled { get; init; } = true;

    public static ProductBoxesSettings Default { get; } = new();

    public static ProductBoxesSettings SafeDisabled { get; } =
        new() { BoxesEnabled = false };

    public bool IsValid => SchemaVersion == CurrentSchemaVersion;
}
