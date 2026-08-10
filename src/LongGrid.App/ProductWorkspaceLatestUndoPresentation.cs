using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceLatestUndoPresentation(
    ProductWorkspaceLatestUndoSelection Selection,
    ProductWorkspaceLayoutRecoveryUndoToken? LayoutRecoveryToken,
    ProductWorkspaceContainerRemovalUndoToken? ContainerRemovalToken,
    ProductWorkspaceReferenceBatchAdditionUndoToken? ReferenceBatchAdditionToken,
    ProductWorkspaceReferenceRemovalUndoToken? ReferenceRemovalToken,
    ProductWorkspaceReferenceReassignmentUndoToken? ReferenceReassignmentToken)
{
    public static ProductWorkspaceLatestUndoPresentation Unavailable { get; } =
        Create(null, null, null, null, null);

    public bool CanUndo => Selection.CanUndo;

    public string ButtonText => Selection.Kind switch
    {
        ProductWorkspaceLatestUndoKind.LayoutRecovery => "撤销布局恢复",
        ProductWorkspaceLatestUndoKind.ContainerRemoval => "撤销删除方格",
        ProductWorkspaceLatestUndoKind.ReferenceBatchAddition => "撤销批量加入",
        ProductWorkspaceLatestUndoKind.ReferenceRemoval => "撤销批量移除",
        ProductWorkspaceLatestUndoKind.ReferenceReassignment => "撤销批量改归属",
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
        ProductWorkspaceReferenceRemovalUndoToken? referenceRemoval,
        ProductWorkspaceReferenceReassignmentUndoToken? referenceReassignment) =>
        new(
            ProductWorkspaceLatestUndoSelector.Select(
                layoutRecovery,
                containerRemoval,
                referenceBatchAddition,
                referenceRemoval,
                referenceReassignment),
            layoutRecovery,
            containerRemoval,
            referenceBatchAddition,
            referenceRemoval,
            referenceReassignment);
}
