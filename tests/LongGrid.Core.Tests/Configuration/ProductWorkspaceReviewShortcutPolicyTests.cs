using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReviewShortcutPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(256, 256)]
    public void MatchingPositiveAvailableCountsCanOpen(
        int workspaceUnresolvedCount,
        int reviewItemCount)
    {
        Assert.True(ProductWorkspaceReviewShortcutPolicy.CanOpen(
            workspaceUnresolvedCount,
            reviewItemCount,
            reviewAvailable: true));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 2, true)]
    [InlineData(1, 1, false)]
    [InlineData(-1, -1, true)]
    public void EmptyMismatchedUnavailableOrInvalidCountsFailClosed(
        int workspaceUnresolvedCount,
        int reviewItemCount,
        bool reviewAvailable)
    {
        Assert.False(ProductWorkspaceReviewShortcutPolicy.CanOpen(
            workspaceUnresolvedCount,
            reviewItemCount,
            reviewAvailable));
    }
}
