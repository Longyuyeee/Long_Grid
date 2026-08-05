using System.Text.Json;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceEditError
{
    None,
    InvalidState,
    ContainerNotFound,
    ItemNotFound,
    ContainerLocked,
    UnresolvedReferenceRequiresConfirmation,
    ConfigurationRejected,
}

public sealed record ProductWorkspaceEditResult(
    ProductWorkspaceEditError Error,
    ProductWorkspaceProjectionError ProjectionError,
    ProductConfigurationError ConfigurationError,
    ProductWorkspaceState? State,
    bool Changed)
{
    public bool IsSuccess =>
        Error == ProductWorkspaceEditError.None && State is not null;
}

public static class ProductWorkspaceReducer
{
    public static ProductWorkspaceEditResult CreateContainer(
        ProductWorkspaceState state,
        ProductContainerState container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return Edit(
            state,
            snapshot => snapshot with
            {
                Containers = [.. snapshot.Containers, Clone(container)],
            });
    }

    public static ProductWorkspaceEditResult RenameContainer(
        ProductWorkspaceState state,
        string containerId,
        string name) =>
        EditContainer(
            state,
            containerId,
            container => container with { Name = name });

    public static ProductWorkspaceEditResult UpdateAppearance(
        ProductWorkspaceState state,
        string containerId,
        ProductContainerAppearanceState appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return EditContainer(
            state,
            containerId,
            container => container with { Appearance = Clone(appearance) });
    }

    public static ProductWorkspaceEditResult UpdatePlacement(
        ProductWorkspaceState state,
        string containerId,
        ProductContainerPlacementState placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return EditContainer(
            state,
            containerId,
            container => container with { Placement = Clone(placement) });
    }

    public static ProductWorkspaceEditResult SetContainerLocked(
        ProductWorkspaceState state,
        string containerId,
        bool isLocked) =>
        EditContainer(
            state,
            containerId,
            container => container with { IsLocked = isLocked },
            allowLockedContainer: true);

    public static ProductWorkspaceEditResult RemoveContainer(
        ProductWorkspaceState state,
        string containerId,
        bool confirmUnresolvedReferences = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProductWorkspaceEditResult? preparation = Prepare(state, out ProductWorkspaceState? snapshot);
        if (preparation is not null)
        {
            return preparation;
        }

        int index = FindContainer(snapshot!, containerId);
        if (index < 0)
        {
            return Failure(ProductWorkspaceEditError.ContainerNotFound);
        }

        ProductContainerState container = snapshot!.Containers[index];
        if (container.IsLocked)
        {
            return Failure(ProductWorkspaceEditError.ContainerLocked);
        }

        if (!confirmUnresolvedReferences
            && container.Items.Any(item =>
                item.Resolution != ProductItemReferenceResolution.Resolved))
        {
            return Failure(
                ProductWorkspaceEditError.UnresolvedReferenceRequiresConfirmation);
        }

        ProductContainerState[] containers = snapshot.Containers
            .Where((_, containerIndex) => containerIndex != index)
            .ToArray();
        return Validate(snapshot with { Containers = containers }, changed: true);
    }

    public static ProductWorkspaceEditResult AddResolvedReference(
        ProductWorkspaceState state,
        string containerId,
        ProductItemReferenceState item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Resolution != ProductItemReferenceResolution.Resolved)
        {
            return Failure(ProductWorkspaceEditError.InvalidState);
        }

