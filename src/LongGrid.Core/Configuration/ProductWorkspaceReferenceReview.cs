using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceReferenceReviewError
{
    None,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceReviewToken(
    long CatalogGeneration,
    long EditRevision,
    string ContainerId,
    string ItemId,
    ProductItemReferenceResolution ExpectedResolution);

public sealed record ProductWorkspaceReferenceReviewItem(
    int Ordinal,
    ProductItemReferenceResolution Resolution,
    bool ContainerLocked,
    ProductWorkspaceReferenceReviewToken Token);

public sealed record ProductWorkspaceReferenceReviewSnapshot(
    long CatalogGeneration,
    long EditRevision,
    IReadOnlyList<ProductWorkspaceReferenceReviewItem> Items);

public sealed record ProductWorkspaceReferenceReviewResult(
    ProductWorkspaceReferenceReviewError Error,
    ProductWorkspaceProjectionError ProjectionError,
    ProductConfigurationError ConfigurationError,
    ProductWorkspaceReferenceReviewSnapshot? Snapshot)
{
    public bool IsSuccess =>
        Error == ProductWorkspaceReferenceReviewError.None
        && Snapshot is not null;
}

public static class ProductWorkspaceReferenceReview
{
    public static ProductWorkspaceReferenceReviewResult Create(
        ProductWorkspaceState state,
        long catalogGeneration,
        long editRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (catalogGeneration <= 0 || editRevision < 0)
        {
            return Invalid();
        }

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            return new(
                ProductWorkspaceReferenceReviewError.InvalidState,
                projection.Error,
                projection.ConfigurationError,
                null);
        }

        var items = new List<ProductWorkspaceReferenceReviewItem>();
        foreach (ProductContainerState container in state.Containers)
        {
            foreach (ProductItemReferenceState item in container.Items)
            {
                if (item.Resolution == ProductItemReferenceResolution.Resolved)
                {
                    continue;
                }

                int ordinal = items.Count + 1;
                items.Add(new(
                    ordinal,
                    item.Resolution,
                    container.IsLocked,
                    new(
                        catalogGeneration,
                        editRevision,
                        container.Id,
                        item.Id,
                        item.Resolution)));
            }
        }

        return new(
            ProductWorkspaceReferenceReviewError.None,
            ProductWorkspaceProjectionError.None,
            ProductConfigurationError.None,
            new(catalogGeneration, editRevision, items));
    }

    private static ProductWorkspaceReferenceReviewResult Invalid() =>
        new(
            ProductWorkspaceReferenceReviewError.InvalidState,
            ProductWorkspaceProjectionError.InvalidState,
            ProductConfigurationError.None,
            null);
}

public enum ProductWorkspaceReferenceAction
{
    Keep,
    Replace,
    Remove,
}

public enum ProductWorkspaceReferenceGateError
{
    None,
    InvalidState,
    StaleCatalogGeneration,
    StaleEditRevision,
    ItemChanged,
    ContainerLocked,
    ConfirmationRequired,
    ReplacementRequired,
    ReplacementNotFound,
    ReplacementAmbiguous,
    ReducerRejected,
}

public sealed record ProductWorkspaceReferenceActionRequest(
    ProductWorkspaceReferenceReviewToken Token,
    ProductWorkspaceReferenceAction Action,
    bool Confirmed = false,
    DesktopCatalogEntry? Replacement = null);

public sealed record ProductWorkspaceReferenceGateResult(
    ProductWorkspaceReferenceGateError Error,
    ProductWorkspaceEditResult? Preview,
    bool WouldChange)
{
    public bool IsSuccess => Error == ProductWorkspaceReferenceGateError.None;
}

