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
    bool IsLocked,
    bool IsCollapsed,
    ProductWorkspaceContainerHealth HealthKind,
    string Health,
    string Detail,
    string Appearance,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadItemPresentation> Items,
    ProductWorkspaceFolderContentStatus? FolderContentStatus = null,
    int FolderContentItemCount = 0,
    ProductContainerFolderBindingResolution?
        FolderBindingRecoveredFrom = null)
{
    public string NavigationAccessibilityName =>
        $"查看并管理方格 {Ordinal}，{DisplayName}";

    public bool CanQuickToggleCollapsed => !IsLocked;

    public string QuickCollapseButtonText => IsCollapsed ? "展开方格" : "折叠方格";

    public string QuickCollapseAccessibilityName => IsLocked
        ? $"方格 {Ordinal}，{DisplayName} 已锁定，不能快速更改折叠状态"
        : $"{QuickCollapseButtonText} {Ordinal}，{DisplayName}";

    public bool CanQuickLock => !IsLocked;

    public string QuickLockButtonText => IsLocked ? "方格已锁定" : "锁定方格";

    public string QuickLockAccessibilityName => IsLocked
        ? $"方格 {Ordinal}，{DisplayName} 已锁定；请在管理区显式解锁"
        : $"锁定方格 {Ordinal}，{DisplayName}";
}

internal sealed record ProductWorkspaceReadFilterPresentation(
    string Detail,
    string MachineStatus,
    IReadOnlyList<ProductWorkspaceReadContainerPresentation> Containers);

