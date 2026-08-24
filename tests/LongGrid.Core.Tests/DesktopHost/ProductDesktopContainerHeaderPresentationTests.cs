using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerHeaderPresentationTests(
    ITestOutputHelper output)
{
    [Fact]
    public void CreateExposesFiniteVisualAndAccessibilityState()
    {
        ProductDesktopContainerHeaderPresentation presentation =
            ProductDesktopContainerHeaderPresentation.Create(
                "工作资料",
                23,
                isLocked: true,
                isCollapsed: false);

        Assert.Equal("▾ 工作资料", presentation.VisualTitle);
        Assert.Equal(
            "23 项 · 安全引用 · 已锁定 · 已展开",
            presentation.VisualStatus);
        Assert.Equal(
            "工作资料；23 个项目；安全引用；已锁定；已展开；标题始终显示；双击标题切换折叠",
            presentation.AccessibilityName);
        Assert.Equal(
            "只读方格；ContainerHeader:Items=23:Locked=True:Collapsed=False:TitleVisibility=Always:DoubleClick=ToggleCollapsed:Source=SafeReferences",
            presentation.AccessibilityStatus);
    }

    [Fact]
    public void ProjectionKeepsTotalCountWhenVisibleItemsAreBounded()
    {
        string[] items = Enumerable.Range(1, 20)
            .Select(index => $"项目 {index}")
            .ToArray();
        ProductDesktopHostReadOnlyProjection projection =
            ProductDesktopHostReadOnlyProjection.Create(
                "container-1",
                "工作资料",
                items,
                "#2457D6",
                0.82,
                isCollapsed: true,
                24,
                36,
                360,
                240,
                isLocked: false,
                totalItemCount: 20);

        Assert.Equal(
            ProductDesktopHostReadOnlyProjection.MaximumVisibleItems,
            projection.ItemNames.Count);
        Assert.Equal(20, projection.TotalItemCount);
        Assert.Equal("▸ 工作资料", projection.Header.VisualTitle);
        Assert.Equal(
            "20 项 · 安全引用 · 可整理 · 已折叠",
            projection.Header.VisualStatus);
    }

    [Fact]
    public void ProjectionRejectsTotalsBelowVisibleOrAboveProductLimit()
    {
        Assert.Throws<ArgumentException>(() => Projection(totalItemCount: 0));
        Assert.Throws<ArgumentException>(() => Projection(totalItemCount: 501));
    }

    [Fact]
    public void RealNativeSurfacePublishesExpectedHeaderEvidence()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostReadOnlyProjection projection = Projection(
            totalItemCount: 7,
            isLocked: true,
            isCollapsed: true);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1280, 720),
                96,
                [projection]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(4004));

        var titleBuffer = new char[128];
        int titleLength = GetWindowText(
            surface.Handle,
            titleBuffer,
            titleBuffer.Length);
        ProductDesktopContainerHeaderPresentation actual = Assert.IsType<
            ProductDesktopContainerHeaderPresentation>(
                surface.GetContainerHeaderPresentationForEvidence(
                    "container-1"));

        Assert.NotEqual(nint.Zero, surface.Handle);
        Assert.True(IsWindow(surface.Handle));
        Assert.True(surface.PassiveWindowContractAttested);
        Assert.Equal(
            "Long方格桌面只读宿主",
            new string(titleBuffer, 0, titleLength));
        Assert.Equal("▸ 工作资料", actual.VisualTitle);
        Assert.Equal(
            "7 项 · 安全引用 · 已锁定 · 已折叠",
            actual.VisualStatus);
        Assert.Equal(
                "工作资料；7 个项目；安全引用；已锁定；已折叠；标题始终显示；双击标题切换折叠",
            actual.AccessibilityName);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf004aRealNativeHeaderSurfaceEvidence",
            Expected = new
            {
                NativeWindow = true,
                NativeWindowTitle = "Long方格桌面只读宿主",
                PassiveWindowContract = true,
                VisualTitle = "▸ 工作资料",
                VisualStatus = "7 项 · 安全引用 · 已锁定 · 已折叠",
                AccessibilityName =
                    "工作资料；7 个项目；安全引用；已锁定；已折叠",
            },
            Actual = new
            {
                NativeWindow = IsWindow(surface.Handle),
                NativeWindowTitle = new string(titleBuffer, 0, titleLength),
                PassiveWindowContract = surface.PassiveWindowContractAttested,
                actual.VisualTitle,
                actual.VisualStatus,
                actual.AccessibilityName,
            },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    [Fact]
    public void RealNativeSurfaceAppliesHoverAndHiddenTitlePolicies()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection hoverDisplay =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1280, 720),
                96,
                [ProductDesktopHostReadOnlyProjection.Create(
                    "container-1", "工作资料", ["计划.docx"], "#2457D6", 0.82,
                    false, 24, 36, 360, 240,
                    titleVisibility: ProductContainerTitleVisibilityPolicy.Hover)]);
        using WindowsProductDesktopHostReadOnlySurface hoverSurface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                hoverDisplay,
                new nint(4005));
        bool before = hoverSurface.IsContainerHeaderVisibleForEvidence(
            "container-1");
        _ = SendMessage(
            hoverSurface.Handle,
            0x0200,
            nint.Zero,
            new nint((40 << 16) | 30));
        bool during = hoverSurface.IsContainerHeaderVisibleForEvidence(
            "container-1");
        _ = SendMessage(hoverSurface.Handle, 0x02A3, nint.Zero, nint.Zero);
        bool after = hoverSurface.IsContainerHeaderVisibleForEvidence(
            "container-1");

        ProductDesktopHostDisplayProjection hiddenDisplay =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1280, 720),
                96,
                [ProductDesktopHostReadOnlyProjection.Create(
                    "container-2", "私密资料", ["计划.docx"], "#2457D6", 0.82,
                    false, 24, 36, 360, 240,
                    titleVisibility: ProductContainerTitleVisibilityPolicy.Hidden,
                    titleDoubleClickAction:
                        ProductContainerTitleDoubleClickAction.None)]);
        using WindowsProductDesktopHostReadOnlySurface hiddenSurface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                hiddenDisplay,
                new nint(4006));
        bool hidden = hiddenSurface.IsContainerHeaderVisibleForEvidence(
            "container-2");

        Assert.False(before);
        Assert.True(during);
        Assert.False(after);
        Assert.False(hidden);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf004eRealNativeTitlePolicyEvidence",
            Expected = new { BeforeHover = false, DuringHover = true, AfterLeave = false, Hidden = false },
            Actual = new { BeforeHover = before, DuringHover = during, AfterLeave = after, Hidden = hidden },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    private static ProductDesktopHostReadOnlyProjection Projection(
        int totalItemCount,
        bool isLocked = false,
        bool isCollapsed = false) =>
        ProductDesktopHostReadOnlyProjection.Create(
            "container-1",
            "工作资料",
            ["计划.docx"],
            "#2457D6",
            0.82,
            isCollapsed,
            24,
            36,
            360,
            240,
            isLocked,
            totalItemCount: totalItemCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        nint window,
        [Out] char[] text,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);
}
