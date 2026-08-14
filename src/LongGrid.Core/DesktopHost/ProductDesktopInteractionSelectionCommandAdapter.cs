namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopSelectionCommand
{
    Previous,
    Next,
    First,
    Last,
    ActivateFocused,
}

public static class ProductDesktopInteractionSelectionCommandAdapter
{
    public static ProductDesktopSelectionRequest? Map(
        ProductDesktopSelectionSnapshot selection,
        ProductDesktopSelectionCommand command,
        ProductDesktopSelectionModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(command)
            || (modifiers & ~(ProductDesktopSelectionModifiers.Control
                | ProductDesktopSelectionModifiers.Shift)) != 0)
        {
            return null;
        }

        ProductDesktopSelectionAction? action = command switch
        {
            ProductDesktopSelectionCommand.Previous =>
                ProductDesktopSelectionAction.MovePrevious,
            ProductDesktopSelectionCommand.Next =>
                ProductDesktopSelectionAction.MoveNext,
            ProductDesktopSelectionCommand.First =>
                ProductDesktopSelectionAction.MoveFirst,
            ProductDesktopSelectionCommand.Last =>
                ProductDesktopSelectionAction.MoveLast,
            _ => null,
        };
        if (action is not null)
        {
            return new(action.Value, modifiers);
        }

        if (command != ProductDesktopSelectionCommand.ActivateFocused)
        {
            return null;
        }

        return selection.FocusedItemId is { } focused
            ? new(
                ProductDesktopSelectionAction.SelectItem,
                modifiers.HasFlag(ProductDesktopSelectionModifiers.Control)
                    ? ProductDesktopSelectionModifiers.Control
                    : ProductDesktopSelectionModifiers.None,
                focused)
            : new(
                ProductDesktopSelectionAction.MoveNext,
                ProductDesktopSelectionModifiers.None);
    }
}
