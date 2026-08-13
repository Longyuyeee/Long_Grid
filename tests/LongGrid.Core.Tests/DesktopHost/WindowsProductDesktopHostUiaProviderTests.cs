using System.Windows.Automation;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class WindowsProductDesktopHostUiaProviderTests
{
    [Fact]
    public void NativeSurfaceExposesReadOnlyNonFocusableProjectionTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [
                    CreateContainer(
                        "container-1",
                        "工作",
                        ["需求文档.docx", "设计参考.fig"],
                        collapsed: false,
                        x: 24,
                        y: 36),
                    CreateContainer(
                        "container-2",
                        "归档",
                        ["历史记录.txt"],
                        collapsed: true,
                        x: 420,
                        y: 48),
                ]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(901));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElementCollection groups = root.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));

        Assert.Equal("LongGrid.DesktopHost.Root", root.Current.AutomationId);
        Assert.Equal("Long方格桌面只读区域", root.Current.Name);
        Assert.False(root.Current.IsKeyboardFocusable);
        Assert.Contains("不接收输入", root.Current.ItemStatus, StringComparison.Ordinal);
        Assert.Equal(2, groups.Count);

        AutomationElement expanded = groups[0];
        AutomationElement collapsed = groups[1];
        Assert.Equal("LongGrid.DesktopHost.Container.1", expanded.Current.AutomationId);
        Assert.Contains("工作", expanded.Current.Name, StringComparison.Ordinal);
        Assert.Contains("只读", expanded.Current.ItemStatus, StringComparison.Ordinal);
        Assert.False(expanded.Current.IsKeyboardFocusable);
        Assert.False(expanded.TryGetCurrentPattern(
            SelectionPattern.Pattern,
            out _));
        Assert.False(expanded.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out _));

        AutomationElementCollection items = expanded.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Text));
        Assert.Equal(2, items.Count);
        Assert.Equal("需求文档.docx", items[0].Current.Name);
        Assert.Equal("设计参考.fig", items[1].Current.Name);
        Assert.All(items.Cast<AutomationElement>(), item =>
        {
            Assert.False(item.Current.IsKeyboardFocusable);
            Assert.Contains("未公开路径", item.Current.ItemStatus, StringComparison.Ordinal);
            Assert.False(item.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        });
        Assert.Empty(collapsed.FindAll(
            TreeScope.Children,
            Condition.TrueCondition).Cast<AutomationElement>());

        System.Windows.Rect bounds = expanded.Current.BoundingRectangle;
        Assert.Equal(124, bounds.Left);
        Assert.Equal(236, bounds.Top);
        Assert.Equal(360, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void NativeSurfaceExplicitModeIsSelectableWithoutTakingForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [CreateContainer("container-1", "工作", ["需求文档.docx"], false, 24, 36)]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(902));
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 11);

        Assert.True(adapter.ApplyExplicit(new(
            Guid.NewGuid(),
            "container-1",
            7,
            9,
            11,
            DateTimeOffset.UtcNow.AddSeconds(1))));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        Assert.True(root.Current.IsKeyboardFocusable);
        Assert.Contains("等待输入消费接线", root.Current.ItemStatus, StringComparison.Ordinal);
        Assert.True(root.TryGetCurrentPattern(SelectionPattern.Pattern, out _));
        ProductDesktopInteractionSurfaceEvidence explicitEvidence =
            adapter.Capture().Evidence!;
        Assert.True(explicitEvidence.IsExplicitContract);
        Assert.False(explicitEvidence.OwnsForeground);

        Assert.True(adapter.ApplyPassive(11));
        root = AutomationElement.FromHandle(surface.Handle);
        Assert.False(root.Current.IsKeyboardFocusable);
        Assert.False(root.TryGetCurrentPattern(SelectionPattern.Pattern, out _));
        Assert.True(adapter.Capture().Evidence!.IsPassiveContract);

        Assert.True(adapter.Hide(11));
        Assert.True(adapter.Capture().Evidence!.IsHiddenContract);
    }

    private static ProductDesktopHostReadOnlyProjection CreateContainer(
        string id,
        string title,
        IReadOnlyList<string> items,
        bool collapsed,
        double x,
        double y) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            title,
            items,
            "#2457D6",
            0.82,
            collapsed,
            x,
            y,
            360,
            240);
}
