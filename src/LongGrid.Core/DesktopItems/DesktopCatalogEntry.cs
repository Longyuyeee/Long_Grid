namespace LongGrid.Core.DesktopItems;

public sealed record DesktopCatalogEntry(
    DesktopItemIdentity Identity,
    string SourceId,
    string DisplayName,
    DesktopItemKind Kind);
