namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceFolderContentStatus
{
    AwaitingRefresh,
    Ready,
    Empty,
    Truncated,
    ReadyWithSkippedEntries,
    BindingUnavailable,
    AccessDenied,
    InvalidTarget,
    EnumerationFailed,
}

public sealed record ProductWorkspaceFolderContentItem(
    string ItemId,
    string DisplayName,
    ConfigurationItemKind Kind,
    string Target);

public sealed record ProductWorkspaceContainerFolderContent(
    string ContainerId,
    long Generation,
    ProductWorkspaceFolderContentStatus Status,
    IReadOnlyList<ProductWorkspaceFolderContentItem> Items,
    int SkippedReparsePointCount = 0)
{
    public const int MaximumItems = 256;

    public bool HasValidShape =>
        !string.IsNullOrWhiteSpace(ContainerId)
        && Generation > 0
        && Items is not null
        && Items.Count <= MaximumItems
        && Items.All(item => item is not null
            && !string.IsNullOrWhiteSpace(item.ItemId)
            && item.ItemId.StartsWith(
                $"folder:{Generation}:",
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.DisplayName)
            && item.DisplayName.Length <= 512
            && !item.DisplayName.Any(char.IsControl)
            && Enum.IsDefined(item.Kind)
            && !string.IsNullOrWhiteSpace(item.Target)
            && Path.IsPathFullyQualified(item.Target))
        && Items.Select(item => item.ItemId)
            .Distinct(StringComparer.Ordinal).Count() == Items.Count;

    public bool HasUsableProjection => HasValidShape && Status is
        ProductWorkspaceFolderContentStatus.Ready
            or ProductWorkspaceFolderContentStatus.Empty
            or ProductWorkspaceFolderContentStatus.Truncated
            or ProductWorkspaceFolderContentStatus.ReadyWithSkippedEntries;
}

public sealed record ProductWorkspaceFolderContentSet(
    long Generation,
    IReadOnlyDictionary<string, ProductWorkspaceContainerFolderContent> Containers)
{
    public static ProductWorkspaceFolderContentSet Empty { get; } = new(
        0,
        new Dictionary<string, ProductWorkspaceContainerFolderContent>(
            StringComparer.Ordinal));

    public ProductWorkspaceContainerFolderContent? Find(string containerId) =>
        Containers.TryGetValue(containerId, out var content) ? content : null;
}

public enum ProductWorkspaceReadItemSource
{
    Reference,
    BoundFolder,
}
