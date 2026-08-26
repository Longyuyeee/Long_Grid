using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public sealed record ProductDesktopReferenceReassignmentRequest(
    string SourceContainerId,
    IReadOnlyList<string> ItemIds,
    string TargetContainerId,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    bool SourceAttested,
    bool IsInjected);

internal enum ProductDesktopReferenceReassignmentAdmissionStatus
{
    Accepted,
    InvalidRequest,
    StaleAuthority,
    ContainerUnavailable,
    ContainerLocked,
    TemporaryFolderItem,
    ReferenceUnavailable,
}

internal sealed record ProductDesktopReferenceReassignmentPreparation(
    ProductDesktopReferenceReassignmentAdmissionStatus Status,
    ProductWorkspaceResolvedReferenceReassignmentCommitRequest? CommitRequest)
{
    internal bool IsAccepted =>
        Status == ProductDesktopReferenceReassignmentAdmissionStatus.Accepted
        && CommitRequest is not null;
}

internal static class ProductDesktopReferenceReassignmentAdmissionAdapter
{
    internal static ProductDesktopReferenceReassignmentPreparation Prepare(
        ProductWorkspaceState state,
        long currentEditRevision,
        long currentTopologyGeneration,
        ProductDesktopReferenceReassignmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.SourceAttested
            || request.IsInjected
            || request.ItemIds.Count is < 1
                or > ProductWorkspaceCommitCoordinator
                    .MaximumResolvedReferenceReassignmentBatchSize
            || request.ItemIds.Any(string.IsNullOrWhiteSpace)
            || request.ItemIds.Distinct(StringComparer.Ordinal).Count()
                != request.ItemIds.Count)
        {
            return Failure(
                ProductDesktopReferenceReassignmentAdmissionStatus.InvalidRequest);
        }
        if (request.WorkspaceRevision != currentEditRevision
            || request.TopologyGeneration != currentTopologyGeneration)
        {
            return Failure(
                ProductDesktopReferenceReassignmentAdmissionStatus.StaleAuthority);
        }

        int sourceIndex = FindContainer(state, request.SourceContainerId);
        int targetIndex = FindContainer(state, request.TargetContainerId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex
            || !string.Equals(
                state.Containers[targetIndex].Placement.DisplayKey,
                request.DisplayId,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductDesktopReferenceReassignmentAdmissionStatus
                    .ContainerUnavailable);
        }
        if (state.Containers[sourceIndex].IsLocked
            || state.Containers[targetIndex].IsLocked)
        {
            return Failure(
                ProductDesktopReferenceReassignmentAdmissionStatus.ContainerLocked);
        }

        var ordinals = new List<int>(request.ItemIds.Count);
        foreach (string itemId in request.ItemIds)
        {
            const string prefix = "item:";
            if (!itemId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Failure(
                    ProductDesktopReferenceReassignmentAdmissionStatus
                        .TemporaryFolderItem);
            }
            if (!int.TryParse(
                    itemId.AsSpan(prefix.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int ordinal)
                || ordinal <= 0
                || ordinal > state.Containers[sourceIndex].Items.Count
                || state.Containers[sourceIndex].Items[ordinal - 1].Resolution
                    != ProductItemReferenceResolution.Resolved)
            {
                return Failure(
                    ProductDesktopReferenceReassignmentAdmissionStatus
                        .ReferenceUnavailable);
            }
            ordinals.Add(ordinal);
        }
        if (ordinals.Distinct().Count() != ordinals.Count)
        {
            return Failure(
                ProductDesktopReferenceReassignmentAdmissionStatus.InvalidRequest);
        }

        return new(
            ProductDesktopReferenceReassignmentAdmissionStatus.Accepted,
            new(
                request.WorkspaceRevision,
                sourceIndex + 1,
                ordinals,
                targetIndex + 1));
    }

    private static int FindContainer(
        ProductWorkspaceState state,
        string containerId) => state.Containers
        .Select((container, index) => new { container.Id, index })
        .Where(entry => string.Equals(
            entry.Id,
            containerId,
            StringComparison.Ordinal))
        .Select(entry => entry.index)
        .SingleOrDefault(-1);

    private static ProductDesktopReferenceReassignmentPreparation Failure(
        ProductDesktopReferenceReassignmentAdmissionStatus status) =>
        new(status, null);
}

internal sealed record ProductDesktopReferenceReassignmentSurfaceInput(
    string SourceContainerId,
    IReadOnlyList<string> ItemIds,
    int PointerScreenX,
    int PointerScreenY,
    long WorkspaceRevision,
    long TopologyGeneration,
    bool SourceAttested,
    bool IsInjected);

internal sealed record ProductDesktopReferenceReassignmentSession(
    string DisplayId,
    string SourceContainerId,
    IReadOnlyList<string> ItemIds,
    Guid LeaseIntentId,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    long SelectionRevision,
    int StartX,
    int StartY,
    int CurrentX,
    int CurrentY,
    bool DragThresholdReached,
    string? HoveredTargetContainerId);

internal static class ProductDesktopReferenceReassignmentAdapter
{
    internal const int MaximumItemCount = 256;
    private const int DragThresholdDip = 6;

