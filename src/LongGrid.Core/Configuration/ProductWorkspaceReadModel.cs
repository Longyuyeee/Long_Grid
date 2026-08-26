namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceReadItem(
    int Ordinal,
    string? UserVisibleName,
    ConfigurationItemKind Kind,
    ProductItemReferenceResolution Resolution);

public enum ProductWorkspaceContainerHealth
{
    Empty,
    Ready,
    NeedsReview,
}

public enum ProductWorkspaceContainerHealthFilter
{
    Invalid,
    All,
    NeedsReview,
    Empty,
    Ready,
}

public static class ProductWorkspaceContainerHealthFilterPolicy
{
    public static bool IsSupported(ProductWorkspaceContainerHealthFilter filter) =>
        filter is ProductWorkspaceContainerHealthFilter.All
            or ProductWorkspaceContainerHealthFilter.NeedsReview
            or ProductWorkspaceContainerHealthFilter.Empty
            or ProductWorkspaceContainerHealthFilter.Ready;

    public static bool Includes(
        ProductWorkspaceContainerHealthFilter filter,
        ProductWorkspaceContainerHealth health) =>
        filter switch
        {
            ProductWorkspaceContainerHealthFilter.All => true,
            ProductWorkspaceContainerHealthFilter.NeedsReview =>
                health == ProductWorkspaceContainerHealth.NeedsReview,
            ProductWorkspaceContainerHealthFilter.Empty =>
                health == ProductWorkspaceContainerHealth.Empty,
            ProductWorkspaceContainerHealthFilter.Ready =>
                health == ProductWorkspaceContainerHealth.Ready,
            _ => false,
        };
}

public sealed record ProductWorkspaceReadContainer(
    int Ordinal,
    string UserVisibleName,
    bool IsLocked,
    string Color,
    double Opacity,
    bool IsCollapsed,
    double XDip,
    double YDip,
    double WidthDip,
    double HeightDip,
    ProductContainerTitleVisibilityPolicy TitleVisibility,
    ProductContainerTitleDoubleClickAction TitleDoubleClickAction,
    IReadOnlyList<ProductWorkspaceReadItem> Items,
    int ResolvedCount,
    int UnresolvedCount,
    ProductWorkspaceContainerHealth Health,
    ProductContainerFolderBindingResolution? FolderBindingResolution = null);

public sealed record ProductWorkspaceReadSnapshot(
    IReadOnlyList<ProductWorkspaceReadContainer> Containers,
    int ItemCount,
    int ResolvedCount,
    int UnresolvedCount,
    int EmptyContainerCount,
    int NeedsReviewContainerCount);

public sealed record ProductWorkspaceReadResult(
    ProductWorkspaceProjectionError Error,
    ProductConfigurationError ConfigurationError,
    ProductWorkspaceReadSnapshot? Snapshot)
{
    public bool IsSuccess =>
        Error == ProductWorkspaceProjectionError.None && Snapshot is not null;
}

public static class ProductWorkspaceReadModel
{
    public static ProductWorkspaceReadResult Create(ProductWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ProductWorkspaceProjectionResult validation =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!validation.IsSuccess)
        {
            return new(validation.Error, validation.ConfigurationError, null);
        }

        var containers = new List<ProductWorkspaceReadContainer>(
            state.Containers.Count);
        int resolvedTotal = 0;
        int unresolvedTotal = 0;
        int emptyContainerTotal = 0;
        int needsReviewContainerTotal = 0;
        for (int containerIndex = 0; containerIndex < state.Containers.Count; containerIndex++)
        {
            ProductContainerState container = state.Containers[containerIndex];
            var items = new List<ProductWorkspaceReadItem>(container.Items.Count);
            int resolved = 0;
            for (int itemIndex = 0; itemIndex < container.Items.Count; itemIndex++)
            {
                ProductItemReferenceState item = container.Items[itemIndex];
                bool isResolved =
                    item.Resolution == ProductItemReferenceResolution.Resolved;
                if (isResolved)
                {
                    resolved++;
                }

                items.Add(new(
                    itemIndex + 1,
                    isResolved ? item.CatalogEntry!.DisplayName : null,
                    isResolved
                        ? ProductWorkspaceIdentityPolicy.MapKind(
                            item.CatalogEntry!.Kind)
                        : item.PersistedKind,
                    item.Resolution));
            }

            int unresolved = items.Count - resolved;
            ProductContainerFolderBindingState? folderBinding =
                container.FolderBinding;
            bool folderNeedsReview = folderBinding is not null
                && folderBinding.Resolution !=
                    ProductContainerFolderBindingResolution.Resolved;
            ProductWorkspaceContainerHealth health = unresolved > 0
                || folderNeedsReview
                ? ProductWorkspaceContainerHealth.NeedsReview
                : items.Count == 0 && folderBinding is null
                    ? ProductWorkspaceContainerHealth.Empty
                    : ProductWorkspaceContainerHealth.Ready;
            if (health == ProductWorkspaceContainerHealth.Empty)
            {
                emptyContainerTotal++;
            }
            else if (health == ProductWorkspaceContainerHealth.NeedsReview)
            {
                needsReviewContainerTotal++;
            }

            resolvedTotal += resolved;
            unresolvedTotal += unresolved;
            containers.Add(new(
                containerIndex + 1,
                container.Name,
                container.IsLocked,
                container.Appearance.Color,
                container.Appearance.Opacity,
                container.Appearance.Collapsed,
                container.Placement.XDip,
                container.Placement.YDip,
                container.Placement.WidthDip,
                container.Placement.HeightDip,
                container.Appearance.TitleVisibility,
                container.Appearance.TitleDoubleClickAction,
                items.ToArray(),
                resolved,
                unresolved,
                health,
                folderBinding?.Resolution));
        }

        return new(
            ProductWorkspaceProjectionError.None,
            ProductConfigurationError.None,
            new(
                containers.ToArray(),
                resolvedTotal + unresolvedTotal,
                resolvedTotal,
                unresolvedTotal,
                emptyContainerTotal,
                needsReviewContainerTotal));
    }
}
