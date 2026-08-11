using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerQuickCollapsePolicyTests
{
    [Fact]
    public void AlignedUnlockedSnapshotsResolveCandidateAndNextState()
    {
        ProductWorkspaceContainerQuickCollapseDecision decision =
            ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
                requestedOrdinal: 2,
                workspaceStates:
                [
                    State(1, collapsed: true),
                    State(2, collapsed: false),
                ],
                candidateStates:
                [
                    State(3, collapsed: false),
                    State(2, collapsed: false),
                ]);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.CandidateIndex);
        Assert.True(decision.NextCollapsed);
    }

    [Fact]
    public void AlignedCollapsedSnapshotResolvesExpand()
    {
        ProductWorkspaceContainerQuickCollapseDecision decision =
            ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
                requestedOrdinal: 1,
                workspaceStates: [State(1, collapsed: true)],
                candidateStates: [State(1, collapsed: true)]);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.NextCollapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidOrdinalFailsClosed(int requestedOrdinal)
    {
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal,
            workspaceStates: [State(1)],
            candidateStates: [State(1)]).IsAllowed);
    }

    [Fact]
    public void MissingOrDuplicateWorkspaceStateFailsClosed()
    {
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(1)],
            candidateStates: [State(2)]).IsAllowed);
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(2), State(2)],
            candidateStates: [State(2)]).IsAllowed);
    }

    [Fact]
    public void MissingOrDuplicateCandidateStateFailsClosed()
    {
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal: 2,
            workspaceStates: [State(2)],
            candidateStates: [State(1)]).IsAllowed);
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
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
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal: 1,
            workspaceStates: [State(1, locked: workspaceLocked)],
            candidateStates: [State(1, locked: candidateLocked)]).IsAllowed);
    }

    [Fact]
    public void CollapsedStateMismatchFailsClosed()
    {
        Assert.False(ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
            requestedOrdinal: 1,
            workspaceStates: [State(1, collapsed: false)],
            candidateStates: [State(1, collapsed: true)]).IsAllowed);
    }

    [Fact]
    public void NullSnapshotsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
                requestedOrdinal: 1,
                workspaceStates: null!,
                candidateStates: Array.Empty<
                    ProductWorkspaceContainerQuickCollapseState>()));
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerQuickCollapsePolicy.Resolve(
                requestedOrdinal: 1,
                workspaceStates: Array.Empty<
                    ProductWorkspaceContainerQuickCollapseState>(),
                candidateStates: null!));
    }

    private static ProductWorkspaceContainerQuickCollapseState State(
        int ordinal,
        bool locked = false,
        bool collapsed = false) =>
        new(ordinal, locked, collapsed);
}
