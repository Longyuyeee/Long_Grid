using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceContainerEditCandidatePresentation(
    int Ordinal,
    string DisplayName,
    bool IsLocked,
    bool IsCollapsed,
    int ItemCount,
    string Color,
    double Opacity,
    double XDip,
    double YDip,
    double WidthDip,
    double HeightDip,
    ProductContainerTitleVisibilityPolicy TitleVisibility,
    ProductContainerTitleDoubleClickAction TitleDoubleClickAction,
    ProductContainerFolderBindingResolution? FolderBindingResolution)
{
    public string AccessibilityName => $"方格 {Ordinal}，{DisplayName}";

    public bool HasFolderBinding => FolderBindingResolution is not null;
}

internal sealed record ProductWorkspaceContainerColorChoicePresentation(
    ProductWorkspaceContainerColorPreset Preset,
    string DisplayName,
    string Color);

internal sealed record ProductWorkspaceContainerOpacityChoicePresentation(
    ProductWorkspaceContainerOpacityPreset Preset,
    string DisplayName,
    double Opacity);

internal sealed record ProductWorkspaceContainerPositionChoicePresentation(
    ProductWorkspaceContainerPositionPreset Preset,
    string DisplayName,
    double XDip,
    double YDip);

internal sealed record ProductWorkspaceContainerSizeChoicePresentation(
    ProductWorkspaceContainerSizePreset Preset,
    string DisplayName,
    double WidthDip,
    double HeightDip);

internal sealed record ProductWorkspaceContainerTitleVisibilityChoicePresentation(
    ProductContainerTitleVisibilityPolicy Policy,
    string DisplayName);

internal sealed record ProductWorkspaceContainerTitleDoubleClickChoicePresentation(
    ProductContainerTitleDoubleClickAction Action,
    string DisplayName);

