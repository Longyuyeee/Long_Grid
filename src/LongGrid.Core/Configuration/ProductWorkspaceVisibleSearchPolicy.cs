namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceVisibleSearchStatus
{
    Empty,
    Applied,
    Invalid,
}

public sealed record ProductWorkspaceVisibleSearchInput(
    string ContainerDisplayName,
    string HealthLabel,
    IReadOnlyList<string> VisibleItemDisplayNames);

public sealed record ProductWorkspaceVisibleSearchResult(
    ProductWorkspaceVisibleSearchStatus Status,
    IReadOnlyList<int> MatchingIndexes)
{
    public bool IsSupported => Status != ProductWorkspaceVisibleSearchStatus.Invalid;
}

public static class ProductWorkspaceVisibleSearchPolicy
{
    public const int MaximumQueryLength = 64;

    public static ProductWorkspaceVisibleSearchResult Resolve(
        string query,
        IReadOnlyList<ProductWorkspaceVisibleSearchInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(inputs);

        string normalized = query.Trim();
        if (normalized.Length > MaximumQueryLength
            || query.Any(char.IsControl)
            || inputs.Any(IsInvalid))
        {
            return new(
                ProductWorkspaceVisibleSearchStatus.Invalid,
                Array.Empty<int>());
        }

        if (normalized.Length == 0)
        {
            return new(
                ProductWorkspaceVisibleSearchStatus.Empty,
                Enumerable.Range(0, inputs.Count).ToArray());
        }

        List<int> matchingIndexes = [];
        for (int index = 0; index < inputs.Count; index++)
        {
            ProductWorkspaceVisibleSearchInput input = inputs[index];
            if (Contains(input.ContainerDisplayName, normalized)
                || Contains(input.HealthLabel, normalized)
                || input.VisibleItemDisplayNames.Any(item => Contains(item, normalized)))
            {
                matchingIndexes.Add(index);
            }
        }

        return new(ProductWorkspaceVisibleSearchStatus.Applied, matchingIndexes);
    }

    private static bool IsInvalid(ProductWorkspaceVisibleSearchInput? input) =>
        input is null
        || input.ContainerDisplayName is null
        || input.HealthLabel is null
        || input.VisibleItemDisplayNames is null
        || input.VisibleItemDisplayNames.Any(item => item is null);

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
