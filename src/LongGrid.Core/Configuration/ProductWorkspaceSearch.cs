using System.Text;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceSearchStatus
{
    EmptyQuery,
    Applied,
    NoResults,
    StaleAuthority,
    Invalid,
}

public enum ProductWorkspaceSearchTargetFilter
{
    Invalid,
    All,
    Containers,
    Items,
}

public enum ProductWorkspaceSearchItemKindFilter
{
    Invalid,
    All,
    File,
    Folder,
    Shortcut,
    Url,
}

public enum ProductWorkspaceSearchMatchKind
{
    Container,
    Item,
}

public sealed record ProductWorkspaceSearchItemInput(
    int Ordinal,
    string? DisplayName,
    ConfigurationItemKind Kind,
    ProductItemReferenceResolution Resolution,
    ProductWorkspaceReadItemSource Source);

public sealed record ProductWorkspaceSearchContainerInput(
    int Ordinal,
    string DisplayName,
    ProductWorkspaceContainerHealth Health,
    string DisplayKey,
    IReadOnlyList<ProductWorkspaceSearchItemInput> Items);

public sealed record ProductWorkspaceSearchRequest(
    string Query,
    long ExpectedRevision,
    ProductWorkspaceSearchTargetFilter TargetFilter,
    ProductWorkspaceSearchItemKindFilter ItemKindFilter,
    ProductWorkspaceContainerHealthFilter HealthFilter,
    string? DisplayKey = null);

public sealed record ProductWorkspaceSearchMatch(
    ProductWorkspaceSearchMatchKind MatchKind,
    int ContainerOrdinal,
    int? ItemOrdinal,
    string DisplayName,
    string ContainerDisplayName,
    ProductWorkspaceContainerHealth ContainerHealth,
    string DisplayKey,
    ConfigurationItemKind? ItemKind = null,
    ProductItemReferenceResolution? Resolution = null,
    ProductWorkspaceReadItemSource? Source = null);

public sealed record ProductWorkspaceSearchResult(
    ProductWorkspaceSearchStatus Status,
    long Revision,
    IReadOnlyList<ProductWorkspaceSearchMatch> Matches,
    int ScannedItemCount,
    bool WasTruncated)
{
    public bool IsSupported => Status is not ProductWorkspaceSearchStatus.Invalid;
}

public static class ProductWorkspaceSearch
{
    public const int MaximumQueryLength = 64;
    public const int MaximumScannedItems = ProductConfigurationLimits.MaximumItems;
    public const int MaximumResults =
        ProductConfigurationLimits.MaximumContainers +
        ProductConfigurationLimits.MaximumItems;

    public static ProductWorkspaceSearchResult Resolve(
        long currentRevision,
        ProductWorkspaceSearchRequest request,
        IReadOnlyList<ProductWorkspaceSearchContainerInput> containers)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(request.Query);
        if (currentRevision < 0
            || request.ExpectedRevision < 0
            || !IsSupported(request.TargetFilter)
            || !IsSupported(request.ItemKindFilter)
            || !ProductWorkspaceContainerHealthFilterPolicy.IsSupported(
                request.HealthFilter)
            || containers.Count > ProductConfigurationLimits.MaximumContainers
            || containers.Any(IsInvalid)
            || !TryNormalizeQuery(request.Query, out string? query)
            || !TryNormalizeDisplayKey(request.DisplayKey, out string? displayKey))
        {
            return Invalid(currentRevision);
        }

        if (request.ExpectedRevision != currentRevision)
        {
            return new(
                ProductWorkspaceSearchStatus.StaleAuthority,
                currentRevision,
                Array.Empty<ProductWorkspaceSearchMatch>(),
                0,
                false);
        }

        if (query!.Length == 0)
        {
            return new(
                ProductWorkspaceSearchStatus.EmptyQuery,
                currentRevision,
                Array.Empty<ProductWorkspaceSearchMatch>(),
                0,
                false);
        }

