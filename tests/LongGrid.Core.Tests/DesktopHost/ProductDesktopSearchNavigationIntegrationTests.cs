using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopSearchNavigationIntegrationTests
{
    [Fact]
    public async Task RealUnicodeSearchTargetIsRevealedWithoutChangingFiles()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.DesktopSearch.Integration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        string[] paths = Enumerable.Range(1, 20)
            .Select(index => Path.Combine(
                sandbox,
                index == 20 ? "目标-项目.txt" : $"普通-{index:D2}.txt"))
            .ToArray();
        foreach (string path in paths)
        {
            await File.WriteAllTextAsync(path, $"content:{Path.GetFileName(path)}");
        }
        string[] before = await HashFiles(paths);

        try
        {
            ProductWorkspaceState state = CreateState(paths);
            ProductWorkspaceReadSnapshot snapshot =
                ProductWorkspaceReadModel.Create(state).Snapshot!;
            ProductWorkspaceSearchContainerInput[] searchInputs = snapshot.Containers
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
            ProductWorkspaceSearchResult search = ProductWorkspaceSearch.Resolve(
                31,
                new(
                    "目标",
                    31,
                    ProductWorkspaceSearchTargetFilter.Items,
                    ProductWorkspaceSearchItemKindFilter.All,
                    ProductWorkspaceContainerHealthFilter.All),
                searchInputs);
            ProductWorkspaceSearchMatch match = Assert.Single(search.Matches);
            ProductDisplayTopologySnapshot topology = CreateTopology();
            ProductDesktopSearchNavigationTarget target =
                ProductDesktopSearchNavigation.Resolve(
                    31,
                    topology,
                    state,
                    snapshot,
                    new(31, 9, match.ContainerOrdinal, match.ItemOrdinal));
            ProductDesktopHostProjectionBatch projection =
                ProductDesktopHostProjectionBuilder.Build(
                    state,
                    snapshot,
                    topology,
                    workspaceRevision: 31,
                    searchTarget: target)!;
            ProductDesktopHostReadOnlyProjection container =
                Assert.Single(projection.Displays[0].Containers);

            Assert.Equal(20, match.ItemOrdinal);
            Assert.Equal(ProductDesktopSearchNavigationStatus.Applied, target.Status);
            Assert.Equal(8, target.ViewportStart);
            Assert.False(container.IsCollapsed);
            Assert.Contains("item:20", container.ItemIds);
            Assert.True(container.SearchHighlighted);
            Assert.Equal("item:20", container.SearchHighlightedItemId);
            if (OperatingSystem.IsWindows())
            {
                using WindowsProductDesktopHostReadOnlySurface surface =
                    WindowsProductDesktopHostReadOnlySurface.Create(
                        projection.Displays[0],
                        new nint(0x509B));
                Assert.NotEqual(nint.Zero, surface.Handle);
                Assert.True(surface.PassiveWindowContractAttested);
                Assert.True(surface.ApplyPresentation(projection.Displays[0]));
            }
            Assert.Equal(before, await HashFiles(paths));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static ProductWorkspaceState CreateState(IReadOnlyList<string> paths) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-search",
                    Name = "搜索资料",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = true,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-primary",
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = paths.Select((path, index) =>
                        ProductItemReferenceState.CreateResolved(
                            $"item-{index + 1}",
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity("filesystem", path),
                                "user-desktop",
                                Path.GetFileName(path),
                                DesktopItemKind.File))).ToArray(),
                },
            ],
        };

    private static ProductDisplayTopologySnapshot CreateTopology() =>
        new(
            ProductDisplayTopologyStatus.Ready,
            9,
            [
                new DisplayTopologyNode(
                    "display-primary",
                    new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040),
                    96,
                    DisplayRotation.Landscape,
                    IsPrimary: true),
            ],
            1,
            1,
            1);

    private static async Task<string[]> HashFiles(IEnumerable<string> paths)
    {
        var hashes = new List<string>();
        foreach (string path in paths)
        {
            hashes.Add(Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
        return hashes.ToArray();
    }
}
