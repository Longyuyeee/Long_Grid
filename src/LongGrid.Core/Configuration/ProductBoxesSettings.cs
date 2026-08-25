using System.Text.Json.Serialization;

namespace LongGrid.Core.Configuration;

public sealed record ProductBoxesSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("boxesEnabled")]
    public bool BoxesEnabled { get; init; } = true;

    [JsonPropertyName("thumbnailsEnabled")]
    public bool ThumbnailsEnabled { get; init; } = true;

    [JsonPropertyName("openItemsWithSingleClick")]
    public bool OpenItemsWithSingleClick { get; init; }

    public static ProductBoxesSettings Default { get; } = new();

    public static ProductBoxesSettings SafeDisabled { get; } =
        new()
        {
            BoxesEnabled = false,
            ThumbnailsEnabled = false,
            OpenItemsWithSingleClick = false,
        };

    public bool IsValid => SchemaVersion == CurrentSchemaVersion;
}
