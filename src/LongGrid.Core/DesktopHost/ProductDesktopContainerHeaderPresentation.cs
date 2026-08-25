using LongGrid.Core.Configuration;

namespace LongGrid.Core.DesktopHost;

public sealed record ProductDesktopContainerHeaderPresentation(
    string Title,
    int TotalItemCount,
    bool IsLocked,
    bool IsCollapsed,
    ProductContainerTitleVisibilityPolicy TitleVisibility,
    ProductContainerTitleDoubleClickAction TitleDoubleClickAction)
{
    public string VisualTitle => IsCollapsed ? $"▸ {Title}" : $"▾ {Title}";

    public string VisualStatus =>
        $"{TotalItemCount} 项 · 安全引用 · "
        + (IsLocked ? "已锁定 · " : "可整理 · ")
        + (IsCollapsed ? "已折叠" : "已展开");

    public string AccessibilityName =>
        $"{Title}；{TotalItemCount} 个项目；安全引用；"
        + (IsLocked ? "已锁定；" : "未锁定；")
        + (IsCollapsed ? "已折叠；" : "已展开；")
        + $"标题{DescribeVisibility(TitleVisibility)}；双击标题{DescribeDoubleClick(TitleDoubleClickAction)}";

    public string AccessibilityStatus =>
        $"只读方格；ContainerHeader:Items={TotalItemCount}:"
        + $"Locked={IsLocked}:Collapsed={IsCollapsed}:"
        + $"TitleVisibility={TitleVisibility}:DoubleClick={TitleDoubleClickAction}:"
        + "Source=SafeReferences";

    public static ProductDesktopContainerHeaderPresentation Create(
        string title,
        int totalItemCount,
        bool isLocked,
        bool isCollapsed,
        ProductContainerTitleVisibilityPolicy titleVisibility =
            ProductContainerTitleVisibilityPolicy.Always,
        ProductContainerTitleDoubleClickAction titleDoubleClickAction =
            ProductContainerTitleDoubleClickAction.ToggleCollapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegative(totalItemCount);
        if (!Enum.IsDefined(titleVisibility)
            || !Enum.IsDefined(titleDoubleClickAction))
        {
            throw new ArgumentOutOfRangeException(nameof(titleVisibility));
        }
        return new(
            title,
            totalItemCount,
            isLocked,
            isCollapsed,
            titleVisibility,
            titleDoubleClickAction);
    }

    private static string DescribeVisibility(
        ProductContainerTitleVisibilityPolicy policy) => policy switch
        {
            ProductContainerTitleVisibilityPolicy.Always => "始终显示",
            ProductContainerTitleVisibilityPolicy.Hover => "悬停显示",
            ProductContainerTitleVisibilityPolicy.Hidden => "隐藏",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    private static string DescribeDoubleClick(
        ProductContainerTitleDoubleClickAction action) => action switch
        {
            ProductContainerTitleDoubleClickAction.ToggleCollapsed =>
                "切换折叠",
            ProductContainerTitleDoubleClickAction.None => "无动作",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
}
