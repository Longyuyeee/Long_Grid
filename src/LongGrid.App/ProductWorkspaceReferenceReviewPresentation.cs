using LongGrid.Core.Configuration;

namespace LongGrid.App;

public sealed record ProductWorkspaceReferenceCandidatePresentation(
    int Ordinal,
    string KindLabel,
    long CatalogGeneration,
    int CatalogIndex);

internal sealed record ProductWorkspaceReferenceReviewPresentation(
    ProductWorkspaceReferenceReviewSnapshot? Snapshot,
    IReadOnlyList<ProductWorkspaceReferenceCandidatePresentation> Candidates,
    bool IsReadOnly,
    ProductWorkspaceReferenceReviewError Error)
{
    public static ProductWorkspaceReferenceReviewPresentation Unavailable { get; } =
        new(
            null,
            Array.Empty<ProductWorkspaceReferenceCandidatePresentation>(),
            IsReadOnly: true,
            ProductWorkspaceReferenceReviewError.None);
}
