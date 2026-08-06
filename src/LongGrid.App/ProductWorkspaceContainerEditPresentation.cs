using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal sealed record ProductWorkspaceContainerEditCandidatePresentation(
    int Ordinal,
    string DisplayName)
{
    public string AccessibilityName => $"方格 {Ordinal}，{DisplayName}";
}

internal sealed record ProductWorkspaceContainerEditPresentation(
    long EditRevision,
    bool CanCreate,
    bool CanRename,
    IReadOnlyList<ProductWorkspaceContainerEditCandidatePresentation> Candidates)
{
    public static ProductWorkspaceContainerEditPresentation Unavailable { get; } =
        new(
            0,
            CanCreate: false,
            CanRename: false,
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
                container.UserVisibleName))
            .ToArray();
        return new(
            editRevision,
            CanCreate: canEdit,
            CanRename: canEdit && candidates.Length > 0,
            candidates);
    }

    public static ProductWorkspaceContainerEditPresentation CreateEmpty(
        long editRevision) =>
        new(
            editRevision,
            CanCreate: true,
            CanRename: false,
            Array.Empty<ProductWorkspaceContainerEditCandidatePresentation>());
}
