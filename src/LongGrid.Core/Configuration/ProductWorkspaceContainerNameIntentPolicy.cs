namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerNameIntentStatus
{
    Invalid,
    Unavailable,
    Empty,
    CreateReady,
    RenameUnavailable,
    RenameLocked,
    RenameNoChange,
    RenameReady,
}

public sealed record ProductWorkspaceContainerNameIntentDecision(
    ProductWorkspaceContainerNameIntentStatus Status,
    bool CanCreate,
    bool CanRename);

public static class ProductWorkspaceContainerNameIntentPolicy
{
    public static ProductWorkspaceContainerNameIntentDecision Evaluate(
        string? name,
        bool canCreate,
        bool canRename,
        string? selectedName,
        bool selectedIsLocked)
    {
        if (name is null
            || name.Length > ProductConfigurationLimits.MaximumNameLength
            || selectedName is not null
                && (string.IsNullOrWhiteSpace(selectedName)
                    || selectedName.Length >
                        ProductConfigurationLimits.MaximumNameLength))
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.Invalid,
                CanCreate: false,
                CanRename: false);
        }

        if (!canCreate && !canRename)
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.Unavailable,
                CanCreate: false,
                CanRename: false);
        }

        string normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.Empty,
                CanCreate: false,
                CanRename: false);
        }

        bool createReady = canCreate;
        if (selectedName is null)
        {
            return new(
                createReady
                    ? ProductWorkspaceContainerNameIntentStatus.CreateReady
                    : ProductWorkspaceContainerNameIntentStatus.RenameUnavailable,
                createReady,
                CanRename: false);
        }

        if (selectedIsLocked)
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.RenameLocked,
                createReady,
                CanRename: false);
        }

        if (!canRename)
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.RenameUnavailable,
                createReady,
                CanRename: false);
        }

        if (string.Equals(normalizedName, selectedName, StringComparison.Ordinal))
        {
            return new(
                ProductWorkspaceContainerNameIntentStatus.RenameNoChange,
                createReady,
                CanRename: false);
        }

        return new(
            ProductWorkspaceContainerNameIntentStatus.RenameReady,
            createReady,
            CanRename: true);
    }
}