        return EditContainer(
            state,
            containerId,
            container => container with
            {
                Items = [.. container.Items, Clone(item)],
            });
    }

    public static ProductWorkspaceEditResult ReplaceReference(
        ProductWorkspaceState state,
        string containerId,
        string itemId,
        DesktopCatalogEntry catalogEntry)
    {
        ArgumentNullException.ThrowIfNull(catalogEntry);

        return EditItem(
            state,
            containerId,
            itemId,
            current => ProductItemReferenceState.CreateResolved(
                itemId,
                catalogEntry,
                CloneExtensions(current.ExtensionData)));
    }

    public static ProductWorkspaceEditResult RemoveReference(
        ProductWorkspaceState state,
        string containerId,
        string itemId,
        bool confirmUnresolvedReference = false) =>
        EditItem(
            state,
            containerId,
            itemId,
            item => item,
            remove: true,
            confirmUnresolvedReference);

    private static ProductWorkspaceEditResult EditContainer(
        ProductWorkspaceState state,
        string containerId,
        Func<ProductContainerState, ProductContainerState> update,
        bool allowLockedContainer = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProductWorkspaceEditResult? preparation = Prepare(state, out ProductWorkspaceState? snapshot);
        if (preparation is not null)
        {
            return preparation;
        }

        int index = FindContainer(snapshot!, containerId);
        if (index < 0)
        {
            return Failure(ProductWorkspaceEditError.ContainerNotFound);
        }

        ProductContainerState current = snapshot!.Containers[index];
        if (current.IsLocked && !allowLockedContainer)
        {
            return Failure(ProductWorkspaceEditError.ContainerLocked);
        }

        ProductContainerState next = update(current);
        ProductContainerState[] containers = snapshot.Containers.ToArray();
        containers[index] = next;
        bool changed = next != current;
        return Validate(snapshot with { Containers = containers }, changed);
    }

    private static ProductWorkspaceEditResult EditItem(
        ProductWorkspaceState state,
        string containerId,
        string itemId,
        Func<ProductItemReferenceState, ProductItemReferenceState> update,
        bool remove = false,
        bool confirmUnresolvedReference = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProductWorkspaceEditResult? preparation = Prepare(state, out ProductWorkspaceState? snapshot);
        if (preparation is not null)
        {
            return preparation;
        }

        int containerIndex = FindContainer(snapshot!, containerId);
        if (containerIndex < 0)
        {
            return Failure(ProductWorkspaceEditError.ContainerNotFound);
        }

        ProductContainerState container = snapshot!.Containers[containerIndex];
        if (container.IsLocked)
        {
            return Failure(ProductWorkspaceEditError.ContainerLocked);
        }

        int itemIndex = FindItem(container, itemId);
        if (itemIndex < 0)
        {
            return Failure(ProductWorkspaceEditError.ItemNotFound);
        }

        ProductItemReferenceState current = container.Items[itemIndex];
        if (remove
            && current.Resolution != ProductItemReferenceResolution.Resolved
            && !confirmUnresolvedReference)
        {
            return Failure(
                ProductWorkspaceEditError.UnresolvedReferenceRequiresConfirmation);
        }

        ProductItemReferenceState[] items = remove
            ? container.Items.Where((_, index) => index != itemIndex).ToArray()
            : container.Items
                .Select((item, index) => index == itemIndex ? update(item) : item)
                .ToArray();
        ProductContainerState[] containers = snapshot.Containers.ToArray();
        containers[containerIndex] = container with { Items = items };
        return Validate(snapshot with { Containers = containers }, changed: true);
    }

    private static ProductWorkspaceEditResult Edit(
        ProductWorkspaceState state,
        Func<ProductWorkspaceState, ProductWorkspaceState> update)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProductWorkspaceEditResult? preparation = Prepare(state, out ProductWorkspaceState? snapshot);
        return preparation ?? Validate(update(snapshot!), changed: true);
    }

    private static ProductWorkspaceEditResult? Prepare(
        ProductWorkspaceState state,
        out ProductWorkspaceState? snapshot)
    {
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            snapshot = null;
            return FromProjection(projection);
        }

        snapshot = Clone(state);
        return null;
    }

    private static ProductWorkspaceEditResult Validate(
        ProductWorkspaceState state,
        bool changed)
    {
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        return projection.IsSuccess
            ? new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                Clone(state),
                changed)
            : FromProjection(projection);
    }

    private static ProductWorkspaceEditResult FromProjection(
        ProductWorkspaceProjectionResult projection) =>
        new(
            projection.Error == ProductWorkspaceProjectionError.InvalidState
                ? ProductWorkspaceEditError.InvalidState
                : ProductWorkspaceEditError.ConfigurationRejected,
            projection.Error,
            projection.ConfigurationError,
            null,
            Changed: false);

    private static ProductWorkspaceEditResult Failure(
        ProductWorkspaceEditError error) =>
        new(
            error,
            ProductWorkspaceProjectionError.None,
            ProductConfigurationError.None,
            null,
            Changed: false);

    private static int FindContainer(ProductWorkspaceState state, string id)
    {
        for (int index = 0; index < state.Containers.Count; index++)
        {
            if (string.Equals(
                state.Containers[index].Id,
                id,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindItem(ProductContainerState container, string id)
    {
        for (int index = 0; index < container.Items.Count; index++)
        {
            if (string.Equals(
                container.Items[index].Id,
                id,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static ProductWorkspaceState Clone(ProductWorkspaceState state) =>
        state with
        {
            Containers = state.Containers.Select(Clone).ToArray(),
            ExtensionData = CloneExtensions(state.ExtensionData),
        };

    private static ProductContainerState Clone(ProductContainerState container) =>
        container with
        {
            Appearance = Clone(container.Appearance),
            Placement = Clone(container.Placement),
            Items = container.Items.Select(Clone).ToArray(),
            ExtensionData = CloneExtensions(container.ExtensionData),
        };

    private static ProductContainerAppearanceState Clone(
        ProductContainerAppearanceState appearance) =>
        appearance with
        {
            ExtensionData = CloneExtensions(appearance.ExtensionData),
        };

    private static ProductContainerPlacementState Clone(
        ProductContainerPlacementState placement) =>
        placement with
        {
            ExtensionData = CloneExtensions(placement.ExtensionData),
        };

    private static ProductItemReferenceState Clone(ProductItemReferenceState item) =>
        item.Resolution == ProductItemReferenceResolution.Resolved
            ? ProductItemReferenceState.CreateResolved(
                item.Id,
                item.CatalogEntry!,
                CloneExtensions(item.ExtensionData))
            : ProductItemReferenceState.RestoreUnresolved(
                item.Id,
                item.PersistedKind,
                item.PersistedTarget,
                item.Resolution,
                CloneExtensions(item.ExtensionData));

    private static Dictionary<string, JsonElement>? CloneExtensions(
        IDictionary<string, JsonElement>? extensions) =>
        extensions?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
}
