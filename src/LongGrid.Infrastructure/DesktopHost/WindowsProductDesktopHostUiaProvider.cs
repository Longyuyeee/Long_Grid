#if WINDOWS
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsProductDesktopHostUiaRootProvider
    : IRawElementProviderFragmentRoot
{
    private readonly nint window;
    private readonly ProductDesktopHostDisplayProjection projection;
    private readonly WindowsProductDesktopHostUiaContainerProvider[] containers;

    internal WindowsProductDesktopHostUiaRootProvider(
        nint window,
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker)
    {
        this.window = window != nint.Zero
            ? window
            : throw new ArgumentOutOfRangeException(nameof(window));
        this.projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        int marker = unchecked((int)instanceMarker.ToInt64());
        containers = projection.Containers
            .Select((container, index) =>
                new WindowsProductDesktopHostUiaContainerProvider(
                    this,
                    container,
                    index,
                    marker))
            .ToArray();
    }

    internal IReadOnlyList<WindowsProductDesktopHostUiaContainerProvider>
        Containers => containers;

    internal ProductDesktopHostDisplayProjection Projection => projection;

    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(window);

    public Rect BoundingRectangle
    {
        get
        {
            Rect bounds = containers[0].BoundingRectangle;
            foreach (WindowsProductDesktopHostUiaContainerProvider container
                in containers.Skip(1))
            {
                bounds.Union(container.BoundingRectangle);
            }

            return bounds;
        }
    }

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            "Long方格桌面只读区域",
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            "LongGrid.DesktopHost.Root",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Pane.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            false,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            $"只读预览；{containers.Length} 个方格；不接收输入",
        _ => null,
    };

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.FirstChild when containers.Length > 0 => containers[0],
            NavigateDirection.LastChild when containers.Length > 0 => containers[^1],
            _ => null,
        };

    public int[]? GetRuntimeId() => null;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        Point point = new(x, y);
        foreach (WindowsProductDesktopHostUiaContainerProvider container
            in containers)
        {
            WindowsProductDesktopHostUiaItemProvider? item = container.Items
                .FirstOrDefault(candidate =>
                    candidate.BoundingRectangle.Contains(point));
            if (item is not null)
            {
                return item;
            }

            if (container.BoundingRectangle.Contains(point))
            {
                return container;
            }
        }

        return null;
    }

    public IRawElementProviderFragment? GetFocus() => null;

    internal Rect ScreenBounds(PixelRect local) => new(
        projection.WorkArea.Left + local.Left,
        projection.WorkArea.Top + local.Top,
        local.Width,
        local.Height);
}

internal sealed class WindowsProductDesktopHostUiaContainerProvider
    : IRawElementProviderFragment
{
    private readonly WindowsProductDesktopHostUiaRootProvider root;
    private readonly ProductDesktopHostReadOnlyProjection projection;
    private readonly int index;
    private readonly int marker;
    private readonly WindowsProductDesktopHostUiaItemProvider[] items;

    internal WindowsProductDesktopHostUiaContainerProvider(
        WindowsProductDesktopHostUiaRootProvider root,
        ProductDesktopHostReadOnlyProjection projection,
        int index,
        int marker)
    {
        this.root = root;
        this.projection = projection;
        this.index = index;
        this.marker = marker;
        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            root.Projection,
            projection);
        double scale = root.Projection.EffectiveDpi / 96d;
        int headerHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        int itemHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.ItemHeightDip,
            scale);
        int visibleItemCount = projection.IsCollapsed
            ? 0
            : Math.Min(
                projection.ItemNames.Count,
                Math.Max(0, (bounds.Height - headerHeight) / itemHeight));
        items = projection.ItemNames.Take(visibleItemCount)
            .Select((name, itemIndex) =>
                new WindowsProductDesktopHostUiaItemProvider(
                    this,
                    name,
                    itemIndex,
                    marker)).ToArray();
    }

    internal IReadOnlyList<WindowsProductDesktopHostUiaItemProvider> Items => items;

    internal int Index => index;

    public ProviderOptions ProviderOptions => root.ProviderOptions;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public Rect BoundingRectangle => root.ScreenBounds(
        ProductDesktopHostSurfaceLayout.GetContainerBounds(
            root.Projection,
            projection));

    public IRawElementProviderFragmentRoot FragmentRoot => root;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            $"{projection.Title}，{items.Length} 个可见项目，" +
            (projection.IsCollapsed ? "已折叠，只读" : "已展开，只读"),
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            $"LongGrid.DesktopHost.Container.{index + 1}",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Group.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            false,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            "只读方格；不支持选择、调用或编辑",
        _ => null,
    };

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => root,
            NavigateDirection.PreviousSibling when index > 0 =>
                root.Containers[index - 1],
            NavigateDirection.NextSibling when index + 1 < root.Containers.Count =>
                root.Containers[index + 1],
            NavigateDirection.FirstChild when items.Length > 0 => items[0],
            NavigateDirection.LastChild when items.Length > 0 => items[^1],
            _ => null,
        };

    public int[] GetRuntimeId() =>
    [
        AutomationInteropProvider.AppendRuntimeId,
        marker,
        1000 + index,
    ];

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }
}

internal sealed class WindowsProductDesktopHostUiaItemProvider
    : IRawElementProviderFragment
{
    private readonly WindowsProductDesktopHostUiaContainerProvider container;
    private readonly string name;
    private readonly int index;
    private readonly int marker;

    internal WindowsProductDesktopHostUiaItemProvider(
        WindowsProductDesktopHostUiaContainerProvider container,
        string name,
        int index,
        int marker)
    {
        this.container = container;
        this.name = name;
        this.index = index;
        this.marker = marker;
    }

    public ProviderOptions ProviderOptions => container.ProviderOptions;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public Rect BoundingRectangle
    {
        get
        {
            Rect parent = container.BoundingRectangle;
            double scale = container.FragmentRoot is
                WindowsProductDesktopHostUiaRootProvider root
                    ? root.Projection.EffectiveDpi / 96d
                    : 1d;
            double header = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.HeaderHeightDip,
                scale);
            double itemHeight = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.ItemHeightDip,
                scale);
            double top = parent.Top + header + (index * itemHeight);
            return new(
                parent.Left,
                top,
                parent.Width,
                Math.Max(0, Math.Min(itemHeight, parent.Bottom - top)));
        }
    }

    public IRawElementProviderFragmentRoot FragmentRoot => container.FragmentRoot;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id => name,
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            $"LongGrid.DesktopHost.Item.{container.Index + 1}.{index + 1}",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Text.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            false,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            "只读项目名称；未公开路径或文件操作",
        _ => null,
    };

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => container,
            NavigateDirection.PreviousSibling when index > 0 =>
                container.Items[index - 1],
            NavigateDirection.NextSibling when index + 1 < container.Items.Count =>
                container.Items[index + 1],
            _ => null,
        };

    public int[] GetRuntimeId() =>
    [
        AutomationInteropProvider.AppendRuntimeId,
        marker,
        2000 + (container.Index *
            ProductDesktopHostReadOnlyProjection.MaximumVisibleItems) + index,
    ];

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }
}
#endif
