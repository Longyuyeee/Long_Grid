using System.Text.Json;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceState
{
    public required string ProfileId { get; init; }

    public required IReadOnlyList<ProductContainerState> Containers { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductContainerState
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsLocked { get; init; }

    public required ProductContainerAppearanceState Appearance { get; init; }

    public required ProductContainerPlacementState Placement { get; init; }

    public required IReadOnlyList<ProductItemReferenceState> Items { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductContainerAppearanceState
{
    public required string Color { get; init; }

    public double Opacity { get; init; }

    public bool Collapsed { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductContainerPlacementState
{
    public required string DisplayKey { get; init; }

    public double XDip { get; init; }

    public double YDip { get; init; }

    public double WidthDip { get; init; }

    public double HeightDip { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductItemReferenceState
{
    public required string Id { get; init; }

    public required DesktopCatalogEntry CatalogEntry { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}