internal sealed record ProductWorkspaceReadPresentation(
    string Detail,
    string MachineStatus,
    int UnresolvedReferenceCount,
    int ItemCount,
    int EmptyContainerCount,
    int NeedsReviewContainerCount,
    bool CanFilter,
    bool IsKnownEmptyWorkspace,
    IReadOnlyList<ProductWorkspaceReadContainerPresentation> Containers)
{
    internal long EditRevision { get; init; }

    internal IReadOnlyList<ProductWorkspaceSearchContainerInput> SearchContainers
    { get; init; } = Array.Empty<ProductWorkspaceSearchContainerInput>();

    public static ProductWorkspaceReadPresentation Unavailable { get; } = new(
        "正在读取盒子…",
        "WorkspaceViewUnavailable:Containers=0:Items=0",
        0,
        0,
        0,
        0,
        false,
        false,
        Array.Empty<ProductWorkspaceReadContainerPresentation>());

    public static ProductWorkspaceReadPresentation NoSavedConfiguration { get; } =
        new(
            "还没有盒子；创建第一个盒子，开始整理桌面。",
            "WorkspaceViewNoSavedConfiguration:Containers=0:Items=0",
            0,
            0,
            0,
            0,
            false,
            true,
            Array.Empty<ProductWorkspaceReadContainerPresentation>());

    public static ProductWorkspaceReadPresentation Create(
        ProductWorkspaceReadSnapshot snapshot,
        long editRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceReadContainerPresentation[] containers = snapshot.Containers
            .Select(container =>
            {
                string health = container.Health switch
                {
                    ProductWorkspaceContainerHealth.Empty => "空盒子",
                    ProductWorkspaceContainerHealth.Ready => "状态正常",
                    ProductWorkspaceContainerHealth.NeedsReview => "需要检查",
                    _ => "状态不可用",
                };
                return new ProductWorkspaceReadContainerPresentation(
                    container.Ordinal,
                    container.UserVisibleName,
                    $"盒子 {container.Ordinal}，{container.UserVisibleName}，状态：{health}",
                    container.IsLocked,
                    container.IsCollapsed,
                    container.Health,
                    health,
                    $"{(container.IsLocked ? "已锁定" : "未锁定")} · " +
                        $"{container.Items.Count} 个项目 · " +
                        $"{container.UnresolvedCount} 个待处理",
                    $"{(container.IsCollapsed ? "已折叠" : "已展开")} · " +
                        $"不透明度 {container.Opacity:P0} · " +
                        $"{container.WidthDip:0} × {container.HeightDip:0} DIP",
                    $"WorkspaceContainer:{container.Ordinal}:Items={container.Items.Count}:" +
                        $"Resolved={container.ResolvedCount}:Unresolved={container.UnresolvedCount}:" +
                        $"Health={container.Health}:Locked={container.IsLocked}:" +
                        $"Collapsed={container.IsCollapsed}",
                    container.IsCollapsed
                        ? Array.Empty<ProductWorkspaceReadItemPresentation>()
                        : container.Items.Select(CreateItem).ToArray(),
                    container.FolderContentStatus,
                    container.FolderContentItemCount,
                    container.FolderBindingRecoveredFrom);
            })
            .ToArray();

        return new(
            snapshot.Containers.Count == 0
                ? "还没有盒子；创建第一个盒子，开始整理桌面。"
                : $"{snapshot.Containers.Count} 个盒子 · {snapshot.ItemCount} 个项目 · " +
                    $"{snapshot.EmptyContainerCount} 个空盒子 · " +
                    $"{snapshot.NeedsReviewContainerCount} 个需要检查",
            $"WorkspaceViewReady:Containers={snapshot.Containers.Count}:" +
                $"Items={snapshot.ItemCount}:Resolved={snapshot.ResolvedCount}:" +
                $"Unresolved={snapshot.UnresolvedCount}:" +
                $"EmptyContainers={snapshot.EmptyContainerCount}:" +
                $"NeedsReviewContainers={snapshot.NeedsReviewContainerCount}",
            snapshot.UnresolvedCount,
            snapshot.ItemCount,
            snapshot.EmptyContainerCount,
            snapshot.NeedsReviewContainerCount,
            true,
            snapshot.Containers.Count == 0,
            containers)
        {
            EditRevision = editRevision,
            SearchContainers = snapshot.Containers.Select(container =>
                new ProductWorkspaceSearchContainerInput(
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
                            item.Source)).ToArray())).ToArray(),
        };
    }

    public ProductWorkspaceReadFilterPresentation ApplyFilter(
        ProductWorkspaceContainerHealthFilter filter,
        string query,
        ProductWorkspaceContainerSort sort)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!CanFilter)
        {
            return new(Detail, MachineStatus, Containers);
        }

        string label = filter switch
        {
            ProductWorkspaceContainerHealthFilter.All => "全部盒子",
            ProductWorkspaceContainerHealthFilter.NeedsReview => "需要检查",
            ProductWorkspaceContainerHealthFilter.Empty => "空盒子",
            ProductWorkspaceContainerHealthFilter.Ready => "状态正常",
            _ => "筛选不可用",
        };
        if (!ProductWorkspaceContainerHealthFilterPolicy.IsSupported(filter))
        {
            return new(
                "方格筛选状态不可用；没有展示部分结果，桌面文件未改变。",
                "WorkspaceViewFilterUnavailable:VisibleContainers=0:DesktopFilesChanged=False",
                Array.Empty<ProductWorkspaceReadContainerPresentation>());
        }

        ProductWorkspaceVisibleSearchResult search =
            ProductWorkspaceVisibleSearchPolicy.Resolve(
                query,
                Containers.Select(container =>
                    new ProductWorkspaceVisibleSearchInput(
                        container.DisplayName,
                        container.Health,
                        container.Items.Select(item => item.DisplayName).ToArray())).ToArray());
        if (!search.IsSupported)
        {
            return new(
                "搜索内容不可用；没有展示部分结果，桌面文件未改变。",
                "WorkspaceViewSearchUnavailable:VisibleContainers=0:" +
                    "Search=Invalid:DesktopFilesChanged=False",
                Array.Empty<ProductWorkspaceReadContainerPresentation>());
        }

        HashSet<int> matchingIndexes = search.MatchingIndexes.ToHashSet();
        ProductWorkspaceReadContainerPresentation[] filtered = Containers
            .Select((container, index) => (Container: container, Index: index))
            .Where(entry => matchingIndexes.Contains(entry.Index)
                && ProductWorkspaceContainerHealthFilterPolicy.Includes(
                    filter,
                    entry.Container.HealthKind))
            .Select(entry => entry.Container)
            .ToArray();
        ProductWorkspaceContainerSortResult sorting =
            ProductWorkspaceContainerSortPolicy.Resolve(
                sort,
                filtered.Select(container => new ProductWorkspaceContainerSortInput(
                    container.DisplayName,
                    container.HealthKind)).ToArray());
        if (!sorting.IsSupported)
        {
            return new(
                "方格排序状态不可用；没有展示部分结果，桌面文件未改变。",
                "WorkspaceViewSortUnavailable:VisibleContainers=0:" +
                    "Sort=Invalid:DesktopFilesChanged=False",
                Array.Empty<ProductWorkspaceReadContainerPresentation>());
        }

        ProductWorkspaceReadContainerPresentation[] visible = sorting.OrderedIndexes
            .Select(index => filtered[index])
            .ToArray();
        string searchDetail = search.Status == ProductWorkspaceVisibleSearchStatus.Empty
            ? string.Empty
            : " · 搜索已应用";
        string sortLabel = sort switch
        {
            ProductWorkspaceContainerSort.ConfigurationOrder => "配置顺序",
            ProductWorkspaceContainerSort.NameAscending => "名称升序",
            ProductWorkspaceContainerSort.NameDescending => "名称降序",
            ProductWorkspaceContainerSort.NeedsReviewFirst => "待审查优先",
            _ => "排序不可用",
        };
        return new(
            $"筛选：{label}{searchDetail} · 排序：{sortLabel} · " +
                $"显示 {visible.Length}/{Containers.Count} 个盒子",
            $"{MachineStatus}:Filter={filter}:Search={search.Status}:Sort={sort}:" +
                $"VisibleContainers={visible.Length}:" +
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
        string source = item.Source == ProductWorkspaceReadItemSource.BoundFolder
            ? "绑定文件夹"
            : "桌面引用";
        return new(
            displayName,
            resolved
                ? $"引用 {item.Ordinal}，{displayName}，{kind}，{resolution}"
                : $"匿名引用 {item.Ordinal}，{kind}，{resolution}",
            $"{source} · {kind} · {resolution}",
            $"WorkspaceItem:{item.Ordinal}:Kind={item.Kind}:Resolution={item.Resolution}:" +
                $"Source={item.Source}");
    }
}
