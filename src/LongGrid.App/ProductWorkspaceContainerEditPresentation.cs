using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceContainerEditCandidatePresentation(
    int Ordinal,
    string DisplayName,
    bool IsLocked,
    bool IsCollapsed)
{
    public string AccessibilityName => $"方格 {Ordinal}，{DisplayName}";
}

internal sealed record ProductWorkspaceContainerEditPresentation(
    long EditRevision,
    bool CanCreate,
    bool CanRename,
    bool CanUpdateState,
    IReadOnlyList<ProductWorkspaceContainerEditCandidatePresentation> Candidates)
{
    public static ProductWorkspaceContainerEditPresentation Unavailable { get; } =
        new(
            0,
            CanCreate: false,
            CanRename: false,
            CanUpdateState: false,
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
                container.IsCollapsed))
            .ToArray();
        return new(
            editRevision,
            CanCreate: canEdit,
            CanRename: canEdit && candidates.Length > 0,
            CanUpdateState: canEdit && candidates.Length > 0,
            candidates);
    }

    public static ProductWorkspaceContainerEditPresentation CreateEmpty(
        long editRevision) =>
        new(
            editRevision,
            CanCreate: true,
            CanRename: false,
            CanUpdateState: false,
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());
}
