using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public sealed record ProductConfigurationDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("profileId")]
    public required string ProfileId { get; init; }

    [JsonPropertyName("containers")]
    public required IReadOnlyList<ContainerConfiguration> Containers { get; init; }

    [JsonPropertyName("savedDisplayTopology")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SavedDisplayConfiguration>? SavedDisplayTopology { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record SavedDisplayConfiguration
{
    [JsonPropertyName("stableId")]
    public required string StableId { get; init; }

    [JsonPropertyName("bounds")]
    public required PixelRectConfiguration Bounds { get; init; }

    [JsonPropertyName("workArea")]
    public required PixelRectConfiguration WorkArea { get; init; }

    [JsonPropertyName("effectiveDpi")]
    public uint EffectiveDpi { get; init; }

    [JsonPropertyName("rotation")]
    public DisplayRotation Rotation { get; init; }

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record PixelRectConfiguration
{
    [JsonPropertyName("left")]
    public int Left { get; init; }

    [JsonPropertyName("top")]
    public int Top { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ContainerConfiguration
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; init; }

    [JsonPropertyName("appearance")]
    public required ContainerAppearanceConfiguration Appearance { get; init; }

    [JsonPropertyName("placement")]
    public required ContainerPlacementConfiguration Placement { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<DesktopItemReferenceConfiguration> Items { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ContainerAppearanceConfiguration
{
    [JsonPropertyName("color")]
    public required string Color { get; init; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; }

    [JsonPropertyName("collapsed")]
    public bool Collapsed { get; init; }

    [JsonPropertyName("titleVisibility")]
    public ProductContainerTitleVisibilityPolicy TitleVisibility { get; init; } =
        ProductContainerTitleVisibilityPolicy.Always;

    [JsonPropertyName("titleDoubleClickAction")]
    public ProductContainerTitleDoubleClickAction TitleDoubleClickAction
    { get; init; } = ProductContainerTitleDoubleClickAction.ToggleCollapsed;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ContainerPlacementConfiguration
{
    [JsonPropertyName("displayKey")]
    public required string DisplayKey { get; init; }

    [JsonPropertyName("xDip")]
    public double XDip { get; init; }

    [JsonPropertyName("yDip")]
    public double YDip { get; init; }

    [JsonPropertyName("widthDip")]
    public double WidthDip { get; init; }

    [JsonPropertyName("heightDip")]
    public double HeightDip { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DesktopItemReferenceConfiguration
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public ConfigurationItemKind Kind { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("behavior")]
    public ConfigurationItemBehavior Behavior { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public enum ConfigurationItemKind
{
    File,
    Folder,
    Shortcut,
    Url,
}

public enum ConfigurationItemBehavior
{
    Reference,
}
