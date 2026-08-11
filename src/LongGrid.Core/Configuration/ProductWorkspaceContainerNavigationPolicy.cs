namespace LongGrid.Core.Configuration;

public static class ProductWorkspaceContainerNavigationPolicy
{
    public static int ResolveCandidateIndex(
        int requestedOrdinal,
        IReadOnlyList<int> workspaceOrdinals,
        IReadOnlyList<int> candidateOrdinals)
    {
        ArgumentNullException.ThrowIfNull(workspaceOrdinals);
        ArgumentNullException.ThrowIfNull(candidateOrdinals);
        if (requestedOrdinal <= 0
            || workspaceOrdinals.Count(value => value == requestedOrdinal) != 1)
        {
            return -1;
        }

        int resolvedIndex = -1;
        for (int index = 0; index < candidateOrdinals.Count; index++)
        {
            if (candidateOrdinals[index] != requestedOrdinal)
            {
                continue;
            }

            if (resolvedIndex >= 0)
            {
                return -1;
            }

            resolvedIndex = index;
        }

        return resolvedIndex;
    }
}
