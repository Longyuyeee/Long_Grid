#if WINDOWS
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class WindowsProductDesktopHostUiaRootProvider
    : IRawElementProviderFragmentRoot, ISelectionProvider
{
    private readonly nint window;
    private readonly ProductDesktopHostDisplayProjection projection;
    private readonly WindowsProductDesktopHostUiaContainerProvider[] containers;
    private readonly Func<bool> isExplicit;
    private readonly Func<ProductDesktopInteractionSurfaceTransactionSnapshot?>
        selectionSnapshot;
    private readonly Func<string, ProductDesktopSelectionRequest, bool>
        applySelection;
    private readonly WindowsProductDesktopHostUiaWorkspaceCreateProvider?
        workspaceCreate;
    private ProductDesktopInteractionSurfaceTransactionSnapshot? lastPublished;

    internal WindowsProductDesktopHostUiaRootProvider(
        nint window,
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        Func<bool>? isExplicit = null,
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?>?
            selectionSnapshot = null,
        Func<string, ProductDesktopSelectionRequest, bool>? applySelection = null,
        Func<bool>? requestWorkspaceCreate = null,
        Func<bool>? workspaceKeyboardCreateAvailable = null)
    {
        this.window = window != nint.Zero
            ? window
            : throw new ArgumentOutOfRangeException(nameof(window));
        this.projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        this.isExplicit = isExplicit ?? (() => false);
        this.selectionSnapshot = selectionSnapshot ?? (() => null);
        this.applySelection = applySelection ?? ((_, _) => false);
        int marker = unchecked((int)instanceMarker.ToInt64());
        containers = projection.Containers.Select((container, index) =>
            new WindowsProductDesktopHostUiaContainerProvider(
                this, container, index, marker)).ToArray();
        workspaceCreate = ProductDesktopHostSurfaceLayout
            .GetWorkspaceCreateButtonBounds(projection) is not null
            ? new(
                this,
                marker,
                requestWorkspaceCreate ?? (() => false),
                workspaceKeyboardCreateAvailable ?? (() => false),
                () => !this.isExplicit())
            : null;
    }

    internal IReadOnlyList<WindowsProductDesktopHostUiaContainerProvider>
        Containers => containers;
    internal WindowsProductDesktopHostUiaWorkspaceCreateProvider?
        WorkspaceCreate => workspaceCreate;
    internal bool WorkspaceCreateAvailable =>
        workspaceCreate is not null && !ExplicitSelectionAvailable;
    internal ProductDesktopHostDisplayProjection Projection => projection;
    internal bool ExplicitSelectionAvailable => isExplicit();

    public bool CanSelectMultiple => true;
    public bool IsSelectionRequired => false;
    public ProviderOptions ProviderOptions =>
        ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;
    public IRawElementProviderSimple HostRawElementProvider =>
        AutomationInteropProvider.HostProviderFromHandle(window);
    public Rect BoundingRectangle
    {
        get
        {
            if (projection.WorkspaceIsEmpty)
            {
                return new(
                    projection.WorkArea.Left,
                    projection.WorkArea.Top,
                    projection.WorkArea.Width,
                    projection.WorkArea.Height);
            }
            Rect bounds = containers.Length == 0
                ? workspaceCreate?.BoundingRectangle ?? Rect.Empty
                : containers[0].BoundingRectangle;
            foreach (var container in containers.Skip(1))
            {
                bounds.Union(container.BoundingRectangle);
            }
            if (WorkspaceCreateAvailable)
            {
                bounds.Union(workspaceCreate!.BoundingRectangle);
            }
            return bounds;
        }
    }
    public IRawElementProviderFragmentRoot FragmentRoot => this;
    public object? GetPatternProvider(int patternId) =>
        ExplicitSelectionAvailable
        && patternId == SelectionPatternIdentifiers.Pattern.Id ? this : null;
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            projection.WorkspaceIsEmpty
                ? "Long\u65b9\u683c\u684c\u9762\u7a7a\u72b6\u6001"
                : "Long\u65b9\u683c\u684c\u9762\u53ea\u8bfb\u533a\u57df",
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            "LongGrid.DesktopHost.Root",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Pane.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            WorkspaceCreateAvailable || ExplicitSelectionAvailable,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            projection.WorkspaceIsEmpty
                ? "空工作区；可以创建第一个方格；不修改桌面文件"
                : ExplicitSelectionAvailable
                ? $"\u663e\u5f0f\u4ea4\u4e92\uff1b{containers.Length} \u4e2a\u65b9\u683c\uff1b\u7b49\u5f85\u8f93\u5165\u6d88\u8d39\u63a5\u7ebf"
                : $"\u53ea\u8bfb\u9884\u89c8\uff1b{containers.Length} \u4e2a\u65b9\u683c\uff1b\u53ef\u65b0\u5efa\u65b9\u683c",
        _ => null,
    };
    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.FirstChild when containers.Length == 0
                && WorkspaceCreateAvailable => workspaceCreate,
            NavigateDirection.FirstChild when containers.Length > 0 => containers[0],
            NavigateDirection.LastChild when WorkspaceCreateAvailable =>
                workspaceCreate,
            NavigateDirection.LastChild when containers.Length > 0 => containers[^1],
            _ => null,
        };
    public int[]? GetRuntimeId() => null;
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus() { }
    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        Point point = new(x, y);
        if (WorkspaceCreateAvailable
            && workspaceCreate?.BoundingRectangle.Contains(point) == true)
        {
            return workspaceCreate;
        }
        foreach (var container in containers)
        {
            var item = container.Items.FirstOrDefault(candidate =>
                candidate.BoundingRectangle.Contains(point));
            if (item is not null) return item;
            if (container.BoundingRectangle.Contains(point)) return container;
        }
        return null;
    }
    public IRawElementProviderFragment? GetFocus()
    {
        var snapshot = selectionSnapshot();
        return FindItems(snapshot?.Selection?.ContainerId)
            .FirstOrDefault(item => string.Equals(
                item.ItemId, snapshot?.Selection?.FocusedItemId,
                StringComparison.Ordinal));
    }
    public IRawElementProviderSimple[] GetSelection()
    {
        var snapshot = selectionSnapshot();
        var selected = snapshot?.Accessibility.SelectedItemIds.ToHashSet(
            StringComparer.Ordinal) ?? [];
        return FindItems(snapshot?.Selection?.ContainerId)
            .Where(item => selected.Contains(item.ItemId))
            .Cast<IRawElementProviderSimple>().ToArray();
    }

    internal Rect ScreenBounds(PixelRect local) => new(
        projection.WorkArea.Left + local.Left,
        projection.WorkArea.Top + local.Top,
        local.Width,
        local.Height);
    internal bool IsInteractiveItem(string containerId, string itemId) =>
        ExplicitSelectionAvailable
        && selectionSnapshot()?.Selection is { } selection
        && string.Equals(selection.ContainerId, containerId,
            StringComparison.Ordinal)
        && selection.VisibleItemIds.Contains(itemId, StringComparer.Ordinal);
    internal ProductDesktopSelectionAccessibilityItem? ItemState(
        string containerId, string itemId) =>
        IsInteractiveItem(containerId, itemId)
            ? selectionSnapshot()?.Accessibility.Items.SingleOrDefault(item =>
                string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
            : null;
    internal bool ApplyAccessibilityAction(
        string containerId,
        string itemId,
        ProductDesktopSelectionAccessibilityAction action)
    {
        var snapshot = selectionSnapshot();
        if (snapshot?.IsExplicit != true
            || !string.Equals(snapshot.Selection?.ContainerId, containerId,
                StringComparison.Ordinal)) return false;
        var mapped = ProductDesktopInteractionSelectionAccessibilityAdapter
            .MapAction(snapshot.Accessibility, action, itemId);
        return mapped.Status ==
                ProductDesktopSelectionAccessibilityActionStatus.AlreadySatisfied
            || (mapped.Request is not null
                && applySelection(containerId, mapped.Request));
    }
    internal void PublishSelectionChanges()
    {
        var current = selectionSnapshot();
        var previous = lastPublished;
        lastPublished = current;
        if (current?.IsExplicit != true) return;
        var before = previous?.Accessibility.SelectedItemIds.ToHashSet(
            StringComparer.Ordinal) ?? [];
        var after = current.Accessibility.SelectedItemIds.ToHashSet(
            StringComparer.Ordinal);
        foreach (var item in FindItems(current.Selection?.ContainerId))
        {
            bool oldValue = before.Contains(item.ItemId);
            bool newValue = after.Contains(item.ItemId);
            if (oldValue == newValue) continue;
            AutomationEvent eventId = newValue
                ? after.Count == 1
                    ? SelectionItemPatternIdentifiers.ElementSelectedEvent
                    : SelectionItemPatternIdentifiers.ElementAddedToSelectionEvent
                : SelectionItemPatternIdentifiers.ElementRemovedFromSelectionEvent;
            TryRaiseSelectionEvent(item, oldValue, newValue, eventId);
        }
    }
    private static void TryRaiseSelectionEvent(
        WindowsProductDesktopHostUiaItemProvider item,
        bool oldValue,
        bool newValue,
        AutomationEvent eventId)
    {
        try
        {
            AutomationInteropProvider.RaiseAutomationPropertyChangedEvent(
                item,
                new AutomationPropertyChangedEventArgs(
                    SelectionItemPatternIdentifiers.IsSelectedProperty,
                    oldValue,
                    newValue));
            AutomationInteropProvider.RaiseAutomationEvent(
                eventId, item, new AutomationEventArgs(eventId));
        }
        catch (InvalidOperationException)
        {
            // The selection transaction remains authoritative if a UIA client
            // disconnects while its notification is being published.
        }
        catch (ElementNotAvailableException)
        {
        }
    }
    private IEnumerable<WindowsProductDesktopHostUiaItemProvider> FindItems(
        string? containerId) => containers
        .Where(container => string.Equals(container.ContainerId, containerId,
            StringComparison.Ordinal))
        .SelectMany(container => container.Items);
}

