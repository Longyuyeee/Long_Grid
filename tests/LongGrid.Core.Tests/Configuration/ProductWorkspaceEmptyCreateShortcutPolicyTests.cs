using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceEmptyCreateShortcutPolicyTests
{
    [Fact]
    public void KnownEmptyEditableWorkspaceOffersShortcut()
    {
        ProductWorkspaceEmptyCreateShortcutDecision decision = Evaluate();

        Assert.True(decision.CanOpen);
        Assert.Equal(
            ProductWorkspaceEmptyCreateShortcutStatus.Available,
            decision.Status);
    }

    [Theory]
    [InlineData(false, 0, true, 0)]
    [InlineData(true, 1, true, 0)]
    [InlineData(true, 0, false, 0)]
    [InlineData(true, 0, true, 1)]
    public void UnalignedContextsDoNotOfferShortcut(
        bool knownEmpty,
        int readCount,
        bool canCreate,
        int editorCount)
    {
        ProductWorkspaceEmptyCreateShortcutDecision decision = Evaluate(
            knownEmpty,
            readCount,
            canCreate,
            editorCount);

        Assert.False(decision.CanOpen);
        Assert.Equal(
            ProductWorkspaceEmptyCreateShortcutStatus.Unavailable,
            decision.Status);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void InvalidCountsFailClosed(int readCount, int editorCount)
    {
        ProductWorkspaceEmptyCreateShortcutDecision decision = Evaluate(
            readCount: readCount,
            editorCount: editorCount);

        Assert.False(decision.CanOpen);
        Assert.Equal(
            ProductWorkspaceEmptyCreateShortcutStatus.Invalid,
            decision.Status);
    }

    private static ProductWorkspaceEmptyCreateShortcutDecision Evaluate(
        bool knownEmpty = true,
        int readCount = 0,
        bool canCreate = true,
        int editorCount = 0) =>
        ProductWorkspaceEmptyCreateShortcutPolicy.Evaluate(
            knownEmpty,
            readCount,
            canCreate,
            editorCount);
}
