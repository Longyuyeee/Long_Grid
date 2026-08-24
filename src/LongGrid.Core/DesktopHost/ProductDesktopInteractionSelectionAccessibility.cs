namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopSelectionAccessibilityMode
{
    PassiveReadOnly,
    ExplicitInteraction,
}

public sealed record ProductDesktopSelectionAccessibilityItem(
    string ItemId,
    int Ordinal,
    bool IsSelected,
    bool HasKeyboardFocus,
    bool IsKeyboardFocusable);

public sealed record ProductDesktopSelectionAccessibilitySnapshot(
    ProductDesktopSelectionAccessibilityMode Mode,
    bool SelectionPatternAvailable,
    bool CanSelectMultiple,
    bool IsSelectionRequired,
    IReadOnlyList<string> SelectedItemIds,
    IReadOnlyList<ProductDesktopSelectionAccessibilityItem> Items);

public enum ProductDesktopSelectionAccessibilityAction
{
    Select,
    AddToSelection,
    RemoveFromSelection,
}

public enum ProductDesktopSelectionAccessibilityActionStatus
{
    Mapped,
    AlreadySatisfied,
    PassiveMode,
    ItemUnavailable,
    InvalidAction,
}

public sealed record ProductDesktopSelectionAccessibilityActionResult(
    ProductDesktopSelectionAccessibilityActionStatus Status,
    ProductDesktopSelectionRequest? Request)
{
    public bool IsMapped =>
        Status == ProductDesktopSelectionAccessibilityActionStatus.Mapped
        && Request is not null;
}

public static class ProductDesktopInteractionSelectionAccessibilityAdapter
{
    public static ProductDesktopSelectionAccessibilitySnapshot CreatePassive(
        IReadOnlyList<string> visibleItemIds)
    {
        ArgumentNullException.ThrowIfNull(visibleItemIds);
        Validate(visibleItemIds);
        return new(
            ProductDesktopSelectionAccessibilityMode.PassiveReadOnly,
            SelectionPatternAvailable: false,
            CanSelectMultiple: false,
            IsSelectionRequired: false,
            Array.Empty<string>(),
            Array.AsReadOnly(visibleItemIds.Select((id, index) =>
                new ProductDesktopSelectionAccessibilityItem(
                    id,
                    index + 1,
                    IsSelected: false,
                    HasKeyboardFocus: false,
                    IsKeyboardFocusable: false)).ToArray()));
    }