internal sealed class WindowsProductDesktopHostUiaWorkspaceCreateProvider
    : IRawElementProviderFragment, IInvokeProvider
{
    private readonly WindowsProductDesktopHostUiaRootProvider root;
    private readonly int marker;
    private readonly Func<bool> requestCreate;
    private readonly Func<bool> keyboardCreateAvailable;
    private readonly Func<bool> createAvailable;

    internal WindowsProductDesktopHostUiaWorkspaceCreateProvider(
        WindowsProductDesktopHostUiaRootProvider root,
        int marker,
        Func<bool> requestCreate,
        Func<bool> keyboardCreateAvailable,
        Func<bool> createAvailable)
    {
        this.root = root;
        this.marker = marker;
        this.requestCreate = requestCreate;
        this.keyboardCreateAvailable = keyboardCreateAvailable;
        this.createAvailable = createAvailable;
    }

    public ProviderOptions ProviderOptions => root.ProviderOptions;
    public IRawElementProviderSimple? HostRawElementProvider => null;
    public Rect BoundingRectangle => root.ScreenBounds(
        ProductDesktopHostSurfaceLayout.GetWorkspaceCreateButtonBounds(
            root.Projection)!.Value);
    public IRawElementProviderFragmentRoot FragmentRoot => root;
    public object? GetPatternProvider(int patternId) =>
        patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            root.Projection.WorkspaceIsEmpty
                ? "\u521b\u5efa\u7b2c\u4e00\u4e2a\u65b9\u683c"
                : "\u65b0\u5efa\u65b9\u683c",
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            root.Projection.WorkspaceIsEmpty
                ? "LongGrid.DesktopHost.EmptyCreateButton"
                : "LongGrid.DesktopHost.WorkspaceCreateButton",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Button.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id =>
            createAvailable(),
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id =>
            createAvailable(),
        var id when id == AutomationElementIdentifiers.IsOffscreenProperty.Id =>
            !createAvailable(),
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            true,
        var id when id == AutomationElementIdentifiers.AccessKeyProperty.Id =>
            keyboardCreateAvailable() ? "Ctrl+Alt+N" : string.Empty,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            keyboardCreateAvailable()
                ? "\u521b\u5efa\u9ed8\u8ba4\u7a7a\u65b9\u683c\uff1b\u53ef\u4f7f\u7528 Ctrl+Alt+N\uff1b\u4e0d\u8bfb\u53d6\u6216\u79fb\u52a8\u684c\u9762\u6587\u4ef6"
                : "\u521b\u5efa\u9ed8\u8ba4\u7a7a\u65b9\u683c\uff1b\u5feb\u6377\u952e\u5f53\u524d\u4e0d\u53ef\u7528\uff1b\u4e0d\u8bfb\u53d6\u6216\u79fb\u52a8\u684c\u9762\u6587\u4ef6",
        _ => null,
    };
    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => root,
            NavigateDirection.PreviousSibling when root.Containers.Count > 0 =>
                root.Containers[^1],
            _ => null,
        };
    public int[] GetRuntimeId() =>
        [AutomationInteropProvider.AppendRuntimeId, marker, 900];
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus()
    {
    }
    public void Invoke()
    {
        if (createAvailable())
        {
            _ = requestCreate();
        }
    }
}

