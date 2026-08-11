using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceVisibleSearchPolicyTests
{
    [Theory]
    [InlineData("项目", 0)]
    [InlineData("引用正常", 0)]
    [InlineData("REPORT.DOCX", 0)]
    [InlineData("待审查", 1)]
    [InlineData("引用 2", 1)]
    public void VisibleLabelsMatchOrdinalIgnoreCase(string query, int expectedIndex)
    {
        ProductWorkspaceVisibleSearchResult result =
            ProductWorkspaceVisibleSearchPolicy.Resolve(query, Inputs());

        Assert.True(result.IsSupported);
        Assert.Equal(ProductWorkspaceVisibleSearchStatus.Applied, result.Status);
        Assert.Equal([expectedIndex], result.MatchingIndexes);
    }

    [Fact]
    public void EmptyOrWhitespaceQueryReturnsAllIndexes()
    {
        ProductWorkspaceVisibleSearchResult empty =
            ProductWorkspaceVisibleSearchPolicy.Resolve(string.Empty, Inputs());
        ProductWorkspaceVisibleSearchResult whitespace =
            ProductWorkspaceVisibleSearchPolicy.Resolve("   ", Inputs());

        Assert.Equal(ProductWorkspaceVisibleSearchStatus.Empty, empty.Status);
        Assert.Equal([0, 1], empty.MatchingIndexes);
        Assert.Equal(ProductWorkspaceVisibleSearchStatus.Empty, whitespace.Status);
        Assert.Equal([0, 1], whitespace.MatchingIndexes);
    }

    [Fact]
    public void NoMatchReturnsAppliedEmptyResult()
    {
        ProductWorkspaceVisibleSearchResult result =
            ProductWorkspaceVisibleSearchPolicy.Resolve("不存在", Inputs());

        Assert.Equal(ProductWorkspaceVisibleSearchStatus.Applied, result.Status);
        Assert.Empty(result.MatchingIndexes);
    }

    [Fact]
    public void OverlongOrControlCharacterQueryFailsClosed()
    {
        Assert.Equal(
            ProductWorkspaceVisibleSearchStatus.Invalid,
            ProductWorkspaceVisibleSearchPolicy.Resolve(
                new string('a', ProductWorkspaceVisibleSearchPolicy.MaximumQueryLength + 1),
                Inputs()).Status);
        Assert.Equal(
            ProductWorkspaceVisibleSearchStatus.Invalid,
            ProductWorkspaceVisibleSearchPolicy.Resolve("项目\n", Inputs()).Status);
    }

    [Fact]
    public void InvalidInputFailsClosed()
    {
        ProductWorkspaceVisibleSearchInput invalid = new(
            null!,
            "引用正常",
            Array.Empty<string>());

        ProductWorkspaceVisibleSearchResult result =
            ProductWorkspaceVisibleSearchPolicy.Resolve("项目", [invalid]);

        Assert.False(result.IsSupported);
        Assert.Empty(result.MatchingIndexes);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceVisibleSearchPolicy.Resolve(null!, Inputs()));
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceVisibleSearchPolicy.Resolve(
                string.Empty,
                null!));
    }

    private static ProductWorkspaceVisibleSearchInput[] Inputs() =>
    [
        new("项目资料", "引用正常", ["Report.docx"]),
        new("待办", "有引用待审查", ["引用 2"]),
    ];
}