    public static ProductDesktopSelectionAccessibilitySnapshot
        CreateExplicit(ProductDesktopSelectionSnapshot selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Validate(selection.VisibleItemIds);
        var selected = selection.SelectedItemIds.ToHashSet(
            StringComparer.Ordinal);
        string[] orderedSelected = selection.VisibleItemIds
            .Where(selected.Contains)
            .ToArray();
        if (selection.Status is not (ProductDesktopSelectionStatus.Ready
                or ProductDesktopSelectionStatus.Applied
                or ProductDesktopSelectionStatus.Reconciled)
            || selection.LeaseIntentId == Guid.Empty
            || string.IsNullOrWhiteSpace(selection.ContainerId)
            || selection.WorkspaceRevision <= 0
            || selection.TopologyGeneration <= 0
            || selection.WindowRegistryGeneration <= 0
            || selection.SelectedItemIds.Count != selected.Count
            || selected.Any(id => !selection.VisibleItemIds.Contains(
                id,
                StringComparer.Ordinal))
            || !selection.SelectedItemIds.SequenceEqual(
                orderedSelected,
                StringComparer.Ordinal)
            || (selection.FocusedItemId is not null
                && !selection.VisibleItemIds.Contains(
                    selection.FocusedItemId,
                    StringComparer.Ordinal))
            || (selection.AnchorItemId is not null
                && !selection.VisibleItemIds.Contains(
                    selection.AnchorItemId,
                    StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "Selection accessibility requires a self-consistent snapshot.",
                nameof(selection));
        }

        return new(
            ProductDesktopSelectionAccessibilityMode.ExplicitInteraction,
            SelectionPatternAvailable: true,
            CanSelectMultiple: true,
            IsSelectionRequired: false,
            Array.AsReadOnly(selection.SelectedItemIds.ToArray()),
            Array.AsReadOnly(selection.VisibleItemIds.Select((id, index) =>
                new ProductDesktopSelectionAccessibilityItem(
                    id,
                    index + 1,
                    selected.Contains(id),
                    string.Equals(
                        id,
                        selection.FocusedItemId,
                        StringComparison.Ordinal),
                    IsKeyboardFocusable: true)).ToArray()));
    }

    public static ProductDesktopSelectionAccessibilityActionResult MapAction(
        ProductDesktopSelectionAccessibilitySnapshot accessibility,
        ProductDesktopSelectionAccessibilityAction action,
        string itemId)
    {
        ArgumentNullException.ThrowIfNull(accessibility);
        if (accessibility.Mode
                != ProductDesktopSelectionAccessibilityMode.ExplicitInteraction
            || !accessibility.SelectionPatternAvailable)
        {
            return Failure(
                ProductDesktopSelectionAccessibilityActionStatus.PassiveMode);
        }

        ProductDesktopSelectionAccessibilityItem[] matches = accessibility.Items
            .Where(candidate => string.Equals(
                candidate.ItemId,
                itemId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1 || !matches[0].IsKeyboardFocusable)
        {
            return Failure(
                ProductDesktopSelectionAccessibilityActionStatus
                    .ItemUnavailable);
        }

        ProductDesktopSelectionAccessibilityItem item = matches[0];

        bool alreadySatisfied = action switch
        {
            ProductDesktopSelectionAccessibilityAction.AddToSelection =>
                item.IsSelected,
            ProductDesktopSelectionAccessibilityAction.RemoveFromSelection =>
                !item.IsSelected,
            _ => false,
        };
        if (alreadySatisfied)
        {
            return Failure(
                ProductDesktopSelectionAccessibilityActionStatus
                    .AlreadySatisfied);
        }

        ProductDesktopSelectionRequest? request = action switch
        {
            ProductDesktopSelectionAccessibilityAction.Select =>
                new(
                    ProductDesktopSelectionAction.SelectItem,
                    ProductDesktopSelectionModifiers.None,
                    itemId),
            ProductDesktopSelectionAccessibilityAction.AddToSelection
                when !item.IsSelected =>
                new(
                    ProductDesktopSelectionAction.SelectItem,
                    ProductDesktopSelectionModifiers.Control,
                    itemId),
            ProductDesktopSelectionAccessibilityAction.RemoveFromSelection
                when item.IsSelected =>
                new(
                    ProductDesktopSelectionAction.SelectItem,
                    ProductDesktopSelectionModifiers.Control,
                    itemId),
            _ => null,
        };
        return request is null
            ? Failure(
                ProductDesktopSelectionAccessibilityActionStatus.InvalidAction)
            : new(
                ProductDesktopSelectionAccessibilityActionStatus.Mapped,
                request);
    }

    private static ProductDesktopSelectionAccessibilityActionResult Failure(
        ProductDesktopSelectionAccessibilityActionStatus status) =>
        new(status, Request: null);

    private static void Validate(IReadOnlyList<string> visibleItemIds)
    {
        if (visibleItemIds.Count
                > ProductDesktopInteractionSelectionController.MaximumVisibleItems
            || visibleItemIds.Any(string.IsNullOrWhiteSpace)
            || visibleItemIds.Distinct(StringComparer.Ordinal).Count()
                != visibleItemIds.Count)
        {
            throw new ArgumentException(
                "Accessibility item identities must be bounded and unique.",
                nameof(visibleItemIds));
        }
    }
}
