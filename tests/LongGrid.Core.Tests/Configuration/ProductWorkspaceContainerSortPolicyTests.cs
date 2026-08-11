using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerSortPolicyTests
{
    private static readonly ProductWorkspaceContainerSortInput[] Inputs =
    [
        new("Zulu", ProductWorkspaceContainerHealth.Ready),
        new("alpha", ProductWorkspaceContainerHealth.Empty),
        new("Beta", ProductWorkspaceContainerHealth.NeedsReview),
        new("ALPHA", ProductWorkspaceContainerHealth.NeedsReview),
    ];

    [Fact]
    public void ConfigurationOrderPreservesPresentationOrder()
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.ConfigurationOrder,
                Inputs);

        Assert.True(result.IsSupported);
        Assert.Equal([0, 1, 2, 3], result.OrderedIndexes);
    }

    [Fact]
    public void NameAscendingIsCaseInsensitiveAndStable()
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NameAscending,
                Inputs);

        Assert.Equal([1, 3, 2, 0], result.OrderedIndexes);
    }

    [Fact]
    public void NameDescendingIsCaseInsensitiveAndStable()
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NameDescending,
                Inputs);

        Assert.Equal([0, 2, 1, 3], result.OrderedIndexes);
    }

    [Fact]
    public void NeedsReviewFirstPreservesOrderWithinBothGroups()
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NeedsReviewFirst,
                Inputs);

        Assert.Equal([2, 3, 0, 1], result.OrderedIndexes);
    }

    [Theory]
    [InlineData(ProductWorkspaceContainerSort.Invalid)]
    [InlineData((ProductWorkspaceContainerSort)999)]
    public void UnsupportedSortFailsClosed(ProductWorkspaceContainerSort sort)
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(sort, Inputs);

        Assert.False(result.IsSupported);
        Assert.Empty(result.OrderedIndexes);
    }

    [Fact]
    public void InvalidInputFailsClosed()
    {
        ProductWorkspaceContainerSortInput[] inputs =
        [
            new(null!, ProductWorkspaceContainerHealth.Ready),
        ];

        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NameAscending,
                inputs);

        Assert.False(result.IsSupported);
        Assert.Empty(result.OrderedIndexes);
    }

    [Fact]
    public void UnknownHealthFailsClosed()
    {
        ProductWorkspaceContainerSortInput[] inputs =
        [
            new("Alpha", (ProductWorkspaceContainerHealth)999),
        ];

        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NeedsReviewFirst,
                inputs);

        Assert.False(result.IsSupported);
        Assert.Empty(result.OrderedIndexes);
    }

    [Fact]
    public void EmptyInputsRemainSupported()
    {
        ProductWorkspaceContainerSortResult result =
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.NameAscending,
                Array.Empty<ProductWorkspaceContainerSortInput>());

        Assert.True(result.IsSupported);
        Assert.Empty(result.OrderedIndexes);
    }

    [Fact]
    public void NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerSortPolicy.Resolve(
                ProductWorkspaceContainerSort.ConfigurationOrder,
                null!));
    }
}
