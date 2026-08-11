using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceViewResetPolicyTests
{
    [Fact]
    public void NonDefaultHealthFilterWithZeroResultsOffersReset()
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(
            filter: ProductWorkspaceContainerHealthFilter.Empty);

        Assert.True(decision.CanReset);
        Assert.Equal(ProductWorkspaceViewResetStatus.Available, decision.Status);
    }

    [Fact]
    public void SearchWithZeroResultsOffersReset()
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(hasSearchQuery: true);

        Assert.True(decision.CanReset);
    }

    [Fact]
    public void NonDefaultSortWithZeroResultsOffersReset()
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(
            sort: ProductWorkspaceContainerSort.NameAscending);

        Assert.True(decision.CanReset);
    }

    [Fact]
    public void UnknownCriteriaWithZeroResultsCanRecoverToDefaults()
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(
            filter: (ProductWorkspaceContainerHealthFilter)999,
            sort: (ProductWorkspaceContainerSort)999);

        Assert.True(decision.CanReset);
    }

    [Theory]
    [InlineData(false, 3, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 3, 1)]
    public void UnavailableContextsDoNotOfferReset(
        bool canFilter,
        int total,
        int visible)
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(
            canFilter: canFilter,
            total: total,
            visible: visible,
            hasSearchQuery: true);

        Assert.False(decision.CanReset);
        Assert.Equal(ProductWorkspaceViewResetStatus.Unavailable, decision.Status);
    }

    [Fact]
    public void DefaultViewWithZeroResultsDoesNotOfferReset()
    {
        ProductWorkspaceViewResetDecision decision = Evaluate();

        Assert.False(decision.CanReset);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void InvalidCountsFailClosed(int total, int visible)
    {
        ProductWorkspaceViewResetDecision decision = Evaluate(
            total: total,
            visible: visible,
            hasSearchQuery: true);

        Assert.False(decision.CanReset);
        Assert.Equal(ProductWorkspaceViewResetStatus.Invalid, decision.Status);
    }

    private static ProductWorkspaceViewResetDecision Evaluate(
        bool canFilter = true,
        int total = 3,
        int visible = 0,
        ProductWorkspaceContainerHealthFilter filter =
            ProductWorkspaceContainerHealthFilter.All,
        bool hasSearchQuery = false,
        ProductWorkspaceContainerSort sort =
            ProductWorkspaceContainerSort.ConfigurationOrder) =>
        ProductWorkspaceViewResetPolicy.Evaluate(
            canFilter,
            total,
            visible,
            filter,
            hasSearchQuery,
            sort);
}
