using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceProjectionError
{
    None,
    InvalidState,
    UnsupportedIdentityProvider,
    InvalidCanonicalTarget,
    ConfigurationRejected,
}

public sealed record ProductWorkspaceProjectionResult(
    ProductWorkspaceProjectionError Error,
    ProductConfigurationError ConfigurationError,
    ProductConfigurationDocument? Document)
{
    public bool IsSuccess =>
        Error == ProductWorkspaceProjectionError.None && Document is not null;
}

public static class ProductWorkspaceConfigurationProjector
{
    public static ProductWorkspaceProjectionResult Project(
        ProductWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Containers is null)
        {
            return Failure(ProductWorkspaceProjectionError.InvalidState);
        }

        var containers = new List<ContainerConfiguration>(
            state.Containers.Count);
        foreach (ProductContainerState? container in state.Containers)
        {
            ProductWorkspaceProjectionResult? failure =
                TryProjectContainer(container, out ContainerConfiguration? projected);
            if (failure is not null)
            {
                return failure;
            }

            containers.Add(projected!);
        }

        ProductConfigurationDocument candidate = new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = state.ProfileId,
            Containers = containers,
            SavedDisplayTopology = state.SavedDisplayTopology?
                .ToArray(),
            ExtensionData = state.ExtensionData,
        };

        try
        {
            byte[] serialized = ProductConfigurationJson.SerializeToUtf8Bytes(candidate);
            ProductConfigurationDocument snapshot =
                ProductConfigurationJson.Deserialize(serialized);
            return new(
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                snapshot);
        }
        catch (ProductConfigurationContractException exception)
        {
            return Failure(
                ProductWorkspaceProjectionError.ConfigurationRejected,
                exception.Error);
        }
    }

    private static ProductWorkspaceProjectionResult? TryProjectContainer(
        ProductContainerState? container,
        out ContainerConfiguration? projected)
    {
        projected = null;
        if (container is null
            || container.Appearance is null
            || container.Placement is null
            || container.Items is null)
        {
            return Failure(ProductWorkspaceProjectionError.InvalidState);
        }

        var items = new List<DesktopItemReferenceConfiguration>(
            container.Items.Count);
        foreach (ProductItemReferenceState? item in container.Items)
        {
            ProductWorkspaceProjectionResult? failure =
                TryProjectItem(item, out DesktopItemReferenceConfiguration? projectedItem);
            if (failure is not null)
            {
                return failure;
            }

            items.Add(projectedItem!);
        }

        projected = new ContainerConfiguration
        {
            Id = container.Id,
            Name = container.Name,
            IsLocked = container.IsLocked,
            Appearance = new ContainerAppearanceConfiguration
            {
                Color = container.Appearance.Color,
                Opacity = container.Appearance.Opacity,
                Collapsed = container.Appearance.Collapsed,
                TitleVisibility = container.Appearance.TitleVisibility,
                TitleDoubleClickAction =
                    container.Appearance.TitleDoubleClickAction,
                ExtensionData = container.Appearance.ExtensionData,
            },
            Placement = new ContainerPlacementConfiguration
            {
                DisplayKey = container.Placement.DisplayKey,
                XDip = container.Placement.XDip,
                YDip = container.Placement.YDip,
                WidthDip = container.Placement.WidthDip,
                HeightDip = container.Placement.HeightDip,
                ExtensionData = container.Placement.ExtensionData,
            },
            Items = items,
            ExtensionData = container.ExtensionData,
        };
        return null;
    }

    private static ProductWorkspaceProjectionResult? TryProjectItem(
        ProductItemReferenceState? item,
        out DesktopItemReferenceConfiguration? projected)
    {
        projected = null;
        if (item is null || !Enum.IsDefined(item.Resolution))
        {
            return Failure(ProductWorkspaceProjectionError.InvalidState);
        }

        if (item.Resolution != ProductItemReferenceResolution.Resolved)
        {
            if (item.CatalogEntry is not null
                || !Enum.IsDefined(item.PersistedKind)
                || string.IsNullOrWhiteSpace(item.PersistedTarget))
            {
                return Failure(ProductWorkspaceProjectionError.InvalidState);
            }

            projected = new DesktopItemReferenceConfiguration
            {
                Id = item.Id,
                Kind = item.PersistedKind,
                Target = item.PersistedTarget,
                Behavior = ConfigurationItemBehavior.Reference,
                ExtensionData = item.ExtensionData,
            };
            return null;
        }

        ProductItemReferenceState resolvedItem = item!;
        DesktopCatalogEntry? entry = resolvedItem.CatalogEntry;
        DesktopItemIdentity? identity = entry?.Identity;
        if (entry is null
            || identity is null
            || string.IsNullOrWhiteSpace(entry.SourceId)
            || string.IsNullOrWhiteSpace(entry.DisplayName)
            || !Enum.IsDefined(entry.Kind)
            || !ProductWorkspaceIdentityPolicy.HasConsistentOptionalFileIdentity(
                identity))
        {
            return Failure(ProductWorkspaceProjectionError.InvalidState);
        }

        if (!ProductWorkspaceIdentityPolicy.IsSupportedProvider(identity.Provider))
        {
            return Failure(
                ProductWorkspaceProjectionError.UnsupportedIdentityProvider);
        }

        if (!ProductWorkspaceIdentityPolicy.TryNormalizeCanonicalTarget(
            identity.CanonicalTarget,
            out string? canonicalTarget))
        {
            return Failure(ProductWorkspaceProjectionError.InvalidCanonicalTarget);
        }

        projected = new DesktopItemReferenceConfiguration
        {
            Id = resolvedItem.Id,
            Kind = ProductWorkspaceIdentityPolicy.MapKind(entry.Kind),
            Target = canonicalTarget!,
            Behavior = ConfigurationItemBehavior.Reference,
            ExtensionData = resolvedItem.ExtensionData,
        };
        return null;
    }

    private static ProductWorkspaceProjectionResult Failure(
        ProductWorkspaceProjectionError error,
        ProductConfigurationError configurationError =
            ProductConfigurationError.None) =>
        new(error, configurationError, null);
}
