using LongGrid.Core.Configuration;

namespace LongGrid.App;

public sealed record ProductWorkspaceResolvedReferenceRemovalCandidatePresentation(
    int ContainerOrdinal,
    int ItemOrdinal,
    string DisplayName,
    string AccessibilityName);

internal sealed record ProductWorkspaceResolvedReferenceRemovalPresentation(
    long EditRevision,
    bool CanRemove,
    ProductWorkspaceReferenceRemovalUndoToken? UndoToken,
    IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>
        Candidates)
{
    public static ProductWorkspaceResolvedReferenceRemovalPresentation Unavailable { get; }
        = new(
            0,
            false,
            null,
            Array.Empty<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>());

    public static ProductWorkspaceResolvedReferenceRemovalPresentation Create(
        long editRevision,
        bool canEdit,
        ProductWorkspaceReadSnapshot snapshot,
        ProductWorkspaceReferenceRemovalUndoToken? undoToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] candidates =
            snapshot.Containers
                .Where(container => !container.IsLocked)
                .SelectMany(container => container.Items
                    .Where(item => item.Resolution ==
                        ProductItemReferenceResolution.Resolved
                        && item.Source ==
                            ProductWorkspaceReadItemSource.Reference)
                    .Select(item =>
                        new ProductWorkspaceResolvedReferenceRemovalCandidatePresentation(
                            container.Ordinal,
                            item.Ordinal,
                            $"{container.UserVisibleName} · {item.UserVisibleName}",
                            $"从方格 {container.UserVisibleName} 移除引用 {item.UserVisibleName}")))
                .ToArray();
        return new(
            editRevision,
            canEdit && candidates.Length > 0,
            canEdit ? undoToken : null,
            candidates);
    }
}
