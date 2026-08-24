namespace LongGrid.Core.DesktopHost;

public sealed record ProductDesktopContainerHeaderPresentation(
    string Title,
    int TotalItemCount,
    bool IsLocked,
    bool IsCollapsed)
{
    public string VisualTitle => IsCollapsed ? $"▸ {Title}" : $"▾ {Title}";

    public string VisualStatus =>
        $"{TotalItemCount} 项 · 安全引用 · "
        + (IsLocked ? "已锁定 · " : "可整理 · ")
        + (IsCollapsed ? "已折叠" : "已展开");

    public string AccessibilityName =>
        $"{Title}；{TotalItemCount} 个项目；安全引用；"
        + (IsLocked ? "已锁定；" : "未锁定；")
        + (IsCollapsed ? "已折叠" : "已展开");

    public string AccessibilityStatus =>
        $"只读方格；ContainerHeader:Items={TotalItemCount}:"
        + $"Locked={IsLocked}:Collapsed={IsCollapsed}:Source=SafeReferences";

    public static ProductDesktopContainerHeaderPresentation Create(
        string title,
        int totalItemCount,
        bool isLocked,
        bool isCollapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegative(totalItemCount);
        return new(title, totalItemCount, isLocked, isCollapsed);
    }
}