internal sealed class WindowsProductDesktopHostUiaContainerProvider
    : IRawElementProviderFragment
{
    private readonly WindowsProductDesktopHostUiaRootProvider root;
    private readonly ProductDesktopHostReadOnlyProjection projection;
    private readonly int index;
    private readonly int marker;
    private readonly WindowsProductDesktopHostUiaItemProvider[] items;
    internal WindowsProductDesktopHostUiaContainerProvider(
        WindowsProductDesktopHostUiaRootProvider root,
        ProductDesktopHostReadOnlyProjection projection,
        int index,
        int marker)
    {
        this.root = root;
        this.projection = projection;
        this.index = index;
        this.marker = marker;
        PixelRect bounds = ProductDesktopHostSurfaceLayout.GetContainerBounds(
            root.Projection, projection);
        double scale = root.Projection.EffectiveDpi / 96d;
        int header = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip, scale);
        int height = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.ItemHeightDip, scale);
        int count = projection.IsCollapsed ? 0 : Math.Min(
            projection.ItemNames.Count,
            Math.Max(0, (bounds.Height - header) / height));
        items = projection.ItemNames.Take(count).Select((name, itemIndex) =>
            new WindowsProductDesktopHostUiaItemProvider(
                this, name, projection.ItemIds[itemIndex], itemIndex, marker))
            .ToArray();
    }
    internal IReadOnlyList<WindowsProductDesktopHostUiaItemProvider> Items => items;
    internal int Index => index;
    internal string ContainerId => projection.ContainerId;
    public ProviderOptions ProviderOptions => root.ProviderOptions;
    public IRawElementProviderSimple? HostRawElementProvider => null;
    public Rect BoundingRectangle => root.ScreenBounds(
        ProductDesktopHostSurfaceLayout.GetContainerBounds(root.Projection, projection));
    public IRawElementProviderFragmentRoot FragmentRoot => root;
    public object? GetPatternProvider(int patternId) => null;
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id =>
            projection.Header.AccessibilityName,
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            $"LongGrid.DesktopHost.Container.{index + 1}",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            ControlType.Group.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id => false,
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            projection.Header.AccessibilityStatus,
        _ => null,
    };
    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => root,
            NavigateDirection.PreviousSibling when index > 0 => root.Containers[index - 1],
            NavigateDirection.NextSibling when index + 1 < root.Containers.Count =>
                root.Containers[index + 1],
            NavigateDirection.NextSibling when index + 1 == root.Containers.Count
                && root.WorkspaceCreateAvailable => root.WorkspaceCreate,
            NavigateDirection.FirstChild when items.Length > 0 => items[0],
            NavigateDirection.LastChild when items.Length > 0 => items[^1],
            _ => null,
        };
    public int[] GetRuntimeId() =>
        [AutomationInteropProvider.AppendRuntimeId, marker, 1000 + index];
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus() { }
}

