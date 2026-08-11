using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerQuickLockPolicyTests
{
    [Fact]
    public void AlignedUnlockedSnapshotsResolveReorderedCandidate()
    {
        ProductWorkspaceContainerQuickLockDecision decision =
            ProductWorkspaceContainerQuickLockPolicy.Resolve(
                requestedOrdinal: 2,
                workspaceStates: [State(1), State(2)],
                candidateStates: [State(3), State(2)]);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.CandidateIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidOrdinalFailsClosed(int requestedOrdinal)
    {
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal,
            workspaceStates: [State(1)],
            candidateStates: [State(1)]).IsAllowed);
    }

    [Fact]
    public void MissingOrDuplicateWorkspaceStateFailsClosed()
    {
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(1)],
            candidateStates: [State(2)]).IsAllowed);
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(2), State(2)],
            candidateStates: [State(2)]).IsAllowed);
    }

    [Fact]
    public void MissingOrDuplicateCandidateStateFailsClosed()
    {
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(2)],
            candidateStates: [State(1)]).IsAllowed);
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(2)],
            candidateStates: [State(2), State(2)]).IsAllowed);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LockedOrLockMismatchedSnapshotsFailClosed(
        bool workspaceLocked,
        bool candidateLocked)
    {
        Assert.False(ProductWorkspaceContainerQuickLockPolicy.Resolve(
            requestedOrdinal: 1,
            workspaceStates: [State(1, workspaceLocked)],
            candidateStates: [State(1, candidateLocked)]).IsAllowed);
    }

    [Fact]
    public void NullSnapshotsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerQuickLockPolicy.Resolve(
                requestedOrdinal: 1,
                workspaceStates: null!,
                candidateStates: Array.Empty<ProductWorkspaceContainerQuickLockState>()));
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerQuickLockPolicy.Resolve(
                requestedOrdinal: 1,
                workspaceStates: Array.Empty<ProductWorkspaceContainerQuickLockState>(),
                candidateStates: null!));
    }

    private static ProductWorkspaceContainerQuickLockState State(
        int ordinal,
        bool locked = false) =>
        new(ordinal, locked);
}
