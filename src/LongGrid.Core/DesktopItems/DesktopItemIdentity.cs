namespace LongGrid.Core.DesktopItems;

public sealed record DesktopItemIdentity(
    string Provider,
    string CanonicalTarget,
    string? VolumeId = null,
    string? FileId = null,
    string? ParsingName = null);
