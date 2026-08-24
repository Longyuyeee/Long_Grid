using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceLatestUndoPresentation(
    ProductWorkspaceLatestUndoSelection Selection,
    ProductWorkspaceLayoutRecoveryUndoToken? LayoutRecoveryToken,
    ProductWorkspaceContainerRemovalUndoToken? ContainerRemovalToken,
    ProductWorkspaceReferenceBatchAdditionUndoToken? ReferenceBatchAdditionToken,
    ProductWorkspaceReferenceBatchAdditionUndoToken? SelectedReferenceContainerToken,
    ProductWorkspaceReferenceRemovalUndoToken? ReferenceRemovalToken,
    ProductWorkspaceReferenceReassignmentUndoToken? ReferenceReassignmentToken,
    ProductWorkspaceContainerEditUndoToken? ContainerEditToken)
{
    public static ProductWorkspaceLatestUndoPresentation Unavailable { get; } =
        Create(null, null, null, null, null, null);

    public bool CanUndo => Selection.CanUndo;

    public string ButtonText => Selection.Kind switch
    {
        ProductWorkspaceLatestUndoKind.LayoutRecovery => "撤销布局恢复",
        ProductWorkspaceLatestUndoKind.ContainerRemoval => "撤销删除方格",
        ProductWorkspaceLatestUndoKind.ReferenceBatchAddition => "撤销批量加入",
        ProductWorkspaceLatestUndoKind.SelectedReferenceContainer =>
            "撤销使用选择创建方格",
        ProductWorkspaceLatestUndoKind.ReferenceRemoval => "撤销批量移除",
        ProductWorkspaceLatestUndoKind.ReferenceReassignment => "撤销批量改归属",
        ProductWorkspaceLatestUndoKind.ContainerEdit => ContainerEditToken?.Kind switch
        {
            ProductWorkspaceContainerEditUndoKind.Rename => "撤销重命名",
            ProductWorkspaceContainerEditUndoKind.Locked => "撤销锁定状态",
            ProductWorkspaceContainerEditUndoKind.Collapsed => "撤销折叠状态",
            ProductWorkspaceContainerEditUndoKind.Appearance => "撤销方格外观",
            ProductWorkspaceContainerEditUndoKind.Placement => "撤销方格布局",
            _ => "撤销方格编辑",
        },
        _ => "没有可撤销的配置编辑",
    };

    public string AccessibilityName => CanUndo
        ? $"{ButtonText}，只恢复 Long方格配置，不修改桌面文件"
        : "没有可撤销的 Long方格配置编辑";

    public string MachineStatus =>
        $"LatestWorkspaceEditUndo:Kind={Selection.Kind}:" +
        $"CanUndo={CanUndo}:DesktopFilesChanged=False:DesktopWindowsChanged=False";

    public static ProductWorkspaceLatestUndoPresentation Create(
        ProductWorkspaceLayoutRecoveryUndoToken? layoutRecovery,
        ProductWorkspaceContainerRemovalUndoToken? containerRemoval,
        ProductWorkspaceReferenceBatchAdditionUndoToken? referenceBatchAddition,
        ProductWorkspaceReferenceBatchAdditionUndoToken? selectedReferenceContainer,
        ProductWorkspaceReferenceRemovalUndoToken? referenceRemoval,
        ProductWorkspaceReferenceReassignmentUndoToken? referenceReassignment,
        ProductWorkspaceContainerEditUndoToken? containerEdit = null) =>
        new(
            ProductWorkspaceLatestUndoSelector.Select(
                layoutRecovery,
                containerRemoval,
                referenceBatchAddition,
                selectedReferenceContainer,
                referenceRemoval,
                referenceReassignment,
                containerEdit),
            layoutRecovery,
            containerRemoval,
            referenceBatchAddition,
            selectedReferenceContainer,
            referenceRemoval,
            referenceReassignment,
            containerEdit);
}
