namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopSelectionStatus
{
    Ready,
    Applied,
    InvalidModel,
    InvalidRequest,
    LeaseMismatch,
    LeaseExpired,
    VisibleItemsChanged,
    Reconciled,
}

[Flags]
public enum ProductDesktopSelectionModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
}

public enum ProductDesktopSelectionAction
{
    SelectItem,
    MovePrevious,
    MoveNext,
    MoveFirst,
    MoveLast,
    SelectAll,
    Clear,
}

public sealed record ProductDesktopSelectionRequest(
    ProductDesktopSelectionAction Action,
    ProductDesktopSelectionModifiers Modifiers =
        ProductDesktopSelectionModifiers.None,
    string? ItemId = null);

public sealed record ProductDesktopSelectionSnapshot(
    ProductDesktopSelectionStatus Status,
    Guid LeaseIntentId,
    string ContainerId,
    long WorkspaceRevision,
    long TopologyGeneration,
    long WindowRegistryGeneration,
    IReadOnlyList<string> VisibleItemIds,
    IReadOnlyList<string> SelectedItemIds,
    string? FocusedItemId,
    string? AnchorItemId,
    long SelectionRevision)
{
    public bool HasSelection => SelectedItemIds.Count > 0;
}

public sealed record ProductDesktopSelectionCreationResult(
    ProductDesktopSelectionStatus Status,
    ProductDesktopInteractionSelectionController? Controller)
{
    public bool IsCreated =>
        Status == ProductDesktopSelectionStatus.Ready
        && Controller is not null;
}

public sealed class ProductDesktopInteractionSelectionController
{
    public const int MaximumVisibleItems = 256;

    private readonly object sync = new();
    private readonly ProductDesktopInteractionLease lease;
    private string[] visibleItemIds;
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private string? focusedItemId;
    private string? anchorItemId;
    private long selectionRevision;

    private ProductDesktopInteractionSelectionController(
        ProductDesktopInteractionLease lease,
        string[] visibleItemIds)
    {
        this.lease = lease;
        this.visibleItemIds = visibleItemIds;
    }

