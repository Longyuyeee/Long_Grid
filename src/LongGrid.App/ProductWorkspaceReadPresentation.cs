using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceReadItemPresentation(
    string DisplayName,
    string AccessibilityName,
    string Detail,
    string MachineStatus);

internal sealed record ProductWorkspaceReadContainerPresentation(
    int Ordinal,
    string DisplayName,
    string AccessibilityName,
    ProductWorkspaceContainerHealth HealthKind,
    string Health,
    string Detail,
    string Appearance,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadItemPresentation> Items)
{
    public string NavigationAccessibilityName =>
        $"查看并管理方格 {Ordinal}，{DisplayName}";
}

internal sealed record ProductWorkspaceReadFilterPresentation(
    string Detail,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadContainerPresentation> Containers);

internal sealed record ProductWorkspaceReadPresentation(
    string Detail,
    string MachineStatus,
    int UnresolvedReferenceCount,
    bool CanFilter,
    IReadOnlyList<ProductWorkspaceReadContainerPresentation> Containers)
{
    public static ProductWorkspaceReadPresentation Unavailable { get; } = new(
        "等待正式产品会话；当前没有展示或修改任何桌面内容。",
        "WorkspaceViewUnavailable:Containers=0:Items=0",
        0,
        false,
        Array.Empty<ProductWorkspaceReadContainerPresentation>());

    public static ProductWorkspaceReadPresentation NoSavedConfiguration { get; } =
        new(
            "尚无正式配置；可以创建第一个方格，系统不会自动生成示例。",
            "WorkspaceViewNoSavedConfiguration:Containers=0:Items=0",
            0,
            false,
            Array.Empty<ProductWorkspaceReadContainerPresentation>());

    public static ProductWorkspaceReadPresentation Create(
        ProductWorkspaceReadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceReadContainerPresentation[] containers = snapshot.Containers
            .Select(container =>
            {
                string health = container.Health switch
                {
                    ProductWorkspaceContainerHealth.Empty => "空方格",
                    ProductWorkspaceContainerHealth.Ready => "引用正常",
                    ProductWorkspaceContainerHealth.NeedsReview => "有引用待审查",
                    _ => "状态不可用",
                };
                return new ProductWorkspaceReadContainerPresentation(
                container.Ordinal,
                container.UserVisibleName,
                $"方格 {container.Ordinal}，{container.UserVisibleName}，引用状态：{health}",
                container.Health,
                health,
                $"{(container.IsLocked ? "已锁定" : "未锁定")} · " +
                    $"{container.Items.Count} 个引用 · " +
                    $"{container.UnresolvedCount} 个待审查",
                $"{(container.IsCollapsed ? "已折叠" : "已展开")} · " +
                    $"不透明度 {container.Opacity:P0} · " +
                    $"{container.WidthDip:0} × {container.HeightDip:0} DIP",
                $"WorkspaceContainer:{container.Ordinal}:Items={container.Items.Count}:" +
                    $"Resolved={container.ResolvedCount}:Unresolved={container.UnresolvedCount}:" +
                    $"Health={container.Health}:Locked={container.IsLocked}:" +
                    $"Collapsed={container.IsCollapsed}",
                container.IsCollapsed
                    ? Array.Empty<ProductWorkspaceReadItemPresentation>()
                    : container.Items.Select(CreateItem).ToArray());
            })
            .ToArray();

        return new(
            snapshot.Containers.Count == 0
                ? "当前正式配置没有方格；这里只读呈现，不会自动创建示例。"
                : $"{snapshot.Containers.Count} 个方格 · {snapshot.ItemCount} 个引用 · " +
                    $"{snapshot.EmptyContainerCount} 个空方格 · " +
                    $"{snapshot.NeedsReviewContainerCount} 个方格待审查",
            $"WorkspaceViewReady:Containers={snapshot.Containers.Count}:" +
                $"Items={snapshot.ItemCount}:Resolved={snapshot.ResolvedCount}:" +
                $"Unresolved={snapshot.UnresolvedCount}:" +
                $"EmptyContainers={snapshot.EmptyContainerCount}:" +
                $"NeedsReviewContainers={snapshot.NeedsReviewContainerCount}",
            snapshot.UnresolvedCount,
            true,
            containers);
    }

    public ProductWorkspaceReadFilterPresentation ApplyFilter(
        ProductWorkspaceContainerHealthFilter filter)
    {
        if (!CanFilter)
        {
            return new(Detail, MachineStatus, Containers);
        }

        string label = filter switch
        {
            ProductWorkspaceContainerHealthFilter.All => "全部方格",
            ProductWorkspaceContainerHealthFilter.NeedsReview => "待审查方格",
            ProductWorkspaceContainerHealthFilter.Empty => "空方格",
            ProductWorkspaceContainerHealthFilter.Ready => "引用正常方格",
            _ => "筛选不可用",
        };
        if (!ProductWorkspaceContainerHealthFilterPolicy.IsSupported(filter))
        {
            return new(
                "方格筛选状态不可用；没有展示部分结果，桌面文件未改变。",
                "WorkspaceViewFilterUnavailable:VisibleContainers=0:DesktopFilesChanged=False",
                Array.Empty<ProductWorkspaceReadContainerPresentation>());
        }

        ProductWorkspaceReadContainerPresentation[] visible = Containers
            .Where(container => ProductWorkspaceContainerHealthFilterPolicy.Includes(
                filter,
                container.HealthKind))
            .ToArray();
        return new(
            $"筛选：{label} · 显示 {visible.Length}/{Containers.Count} 个方格；桌面文件未改变。",
            $"{MachineStatus}:Filter={filter}:VisibleContainers={visible.Length}:" +
                "DesktopFilesChanged=False",
            visible);
    }

    private static ProductWorkspaceReadItemPresentation CreateItem(
        ProductWorkspaceReadItem item)
    {
        bool resolved = item.Resolution == ProductItemReferenceResolution.Resolved;
        string displayName = resolved ? item.UserVisibleName! : $"引用 {item.Ordinal}";
        string kind = item.Kind switch
        {
            ConfigurationItemKind.File => "文件",
            ConfigurationItemKind.Folder => "文件夹",
            ConfigurationItemKind.Shortcut => "快捷方式",
            ConfigurationItemKind.Url => "网址快捷方式",
            _ => "未知类型",
        };
        string resolution = item.Resolution switch
        {
            ProductItemReferenceResolution.Resolved => "已解析",
            ProductItemReferenceResolution.Missing => "未找到",
            ProductItemReferenceResolution.TypeChanged => "类型已变化",
            ProductItemReferenceResolution.Ambiguous => "存在多个候选",
            ProductItemReferenceResolution.UnsupportedTarget => "目标不受支持",
            _ => "状态不可用",
        };
        return new(
            displayName,
            resolved
                ? $"引用 {item.Ordinal}，{displayName}，{kind}，{resolution}"
                : $"匿名引用 {item.Ordinal}，{kind}，{resolution}",
            $"{kind} · {resolution}",
            $"WorkspaceItem:{item.Ordinal}:Kind={item.Kind}:Resolution={item.Resolution}");
    }
}
