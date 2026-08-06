using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceLayoutRecoveryPresentation(
    string Title,
    string Detail,
    string Summary,
    string MachineStatus,
    bool CanConfirm,
    ProductWorkspaceLayoutRecoveryReviewToken? Token)
{
    public bool CanUndo { get; init; }

    public ProductWorkspaceLayoutRecoveryUndoToken? UndoToken { get; init; }

    public static ProductWorkspaceLayoutRecoveryPresentation Create(
        ProductWorkspaceLayoutRecoveryReviewResult review,
        ProductWorkspaceLayoutRecoveryUndoToken? undoToken = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        ProductWorkspaceLayoutRecoveryPreviewResult result = review.Preview;
        ArgumentNullException.ThrowIfNull(result);
        string counts =
            $"Containers={result.ContainerCount}:Mappings={result.DisplayMappingCount}:" +
            $"Unresolved={result.UnresolvedDisplayCount}:" +
            $"Corrected={result.VisibilityCorrectionCount}:DesktopWindowsChanged=False";
        ProductWorkspaceLayoutRecoveryPresentation presentation = result.Status switch
        {
            ProductWorkspaceLayoutRecoveryPreviewStatus.UnavailableSession => new(
                "布局恢复预览尚不可用",
                "等待正式产品会话；没有读取或移动任何桌面窗口。",
                "容器与显示拓扑尚不可用",
                $"LayoutRecoveryPreviewUnavailableSession:{counts}", false, null),
            ProductWorkspaceLayoutRecoveryPreviewStatus
                .AwaitingAuthoritativeTopology => new(
                    "等待权威显示拓扑",
                    "当前没有可确认的生产级显示拓扑；不会用空结果或探针数据生成恢复计划。",
                    $"正式容器 {result.ContainerCount} · 拓扑待连接",
                    $"LayoutRecoveryPreviewAwaitingAuthoritativeTopology:{counts}", false, null),
            ProductWorkspaceLayoutRecoveryPreviewStatus.SavedTopologyMissing => new(
                "缺少保存时显示拓扑",
                "旧配置或首次保存没有保存时 Bounds、DPI 与旋转；恢复保持阻断。",
                $"正式容器 {result.ContainerCount} · 需要版本化拓扑元数据",
                $"LayoutRecoveryPreviewSavedTopologyMissing:{counts}", false, null),
            ProductWorkspaceLayoutRecoveryPreviewStatus.Automatic => new(
                "布局可自动恢复",
                "保存时与当前拓扑精确一致且无需可见性纠正；本页仍只预览，不移动窗口。",
                DescribeCounts(result),
                $"LayoutRecoveryPreviewAutomatic:{counts}", false, null),
            ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired => new(
                "布局恢复需要确认",
                "检测到 DPI、工作区、显示器映射或可见性差异；必须预览确认后才能进入未来提交。",
                DescribeCounts(result),
                $"LayoutRecoveryPreviewReviewRequired:{counts}", review.CanConfirm, review.Token),
            ProductWorkspaceLayoutRecoveryPreviewStatus.Blocked => new(
                "布局恢复已阻断",
                "至少一个保存时显示器无法唯一解析；不会生成或应用部分恢复。",
                DescribeCounts(result),
                $"LayoutRecoveryPreviewBlocked:{counts}", false, null),
            _ => new(
                "布局恢复状态无效",
                "产品状态或拓扑没有通过有限校验；没有移动任何窗口。",
                "恢复计划未生成",
                $"LayoutRecoveryPreviewInvalidState:{counts}", false, null),
        };
        return presentation with
        {
            CanUndo = undoToken is not null,
            UndoToken = undoToken,
        };
    }

    private static string DescribeCounts(
        ProductWorkspaceLayoutRecoveryPreviewResult result) =>
        $"容器 {result.ContainerCount} · 映射 {result.DisplayMappingCount} · " +
        $"未解析 {result.UnresolvedDisplayCount} · 可见性纠正 {result.VisibilityCorrectionCount}";
}
