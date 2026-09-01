using System.Globalization;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceSessionHistoryRowPresentation(
    string Action,
    string Target,
    string Time,
    string State,
    string AccessibilityName);

internal sealed record ProductWorkspaceSessionHistoryPresentation(
    IReadOnlyList<ProductWorkspaceSessionHistoryRowPresentation> Items,
    bool CanUndo,
    bool CanRedo,
    string Detail,
    string MachineStatus)
{
    public static ProductWorkspaceSessionHistoryPresentation Empty { get; } =
        Create(ProductWorkspaceSessionHistorySnapshot.Empty);

    public string UndoAccessibilityName => CanUndo
        ? "撤销最近一次方格配置操作，不会删除或移动真实文件"
        : "没有可撤销的会话历史";

    public string RedoAccessibilityName => CanRedo
        ? "重做下一次方格配置操作，不会删除或移动真实文件"
        : "没有可重做的会话历史";

    public static ProductWorkspaceSessionHistoryPresentation Create(
        ProductWorkspaceSessionHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceSessionHistoryRowPresentation[] items = snapshot.Items
            .Select(item => new ProductWorkspaceSessionHistoryRowPresentation(
                item.ActionText,
                $"{item.TargetType} · {item.TargetName} · {item.TargetCount} 项",
                item.OccurredAtUtc.ToLocalTime().ToString(
                    "HH:mm:ss",
                    CultureInfo.CurrentCulture),
                item.CanUndo
                    ? "可撤销"
                    : item.CanRedo
                        ? "可重做"
                        : item.IsApplied
                            ? "已应用"
                            : "已撤销",
                $"{item.ActionText}，{item.TargetType} {item.TargetName}，" +
                    $"{item.TargetCount} 项，" +
                    (item.IsApplied ? "已应用" : "已撤销")))
            .ToArray();
        string detail = snapshot.UnavailableReason is { } reason
            ? $"历史暂不可用：{reason}。配置与真实文件均未改变。"
            : items.Length == 0
                ? "本次会话还没有可撤销的方格操作。"
                : $"本次会话记录 {items.Length}/{snapshot.Capacity} 步；" +
                    "撤销和重做只修改 Long方格配置，不会删除或移动真实文件。";
        return new(
            items,
            snapshot.CanUndo,
            snapshot.CanRedo,
            detail,
            $"WorkspaceSessionHistory:Count={items.Length}:" +
                $"Cursor={snapshot.Cursor}:Capacity={snapshot.Capacity}:" +
                $"CanUndo={snapshot.CanUndo}:CanRedo={snapshot.CanRedo}:" +
                $"Unavailable={snapshot.UnavailableReason is not null}:" +
                "DesktopFilesChanged=False");
    }
}
