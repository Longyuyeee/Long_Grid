using System.Text.Json;
using System.Windows.Automation;
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

    [Fact]
    public void RealHwndAcceptsBoundedTopDownBgraThumbnailFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        byte[] pixels = Enumerable.Range(0, 8 * 8)
            .SelectMany(index => new byte[]
            {
                (byte)(index * 3),
                (byte)(255 - index * 2),
                (byte)(index * 4),
                255,
            })
            .ToArray();
        ProductDesktopThumbnailFrame frame =
            ProductDesktopThumbnailFrame.Create(8, 8, 32, pixels);
        ProductDesktopItemVisualPresentation visual = new(
            ProductDesktopItemTypeIconKind.File,
            ProductDesktopItemVisualStatus.ReadyThumbnail,
            frame);
        ProductDesktopHostReadOnlyProjection container =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-thumbnail", "真实缩略图", ["像素图"],
                "#2457D6", 0.82, false, 24, 36, 320, 240,
                itemVisuals: [visual]);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(5208));

        int actualScanLines = surface.DrawThumbnailFrameForEvidence(frame);

        Assert.Equal(frame.Height, actualScanLines);
        Assert.NotEqual(nint.Zero, surface.Handle);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf005b2RealHwndBgraEvidence",
            Expected = new { RealHwnd = true, BgraScanLines = 8 },
            Actual = new
            {
                RealHwnd = surface.Handle != nint.Zero,
                BgraScanLines = actualScanLines,
            },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    [Fact]
    public void RealHwndAppliesPresentationInPlaceAndBindsBoundedViewportRequest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        string[] names = Enumerable.Range(1, 24)
            .Select(index => $"项目 {index}")
            .ToArray();
        ProductDesktopHostReadOnlyProjection loading =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-scroll", "滚动方格", names, "#2457D6", 0.82,
                false, 24, 36, 320, 300, totalItemCount: 24,
                itemVisuals: names.Select(_ => new
                    ProductDesktopItemVisualPresentation(
                        ProductDesktopItemTypeIconKind.File,
                        ProductDesktopItemVisualStatus.LoadingThumbnail)));
        ProductDesktopHostDisplayProjection initial =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [loading]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                initial,
                new nint(5224));
        nint originalHandle = surface.Handle;
        ProductDesktopThumbnailFrame frame =
            ProductDesktopThumbnailFrame.Create(2, 2, 8, new byte[16]);
        ProductDesktopHostReadOnlyProjection ready =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-scroll", "滚动方格", names, "#2457D6", 0.82,
                false, 24, 36, 320, 300, totalItemCount: 24,
                itemVisuals: names.Select(_ => new
                    ProductDesktopItemVisualPresentation(
                        ProductDesktopItemTypeIconKind.File,
                        ProductDesktopItemVisualStatus.ReadyThumbnail,
                        frame)));
        ProductDesktopHostDisplayProjection resolved =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [ready]);
        ProductDesktopItemViewportSurfaceInput? captured = null;
        surface.BindItemViewport(input =>
        {
            captured = input;
            return true;
        });

        bool presentationApplied = surface.ApplyPresentation(resolved);
        bool viewportAccepted = surface.SubmitItemViewportWheelForEvidence(
            "container-scroll",
            wheelDelta: -120);

        Assert.True(presentationApplied);
        Assert.Equal(originalHandle, surface.Handle);
        Assert.Equal(
            ProductDesktopItemVisualStatus.ReadyThumbnail,
            surface.GetItemVisualForEvidence("container-scroll", 0)!.Status);
        Assert.True(viewportAccepted);
        Assert.Equal(-120, Assert.IsType<
            ProductDesktopItemViewportSurfaceInput>(captured).WheelDelta);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf005cRealHwndPresentationViewportEvidence",
            Expected = new
            {
                SameHwnd = true,
                TerminalVisual = "ReadyThumbnail",
                ViewportWheelDelta = -120,
            },
            Actual = new
            {
                SameHwnd = originalHandle == surface.Handle,
                TerminalVisual = surface.GetItemVisualForEvidence(
                    "container-scroll", 0)!.Status.ToString(),
                ViewportWheelDelta = captured?.WheelDelta,
            },
            Difference = "None",
        }));
    }

    [Fact]
    public void RealHwndViewportAndSelectionConvergeOnSecondPage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-reconcile", 7, 11, 19,
            now.AddSeconds(5));
        string[] firstNames = Enumerable.Range(1, 12)
            .Select(index => $"项目 {index}")
            .ToArray();
        string[] firstIds = Enumerable.Range(1, 12)
            .Select(index => $"item:{index}")
            .ToArray();
        ProductDesktopHostReadOnlyProjection first =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-reconcile", "翻页选择", firstNames,
                "#2457D6", 0.82, false, 24, 36, 360, 400,
                itemIds: firstIds,
                totalItemCount: 24,
                visibleItemStartOrdinal: 1);
        ProductDesktopHostDisplayProjection initial =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [first]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                initial,
                new nint(5225));
        nint originalHandle = surface.Handle;
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                firstIds,
                now).Controller!;
        _ = selection.Apply(
            lease,
            firstIds,
            new(ProductDesktopSelectionAction.SelectItem,
                ItemId: "item:2"),
            now);
        string[] secondNames = Enumerable.Range(13, 12)
            .Select(index => $"项目 {index}")
            .ToArray();
        string[] secondIds = Enumerable.Range(13, 12)
            .Select(index => $"item:{index}")
            .ToArray();
        ProductDesktopHostReadOnlyProjection second =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-reconcile", "翻页选择", secondNames,
                "#2457D6", 0.82, false, 24, 36, 360, 400,
                itemIds: secondIds,
                totalItemCount: 24,
                visibleItemStartOrdinal: 13);
        ProductDesktopHostDisplayProjection next =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), 96, [second]);

        bool applied = surface.ApplyPresentation(next);
        ProductDesktopSelectionSnapshot reconciled =
            selection.ReconcileVisibleItems(lease, secondIds, now);
        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElement group = root.FindFirst(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));
        AutomationElementCollection items = group.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Text));
        string firstVisibleName = items[0].Current.Name;

        Assert.True(applied);
        Assert.Equal(originalHandle, surface.Handle);
        Assert.Equal(12, items.Count);
        Assert.StartsWith("项目 13；", firstVisibleName,
            StringComparison.Ordinal);
        Assert.Empty(reconciled.SelectedItemIds);
        Assert.Equal("item:13", reconciled.FocusedItemId);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf006aRealHwndViewportSelectionConvergenceEvidence",
            Expected = new
            {
                SameHwnd = true,
                VisibleCount = 12,
                FirstVisibleName = "项目 13",
                SelectedCount = 0,
                FocusedItemId = "item:13",
            },
            Actual = new
            {
                SameHwnd = originalHandle == surface.Handle,
                VisibleCount = items.Count,
                FirstVisibleName = firstVisibleName.Split('；')[0],
                SelectedCount = reconciled.SelectedItemIds.Count,
                reconciled.FocusedItemId,
            },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    [Theory]
    [InlineData(96u, 20)]
    [InlineData(192u, 40)]
    [InlineData(384u, 80)]
    public void RealGdiThumbnailPixelRemainsExactAcrossDpi(
        uint dpi,
        int expectedPixels)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        byte[] pixels = Enumerable.Repeat(
                new byte[] { 30, 20, 10, 255 },
                4)
            .SelectMany(pixel => pixel)
            .ToArray();
        ProductDesktopThumbnailFrame frame =
            ProductDesktopThumbnailFrame.Create(2, 2, 8, pixels);
        ProductDesktopHostReadOnlyProjection container =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-pixel", "像素", ["图片"], "#2457D6", 0.82,
                false, 0, 0, 200, 150);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(0, 0, 1280, 720), dpi, [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(5300 + expectedPixels));

        uint actualColorRef =
            surface.DrawThumbnailFrameAndReadCenterForEvidence(frame);

        Assert.Equal(expectedPixels, surface.GetSystemTypeIconSizeForEvidence());
        Assert.Equal(0x001E140Au, actualColorRef);
    }
}
