using System.Windows.Automation;
using System.Windows.Automation.Provider;
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

    [Fact]
    public void NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract()
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
                [CreateContainer(
                    "container-1",
                    "工作",
                    ["需求文档.docx"],
                    false,
                    24,
                    36)]);
        ProductDesktopInteractionForwardedInput? forwarded = null;
        using WindowsProductDesktopInteractionActivationSource source =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(903),
                input =>
                {
                    forwarded = input;
                    return true;
                });

        AutomationElement root = AutomationElement.FromHandle(source.Handle);
        Assert.Equal(
            "LongGrid.DesktopHost.Activation.display-primary",
            root.Current.AutomationId);
        Assert.Equal("桌面方格交互入口", root.Current.Name);
        Assert.Equal(ControlType.Pane, root.Current.ControlType);
        AutomationElement button = root.FindFirst(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        Assert.NotNull(button);
        Assert.Equal("进入桌面方格交互", button.Current.Name);
        Assert.Equal(
            "LongGrid.DesktopHost.ActivationButton.1",
            button.Current.AutomationId);
        Assert.False(button.Current.IsKeyboardFocusable);
        Assert.Equal(new System.Windows.Rect(454, 236, 30, 30),
            button.Current.BoundingRectangle);
        Assert.True(button.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out object? pattern));
        ((InvokePattern)pattern!).Invoke();

        Assert.NotNull(forwarded);
        Assert.Equal(
            ProductDesktopInteractionForwardedInputKind
                .AssistiveTechnologyActivation,
            forwarded.Kind);
        Assert.Equal("display-primary", forwarded.DisplayId);
        Assert.True(forwarded.SourceAttested);
        Assert.False(forwarded.IsInjected);
        Assert.False(forwarded.IsAutoRepeat);
        Assert.True(source.ContractAttested);
        Assert.True(source.ApplyHidden());
        Assert.False(source.IsVisible);
        Assert.True(source.ApplyVisible());
        Assert.True(source.ContractAttested);
    }

    [Fact]
    public void ActivationUiaFragmentsExposeOnlyFiniteButtonsAndDirectInvokePath()
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
                [CreateContainer(
                    "container-1",
                    "工作",
                    ["需求文档.docx"],
                    false,
                    24,
                    36)]);
        using WindowsProductDesktopInteractionActivationSource source =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(904),
                _ => true);
        var firstRegion =
            new WindowsProductDesktopInteractionActivationSource.ActivationRegion(
                24,
                36,
                30,
                30,
                39,
                51);
        var secondRegion =
            new WindowsProductDesktopInteractionActivationSource.ActivationRegion(
                84,
                36,
                30,
                30,
                99,
                51);
        bool available = true;
        bool invokeResult = true;
        WindowsProductDesktopInteractionActivationSource.ActivationRegion?
            invoked = null;
        var provider = new WindowsProductDesktopInteractionActivationSource
            .ActivationUiaProvider(
                source.Handle,
                display,
                [firstRegion, secondRegion],
                new nint(904),
                () => available,
                region =>
                {
                    invoked = region;
                    return invokeResult;
                });

        Assert.NotNull(provider.HostRawElementProvider);
        Assert.Equal(new System.Windows.Rect(124, 236, 90, 30),
            provider.BoundingRectangle);
        Assert.Same(provider, provider.FragmentRoot);
        Assert.Null(provider.GetPatternProvider(
            InvokePatternIdentifiers.Pattern.Id));
        Assert.Equal("桌面方格交互入口", provider.GetPropertyValue(
            AutomationElementIdentifiers.NameProperty.Id));
        Assert.Equal("LongGrid.DesktopHost.Activation.display-primary",
            provider.GetPropertyValue(
                AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.Equal(ControlType.Pane.Id, provider.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsControlElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsContentElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.False((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.Null(provider.GetPropertyValue(-1));
        Assert.Null(provider.GetRuntimeId());
        Assert.Null(provider.GetEmbeddedFragmentRoots());
        Assert.Null(provider.GetFocus());
        provider.SetFocus();

        var first = Assert.IsType<
            WindowsProductDesktopInteractionActivationSource
                .ActivationUiaButtonProvider>(
                    provider.Navigate(NavigateDirection.FirstChild));
        var second = Assert.IsType<
            WindowsProductDesktopInteractionActivationSource
                .ActivationUiaButtonProvider>(
                    provider.Navigate(NavigateDirection.LastChild));
        Assert.Null(provider.Navigate(NavigateDirection.Parent));
        Assert.Same(first, provider.ElementProviderFromPoint(125, 237));
        Assert.Null(provider.ElementProviderFromPoint(500, 500));
        Assert.True(firstRegion.Contains(24, 36));
        Assert.False(firstRegion.Contains(54, 66));

        Assert.Null(first.HostRawElementProvider);
        Assert.Equal(new System.Windows.Rect(124, 236, 30, 30),
            first.BoundingRectangle);
        Assert.Same(provider, first.FragmentRoot);
        Assert.Same(first, first.GetPatternProvider(
            InvokePatternIdentifiers.Pattern.Id));
        Assert.Null(first.GetPatternProvider(-1));
        Assert.Equal("进入桌面方格交互", first.GetPropertyValue(
            AutomationElementIdentifiers.NameProperty.Id));
        Assert.Equal("LongGrid.DesktopHost.ActivationButton.1",
            first.GetPropertyValue(
                AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.Equal(ControlType.Button.Id, first.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsControlElementProperty.Id)!);
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsContentElementProperty.Id)!);
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.False((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.Null(first.GetPropertyValue(-1));
        Assert.Same(provider, first.Navigate(NavigateDirection.Parent));
        Assert.Same(second, first.Navigate(NavigateDirection.NextSibling));
        Assert.Null(first.Navigate(NavigateDirection.PreviousSibling));
        Assert.Same(first, second.Navigate(NavigateDirection.PreviousSibling));
        Assert.Null(second.Navigate(NavigateDirection.NextSibling));
        Assert.Equal(3, first.GetRuntimeId().Length);
        Assert.Null(first.GetEmbeddedFragmentRoots());
        first.SetFocus();

        first.Invoke();
        Assert.Equal(firstRegion, invoked);
        available = false;
        Assert.Throws<ElementNotEnabledException>(first.Invoke);
        available = true;
        invokeResult = false;
        Assert.Throws<ElementNotEnabledException>(first.Invoke);
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