    public ProductDesktopSelectionSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return CreateSnapshot(ProductDesktopSelectionStatus.Ready);
            }
        }
    }

    public static ProductDesktopSelectionCreationResult TryCreate(
        ProductDesktopInteractionLease lease,
        IReadOnlyList<string> visibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(visibleItemIds);
        string[] copied = visibleItemIds.ToArray();
        if (lease.IntentId == Guid.Empty
            || string.IsNullOrWhiteSpace(lease.TargetContainerId)
            || lease.WorkspaceRevision <= 0
            || lease.TopologyGeneration <= 0
            || lease.WindowRegistryGeneration <= 0
            || copied.Length > MaximumVisibleItems
            || copied.Any(string.IsNullOrWhiteSpace)
            || copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            return new(ProductDesktopSelectionStatus.InvalidModel, null);
        }

        if (lease.ExpiresAtUtc <= nowUtc)
        {
            return new(ProductDesktopSelectionStatus.LeaseExpired, null);
        }

        return new(
            ProductDesktopSelectionStatus.Ready,
            new ProductDesktopInteractionSelectionController(lease, copied));
    }

    public ProductDesktopSelectionSnapshot Apply(
        ProductDesktopInteractionLease currentLease,
        IReadOnlyList<string> currentVisibleItemIds,
        ProductDesktopSelectionRequest request,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currentLease);
        ArgumentNullException.ThrowIfNull(currentVisibleItemIds);
        ArgumentNullException.ThrowIfNull(request);
        lock (sync)
        {
            if (!LeaseMatches(currentLease))
            {
                return CreateSnapshot(
                    ProductDesktopSelectionStatus.LeaseMismatch);
            }

            if (currentLease.ExpiresAtUtc <= nowUtc)
            {
                return CreateSnapshot(
                    ProductDesktopSelectionStatus.LeaseExpired);
            }

            if (!visibleItemIds.SequenceEqual(
                    currentVisibleItemIds,
                    StringComparer.Ordinal))
            {
                return CreateSnapshot(
                    ProductDesktopSelectionStatus.VisibleItemsChanged);
            }

            if (!Enum.IsDefined(request.Action)
                || (request.Modifiers & ~(ProductDesktopSelectionModifiers
                    .Control | ProductDesktopSelectionModifiers.Shift)) != 0
                || !RequestIsValid(request))
            {
                return CreateSnapshot(
                    ProductDesktopSelectionStatus.InvalidRequest);
            }

            bool changed = request.Action switch
            {
                ProductDesktopSelectionAction.SelectItem =>
                    Select(request.ItemId, request.Modifiers),
                ProductDesktopSelectionAction.MovePrevious =>
                    Move(-1, request.Modifiers),
                ProductDesktopSelectionAction.MoveNext =>
                    Move(1, request.Modifiers),
                ProductDesktopSelectionAction.MoveFirst =>
                    MoveTo(0, request.Modifiers),
                ProductDesktopSelectionAction.MoveLast =>
                    MoveTo(visibleItemIds.Length - 1, request.Modifiers),
                ProductDesktopSelectionAction.SelectAll => SelectAll(),
                ProductDesktopSelectionAction.Clear => Clear(request),
                _ => false,
            };

            if (changed)
            {
                selectionRevision++;
            }

            return CreateSnapshot(ProductDesktopSelectionStatus.Applied);
        }
    }

    public ProductDesktopSelectionSnapshot ReconcileVisibleItems(
        ProductDesktopInteractionLease currentLease,
        IReadOnlyList<string> currentVisibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currentLease);
        ArgumentNullException.ThrowIfNull(currentVisibleItemIds);
        lock (sync)
        {
            if (!LeaseMatches(currentLease))
            {
                return CreateSnapshot(ProductDesktopSelectionStatus.LeaseMismatch);
            }

            if (currentLease.ExpiresAtUtc <= nowUtc)
            {
                return CreateSnapshot(ProductDesktopSelectionStatus.LeaseExpired);
            }

            string[] next = currentVisibleItemIds.ToArray();
            if (next.Length > MaximumVisibleItems
                || next.Any(string.IsNullOrWhiteSpace)
                || next.Distinct(StringComparer.Ordinal).Count() != next.Length)
            {
                return CreateSnapshot(ProductDesktopSelectionStatus.InvalidModel);
            }

            if (visibleItemIds.SequenceEqual(next, StringComparer.Ordinal))
            {
                return CreateSnapshot(ProductDesktopSelectionStatus.Reconciled);
            }

            visibleItemIds = next;
            selected.IntersectWith(next);
            if (focusedItemId is null || !next.Contains(
                    focusedItemId,
                    StringComparer.Ordinal))
            {
                focusedItemId = next.FirstOrDefault();
            }
            if (anchorItemId is null || !next.Contains(
                    anchorItemId,
                    StringComparer.Ordinal))
            {
                anchorItemId = focusedItemId;
            }

            selectionRevision++;

            return CreateSnapshot(ProductDesktopSelectionStatus.Reconciled);
        }
    }

    private bool RequestIsValid(ProductDesktopSelectionRequest request) =>
        request.Action == ProductDesktopSelectionAction.SelectItem
            ? IndexOf(request.ItemId) >= 0
            : request.Action is ProductDesktopSelectionAction.Clear
                or ProductDesktopSelectionAction.SelectAll
                ? request.ItemId is null
                    && request.Modifiers == ProductDesktopSelectionModifiers.None
                : request.ItemId is null;

    private bool SelectAll()
    {
        string? oldFocus = focusedItemId;
        string? oldAnchor = anchorItemId;
        string[] oldSelection = OrderedSelection();
        selected.Clear();
        selected.UnionWith(visibleItemIds);
        focusedItemId ??= visibleItemIds.FirstOrDefault();
        anchorItemId ??= focusedItemId;
        return !string.Equals(oldFocus, focusedItemId, StringComparison.Ordinal)
            || !string.Equals(oldAnchor, anchorItemId, StringComparison.Ordinal)
            || !oldSelection.SequenceEqual(
                OrderedSelection(),
                StringComparer.Ordinal);
    }

    private bool Select(
        string? itemId,
        ProductDesktopSelectionModifiers modifiers)
    {
        int index = IndexOf(itemId);
        if (index < 0)
        {
            return false;
        }

        return ApplyIndex(index, modifiers);
    }

    private bool Move(int delta, ProductDesktopSelectionModifiers modifiers)
    {
        if (visibleItemIds.Length == 0)
        {
            return false;
        }

        int current = IndexOf(focusedItemId);
        int target = current < 0
            ? delta < 0 ? visibleItemIds.Length - 1 : 0
            : Math.Clamp(current + delta, 0, visibleItemIds.Length - 1);
        return ApplyNavigationIndex(target, modifiers);
    }

    private bool MoveTo(
        int index,
        ProductDesktopSelectionModifiers modifiers) =>
        index >= 0 && index < visibleItemIds.Length
        && ApplyNavigationIndex(index, modifiers);

    private bool ApplyNavigationIndex(
        int index,
        ProductDesktopSelectionModifiers modifiers)
    {
        if (modifiers == ProductDesktopSelectionModifiers.Control)
        {
            string target = visibleItemIds[index];
            if (string.Equals(
                focusedItemId,
                target,
                StringComparison.Ordinal))
            {
                return false;
            }

            focusedItemId = target;
            return true;
        }

        return ApplyIndex(index, modifiers);
    }

    private bool ApplyIndex(
        int index,
        ProductDesktopSelectionModifiers modifiers)
    {
        string target = visibleItemIds[index];
        string? oldFocus = focusedItemId;
        string? oldAnchor = anchorItemId;
        string[] oldSelection = OrderedSelection();
        bool control = modifiers.HasFlag(ProductDesktopSelectionModifiers.Control);
        bool shift = modifiers.HasFlag(ProductDesktopSelectionModifiers.Shift);

        focusedItemId = target;
        if (shift)
        {
            int anchorIndex = IndexOf(anchorItemId);
            if (anchorIndex < 0)
            {
                anchorIndex = IndexOf(oldFocus);
                if (anchorIndex < 0)
                {
                    anchorIndex = index;
                }

                anchorItemId = visibleItemIds[anchorIndex];
            }

            if (!control)
            {
                selected.Clear();
            }

            int start = Math.Min(anchorIndex, index);
            int end = Math.Max(anchorIndex, index);
            for (int itemIndex = start; itemIndex <= end; itemIndex++)
            {
                selected.Add(visibleItemIds[itemIndex]);
            }
        }
        else if (control)
        {
            if (!selected.Remove(target))
            {
                selected.Add(target);
            }

            anchorItemId = target;
        }
        else
        {
            selected.Clear();
            selected.Add(target);
            anchorItemId = target;
        }

        return !string.Equals(oldFocus, focusedItemId, StringComparison.Ordinal)
            || !string.Equals(oldAnchor, anchorItemId, StringComparison.Ordinal)
            || !oldSelection.SequenceEqual(
                OrderedSelection(),
                StringComparer.Ordinal);
    }

    private bool Clear(ProductDesktopSelectionRequest request)
    {
        if (request.ItemId is not null
            || request.Modifiers != ProductDesktopSelectionModifiers.None)
        {
            return false;
        }

        bool changed = selected.Count > 0
            || focusedItemId is not null
            || anchorItemId is not null;
        selected.Clear();
        focusedItemId = null;
        anchorItemId = null;
        return changed;
    }

    private int IndexOf(string? itemId) =>
        itemId is null
            ? -1
            : Array.FindIndex(
                visibleItemIds,
                candidate => string.Equals(
                    candidate,
                    itemId,
                    StringComparison.Ordinal));

    private bool LeaseMatches(ProductDesktopInteractionLease current) =>
        current.IntentId == lease.IntentId
        && string.Equals(
            current.TargetContainerId,
            lease.TargetContainerId,
            StringComparison.Ordinal)
        && current.WorkspaceRevision == lease.WorkspaceRevision
        && current.TopologyGeneration == lease.TopologyGeneration
        && current.WindowRegistryGeneration == lease.WindowRegistryGeneration
        && current.ExpiresAtUtc == lease.ExpiresAtUtc;

    private string[] OrderedSelection() =>
        visibleItemIds.Where(selected.Contains).ToArray();

    private ProductDesktopSelectionSnapshot CreateSnapshot(
        ProductDesktopSelectionStatus status) =>
        new(
            status,
            lease.IntentId,
            lease.TargetContainerId,
            lease.WorkspaceRevision,
            lease.TopologyGeneration,
            lease.WindowRegistryGeneration,
            Array.AsReadOnly((string[])visibleItemIds.Clone()),
            Array.AsReadOnly(OrderedSelection()),
            focusedItemId,
            anchorItemId,
            selectionRevision);
}