    internal static ProductDesktopReferenceReassignmentSession? TryStart(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (transaction?.IsExplicit != true
            || transaction.Admission.Lease is not { } lease
            || transaction.Selection is not { } selection
            || selection.SelectedItemIds.Count is < 1 or > MaximumItemCount)
        {
            return null;
        }

        ProductDesktopPointerSelectionCommand? hit =
            ProductDesktopPointerSelectionAdapter.Map(
                projection,
                transaction,
                x,
                y,
                control: false,
                shift: false);
        if (hit?.Request is not
            {
                Action: ProductDesktopSelectionAction.SelectItem,
                ItemId: { } hitItemId,
            }
            || !string.Equals(hit.ContainerId, selection.ContainerId,
                StringComparison.Ordinal)
            || !selection.SelectedItemIds.Contains(
                hitItemId,
                StringComparer.Ordinal)
            || selection.SelectedItemIds.Any(itemId =>
                !selection.VisibleItemIds.Contains(itemId, StringComparer.Ordinal)))
        {
            return null;
        }

        ProductDesktopHostReadOnlyProjection? source = projection.Containers
            .SingleOrDefault(container => string.Equals(
                container.ContainerId,
                selection.ContainerId,
                StringComparison.Ordinal));
        if (source is null || source.IsLocked || source.IsCollapsed)
        {
            return null;
        }

        return new(
            projection.DisplayId,
            source.ContainerId,
            Array.AsReadOnly(selection.SelectedItemIds.ToArray()),
            lease.IntentId,
            lease.WorkspaceRevision,
            lease.TopologyGeneration,
            lease.WindowRegistryGeneration,
            selection.SelectionRevision,
            x,
            y,
            x,
            y,
            DragThresholdReached: false,
            HoveredTargetContainerId: null);
    }

    internal static ProductDesktopReferenceReassignmentSession? TryUpdate(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopReferenceReassignmentSession session,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(session);
        if (!MatchesFrozenAuthority(projection, transaction, session))
        {
            return null;
        }

        int threshold = ProductDesktopHostSurfaceLayout.ToPixels(
            DragThresholdDip,
            projection.EffectiveDpi / 96d);
        bool reached = session.DragThresholdReached
            || Math.Abs(x - session.StartX) >= threshold
            || Math.Abs(y - session.StartY) >= threshold;
        string? target = reached
            ? ResolveTarget(projection, session.SourceContainerId, x, y)
            : null;
        return session with
        {
            CurrentX = x,
            CurrentY = y,
            DragThresholdReached = reached,
            HoveredTargetContainerId = target,
        };
    }

    internal static ProductDesktopReferenceReassignmentSurfaceInput? TryComplete(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopReferenceReassignmentSession session,
        int x,
        int y)
    {
        ProductDesktopReferenceReassignmentSession? updated = TryUpdate(
            projection,
            transaction,
            session,
            x,
            y);
        if (updated?.DragThresholdReached != true)
        {
            return null;
        }

        return new(
            updated.SourceContainerId,
            updated.ItemIds,
            checked(projection.WorkArea.Left + x),
            checked(projection.WorkArea.Top + y),
            updated.WorkspaceRevision,
            updated.TopologyGeneration,
            SourceAttested: true,
            IsInjected: false);
    }

    private static string? ResolveTarget(
        ProductDesktopHostDisplayProjection projection,
        string sourceContainerId,
        int x,
        int y) => projection.Containers
        .Where(container => !container.IsLocked
            && !string.Equals(container.ContainerId, sourceContainerId,
                StringComparison.Ordinal)
            && Contains(
                ProductDesktopHostSurfaceLayout.GetContainerBounds(
                    projection,
                    container),
                x,
                y))
        .Select(container => container.ContainerId)
        .SingleOrDefault();

    private static bool MatchesFrozenAuthority(
        ProductDesktopHostDisplayProjection projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction,
        ProductDesktopReferenceReassignmentSession session)
    {
        if (transaction?.IsExplicit != true
            || transaction.Admission.Lease is not { } lease
            || transaction.Selection is not { } selection
            || !string.Equals(projection.DisplayId, session.DisplayId,
                StringComparison.Ordinal)
            || lease.IntentId != session.LeaseIntentId
            || lease.WorkspaceRevision != session.WorkspaceRevision
            || lease.TopologyGeneration != session.TopologyGeneration
            || lease.WindowRegistryGeneration != session.WindowRegistryGeneration
            || selection.SelectionRevision != session.SelectionRevision
            || !string.Equals(selection.ContainerId, session.SourceContainerId,
                StringComparison.Ordinal)
            || !selection.SelectedItemIds.SequenceEqual(
                session.ItemIds,
                StringComparer.Ordinal))
        {
            return false;
        }

        return projection.Containers.SingleOrDefault(container => string.Equals(
            container.ContainerId,
            session.SourceContainerId,
            StringComparison.Ordinal)) is { IsLocked: false, IsCollapsed: false };
    }

    private static bool Contains(PixelRect bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right
        && y >= bounds.Top && y < bounds.Bottom;
}