internal sealed class WindowsProductDesktopHostUiaItemProvider
    : IRawElementProviderFragment, ISelectionItemProvider, IInvokeProvider
{
    private readonly WindowsProductDesktopHostUiaContainerProvider container;
    private readonly string name;
    private readonly string itemId;
    private readonly int index;
    private readonly int marker;
    internal WindowsProductDesktopHostUiaItemProvider(
        WindowsProductDesktopHostUiaContainerProvider container,
        string name,
        string itemId,
        int index,
        int marker)
    {
        this.container = container;
        this.name = name;
        this.itemId = itemId;
        this.index = index;
        this.marker = marker;
    }
    internal string ItemId => itemId;
    private WindowsProductDesktopHostUiaRootProvider Root =>
        (WindowsProductDesktopHostUiaRootProvider)container.FragmentRoot;
    public ProviderOptions ProviderOptions => container.ProviderOptions;
    public IRawElementProviderSimple? HostRawElementProvider => null;
    public Rect BoundingRectangle
    {
        get
        {
            Rect parent = container.BoundingRectangle;
            double scale = Root.Projection.EffectiveDpi / 96d;
            double header = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.HeaderHeightDip, scale);
            double height = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.ItemHeightDip, scale);
            double top = parent.Top + header + (index * height);
            return new(parent.Left, top, parent.Width,
                Math.Max(0, Math.Min(height, parent.Bottom - top)));
        }
    }
    public IRawElementProviderFragmentRoot FragmentRoot => Root;
    public object? GetPatternProvider(int patternId) =>
        Root.IsInteractiveItem(container.ContainerId, itemId)
        && (patternId == SelectionItemPatternIdentifiers.Pattern.Id
            || patternId == InvokePatternIdentifiers.Pattern.Id) ? this : null;
    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        var id when id == AutomationElementIdentifiers.NameProperty.Id => name,
        var id when id == AutomationElementIdentifiers.AutomationIdProperty.Id =>
            $"LongGrid.DesktopHost.Item.{container.Index + 1}.{index + 1}",
        var id when id == AutomationElementIdentifiers.ControlTypeProperty.Id =>
            Root.IsInteractiveItem(container.ContainerId, itemId)
                ? ControlType.ListItem.Id : ControlType.Text.Id,
        var id when id == AutomationElementIdentifiers.IsControlElementProperty.Id
            || id == AutomationElementIdentifiers.IsContentElementProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsEnabledProperty.Id => true,
        var id when id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id =>
            Root.IsInteractiveItem(container.ContainerId, itemId),
        var id when id == AutomationElementIdentifiers.ItemStatusProperty.Id =>
            Root.IsInteractiveItem(container.ContainerId, itemId)
                ? "\u663e\u5f0f\u9009\u62e9\u9879\u76ee\uff1b\u672a\u516c\u5f00\u8def\u5f84\u6216\u6587\u4ef6\u64cd\u4f5c"
                : "\u53ea\u8bfb\u9879\u76ee\u540d\u79f0\uff1b\u672a\u516c\u5f00\u8def\u5f84\u6216\u6587\u4ef6\u64cd\u4f5c",
        _ => null,
    };
    public IRawElementProviderFragment? Navigate(NavigateDirection direction) =>
        direction switch
        {
            NavigateDirection.Parent => container,
            NavigateDirection.PreviousSibling when index > 0 => container.Items[index - 1],
            NavigateDirection.NextSibling when index + 1 < container.Items.Count =>
                container.Items[index + 1],
            _ => null,
        };
    public int[] GetRuntimeId() =>
        [AutomationInteropProvider.AppendRuntimeId, marker,
            2000 + (container.Index *
                ProductDesktopHostReadOnlyProjection.MaximumVisibleItems) + index];
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
    public void SetFocus() => Select();
    public bool IsSelected =>
        Root.ItemState(container.ContainerId, itemId)?.IsSelected == true;
    public IRawElementProviderSimple SelectionContainer => Root;
    public void Select() => Apply(ProductDesktopSelectionAccessibilityAction.Select);
    public void AddToSelection() =>
        Apply(ProductDesktopSelectionAccessibilityAction.AddToSelection);
    public void RemoveFromSelection() =>
        Apply(ProductDesktopSelectionAccessibilityAction.RemoveFromSelection);
    public void Invoke() => Select();
    private void Apply(ProductDesktopSelectionAccessibilityAction action)
    {
        if (!Root.ApplyAccessibilityAction(container.ContainerId, itemId, action))
        {
            throw new ElementNotEnabledException();
        }
    }
}
#endif
