using LongGrid.Core.Configuration;

namespace LongGrid.App;

internal static class ProductQuickStartPreviewContinuity
{
    internal static bool CanRetain(
        bool previewOpen,
        ProductQuickStartSuggestionSnapshot? previous,
        ProductQuickStartSuggestionSnapshot next) =>
        previewOpen && previous?.CanCommit == true && next.CanCommit
        && previous.WorkspaceRevision == next.WorkspaceRevision
        && previous.CatalogGeneration == next.CatalogGeneration
        && string.Equals(previous.WorkspaceFingerprint, next.WorkspaceFingerprint, StringComparison.Ordinal)
        && string.Equals(previous.CatalogFingerprint, next.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(previous.ContainerName, next.ContainerName, StringComparison.Ordinal)
        && previous.TotalCandidateCount == next.TotalCandidateCount
        && previous.IsTruncated == next.IsTruncated
        && previous.Items.SequenceEqual(next.Items);
}
