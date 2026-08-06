using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceContainerEditCandidatePresentation(
    int Ordinal,
    string DisplayName,
    bool IsLocked,
    bool IsCollapsed,
    string Color,
    double Opacity)
{
    public string AccessibilityName => $"方格 {Ordinal}，{DisplayName}";
}

internal sealed record ProductWorkspaceContainerColorChoicePresentation(
    ProductWorkspaceContainerColorPreset Preset,
    string DisplayName,
    string Color);

internal sealed record ProductWorkspaceContainerOpacityChoicePresentation(
    ProductWorkspaceContainerOpacityPreset Preset,
    string DisplayName,
    double Opacity);

internal sealed record ProductWorkspaceContainerEditPresentation(
    long EditRevision,
    bool CanCreate,
    bool CanRename,
    bool CanUpdateState,
    bool CanUpdateAppearance,
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

    public static ProductWorkspaceContainerEditPresentation Unavailable { get; } =
        new(
            0,
            CanCreate: false,
            CanRename: false,
            CanUpdateState: false,
            CanUpdateAppearance: false,
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());

    public static ProductWorkspaceContainerEditPresentation Create(
        long editRevision,
        bool canEdit,
        IEnumerable<ProductWorkspaceReadContainer> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ProductWorkspaceContainerEditCandidatePresentation[] candidates = containers
            .Select(container => new ProductWorkspaceContainerEditCandidatePresentation(
                container.Ordinal,
                container.UserVisibleName,
                container.IsLocked,
                container.IsCollapsed,
                container.Color,
                container.Opacity))
            .ToArray();
        return new(
            editRevision,
            CanCreate: canEdit,
            CanRename: canEdit && candidates.Length > 0,
            CanUpdateState: canEdit && candidates.Length > 0,
            CanUpdateAppearance: canEdit && candidates.Length > 0,
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
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());
}
