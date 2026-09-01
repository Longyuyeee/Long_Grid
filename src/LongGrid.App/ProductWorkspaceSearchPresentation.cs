using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceSearchDisplayChoicePresentation(
    string DisplayName,
    string? DisplayKey);

internal sealed record ProductWorkspaceSearchResultPresentation(
    string DisplayName,
    string Detail,
    string AccessibilityName,
    string MachineStatus,
    int ContainerOrdinal,
    int? ItemOrdinal);

internal sealed record ProductWorkspaceSearchPresentation(
    ProductWorkspaceSearchStatus Status,
    long Revision,
    string Detail,
    string MachineStatus,
    bool CanSearch,
    IReadOnlyList<ProductWorkspaceSearchResultPresentation> Results,
    IReadOnlyList<ProductWorkspaceSearchDisplayChoicePresentation> DisplayChoices)
{
    public static ProductWorkspaceSearchPresentation Unavailable { get; } = new(
        ProductWorkspaceSearchStatus.Invalid,
        0,
        "搜索结果尚不可用。",
        "WorkspaceSearchUnavailable:Results=0:Changed=False",
        false,
        Array.Empty<ProductWorkspaceSearchResultPresentation>(),
        [new("全部显示器", null)]);

    public static ProductWorkspaceSearchPresentation Create(
        long currentRevision,
        ProductWorkspaceSearchRequest request,
        IReadOnlyList<ProductWorkspaceSearchContainerInput> containers)
    {
        ProductWorkspaceSearchResult search = ProductWorkspaceSearch.Resolve(
            currentRevision,
            request,
            containers);
        ProductWorkspaceSearchResultPresentation[] results = search.Matches
            .Select(CreateResult)
            .ToArray();
        ProductWorkspaceSearchDisplayChoicePresentation[] displays =
        [
            new("全部显示器", null),
            .. containers
                .Select(container => container.DisplayKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((displayKey, index) =>
                    new ProductWorkspaceSearchDisplayChoicePresentation(
                        $"显示器 {index + 1}",
                        displayKey)),
        ];
        string detail = search.Status switch
        {
            ProductWorkspaceSearchStatus.EmptyQuery =>
                "输入方格名、项目名或类型开始搜索。",
            ProductWorkspaceSearchStatus.Applied when search.WasTruncated =>
                $"找到 {results.Length} 项；已检查前 {search.ScannedItemCount} 个项目。",
            ProductWorkspaceSearchStatus.Applied =>
                $"找到 {results.Length} 项。",
            ProductWorkspaceSearchStatus.NoResults =>
                "没有匹配结果；可调整类型、健康状态或显示器范围。",
            ProductWorkspaceSearchStatus.StaleAuthority =>
                "工作区已经更新，旧搜索结果已丢弃。",
            _ => "搜索条件不可用，没有展示部分结果。",
        };
        return new(
            search.Status,
            search.Revision,
            detail,
            $"WorkspaceSearch:{search.Status}:Revision={search.Revision}:" +
                $"Results={results.Length}:ScannedItems={search.ScannedItemCount}:" +
                $"Truncated={search.WasTruncated}:Changed=False:" +
                "DesktopFilesChanged=False",
            search.IsSupported,
            results,
            displays);
    }

    private static ProductWorkspaceSearchResultPresentation CreateResult(
        ProductWorkspaceSearchMatch match)
    {
        if (match.MatchKind == ProductWorkspaceSearchMatchKind.Container)
        {
            return new(
                match.DisplayName,
                $"盒子 · {DescribeHealth(match.ContainerHealth)}",
                $"盒子 {match.DisplayName}，{DescribeHealth(match.ContainerHealth)}",
                $"WorkspaceSearchResult:Container={match.ContainerOrdinal}:" +
                    $"Health={match.ContainerHealth}",
                match.ContainerOrdinal,
                null);
        }

        string kind = DescribeKind(match.ItemKind);
        string resolution = DescribeResolution(match.Resolution);
        string source = match.Source == ProductWorkspaceReadItemSource.BoundFolder
            ? "绑定文件夹"
            : "方格引用";
        return new(
            match.DisplayName,
            $"{match.ContainerDisplayName} · {source} · {kind} · {resolution}",
            $"{match.DisplayName}，所属盒子 {match.ContainerDisplayName}，" +
                $"{kind}，{resolution}",
            $"WorkspaceSearchResult:Container={match.ContainerOrdinal}:" +
                $"Item={match.ItemOrdinal}:Kind={match.ItemKind}:" +
                $"Resolution={match.Resolution}:Source={match.Source}",
            match.ContainerOrdinal,
            match.ItemOrdinal);
    }

    private static string DescribeHealth(ProductWorkspaceContainerHealth health) =>
        health switch
        {
            ProductWorkspaceContainerHealth.Empty => "空盒子",
            ProductWorkspaceContainerHealth.Ready => "状态正常",
            ProductWorkspaceContainerHealth.NeedsReview => "需要检查",
            _ => "状态不可用",
        };

    private static string DescribeKind(ConfigurationItemKind? kind) => kind switch
    {
        ConfigurationItemKind.File => "文件",
        ConfigurationItemKind.Folder => "文件夹",
        ConfigurationItemKind.Shortcut => "快捷方式",
        ConfigurationItemKind.Url => "网址快捷方式",
        _ => "未知类型",
    };

    private static string DescribeResolution(
        ProductItemReferenceResolution? resolution) => resolution switch
        {
            ProductItemReferenceResolution.Resolved => "可用",
            ProductItemReferenceResolution.Missing => "离线或未找到",
            ProductItemReferenceResolution.TypeChanged => "类型已变化",
            ProductItemReferenceResolution.Ambiguous => "存在多个候选",
            ProductItemReferenceResolution.UnsupportedTarget => "目标不受支持",
            _ => "状态不可用",
        };
}
