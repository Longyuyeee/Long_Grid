namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceSelectedFolderBindingPathStatus
{
    Available,
    UnavailableState,
    StaleEditRevision,
    InvalidOrdinal,
    Unbound,
    InvalidPath,
}

public sealed record ProductWorkspaceSelectedFolderBindingPathResult(
    ProductWorkspaceSelectedFolderBindingPathStatus Status,
    string? DisplayPath)
{
    public bool IsAvailable =>
        Status == ProductWorkspaceSelectedFolderBindingPathStatus.Available
        && DisplayPath is not null;
}

public static class ProductWorkspaceSelectedFolderBindingPathPolicy
{
    public static ProductWorkspaceSelectedFolderBindingPathResult Resolve(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long requestedEditRevision,
        int containerOrdinal)
    {
        if (state is null)
        {
            return Unavailable(
                ProductWorkspaceSelectedFolderBindingPathStatus.UnavailableState);
        }

        if (requestedEditRevision != currentEditRevision)
        {
            return Unavailable(
                ProductWorkspaceSelectedFolderBindingPathStatus.StaleEditRevision);
        }

        if (containerOrdinal <= 0 || containerOrdinal > state.Containers.Count)
        {
            return Unavailable(
                ProductWorkspaceSelectedFolderBindingPathStatus.InvalidOrdinal);
        }

        ProductContainerFolderBindingState? binding =
            state.Containers[containerOrdinal - 1].FolderBinding;
        if (binding is null)
        {
            return Unavailable(
                ProductWorkspaceSelectedFolderBindingPathStatus.Unbound);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(binding.PersistedTarget)
                || binding.PersistedTarget.Any(char.IsControl))
            {
                return Unavailable(
                    ProductWorkspaceSelectedFolderBindingPathStatus.InvalidPath);
            }

            string path = Path.GetFullPath(binding.PersistedTarget);
            return Path.IsPathFullyQualified(path)
                ? new(
                    ProductWorkspaceSelectedFolderBindingPathStatus.Available,
                    path)
                : Unavailable(
                    ProductWorkspaceSelectedFolderBindingPathStatus.InvalidPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Unavailable(
                ProductWorkspaceSelectedFolderBindingPathStatus.InvalidPath);
        }
    }

    private static ProductWorkspaceSelectedFolderBindingPathResult Unavailable(
        ProductWorkspaceSelectedFolderBindingPathStatus status) =>
        new(status, null);
}
