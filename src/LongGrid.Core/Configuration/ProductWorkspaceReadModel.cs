namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceReadItem(
    int Ordinal,
    string? UserVisibleName,
    ConfigurationItemKind Kind,
    ProductItemReferenceResolution Resolution);

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
    IReadOnlyList<ProductWorkspaceReadItem> Items,
    int ResolvedCount,
    int UnresolvedCount);

public sealed record ProductWorkspaceReadSnapshot(
    IReadOnlyList<ProductWorkspaceReadContainer> Containers,
    int ItemCount,
    int ResolvedCount,
    int UnresolvedCount);

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
                items.ToArray(),
                resolved,
                unresolved));
        }

        return new(
            ProductWorkspaceProjectionError.None,
            ProductConfigurationError.None,
            new(
                containers.ToArray(),
                resolvedTotal + unresolvedTotal,
                resolvedTotal,
                unresolvedTotal));
    }
}
