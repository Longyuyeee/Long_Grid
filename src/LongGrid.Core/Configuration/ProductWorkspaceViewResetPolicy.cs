namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceViewResetStatus
{
    Invalid,
    Unavailable,
    Available,
}

public sealed record ProductWorkspaceViewResetDecision(
    ProductWorkspaceViewResetStatus Status)
{
    public bool CanReset => Status == ProductWorkspaceViewResetStatus.Available;
}

public static class ProductWorkspaceViewResetPolicy
{
    public static ProductWorkspaceViewResetDecision Evaluate(
        bool canFilter,
        int totalContainerCount,
        int visibleContainerCount,
        ProductWorkspaceContainerHealthFilter filter,
        bool hasSearchQuery,
        ProductWorkspaceContainerSort sort)
    {
        if (totalContainerCount < 0
            || visibleContainerCount < 0
            || visibleContainerCount > totalContainerCount)
        {
            return new(ProductWorkspaceViewResetStatus.Invalid);
        }

        bool hasNonDefaultCriteria =
            filter != ProductWorkspaceContainerHealthFilter.All
            || hasSearchQuery
            || sort != ProductWorkspaceContainerSort.ConfigurationOrder;
        bool available = canFilter
            && totalContainerCount > 0
            && visibleContainerCount == 0
            && hasNonDefaultCriteria;
        return new(available
            ? ProductWorkspaceViewResetStatus.Available
            : ProductWorkspaceViewResetStatus.Unavailable);
    }
}
