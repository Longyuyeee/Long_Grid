using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class ProductDesktopItemVisualPresentationTests(
    ITestOutputHelper output)
{
    [Theory]
    [InlineData(ConfigurationItemKind.File, ProductItemReferenceResolution.Resolved,
        ProductDesktopItemTypeIconKind.File, ProductDesktopItemVisualStatus.ReadyTypeIcon)]
    [InlineData(ConfigurationItemKind.Folder, ProductItemReferenceResolution.Missing,
        ProductDesktopItemTypeIconKind.Folder, ProductDesktopItemVisualStatus.Offline)]
    [InlineData(ConfigurationItemKind.Shortcut, ProductItemReferenceResolution.TypeChanged,
        ProductDesktopItemTypeIconKind.Shortcut, ProductDesktopItemVisualStatus.TargetChanged)]
    [InlineData(ConfigurationItemKind.Url, ProductItemReferenceResolution.Ambiguous,
        ProductDesktopItemTypeIconKind.Url, ProductDesktopItemVisualStatus.Ambiguous)]
    [InlineData(ConfigurationItemKind.Url, ProductItemReferenceResolution.UnsupportedTarget,
        ProductDesktopItemTypeIconKind.Url, ProductDesktopItemVisualStatus.Unsupported)]
    public void ResolutionMapsToFinitePrivacySafeVisualState(
        ConfigurationItemKind kind,
        ProductItemReferenceResolution resolution,
        ProductDesktopItemTypeIconKind expectedType,
        ProductDesktopItemVisualStatus expectedStatus)
    {
        ProductDesktopItemVisualPresentation actual =
            ProductDesktopItemVisualPresentation.Create(kind, resolution);

        Assert.Equal(expectedType, actual.TypeIcon);
        Assert.Equal(expectedStatus, actual.Status);
        Assert.DoesNotContain(Path.GetTempPath(),
            actual.AccessibilityName("可见名称"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiveHundredItemsProjectOnlyBoundedFirstSurfaceVisuals()
    {
        string[] names = Enumerable.Range(1, 500)
            .Select(index => $"项目 {index}")
            .ToArray();
        ProductDesktopItemVisualPresentation[] visuals = names.Select((_, index) =>
            ProductDesktopItemVisualPresentation.Create(
                index % 2 == 0
                    ? ConfigurationItemKind.File
                    : ConfigurationItemKind.Folder,
                ProductItemReferenceResolution.Resolved)).ToArray();

        ProductDesktopHostReadOnlyProjection projection =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-500", "五百项目", names, "#2457D6", 0.82,
                false, 24, 36, 360, 720,
                totalItemCount: 500,
                itemVisuals: visuals);

        Assert.Equal(
            ProductDesktopHostReadOnlyProjection.MaximumVisibleItems,
            projection.ItemNames.Count);
        Assert.Equal(projection.ItemNames.Count, projection.ItemVisuals.Count);
        Assert.Equal(500, projection.TotalItemCount);
    }

    [Fact]
    public void RealWindowsShellProvidesEveryFiniteTypeAndFallbackIcon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopItemVisualPresentation[] visuals =
        [
            new(ProductDesktopItemTypeIconKind.File,
                ProductDesktopItemVisualStatus.ReadyTypeIcon),
            new(ProductDesktopItemTypeIconKind.Folder,
                ProductDesktopItemVisualStatus.ReadyTypeIcon),
            new(ProductDesktopItemTypeIconKind.Shortcut,
                ProductDesktopItemVisualStatus.ReadyTypeIcon),
            new(ProductDesktopItemTypeIconKind.Url,
                ProductDesktopItemVisualStatus.ReadyTypeIcon),
            new(ProductDesktopItemTypeIconKind.Url,
                ProductDesktopItemVisualStatus.Offline),
        ];
        ProductDesktopHostReadOnlyProjection container =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-icons", "真实系统图标",
                ["文件", "文件夹", "快捷方式", "网址", "离线网址"],
                "#2457D6", 0.82, false, 24, 36, 420, 320,
                itemVisuals: visuals);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(5005));

        bool[] actual = Enumerable.Range(0, visuals.Length)
            .Select(index => surface.IsSystemTypeIconAvailableForEvidence(
                "container-icons", index))
            .ToArray();

        Assert.All(actual, Assert.True);
        Assert.Equal(
            ProductDesktopItemVisualStatus.Offline,
            surface.GetItemVisualForEvidence("container-icons", 4)!.Status);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf005aRealWindowsShellTypeIconEvidence",
            Expected = new
            {
                File = true,
                Folder = true,
                Shortcut = true,
                Url = true,
                OfflineFallback = true,
            },
            Actual = new
            {
                File = actual[0],
                Folder = actual[1],
                Shortcut = actual[2],
                Url = actual[3],
                OfflineFallback = actual[4],
            },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    [Theory]
    [InlineData(96u, 20)]
    [InlineData(192u, 40)]
    [InlineData(384u, 80)]
    public void NativeSurfaceScalesTypeIconFromOneToFourHundredPercent(
        uint dpi,
        int expectedPixels)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ProductDesktopHostReadOnlyProjection container =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-dpi", "DPI", ["文件"], "#2457D6", 0.82,
                false, 0, 0, 200, 150);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1920, 1080), dpi, [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(5100 + expectedPixels));

        Assert.Equal(expectedPixels, surface.GetSystemTypeIconSizeForEvidence());
        Assert.True(surface.IsSystemTypeIconAvailableForEvidence(
            "container-dpi", 0));
    }
}
