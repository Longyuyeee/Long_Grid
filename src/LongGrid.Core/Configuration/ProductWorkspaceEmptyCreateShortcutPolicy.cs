namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceEmptyCreateShortcutStatus
{
    Invalid,
    Unavailable,
    Available,
}

public sealed record ProductWorkspaceEmptyCreateShortcutDecision(
    ProductWorkspaceEmptyCreateShortcutStatus Status)
{
    public bool CanOpen =>
        Status == ProductWorkspaceEmptyCreateShortcutStatus.Available;
}

public static class ProductWorkspaceEmptyCreateShortcutPolicy
{
    public static ProductWorkspaceEmptyCreateShortcutDecision Evaluate(
        bool isKnownEmptyWorkspace,
        int readContainerCount,
        bool canCreateContainer,
        int editorCandidateCount)
    {
        if (readContainerCount < 0 || editorCandidateCount < 0)
        {
            return new(ProductWorkspaceEmptyCreateShortcutStatus.Invalid);
        }

        bool available = isKnownEmptyWorkspace
            && readContainerCount == 0
            && canCreateContainer
            && editorCandidateCount == 0;
        return new(available
            ? ProductWorkspaceEmptyCreateShortcutStatus.Available
            : ProductWorkspaceEmptyCreateShortcutStatus.Unavailable);
    }
}
