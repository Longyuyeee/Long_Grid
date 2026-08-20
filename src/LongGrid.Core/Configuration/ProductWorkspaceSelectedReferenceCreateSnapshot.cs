namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceSelectedReferenceCreateSnapshotStatus
{
    Ready,
    InvalidRequest,
    SourceNotFound,
    SourceLocked,
    ItemNotFound,
    ItemUnresolved,
    SelectionChanged,
}

public sealed record ProductWorkspaceSelectedReferenceCreateSnapshot(
    int SourceContainerOrdinal,
    IReadOnlyList<string> ItemIds,
    string ConfigurationFingerprint)
{
    public const int MaximumItemCount = 256;
}

public sealed record ProductWorkspaceSelectedReferenceCreateSnapshotResult(
    ProductWorkspaceSelectedReferenceCreateSnapshotStatus Status,
    ProductWorkspaceSelectedReferenceCreateSnapshot? Snapshot)
{
    public bool IsReady =>
        Status == ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready
        && Snapshot is not null;
}

public static class ProductWorkspaceSelectedReferenceCreateSnapshots
{
    public static bool HasValidShape(
        ProductWorkspaceSelectedReferenceCreateSnapshot? snapshot) =>
        snapshot is not null
        && snapshot.SourceContainerOrdinal > 0
        && snapshot.ItemIds is { } itemIds
        && itemIds.Count is > 0 and <=
            ProductWorkspaceSelectedReferenceCreateSnapshot.MaximumItemCount
        && !itemIds.Any(string.IsNullOrWhiteSpace)
        && itemIds.Distinct(StringComparer.Ordinal).Count() == itemIds.Count
        && snapshot.ConfigurationFingerprint is { Length: 64 } fingerprint
        && fingerprint.All(Uri.IsHexDigit);

    public static ProductWorkspaceSelectedReferenceCreateSnapshotResult Capture(
        ProductWorkspaceState? state,
        int sourceContainerOrdinal,
        IReadOnlyList<int>? itemOrdinals)
    {
        if (state is null
            || itemOrdinals is null
            || sourceContainerOrdinal <= 0
            || itemOrdinals.Count is <= 0 or >
                ProductWorkspaceSelectedReferenceCreateSnapshot.MaximumItemCount
            || itemOrdinals.Any(ordinal => ordinal <= 0)
            || itemOrdinals.Distinct().Count() != itemOrdinals.Count)
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.InvalidRequest);
        }

        if (sourceContainerOrdinal > state.Containers.Count)
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SourceNotFound);
        }

        ProductContainerState source = state.Containers[sourceContainerOrdinal - 1];
        if (source.IsLocked)
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SourceLocked);
        }
        if (itemOrdinals.Any(ordinal => ordinal > source.Items.Count))
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.ItemNotFound);
        }

        ProductItemReferenceState[] selected = itemOrdinals
            .Select(ordinal => source.Items[ordinal - 1])
            .ToArray();
        if (selected.Any(item => item.Resolution !=
            ProductItemReferenceResolution.Resolved))
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.ItemUnresolved);
        }

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            return Failure(
                ProductWorkspaceSelectedReferenceCreateSnapshotStatus.InvalidRequest);
        }

        return new(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready,
            new(
                sourceContainerOrdinal,
                selected.Select(item => item.Id).ToArray(),
                ProductWorkspaceConfigurationFingerprint.Compute(
                    projection.Document!)));
    }

    public static ProductWorkspaceSelectedReferenceCreateSnapshotStatus Evaluate(
        ProductWorkspaceSelectedReferenceCreateSnapshot? snapshot,
        ProductWorkspaceState? state)
    {
        if (!HasValidShape(snapshot) || state is null)
        {
            return ProductWorkspaceSelectedReferenceCreateSnapshotStatus.InvalidRequest;
        }

        ProductWorkspaceSelectedReferenceCreateSnapshot current = snapshot!;

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess
            || !string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(projection.Document!),
                current.ConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return ProductWorkspaceSelectedReferenceCreateSnapshotStatus
                .SelectionChanged;
        }
        if (current.SourceContainerOrdinal > state.Containers.Count)
        {
            return ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SourceNotFound;
        }

        ProductContainerState source =
            state.Containers[current.SourceContainerOrdinal - 1];
        if (source.IsLocked)
        {
            return ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SourceLocked;
        }
        var selectedIds = current.ItemIds.ToHashSet(StringComparer.Ordinal);
        ProductItemReferenceState[] selected = source.Items
            .Where(item => selectedIds.Contains(item.Id))
            .ToArray();
        if (selected.Length != selectedIds.Count)
        {
            return ProductWorkspaceSelectedReferenceCreateSnapshotStatus.ItemNotFound;
        }
        return selected.Any(item => item.Resolution !=
            ProductItemReferenceResolution.Resolved)
                ? ProductWorkspaceSelectedReferenceCreateSnapshotStatus.ItemUnresolved
                : ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready;
    }

    private static ProductWorkspaceSelectedReferenceCreateSnapshotResult Failure(
        ProductWorkspaceSelectedReferenceCreateSnapshotStatus status) =>
        new(status, null);
}
