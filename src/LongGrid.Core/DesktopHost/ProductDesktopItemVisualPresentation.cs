using LongGrid.Core.Configuration;

namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopItemTypeIconKind
{
    File,
    Folder,
    Shortcut,
    Url,
}

public enum ProductDesktopItemVisualStatus
{
    ReadyTypeIcon,
    LoadingThumbnail,
    ReadyThumbnail,
    Offline,
    TargetChanged,
    Ambiguous,
    Unsupported,
    AccessDenied,
    FailedFallback,
}

public sealed record ProductDesktopItemVisualPresentation(
    ProductDesktopItemTypeIconKind TypeIcon,
    ProductDesktopItemVisualStatus Status)
{
    public string TypeName => TypeIcon switch
    {
        ProductDesktopItemTypeIconKind.File => "文件",
        ProductDesktopItemTypeIconKind.Folder => "文件夹",
        ProductDesktopItemTypeIconKind.Shortcut => "快捷方式",
        ProductDesktopItemTypeIconKind.Url => "网址",
        _ => "项目",
    };

    public string StatusName => Status switch
    {
        ProductDesktopItemVisualStatus.ReadyTypeIcon => "类型图标已就绪",
        ProductDesktopItemVisualStatus.LoadingThumbnail => "缩略图加载中",
        ProductDesktopItemVisualStatus.ReadyThumbnail => "缩略图已就绪",
        ProductDesktopItemVisualStatus.Offline => "目标离线或不存在",
        ProductDesktopItemVisualStatus.TargetChanged => "目标类型已变化",
        ProductDesktopItemVisualStatus.Ambiguous => "目标身份需要确认",
        ProductDesktopItemVisualStatus.Unsupported => "目标不受支持",
        ProductDesktopItemVisualStatus.AccessDenied => "无权读取目标",
        ProductDesktopItemVisualStatus.FailedFallback => "缩略图失败，已回退类型图标",
        _ => "状态未知",
    };

    public bool UsesFallbackTypeIcon => Status !=
        ProductDesktopItemVisualStatus.ReadyThumbnail;

    public string AccessibilityName(string visibleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visibleName);
        return $"{visibleName}；{TypeName}；{StatusName}";
    }

    public static ProductDesktopItemVisualPresentation Create(
        ConfigurationItemKind kind,
        ProductItemReferenceResolution resolution)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return new(
            kind switch
            {
                ConfigurationItemKind.File => ProductDesktopItemTypeIconKind.File,
                ConfigurationItemKind.Folder => ProductDesktopItemTypeIconKind.Folder,
                ConfigurationItemKind.Shortcut =>
                    ProductDesktopItemTypeIconKind.Shortcut,
                ConfigurationItemKind.Url => ProductDesktopItemTypeIconKind.Url,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            },
            resolution switch
            {
                ProductItemReferenceResolution.Resolved =>
                    ProductDesktopItemVisualStatus.ReadyTypeIcon,
                ProductItemReferenceResolution.Missing =>
                    ProductDesktopItemVisualStatus.Offline,
                ProductItemReferenceResolution.TypeChanged =>
                    ProductDesktopItemVisualStatus.TargetChanged,
                ProductItemReferenceResolution.Ambiguous =>
                    ProductDesktopItemVisualStatus.Ambiguous,
                ProductItemReferenceResolution.UnsupportedTarget =>
                    ProductDesktopItemVisualStatus.Unsupported,
                _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
            });
    }
}