public static class ProductWorkspaceReferenceGate
{
    public static ProductWorkspaceReferenceGateResult Evaluate(
        ProductWorkspaceState state,
        long currentCatalogGeneration,
        long currentEditRevision,
        IReadOnlyList<DesktopCatalogEntry> currentCatalog,
        ProductWorkspaceReferenceActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(currentCatalog);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Token);

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess || currentCatalogGeneration <= 0
            || currentEditRevision < 0)
        {
            return Failure(ProductWorkspaceReferenceGateError.InvalidState);
        }

        if (request.Token.CatalogGeneration != currentCatalogGeneration)
        {
            return Failure(
                ProductWorkspaceReferenceGateError.StaleCatalogGeneration);
        }

        if (request.Token.EditRevision != currentEditRevision)
        {
            return Failure(ProductWorkspaceReferenceGateError.StaleEditRevision);
        }

        ProductContainerState? container = state.Containers.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                request.Token.ContainerId,
                StringComparison.Ordinal));
        ProductItemReferenceState? item = container?.Items.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                request.Token.ItemId,
                StringComparison.Ordinal));
        if (container is null || item is null
            || item.Resolution == ProductItemReferenceResolution.Resolved
            || item.Resolution != request.Token.ExpectedResolution)
        {
            return Failure(ProductWorkspaceReferenceGateError.ItemChanged);
        }

        if (container.IsLocked)
        {
            return Failure(ProductWorkspaceReferenceGateError.ContainerLocked);
        }

        return request.Action switch
        {
            ProductWorkspaceReferenceAction.Keep =>
                new(ProductWorkspaceReferenceGateError.None, null, WouldChange: false),
            ProductWorkspaceReferenceAction.Remove =>
                PreviewRemove(state, request),
            ProductWorkspaceReferenceAction.Replace =>
                PreviewReplacement(state, currentCatalog, request),
            _ => Failure(ProductWorkspaceReferenceGateError.InvalidState),
        };
    }

    private static ProductWorkspaceReferenceGateResult PreviewRemove(
        ProductWorkspaceState state,
        ProductWorkspaceReferenceActionRequest request)
    {
        if (!request.Confirmed)
        {
            return Failure(
                ProductWorkspaceReferenceGateError.ConfirmationRequired);
        }

        ProductWorkspaceEditResult edit =
            ProductWorkspaceReducer.RemoveReference(
                state,
                request.Token.ContainerId,
                request.Token.ItemId,
                confirmUnresolvedReference: true);
        return FromEdit(edit);
    }

    private static ProductWorkspaceReferenceGateResult PreviewReplacement(
        ProductWorkspaceState state,
        IReadOnlyList<DesktopCatalogEntry> currentCatalog,
        ProductWorkspaceReferenceActionRequest request)
    {
        if (request.Replacement is null)
        {
            return Failure(ProductWorkspaceReferenceGateError.ReplacementRequired);
        }

        DesktopCatalogEntry[] matches = currentCatalog
            .Where(candidate => SameIdentity(candidate, request.Replacement))
            .ToArray();
        if (matches.Length == 0)
        {
            return Failure(ProductWorkspaceReferenceGateError.ReplacementNotFound);
        }

        if (matches.Length > 1)
        {
            return Failure(ProductWorkspaceReferenceGateError.ReplacementAmbiguous);
        }

        ProductWorkspaceEditResult edit =
            ProductWorkspaceReducer.ReplaceReference(
                state,
                request.Token.ContainerId,
                request.Token.ItemId,
                matches[0]);
        return FromEdit(edit);
    }

    private static bool SameIdentity(
        DesktopCatalogEntry left,
        DesktopCatalogEntry right)
    {
        DesktopItemIdentity? leftIdentity = left.Identity;
        DesktopItemIdentity? rightIdentity = right.Identity;
        return leftIdentity is not null
            && rightIdentity is not null
            && left.Kind == right.Kind
            && string.Equals(
                leftIdentity.Provider,
                rightIdentity.Provider,
                StringComparison.OrdinalIgnoreCase)
            && ProductWorkspaceIdentityPolicy.TryNormalizeCanonicalTarget(
                leftIdentity.CanonicalTarget,
                out string? leftTarget)
            && ProductWorkspaceIdentityPolicy.TryNormalizeCanonicalTarget(
                rightIdentity.CanonicalTarget,
                out string? rightTarget)
            && string.Equals(
                leftTarget,
                rightTarget,
                StringComparison.OrdinalIgnoreCase);
    }

    private static ProductWorkspaceReferenceGateResult FromEdit(
        ProductWorkspaceEditResult edit) =>
        edit.IsSuccess
            ? new(
                ProductWorkspaceReferenceGateError.None,
                edit,
                WouldChange: edit.Changed)
            : new(
                edit.Error == ProductWorkspaceEditError.ContainerLocked
                    ? ProductWorkspaceReferenceGateError.ContainerLocked
                    : ProductWorkspaceReferenceGateError.ReducerRejected,
                edit,
                WouldChange: false);

    private static ProductWorkspaceReferenceGateResult Failure(
        ProductWorkspaceReferenceGateError error) =>
        new(error, null, WouldChange: false);
}