        var matches = new List<ProductWorkspaceSearchMatch>();
        int scannedItems = 0;
        bool truncated = false;
        foreach (ProductWorkspaceSearchContainerInput container in containers)
        {
            if (!ProductWorkspaceContainerHealthFilterPolicy.Includes(
                    request.HealthFilter,
                    container.Health)
                || (displayKey is not null
                    && !string.Equals(
                        displayKey,
                        container.DisplayKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (request.TargetFilter is ProductWorkspaceSearchTargetFilter.All
                    or ProductWorkspaceSearchTargetFilter.Containers
                && Contains(container.DisplayName, query))
            {
                matches.Add(new(
                    ProductWorkspaceSearchMatchKind.Container,
                    container.Ordinal,
                    null,
                    container.DisplayName,
                    container.DisplayName,
                    container.Health,
                    container.DisplayKey));
            }

            if (request.TargetFilter == ProductWorkspaceSearchTargetFilter.Containers)
            {
                continue;
            }

            foreach (ProductWorkspaceSearchItemInput item in container.Items)
            {
                if (scannedItems >= MaximumScannedItems)
                {
                    truncated = true;
                    break;
                }

                scannedItems++;
                if (!Includes(request.ItemKindFilter, item.Kind)
                    || (!Contains(item.DisplayName, query)
                        && !Contains(DescribeKind(item.Kind), query)))
                {
                    continue;
                }

                matches.Add(new(
                    ProductWorkspaceSearchMatchKind.Item,
                    container.Ordinal,
                    item.Ordinal,
                    item.DisplayName ?? $"引用 {item.Ordinal}",
                    container.DisplayName,
                    container.Health,
                    container.DisplayKey,
                    item.Kind,
                    item.Resolution,
                    item.Source));
                if (matches.Count >= MaximumResults)
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        return new(
            matches.Count == 0
                ? ProductWorkspaceSearchStatus.NoResults
                : ProductWorkspaceSearchStatus.Applied,
            currentRevision,
            matches.ToArray(),
            scannedItems,
            truncated);
    }

    private static bool IsInvalid(ProductWorkspaceSearchContainerInput? container) =>
        container is null
        || container.Ordinal <= 0
        || string.IsNullOrWhiteSpace(container.DisplayName)
        || string.IsNullOrWhiteSpace(container.DisplayKey)
        || !Enum.IsDefined(container.Health)
        || container.Items is null
        || container.Items.Any(item => item is null
            || item.Ordinal <= 0
            || !Enum.IsDefined(item.Kind)
            || !Enum.IsDefined(item.Resolution)
            || !Enum.IsDefined(item.Source)
            || item.DisplayName is not null && item.DisplayName.Any(char.IsControl));

    private static bool TryNormalizeQuery(string value, out string? normalized)
    {
        normalized = value.Trim().Normalize(NormalizationForm.FormC);
        return normalized.Length <= MaximumQueryLength && !value.Any(char.IsControl);
    }

    private static bool TryNormalizeDisplayKey(
        string? value,
        out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null
            || normalized.Length <= ProductConfigurationLimits.MaximumDisplayKeyLength
                && !value!.Any(char.IsControl);
    }

    private static bool Contains(string? value, string query) =>
        value is not null
        && value.Normalize(NormalizationForm.FormC)
            .Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string DescribeKind(ConfigurationItemKind kind) => kind switch
    {
        ConfigurationItemKind.File => "文件 file",
        ConfigurationItemKind.Folder => "文件夹 folder directory",
        ConfigurationItemKind.Shortcut => "快捷方式 shortcut link",
        ConfigurationItemKind.Url => "网址 URL internet shortcut",
        _ => string.Empty,
    };

    private static bool IsSupported(ProductWorkspaceSearchTargetFilter filter) =>
        filter is ProductWorkspaceSearchTargetFilter.All
            or ProductWorkspaceSearchTargetFilter.Containers
            or ProductWorkspaceSearchTargetFilter.Items;

    private static bool IsSupported(ProductWorkspaceSearchItemKindFilter filter) =>
        filter is ProductWorkspaceSearchItemKindFilter.All
            or ProductWorkspaceSearchItemKindFilter.File
            or ProductWorkspaceSearchItemKindFilter.Folder
            or ProductWorkspaceSearchItemKindFilter.Shortcut
            or ProductWorkspaceSearchItemKindFilter.Url;

    private static bool Includes(
        ProductWorkspaceSearchItemKindFilter filter,
        ConfigurationItemKind kind) =>
        filter switch
        {
            ProductWorkspaceSearchItemKindFilter.All => true,
            ProductWorkspaceSearchItemKindFilter.File =>
                kind == ConfigurationItemKind.File,
            ProductWorkspaceSearchItemKindFilter.Folder =>
                kind == ConfigurationItemKind.Folder,
            ProductWorkspaceSearchItemKindFilter.Shortcut =>
                kind == ConfigurationItemKind.Shortcut,
            ProductWorkspaceSearchItemKindFilter.Url =>
                kind == ConfigurationItemKind.Url,
            _ => false,
        };

    private static ProductWorkspaceSearchResult Invalid(long revision) =>
        new(
            ProductWorkspaceSearchStatus.Invalid,
            revision,
            Array.Empty<ProductWorkspaceSearchMatch>(),
            0,
            false);
}
