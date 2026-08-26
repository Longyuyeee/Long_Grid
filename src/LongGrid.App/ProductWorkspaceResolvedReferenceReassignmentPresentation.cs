using LongGrid.Core.Configuration;

namespace LongGrid.App;

public sealed record ProductWorkspaceReferenceReassignmentTargetPresentation(
    int ContainerOrdinal,
    string DisplayName,
    string AccessibilityName);

internal sealed record ProductWorkspaceResolvedReferenceReassignmentPresentation(
    long EditRevision,
    bool CanReassign,
    ProductWorkspaceReferenceReassignmentUndoToken? UndoToken,
    IReadOnlyList<ProductWorkspaceReferenceReassignmentTargetPresentation> Targets)
{
    public static ProductWorkspaceResolvedReferenceReassignmentPresentation
        Unavailable
    { get; } =
        new(
            0,
            false,
            null,
            Array.Empty<ProductWorkspaceReferenceReassignmentTargetPresentation>());

    public static ProductWorkspaceResolvedReferenceReassignmentPresentation Create(
        long editRevision,
        bool canEdit,
        ProductWorkspaceReadSnapshot snapshot,
        ProductWorkspaceReferenceReassignmentUndoToken? undoToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProductWorkspaceReferenceReassignmentTargetPresentation[] targets =
            snapshot.Containers
                .Where(container => !container.IsLocked)
                .Select(container =>
                    new ProductWorkspaceReferenceReassignmentTargetPresentation(
                        container.Ordinal,
                        container.UserVisibleName,
                        $"把引用改归属到方格 {container.UserVisibleName}"))
                .ToArray();
        bool hasResolvedSource = snapshot.Containers.Any(container =>
            !container.IsLocked
            && container.Items.Any(item => item.Resolution ==
                ProductItemReferenceResolution.Resolved
                && item.Source == ProductWorkspaceReadItemSource.Reference));
        return new(
            editRevision,
            canEdit && hasResolvedSource && targets.Length >= 2,
            canEdit ? undoToken : null,
            targets);
    }
}
