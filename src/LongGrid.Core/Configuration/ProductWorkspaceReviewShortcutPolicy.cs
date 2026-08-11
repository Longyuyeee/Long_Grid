namespace LongGrid.Core.Configuration;

public static class ProductWorkspaceReviewShortcutPolicy
{
    public static bool CanOpen(
        int workspaceUnresolvedCount,
        int reviewItemCount,
        bool reviewAvailable) =>
        reviewAvailable
        && workspaceUnresolvedCount > 0
        && reviewItemCount == workspaceUnresolvedCount;
}
