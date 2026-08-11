namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceContainerQuickLockState(
    int Ordinal,
    bool IsLocked);

public sealed record ProductWorkspaceContainerQuickLockDecision(int CandidateIndex)
{
    public bool IsAllowed => CandidateIndex >= 0;

    public static ProductWorkspaceContainerQuickLockDecision Unavailable { get; } = new(-1);
}

public static class ProductWorkspaceContainerQuickLockPolicy
{
    public static ProductWorkspaceContainerQuickLockDecision Resolve(
        int requestedOrdinal,
        IReadOnlyList<ProductWorkspaceContainerQuickLockState> workspaceStates,
        IReadOnlyList<ProductWorkspaceContainerQuickLockState> candidateStates)
    {
        ArgumentNullException.ThrowIfNull(workspaceStates);
        ArgumentNullException.ThrowIfNull(candidateStates);
        if (requestedOrdinal <= 0)
        {
            return ProductWorkspaceContainerQuickLockDecision.Unavailable;
        }

        ProductWorkspaceContainerQuickLockState? workspace = null;
        foreach (ProductWorkspaceContainerQuickLockState state in workspaceStates)
        {
            if (state.Ordinal != requestedOrdinal)
            {
                continue;
            }

            if (workspace is not null)
            {
                return ProductWorkspaceContainerQuickLockDecision.Unavailable;
            }

            workspace = state;
        }

        ProductWorkspaceContainerQuickLockState? candidate = null;
        int candidateIndex = -1;
        for (int index = 0; index < candidateStates.Count; index++)
        {
            ProductWorkspaceContainerQuickLockState state = candidateStates[index];
            if (state.Ordinal != requestedOrdinal)
            {
                continue;
            }

            if (candidate is not null)
            {
                return ProductWorkspaceContainerQuickLockDecision.Unavailable;
            }

            candidate = state;
            candidateIndex = index;
        }

        if (workspace is null
            || candidate is null
            || workspace.IsLocked
            || candidate.IsLocked)
        {
            return ProductWorkspaceContainerQuickLockDecision.Unavailable;
        }

        return new(candidateIndex);
    }
}
