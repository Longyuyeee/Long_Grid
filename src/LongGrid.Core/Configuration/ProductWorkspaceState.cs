using System.Text.Json;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceState
{
    public required string ProfileId { get; init; }

    public required IReadOnlyList<ProductContainerState> Containers { get; init; }

    public IReadOnlyList<SavedDisplayConfiguration>? SavedDisplayTopology { get; init; }

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

    public ProductContainerFolderBindingState? FolderBinding { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public enum ProductContainerFolderBindingResolution
{
    Resolved,
    Missing,
    AccessDenied,
    Replaced,
    InvalidTarget,
    Unavailable,
}

public sealed record ProductContainerFolderBindingState
{
    public required string PersistedTarget { get; init; }

    public required ulong VolumeSerialNumber { get; init; }

    public required string FileId { get; init; }

    public ProductContainerFolderBindingResolution Resolution { get; init; }

    public string? ResolvedTarget { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductContainerAppearanceState
{
    public required string Color { get; init; }

    public double Opacity { get; init; }

    public bool Collapsed { get; init; }

    public ProductContainerTitleVisibilityPolicy TitleVisibility { get; init; } =
        ProductContainerTitleVisibilityPolicy.Always;

    public ProductContainerTitleDoubleClickAction TitleDoubleClickAction
    { get; init; } = ProductContainerTitleDoubleClickAction.ToggleCollapsed;

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public enum ProductContainerTitleVisibilityPolicy
{
    Always,
    Hover,
    Hidden,
}

public enum ProductContainerTitleDoubleClickAction
{
    ToggleCollapsed,
    None,
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

public enum ProductItemReferenceResolution
{
    Resolved,
    Missing,
    TypeChanged,
    Ambiguous,
    UnsupportedTarget,
}

public sealed record ProductItemReferenceState
{
    private ProductItemReferenceState(
        string id,
        ConfigurationItemKind persistedKind,
        string persistedTarget,
        ProductItemReferenceResolution resolution,
        DesktopCatalogEntry? catalogEntry,
        IDictionary<string, JsonElement>? extensionData)
    {
        Id = id;
        PersistedKind = persistedKind;
        PersistedTarget = persistedTarget;
        Resolution = resolution;
        CatalogEntry = catalogEntry;
        ExtensionData = extensionData;
    }

    public string Id { get; }

    public ConfigurationItemKind PersistedKind { get; }

    public string PersistedTarget { get; }

    public ProductItemReferenceResolution Resolution { get; }

    public DesktopCatalogEntry? CatalogEntry { get; }

    public IDictionary<string, JsonElement>? ExtensionData { get; }

    public static ProductItemReferenceState CreateResolved(
        string id,
        DesktopCatalogEntry catalogEntry,
        IDictionary<string, JsonElement>? extensionData = null)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);
        return new(
            id,
            Enum.IsDefined(catalogEntry.Kind)
                ? ProductWorkspaceIdentityPolicy.MapKind(catalogEntry.Kind)
                : default,
            catalogEntry.Identity?.CanonicalTarget ?? string.Empty,
            ProductItemReferenceResolution.Resolved,
            catalogEntry,
            extensionData);
    }

    internal static ProductItemReferenceState RestoreUnresolved(
        string id,
        ConfigurationItemKind persistedKind,
        string persistedTarget,
        ProductItemReferenceResolution resolution,
        IDictionary<string, JsonElement>? extensionData)
    {
        if (resolution == ProductItemReferenceResolution.Resolved)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        return new(
            id,
            persistedKind,
            persistedTarget,
            resolution,
            catalogEntry: null,
            extensionData);
    }
}
