using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSearchIntegrationTests
{
    [Fact]
    public async Task RealUnicodeFileCanBeFoundAsAnItemByItsApprovedType()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.Search.Integration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        string path = Path.Combine(sandbox, "项目-报告.txt");
        await File.WriteAllTextAsync(path, "真实文件正文不得进入搜索索引");
        string before = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(path)));

        try
        {
            ProductWorkspaceReadSnapshot snapshot = ProductWorkspaceReadModel.Create(
                CreateState(path)).Snapshot!;
            ProductWorkspaceSearchContainerInput[] inputs = snapshot.Containers
                .Select(container => new ProductWorkspaceSearchContainerInput(
                    container.Ordinal,
                    container.UserVisibleName,
                    container.Health,
                    container.DisplayKey,
                    container.Items.Select(item =>
                        new ProductWorkspaceSearchItemInput(
                            item.Ordinal,
                            item.UserVisibleName,
                            item.Kind,
                            item.Resolution,
                            item.Source)).ToArray()))
                .ToArray();
            ProductWorkspaceSearchResult actual = ProductWorkspaceSearch.Resolve(
                7,
                new(
                    "文件",
                    7,
                    ProductWorkspaceSearchTargetFilter.Items,
                    ProductWorkspaceSearchItemKindFilter.All,
                    ProductWorkspaceContainerHealthFilter.All),
                inputs);
            ProductWorkspaceSearchResult contentProbe = ProductWorkspaceSearch.Resolve(
                7,
                new(
                    "真实文件正文不得进入搜索索引",
                    7,
                    ProductWorkspaceSearchTargetFilter.Items,
                    ProductWorkspaceSearchItemKindFilter.All,
                    ProductWorkspaceContainerHealthFilter.All),
                inputs);

            ProductWorkspaceSearchMatch match = Assert.Single(actual.Matches);
            Assert.Equal(ProductWorkspaceSearchStatus.Applied, actual.Status);
            Assert.Equal(ProductWorkspaceSearchMatchKind.Item, match.MatchKind);
            Assert.Equal("项目-报告.txt", match.DisplayName);
            Assert.Equal(ProductWorkspaceSearchStatus.NoResults, contentProbe.Status);
            Assert.Empty(contentProbe.Matches);
            Assert.Equal(
                before,
                Convert.ToHexString(
                    SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceState CreateState(string path) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "工作资料",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-primary",
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        ProductItemReferenceState.CreateResolved(
                            "item-1",
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity("filesystem", path),
                                "user-desktop",
                                Path.GetFileName(path),
                                DesktopItemKind.File)),
                    ],
                },
            ],
        };
}