internal sealed record ProductWorkspaceContainerEditPresentation(
    long EditRevision,
    bool CanCreate,
    bool CanRename,
    bool CanUpdateState,
    bool CanUpdateAppearance,
    bool CanUpdatePlacement,
    bool CanUpdateFolderBinding,
    bool CanRemove,
    ProductWorkspaceContainerRemovalUndoToken? RemovalUndoToken,
    IReadOnlyList<ProductWorkspaceContainerEditCandidatePresentation> Candidates)
{
    public static IReadOnlyList<ProductWorkspaceContainerColorChoicePresentation>
        ColorChoices
    { get; } =
        Enum.GetValues<ProductWorkspaceContainerColorPreset>()
            .Select(preset => new ProductWorkspaceContainerColorChoicePresentation(
                preset,
                preset switch
                {
                    ProductWorkspaceContainerColorPreset.Azure => "晴空蓝",
                    ProductWorkspaceContainerColorPreset.Indigo => "品牌靛蓝",
                    ProductWorkspaceContainerColorPreset.Slate => "石板灰",
                    ProductWorkspaceContainerColorPreset.Emerald => "翡翠绿",
                    ProductWorkspaceContainerColorPreset.Amber => "琥珀橙",
                    _ => preset.ToString(),
                },
                ProductWorkspaceCommitCoordinator.ResolveColor(preset)))
            .ToArray();

    public static IReadOnlyList<ProductWorkspaceContainerTitleVisibilityChoicePresentation>
        TitleVisibilityChoices
    { get; } =
    [
        new(ProductContainerTitleVisibilityPolicy.Always, "始终显示"),
        new(ProductContainerTitleVisibilityPolicy.Hover, "悬停显示"),
        new(ProductContainerTitleVisibilityPolicy.Hidden, "始终隐藏"),
    ];

    public static IReadOnlyList<ProductWorkspaceContainerTitleDoubleClickChoicePresentation>
        TitleDoubleClickChoices
    { get; } =
    [
        new(ProductContainerTitleDoubleClickAction.ToggleCollapsed, "双击折叠 / 展开"),
        new(ProductContainerTitleDoubleClickAction.None, "双击不执行操作"),
    ];

    public static IReadOnlyList<ProductWorkspaceContainerOpacityChoicePresentation>
        OpacityChoices
    { get; } =
        Enum.GetValues<ProductWorkspaceContainerOpacityPreset>()
            .Select(preset => new ProductWorkspaceContainerOpacityChoicePresentation(
                preset,
                preset switch
                {
                    ProductWorkspaceContainerOpacityPreset.Solid => "实体 · 100%",
                    ProductWorkspaceContainerOpacityPreset.Strong => "清晰 · 88%",
                    ProductWorkspaceContainerOpacityPreset.Soft => "柔和 · 72%",
                    ProductWorkspaceContainerOpacityPreset.Subtle => "轻盈 · 56%",
                    _ => preset.ToString(),
                },
                ProductWorkspaceCommitCoordinator.ResolveOpacity(preset)))
            .ToArray();

    public static IReadOnlyList<ProductWorkspaceContainerPositionChoicePresentation>
        PositionChoices
    { get; } =
        Enum.GetValues<ProductWorkspaceContainerPositionPreset>()
            .Select(preset =>
            {
                (double xDip, double yDip) =
                    ProductWorkspaceCommitCoordinator.ResolvePosition(preset);
                return new ProductWorkspaceContainerPositionChoicePresentation(
                    preset,
                    preset switch
                    {
                        ProductWorkspaceContainerPositionPreset.Start => "起始位 · 32, 48 DIP",
                        ProductWorkspaceContainerPositionPreset.OffsetOne => "偏移一 · 56, 72 DIP",
                        ProductWorkspaceContainerPositionPreset.OffsetTwo => "偏移二 · 80, 96 DIP",
                        ProductWorkspaceContainerPositionPreset.OffsetThree => "偏移三 · 104, 120 DIP",
                        _ => preset.ToString(),
                    },
                    xDip,
                    yDip);
            })
            .ToArray();

    public static IReadOnlyList<ProductWorkspaceContainerSizeChoicePresentation>
        SizeChoices
    { get; } =
        Enum.GetValues<ProductWorkspaceContainerSizePreset>()
            .Select(preset =>
            {
                (double widthDip, double heightDip) =
                    ProductWorkspaceCommitCoordinator.ResolveSize(preset);
                return new ProductWorkspaceContainerSizeChoicePresentation(
                    preset,
                    preset switch
                    {
                        ProductWorkspaceContainerSizePreset.Compact => "紧凑 · 280 × 192 DIP",
                        ProductWorkspaceContainerSizePreset.Standard => "标准 · 360 × 240 DIP",
                        ProductWorkspaceContainerSizePreset.Wide => "宽屏 · 480 × 280 DIP",
                        ProductWorkspaceContainerSizePreset.Large => "大号 · 560 × 360 DIP",
                        _ => preset.ToString(),
                    },
                    widthDip,
                    heightDip);
            })
            .ToArray();

    public static ProductWorkspaceContainerEditPresentation Unavailable { get; } =
        new(
            0,
            CanCreate: false,
            CanRename: false,
            CanUpdateState: false,
            CanUpdateAppearance: false,
            CanUpdatePlacement: false,
            CanUpdateFolderBinding: false,
            CanRemove: false,
            RemovalUndoToken: null,
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());

    public static ProductWorkspaceContainerEditPresentation Create(
        long editRevision,
        bool canEdit,
        IEnumerable<ProductWorkspaceReadContainer> containers,
        ProductWorkspaceContainerRemovalUndoToken? removalUndoToken)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ProductWorkspaceContainerEditCandidatePresentation[] candidates = containers
            .Select(container => new ProductWorkspaceContainerEditCandidatePresentation(
                container.Ordinal,
                container.UserVisibleName,
                container.IsLocked,
                container.IsCollapsed,
                container.Items.Count,
                container.Color,
                container.Opacity,
                container.XDip,
                container.YDip,
                container.WidthDip,
                container.HeightDip,
                container.TitleVisibility,
                container.TitleDoubleClickAction,
                container.FolderBindingResolution))
            .ToArray();
        return new(
            editRevision,
            CanCreate: canEdit,
            CanRename: canEdit && candidates.Length > 0,
            CanUpdateState: canEdit && candidates.Length > 0,
            CanUpdateAppearance: canEdit && candidates.Length > 0,
            CanUpdatePlacement: canEdit && candidates.Length > 0,
            CanUpdateFolderBinding: canEdit && candidates.Length > 0,
            CanRemove: canEdit && candidates.Any(candidate => !candidate.IsLocked),
            RemovalUndoToken: canEdit ? removalUndoToken : null,
            candidates);
    }

    public static ProductWorkspaceContainerEditPresentation CreateEmpty(
        long editRevision) =>
        new(
            editRevision,
            CanCreate: true,
            CanRename: false,
            CanUpdateState: false,
            CanUpdateAppearance: false,
            CanUpdatePlacement: false,
            CanUpdateFolderBinding: false,
            CanRemove: false,
            RemovalUndoToken: null,
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());
}
