using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceReadItemPresentation(
    string DisplayName,
    string AccessibilityName,
    string Detail,
    string MachineStatus);

internal sealed record ProductWorkspaceReadContainerPresentation(
    string DisplayName,
    string AccessibilityName,
    string Detail,
    string Appearance,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadItemPresentation> Items);

internal sealed record ProductWorkspaceReadPresentation(
    string Detail,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadContainerPresentation> Containers)
{
    public static ProductWorkspaceReadPresentation Unavailable { get; } = new(
        "等待正式产品会话；当前没有展示或修改任何桌面内容。",
        "WorkspaceViewUnavailable:Containers=0:Items=0",
        Array.Empty<ProductWorkspaceReadContainerPresentation>());

    public static ProductWorkspaceReadPresentation Create(
        ProductWorkspaceReadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceReadContainerPresentation[] containers = snapshot.Containers
            .Select(container => new ProductWorkspaceReadContainerPresentation(
                container.UserVisibleName,
                $"方格 {container.Ordinal}，{container.UserVisibleName}",
                $"{(container.IsLocked ? "已锁定" : "未锁定")} · " +
                    $"{container.Items.Count} 个引用 · " +
                    $"{container.UnresolvedCount} 个待处理",
                $"{(container.IsCollapsed ? "已折叠" : "已展开")} · " +
                    $"不透明度 {container.Opacity:P0}",
                $"WorkspaceContainer:{container.Ordinal}:Items={container.Items.Count}:" +
                    $"Resolved={container.ResolvedCount}:Unresolved={container.UnresolvedCount}:" +
                    $"Locked={container.IsLocked}:Collapsed={container.IsCollapsed}",
                container.IsCollapsed
                    ? Array.Empty<ProductWorkspaceReadItemPresentation>()
                    : container.Items.Select(CreateItem).ToArray()))
            .ToArray();

        return new(
            snapshot.Containers.Count == 0
                ? "当前正式配置没有方格；这里只读呈现，不会自动创建示例。"
                : $"{snapshot.Containers.Count} 个方格 · {snapshot.ItemCount} 个引用 · " +
                    $"{snapshot.UnresolvedCount} 个待处理",
            $"WorkspaceViewReady:Containers={snapshot.Containers.Count}:" +
                $"Items={snapshot.ItemCount}:Resolved={snapshot.ResolvedCount}:" +
                $"Unresolved={snapshot.UnresolvedCount}",
            containers);
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
