using System.Diagnostics;
using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSearchTests
{
    [Fact]
    public void UnicodeNamesAreNormalizedBeforeMatching()
    {
        ProductWorkspaceSearchResult result = Resolve(
            "Café",
            [Container("Cafe\u0301 项目", "display-primary")]);

        ProductWorkspaceSearchMatch match = Assert.Single(result.Matches);
        Assert.Equal(ProductWorkspaceSearchStatus.Applied, result.Status);
        Assert.Equal(ProductWorkspaceSearchMatchKind.Container, match.MatchKind);
        Assert.Equal("Cafe\u0301 项目", match.DisplayName);
    }

    [Fact]
    public void TargetKindHealthAndDisplayFiltersComposeDeterministically()
    {
        ProductWorkspaceSearchContainerInput[] containers =
        [
            Container(
                "主工作区",
                "display-primary",
                ProductWorkspaceContainerHealth.Ready,
                new ProductWorkspaceSearchItemInput(1, "会议记录.txt", ConfigurationItemKind.File,
                    ProductItemReferenceResolution.Resolved,
                    ProductWorkspaceReadItemSource.Reference),
                new ProductWorkspaceSearchItemInput(2, "会议网址", ConfigurationItemKind.Url,
                    ProductItemReferenceResolution.Resolved,
                    ProductWorkspaceReadItemSource.Reference)),
            Container(
                "副工作区",
                "display-secondary",
                ProductWorkspaceContainerHealth.NeedsReview,
                new ProductWorkspaceSearchItemInput(1, "会议记录.txt", ConfigurationItemKind.File,
                    ProductItemReferenceResolution.Missing,
                    ProductWorkspaceReadItemSource.Reference)),
        ];
        var request = new ProductWorkspaceSearchRequest(
            "会议",
            12,
            ProductWorkspaceSearchTargetFilter.Items,
            ProductWorkspaceSearchItemKindFilter.File,
            ProductWorkspaceContainerHealthFilter.Ready,
            "display-primary");

        ProductWorkspaceSearchResult result = ProductWorkspaceSearch.Resolve(
            12,
            request,
            containers);

        ProductWorkspaceSearchMatch match = Assert.Single(result.Matches);
        Assert.Equal(1, match.ContainerOrdinal);
        Assert.Equal(ConfigurationItemKind.File, match.ItemKind);
        Assert.Equal("display-primary", match.DisplayKey);
    }

    [Fact]
    public void MissingItemCanBeFoundByApprovedTypeWithoutExposingTarget()
    {
        ProductWorkspaceSearchResult result = Resolve(
            "文件",
            [Container(
                "待检查",
                "display-primary",
                ProductWorkspaceContainerHealth.NeedsReview,
                new ProductWorkspaceSearchItemInput(1, null, ConfigurationItemKind.File,
                    ProductItemReferenceResolution.Missing,
                    ProductWorkspaceReadItemSource.Reference))],
            ProductWorkspaceSearchTargetFilter.Items);

        ProductWorkspaceSearchMatch match = Assert.Single(result.Matches);
        Assert.Equal("引用 1", match.DisplayName);
        Assert.Equal(ProductItemReferenceResolution.Missing, match.Resolution);
    }

    [Fact]
    public void EmptyNoResultStaleAndInvalidStatesAreExplicit()
    {
        ProductWorkspaceSearchContainerInput[] containers =
            [Container("工作区", "display-primary")];

        Assert.Equal(
            ProductWorkspaceSearchStatus.EmptyQuery,
            Resolve("  ", containers).Status);
        Assert.Equal(
            ProductWorkspaceSearchStatus.NoResults,
            Resolve("不存在", containers).Status);
        Assert.Equal(
            ProductWorkspaceSearchStatus.StaleAuthority,
            ProductWorkspaceSearch.Resolve(
                13,
                Request("工作区", expectedRevision: 12),
                containers).Status);
        Assert.Equal(
            ProductWorkspaceSearchStatus.Invalid,
            Resolve("bad\nquery", containers).Status);
    }

    [Fact]
    public void FiveHundredItemSearchMeetsBudgetAndTruncatesAdditionalInput()
    {
        ProductWorkspaceSearchItemInput[] items = Enumerable.Range(1, 501)
            .Select(index => new ProductWorkspaceSearchItemInput(
                index,
                $"项目-{index:D3}.txt",
                ConfigurationItemKind.File,
                ProductItemReferenceResolution.Resolved,
                ProductWorkspaceReadItemSource.Reference))
            .ToArray();
        ProductWorkspaceSearchContainerInput[] containers =
            [Container("规模工作区", "display-primary", items: items)];
        ProductWorkspaceSearchRequest request = Request("项目");
        _ = ProductWorkspaceSearch.Resolve(12, request, containers);

        var stopwatch = Stopwatch.StartNew();
        ProductWorkspaceSearchResult result = ProductWorkspaceSearch.Resolve(
            12,
            request,
            containers);
        stopwatch.Stop();

        Assert.Equal(ProductWorkspaceSearchStatus.Applied, result.Status);
        Assert.Equal(500, result.Matches.Count);
        Assert.Equal(500, result.ScannedItemCount);
        Assert.True(result.WasTruncated);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(100),
            $"500-item search took {stopwatch.Elapsed.TotalMilliseconds:F2} ms.");
    }

    private static ProductWorkspaceSearchResult Resolve(
        string query,
        IReadOnlyList<ProductWorkspaceSearchContainerInput> containers,
        ProductWorkspaceSearchTargetFilter target =
            ProductWorkspaceSearchTargetFilter.All) =>
        ProductWorkspaceSearch.Resolve(12, Request(query, target: target), containers);

    private static ProductWorkspaceSearchRequest Request(
        string query,
        long expectedRevision = 12,
        ProductWorkspaceSearchTargetFilter target =
            ProductWorkspaceSearchTargetFilter.All) =>
        new(
            query,
            expectedRevision,
            target,
            ProductWorkspaceSearchItemKindFilter.All,
            ProductWorkspaceContainerHealthFilter.All);

    private static ProductWorkspaceSearchContainerInput Container(
        string name,
        string displayKey,
        ProductWorkspaceContainerHealth health = ProductWorkspaceContainerHealth.Ready,
        params ProductWorkspaceSearchItemInput[] items) =>
        new(1, name, health, displayKey, items);
}
