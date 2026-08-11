namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceContainerQuickCollapseState(
    int Ordinal,
    bool IsLocked,
    bool IsCollapsed);

public sealed record ProductWorkspaceContainerQuickCollapseDecision(
    int CandidateIndex,
    bool NextCollapsed)
{
    public bool IsAllowed => CandidateIndex >= 0;

    public static ProductWorkspaceContainerQuickCollapseDecision Unavailable { get; } =
        new(-1, NextCollapsed: false);
}

public static class ProductWorkspaceContainerQuickCollapsePolicy
{
    public static ProductWorkspaceContainerQuickCollapseDecision Resolve(
        int requestedOrdinal,
        IReadOnlyList<ProductWorkspaceContainerQuickCollapseState> workspaceStates,
        IReadOnlyList<ProductWorkspaceContainerQuickCollapseState> candidateStates)
    {
        ArgumentNullException.ThrowIfNull(workspaceStates);
        ArgumentNullException.ThrowIfNull(candidateStates);
        if (requestedOrdinal <= 0)
        {
            return ProductWorkspaceContainerQuickCollapseDecision.Unavailable;
        }

        ProductWorkspaceContainerQuickCollapseState? workspace = null;
        foreach (ProductWorkspaceContainerQuickCollapseState state in workspaceStates)
        {
            if (state.Ordinal != requestedOrdinal)
            {
                continue;
            }

            if (workspace is not null)
            {
                return ProductWorkspaceContainerQuickCollapseDecision.Unavailable;
            }

            workspace = state;
        }

        ProductWorkspaceContainerQuickCollapseState? candidate = null;
        int candidateIndex = -1;
        for (int index = 0; index < candidateStates.Count; index++)
        {
            ProductWorkspaceContainerQuickCollapseState state = candidateStates[index];
            if (state.Ordinal != requestedOrdinal)
            {
                continue;
            }

            if (candidate is not null)
            {
                return ProductWorkspaceContainerQuickCollapseDecision.Unavailable;
            }

            candidate = state;
            candidateIndex = index;
        }

        if (workspace is null
            || candidate is null
            || workspace.IsLocked
            || candidate.IsLocked
            || workspace.IsCollapsed != candidate.IsCollapsed)
        {
            return ProductWorkspaceContainerQuickCollapseDecision.Unavailable;
        }

        return new(candidateIndex, !workspace.IsCollapsed);
    }
}
