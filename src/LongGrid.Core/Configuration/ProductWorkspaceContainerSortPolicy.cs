namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerSort
{
    Invalid,
    ConfigurationOrder,
    NameAscending,
    NameDescending,
    NeedsReviewFirst,
}

public sealed record ProductWorkspaceContainerSortInput(
    string DisplayName,
    ProductWorkspaceContainerHealth Health);

public sealed record ProductWorkspaceContainerSortResult(
    ProductWorkspaceContainerSort Sort,
    IReadOnlyList<int> OrderedIndexes)
{
    public bool IsSupported => Sort != ProductWorkspaceContainerSort.Invalid;
}

public static class ProductWorkspaceContainerSortPolicy
{
    public static ProductWorkspaceContainerSortResult Resolve(
        ProductWorkspaceContainerSort sort,
        IReadOnlyList<ProductWorkspaceContainerSortInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (!IsSupported(sort) || inputs.Any(IsInvalid))
        {
            return new(
                ProductWorkspaceContainerSort.Invalid,
                Array.Empty<int>());
        }

        IEnumerable<(ProductWorkspaceContainerSortInput Input, int Index)> indexed =
            inputs.Select((input, index) => (Input: input, Index: index));
        IEnumerable<int> ordered = sort switch
        {
            ProductWorkspaceContainerSort.ConfigurationOrder =>
                indexed.Select(entry => entry.Index),
            ProductWorkspaceContainerSort.NameAscending =>
                indexed
                    .OrderBy(entry => entry.Input.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Index),
            ProductWorkspaceContainerSort.NameDescending =>
                indexed
                    .OrderByDescending(
                        entry => entry.Input.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Index),
            ProductWorkspaceContainerSort.NeedsReviewFirst =>
                indexed
                    .OrderBy(entry => entry.Input.Health ==
                        ProductWorkspaceContainerHealth.NeedsReview ? 0 : 1)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Index),
            _ => Array.Empty<int>(),
        };

        return new(sort, ordered.ToArray());
    }

    public static bool IsSupported(ProductWorkspaceContainerSort sort) =>
        sort is ProductWorkspaceContainerSort.ConfigurationOrder
            or ProductWorkspaceContainerSort.NameAscending
            or ProductWorkspaceContainerSort.NameDescending
            or ProductWorkspaceContainerSort.NeedsReviewFirst;

    private static bool IsInvalid(ProductWorkspaceContainerSortInput? input) =>
        input is null
        || input.DisplayName is null
        || !Enum.IsDefined(input.Health);
}
