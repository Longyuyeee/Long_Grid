using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionSelectionTests
{
    private static readonly string[] Items = ["a", "b", "c", "d", "e"];
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        12,
        2,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CreationBindsLeaseAndCopiesVisibleIdentities()
    {
        string[] source = ["a", "b"];
        ProductDesktopSelectionCreationResult result =
            ProductDesktopInteractionSelectionController.TryCreate(
                Lease(),
                source,
                Now);
        source[0] = "changed";

        Assert.True(result.IsCreated);
        ProductDesktopSelectionSnapshot snapshot = result.Controller!.Snapshot;
        Assert.Equal(ProductDesktopSelectionStatus.Ready, snapshot.Status);
        Assert.Equal(["a", "b"], snapshot.VisibleItemIds);
        Assert.Empty(snapshot.SelectedItemIds);
        Assert.Null(snapshot.FocusedItemId);
        Assert.Null(snapshot.AnchorItemId);
        Assert.Equal(0, snapshot.SelectionRevision);
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("invalid-lease")]
    public void InvalidModelsFailClosed(string fault)
    {
        ProductDesktopInteractionLease lease = Lease();
        IReadOnlyList<string> items = Items;
        switch (fault)
        {
            case "empty-id":
                items = ["a", ""];
                break;
            case "duplicate":
                items = ["a", "a"];
                break;
            case "too-many":
                items = Enumerable.Range(
                    0,
                    ProductDesktopInteractionSelectionController
                        .MaximumVisibleItems + 1)
                    .Select(index => $"item-{index}")
                    .ToArray();
                break;
            case "invalid-lease":
                lease = lease with { IntentId = Guid.Empty };
                break;
        }

        ProductDesktopSelectionCreationResult result =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                items,
                Now);

        Assert.Equal(ProductDesktopSelectionStatus.InvalidModel, result.Status);
        Assert.Null(result.Controller);
    }

    [Fact]
    public void PlainSelectionReplacesAndSetsFocusAndAnchor()
    {
        ProductDesktopInteractionSelectionController controller = Controller();

        Apply(controller, Select("b"));
        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select("d"));

        Assert.Equal(["d"], snapshot.SelectedItemIds);
        Assert.Equal("d", snapshot.FocusedItemId);
        Assert.Equal("d", snapshot.AnchorItemId);
        Assert.Equal(2, snapshot.SelectionRevision);
    }

    [Fact]
    public void DisjointViewportReconcileClearsSelectionAndFocusesFirstVisible()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot selected = Apply(
            controller,
            Select("d"));

        ProductDesktopSelectionSnapshot reconciled =
            controller.ReconcileVisibleItems(
                Lease(),
                ["f", "g", "h"],
                Now);

        Assert.Equal(ProductDesktopSelectionStatus.Reconciled,
            reconciled.Status);
        Assert.Equal(["f", "g", "h"], reconciled.VisibleItemIds);
        Assert.Empty(reconciled.SelectedItemIds);
        Assert.Equal("f", reconciled.FocusedItemId);
        Assert.Equal("f", reconciled.AnchorItemId);
        Assert.Equal(selected.SelectionRevision + 1,
            reconciled.SelectionRevision);
    }

    [Fact]
    public void OverlappingViewportReconcilePreservesVisibleSelection()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot selected = Apply(
            controller,
            Select("d"));

        ProductDesktopSelectionSnapshot reconciled =
            controller.ReconcileVisibleItems(
                Lease(),
                ["c", "d", "e"],
                Now);

        Assert.Equal(["d"], reconciled.SelectedItemIds);
        Assert.Equal("d", reconciled.FocusedItemId);
        Assert.Equal("d", reconciled.AnchorItemId);
        Assert.Equal(selected.SelectionRevision + 1,
            reconciled.SelectionRevision);
    }

    [Fact]
    public void InvalidViewportReconcileFailsClosedWithoutMutation()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot selected = Apply(
            controller,
            Select("b"));

        ProductDesktopSelectionSnapshot rejected =
            controller.ReconcileVisibleItems(
                Lease(),
                ["duplicate", "duplicate"],
                Now);

        Assert.Equal(ProductDesktopSelectionStatus.InvalidModel,
            rejected.Status);
        Assert.Equal(selected.VisibleItemIds, rejected.VisibleItemIds);
        Assert.Equal(selected.SelectedItemIds, rejected.SelectedItemIds);
        Assert.Equal(selected.SelectionRevision, rejected.SelectionRevision);
    }

    [Fact]
    public void ControlSelectionTogglesWithoutDroppingOtherItems()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));
        Apply(controller, Select("d", ProductDesktopSelectionModifiers.Control));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select("b", ProductDesktopSelectionModifiers.Control));

        Assert.Equal(["d"], snapshot.SelectedItemIds);
        Assert.Equal("b", snapshot.FocusedItemId);
        Assert.Equal("b", snapshot.AnchorItemId);
    }

    [Fact]
    public void ShiftSelectionReplacesWithContiguousAnchorRange()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select("e", ProductDesktopSelectionModifiers.Shift));

        Assert.Equal(["b", "c", "d", "e"], snapshot.SelectedItemIds);
        Assert.Equal("e", snapshot.FocusedItemId);
        Assert.Equal("b", snapshot.AnchorItemId);
    }

    [Fact]
    public void ControlShiftAddsRangeToExistingSelection()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("a"));
        Apply(controller, Select("d", ProductDesktopSelectionModifiers.Control));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select(
                "b",
                ProductDesktopSelectionModifiers.Control
                    | ProductDesktopSelectionModifiers.Shift));

        Assert.Equal(["a", "b", "c", "d"], snapshot.SelectedItemIds);
        Assert.Equal("d", snapshot.AnchorItemId);
        Assert.Equal("b", snapshot.FocusedItemId);
    }

    [Fact]
    public void ShiftWithoutAnchorUsesFocusedItemOrTarget()
    {
        ProductDesktopInteractionSelectionController controller = Controller();

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select("c", ProductDesktopSelectionModifiers.Shift));

        Assert.Equal(["c"], snapshot.SelectedItemIds);
        Assert.Equal("c", snapshot.AnchorItemId);
    }

    [Fact]
    public void DirectionNavigationClampsAndPlainModeSelects()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot first = Apply(
            controller,
            new(ProductDesktopSelectionAction.MovePrevious));
        ProductDesktopSelectionSnapshot stillFirst = Apply(
            controller,
            new(ProductDesktopSelectionAction.MoveNext));
        ProductDesktopSelectionSnapshot last = Apply(
            controller,
            new(ProductDesktopSelectionAction.MoveLast));
        ProductDesktopSelectionSnapshot stillLast = Apply(
            controller,
            new(ProductDesktopSelectionAction.MoveNext));

        Assert.Equal("e", first.FocusedItemId);
        Assert.Equal("e", stillFirst.FocusedItemId);
        Assert.Equal("e", last.FocusedItemId);
        Assert.Equal("e", stillLast.FocusedItemId);
        Assert.Equal(["e"], stillLast.SelectedItemIds);
    }

    [Fact]
    public void ControlNavigationMovesFocusWithoutChangingSelectionOrAnchor()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            new(
                ProductDesktopSelectionAction.MoveNext,
                ProductDesktopSelectionModifiers.Control));

        Assert.Equal(["b"], snapshot.SelectedItemIds);
        Assert.Equal("c", snapshot.FocusedItemId);
        Assert.Equal("b", snapshot.AnchorItemId);
    }

    [Fact]
    public void ShiftNavigationExtendsFromStableAnchor()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));
        Apply(controller, new(
            ProductDesktopSelectionAction.MoveNext,
            ProductDesktopSelectionModifiers.Shift));

        ProductDesktopSelectionSnapshot snapshot = Apply(controller, new(
            ProductDesktopSelectionAction.MoveNext,
            ProductDesktopSelectionModifiers.Shift));

        Assert.Equal(["b", "c", "d"], snapshot.SelectedItemIds);
        Assert.Equal("b", snapshot.AnchorItemId);
        Assert.Equal("d", snapshot.FocusedItemId);
    }

    [Fact]
    public void ClearResetsSelectionFocusAndAnchor()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            new(ProductDesktopSelectionAction.Clear));

        Assert.Empty(snapshot.SelectedItemIds);
        Assert.Null(snapshot.FocusedItemId);
        Assert.Null(snapshot.AnchorItemId);
        Assert.Equal(2, snapshot.SelectionRevision);
    }

    [Fact]
    public void SelectAllSelectsBoundedViewportAndKeepsExistingFocus()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        _ = Apply(controller, Select("c"));

        ProductDesktopSelectionSnapshot selected = Apply(
            controller,
            new(ProductDesktopSelectionAction.SelectAll));

        Assert.Equal(Items, selected.SelectedItemIds);
        Assert.Equal("c", selected.FocusedItemId);
        Assert.Equal("c", selected.AnchorItemId);
    }

    [Theory]
    [InlineData("missing-item")]
    [InlineData("item-on-navigation")]
    [InlineData("modifier-on-clear")]
    [InlineData("undefined-action")]
    [InlineData("undefined-modifier")]
    public void InvalidRequestsDoNotMutateState(string fault)
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));
        ProductDesktopSelectionRequest request = fault switch
        {
            "missing-item" => Select("missing"),
            "item-on-navigation" => new(
                ProductDesktopSelectionAction.MoveNext,
                ItemId: "c"),
            "modifier-on-clear" => new(
                ProductDesktopSelectionAction.Clear,
                ProductDesktopSelectionModifiers.Control),
            "undefined-action" => new((ProductDesktopSelectionAction)999),
            "undefined-modifier" => Select(
                "c",
                (ProductDesktopSelectionModifiers)8),
            _ => throw new InvalidOperationException(),
        };

        ProductDesktopSelectionSnapshot snapshot = Apply(controller, request);

        Assert.Equal(ProductDesktopSelectionStatus.InvalidRequest, snapshot.Status);
        Assert.Equal(["b"], snapshot.SelectedItemIds);
        Assert.Equal("b", snapshot.FocusedItemId);
        Assert.Equal(1, snapshot.SelectionRevision);
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("container")]
    [InlineData("workspace")]
    [InlineData("topology")]
    [InlineData("registry")]
    [InlineData("expiry")]
    public void AnyLeaseMismatchRejectsWithoutMutation(string fault)
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopInteractionLease current = Lease();
        current = fault switch
        {
            "intent" => current with { IntentId = Guid.NewGuid() },
            "container" => current with { TargetContainerId = "other" },
            "workspace" => current with { WorkspaceRevision = 8 },
            "topology" => current with { TopologyGeneration = 10 },
            "registry" => current with { WindowRegistryGeneration = 12 },
            "expiry" => current with
            {
                ExpiresAtUtc = current.ExpiresAtUtc.AddSeconds(1),
            },
            _ => throw new InvalidOperationException(),
        };

        ProductDesktopSelectionSnapshot snapshot = controller.Apply(
            current,
            Items,
            Select("b"),
            Now);

        Assert.Equal(ProductDesktopSelectionStatus.LeaseMismatch, snapshot.Status);
        Assert.Empty(snapshot.SelectedItemIds);
        Assert.Equal(0, snapshot.SelectionRevision);
    }

    [Fact]
    public void VisibleIdentityOrderChangeRejectsWithoutMutation()
    {
        ProductDesktopInteractionSelectionController controller = Controller();

        ProductDesktopSelectionSnapshot snapshot = controller.Apply(
            Lease(),
            ["a", "c", "b", "d", "e"],
            Select("b"),
            Now);

        Assert.Equal(
            ProductDesktopSelectionStatus.VisibleItemsChanged,
            snapshot.Status);
        Assert.Empty(snapshot.SelectedItemIds);
    }

    [Fact]
    public void NoOpDoesNotAdvanceSelectionRevision()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));

        ProductDesktopSelectionSnapshot snapshot = Apply(
            controller,
            Select("b"));

        Assert.Equal(1, snapshot.SelectionRevision);
    }

    [Fact]
    public void ExpiredLeaseCannotCreateOrMutateSelection()
    {
        ProductDesktopSelectionCreationResult expiredCreation =
            ProductDesktopInteractionSelectionController.TryCreate(
                Lease(),
                Items,
                Now.AddSeconds(5));
        ProductDesktopInteractionSelectionController controller = Controller();

        ProductDesktopSelectionSnapshot expiredApply = controller.Apply(
            Lease(),
            Items,
            Select("b"),
            Now.AddSeconds(5));

        Assert.Equal(
            ProductDesktopSelectionStatus.LeaseExpired,
            expiredCreation.Status);
        Assert.Equal(
            ProductDesktopSelectionStatus.LeaseExpired,
            expiredApply.Status);
        Assert.Empty(expiredApply.SelectedItemIds);
        Assert.Equal(0, expiredApply.SelectionRevision);
    }

    [Fact]
    public void PassiveAccessibilityIsReadOnlyAndNonFocusable()
    {
        ProductDesktopSelectionAccessibilitySnapshot snapshot =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreatePassive(Items);

        Assert.Equal(
            ProductDesktopSelectionAccessibilityMode.PassiveReadOnly,
            snapshot.Mode);
        Assert.False(snapshot.SelectionPatternAvailable);
        Assert.False(snapshot.CanSelectMultiple);
        Assert.Empty(snapshot.SelectedItemIds);
        Assert.All(snapshot.Items, item =>
        {
            Assert.False(item.IsSelected);
            Assert.False(item.HasKeyboardFocus);
            Assert.False(item.IsKeyboardFocusable);
        });
    }

    [Fact]
    public void ExplicitAccessibilitySeparatesFocusAndSelection()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        Apply(controller, Select("b"));
        ProductDesktopSelectionSnapshot selection = Apply(controller, new(
            ProductDesktopSelectionAction.MoveNext,
            ProductDesktopSelectionModifiers.Control));

        ProductDesktopSelectionAccessibilitySnapshot snapshot =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(selection);

        Assert.True(snapshot.SelectionPatternAvailable);
        Assert.True(snapshot.CanSelectMultiple);
        Assert.False(snapshot.IsSelectionRequired);
        Assert.Equal(["b"], snapshot.SelectedItemIds);
        Assert.True(snapshot.Items.Single(item => item.ItemId == "b").IsSelected);
        Assert.False(snapshot.Items.Single(item => item.ItemId == "b").HasKeyboardFocus);
        Assert.True(snapshot.Items.Single(item => item.ItemId == "c").HasKeyboardFocus);
        Assert.All(snapshot.Items, item => Assert.True(item.IsKeyboardFocusable));
    }

    [Theory]
    [InlineData("duplicate-visible")]
    [InlineData("foreign-selected")]
    [InlineData("foreign-focus")]
    [InlineData("foreign-anchor")]
    [InlineData("selected-order")]
    public void AccessibilityRejectsInconsistentSnapshots(string fault)
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot snapshot = Apply(controller, Select("b"));
        snapshot = fault switch
        {
            "duplicate-visible" => snapshot with
            {
                VisibleItemIds = ["a", "a"],
            },
            "foreign-selected" => snapshot with
            {
                SelectedItemIds = ["missing"],
            },
            "foreign-focus" => snapshot with { FocusedItemId = "missing" },
            "foreign-anchor" => snapshot with { AnchorItemId = "missing" },
            "selected-order" => snapshot with
            {
                SelectedItemIds = ["c", "b"],
                VisibleItemIds = ["a", "b", "c"],
            },
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<ArgumentException>(() =>
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(snapshot));
    }

    [Fact]
    public void AccessibilityRejectsARejectedSelectionResult()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot rejected = controller.Apply(
            Lease() with { TopologyGeneration = 10 },
            Items,
            Select("b"),
            Now);

        Assert.Equal(ProductDesktopSelectionStatus.LeaseMismatch, rejected.Status);
        Assert.Throws<ArgumentException>(() =>
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(rejected));
    }

    [Theory]
    [InlineData(ProductDesktopSelectionAccessibilityAction.Select,
        ProductDesktopSelectionModifiers.None)]
    [InlineData(ProductDesktopSelectionAccessibilityAction.AddToSelection,
        ProductDesktopSelectionModifiers.Control)]
    public void AccessibilityActionsMapToSharedSelectionRequests(
        ProductDesktopSelectionAccessibilityAction action,
        ProductDesktopSelectionModifiers modifiers)
    {
        ProductDesktopSelectionAccessibilitySnapshot accessibility =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(Controller().Snapshot);

        ProductDesktopSelectionAccessibilityActionResult result =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                accessibility,
                action,
                "b");

        Assert.True(result.IsMapped);
        Assert.Equal(ProductDesktopSelectionAction.SelectItem,
            result.Request!.Action);
        Assert.Equal(modifiers, result.Request.Modifiers);
        Assert.Equal("b", result.Request.ItemId);
    }

    [Fact]
    public void AccessibilityRemoveMapsOnlyForSelectedItem()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot selection = Apply(controller, Select("b"));
        ProductDesktopSelectionAccessibilitySnapshot accessibility =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(selection);

        ProductDesktopSelectionAccessibilityActionResult result =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                accessibility,
                ProductDesktopSelectionAccessibilityAction.RemoveFromSelection,
                "b");

        Assert.True(result.IsMapped);
        Assert.Equal(ProductDesktopSelectionModifiers.Control,
            result.Request!.Modifiers);
        Assert.Equal("b", result.Request.ItemId);
    }

    [Fact]
    public void IdempotentAccessibilityAddOrRemoveProducesNoRequest()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot selection = Apply(controller, Select("b"));
        ProductDesktopSelectionAccessibilitySnapshot accessibility =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(selection);

        ProductDesktopSelectionAccessibilityActionResult add =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                accessibility,
                ProductDesktopSelectionAccessibilityAction.AddToSelection,
                "b");
        ProductDesktopSelectionAccessibilityActionResult remove =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                accessibility,
                ProductDesktopSelectionAccessibilityAction.RemoveFromSelection,
                "c");

        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.AlreadySatisfied,
            add.Status);
        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.AlreadySatisfied,
            remove.Status);
        Assert.Null(add.Request);
        Assert.Null(remove.Request);
    }

    [Fact]
    public void PassiveUnknownItemAndUnknownActionCannotMap()
    {
        ProductDesktopSelectionAccessibilitySnapshot passive =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreatePassive(Items);
        ProductDesktopSelectionAccessibilitySnapshot explicitSnapshot =
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreateExplicit(Controller().Snapshot);

        ProductDesktopSelectionAccessibilityActionResult passiveResult =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                passive,
                ProductDesktopSelectionAccessibilityAction.Select,
                "a");
        ProductDesktopSelectionAccessibilityActionResult missing =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                explicitSnapshot,
                ProductDesktopSelectionAccessibilityAction.Select,
                "missing");
        ProductDesktopSelectionAccessibilityActionResult invalid =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                explicitSnapshot,
                (ProductDesktopSelectionAccessibilityAction)999,
                "a");

        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.PassiveMode,
            passiveResult.Status);
        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.ItemUnavailable,
            missing.Status);
        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.InvalidAction,
            invalid.Status);
        Assert.Null(passiveResult.Request);
    }

    [Fact]
    public void DuplicateAccessibilityActionTargetFailsClosed()
    {
        ProductDesktopSelectionAccessibilitySnapshot forged =
            new(
                ProductDesktopSelectionAccessibilityMode.ExplicitInteraction,
                SelectionPatternAvailable: true,
                CanSelectMultiple: true,
                IsSelectionRequired: false,
                SelectedItemIds: [],
                Items:
                [
                    new("a", 1, false, false, true),
                    new("a", 2, false, false, true),
                ]);

        ProductDesktopSelectionAccessibilityActionResult result =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                forged,
                ProductDesktopSelectionAccessibilityAction.Select,
                "a");

        Assert.Equal(
            ProductDesktopSelectionAccessibilityActionStatus.ItemUnavailable,
            result.Status);
        Assert.Null(result.Request);
    }

    [Theory]
    [InlineData(ProductDesktopSelectionCommand.Previous,
        ProductDesktopSelectionAction.MovePrevious)]
    [InlineData(ProductDesktopSelectionCommand.Next,
        ProductDesktopSelectionAction.MoveNext)]
    [InlineData(ProductDesktopSelectionCommand.First,
        ProductDesktopSelectionAction.MoveFirst)]
    [InlineData(ProductDesktopSelectionCommand.Last,
        ProductDesktopSelectionAction.MoveLast)]
    public void KeyboardNavigationMapsToExistingSelectionActions(
        ProductDesktopSelectionCommand command,
        ProductDesktopSelectionAction expected)
    {
        ProductDesktopSelectionRequest? request =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                Controller().Snapshot,
                command,
                ProductDesktopSelectionModifiers.Shift);

        Assert.NotNull(request);
        Assert.Equal(expected, request.Action);
        Assert.Equal(ProductDesktopSelectionModifiers.Shift, request.Modifiers);
        Assert.Null(request.ItemId);
    }

    [Fact]
    public void PointerKeyboardAndUiaConvergeOnOneSelectionSnapshot()
    {
        ProductDesktopInteractionSelectionController pointer = Controller();
        ProductDesktopSelectionSnapshot pointerResult = Apply(
            pointer,
            Select("a"));

        ProductDesktopInteractionSelectionController keyboard = Controller();
        ProductDesktopSelectionRequest keyboardRequest =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                keyboard.Snapshot,
                ProductDesktopSelectionCommand.Next,
                ProductDesktopSelectionModifiers.None)!;
        ProductDesktopSelectionSnapshot keyboardResult = Apply(
            keyboard,
            keyboardRequest);

        ProductDesktopInteractionSelectionController uia = Controller();
        ProductDesktopSelectionAccessibilityActionResult mapped =
            ProductDesktopInteractionSelectionAccessibilityAdapter.MapAction(
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(uia.Snapshot),
                ProductDesktopSelectionAccessibilityAction.Select,
                "a");
        ProductDesktopSelectionSnapshot uiaResult = Apply(uia, mapped.Request!);

        Assert.Equal(pointerResult.SelectionRevision,
            keyboardResult.SelectionRevision);
        Assert.Equal(pointerResult.SelectionRevision, uiaResult.SelectionRevision);
        Assert.Equal(pointerResult.SelectedItemIds, keyboardResult.SelectedItemIds);
        Assert.Equal(pointerResult.SelectedItemIds, uiaResult.SelectedItemIds);
        Assert.Equal(pointerResult.FocusedItemId, keyboardResult.FocusedItemId);
        Assert.Equal(pointerResult.FocusedItemId, uiaResult.FocusedItemId);
        Assert.Equal(pointerResult.AnchorItemId, keyboardResult.AnchorItemId);
        Assert.Equal(pointerResult.AnchorItemId, uiaResult.AnchorItemId);
    }

    [Fact]
    public void SpaceActivatesFocusAndControlSpaceTogglesIt()
    {
        ProductDesktopInteractionSelectionController controller = Controller();
        ProductDesktopSelectionSnapshot focused = Apply(
            controller,
            new(ProductDesktopSelectionAction.MoveNext,
                ProductDesktopSelectionModifiers.Control));

        ProductDesktopSelectionRequest select =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                focused,
                ProductDesktopSelectionCommand.ActivateFocused,
                ProductDesktopSelectionModifiers.None)!;
        ProductDesktopSelectionSnapshot selected = Apply(controller, select);
        ProductDesktopSelectionRequest toggle =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                selected,
                ProductDesktopSelectionCommand.ActivateFocused,
                ProductDesktopSelectionModifiers.Control)!;
        ProductDesktopSelectionSnapshot cleared = Apply(controller, toggle);

        Assert.Equal(["a"], selected.SelectedItemIds);
        Assert.Empty(cleared.SelectedItemIds);
        Assert.Equal("a", cleared.FocusedItemId);
    }

    [Fact]
    public void KeyboardCommandAdapterFailsClosedForInvalidInputs()
    {
        ProductDesktopSelectionRequest? invalidCommand =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                Controller().Snapshot,
                (ProductDesktopSelectionCommand)999,
                ProductDesktopSelectionModifiers.None);
        ProductDesktopSelectionRequest? invalidModifiers =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                Controller().Snapshot,
                ProductDesktopSelectionCommand.Next,
                (ProductDesktopSelectionModifiers)8);
        ProductDesktopSelectionRequest? noFocusSpace =
            ProductDesktopInteractionSelectionCommandAdapter.Map(
                Controller().Snapshot,
                ProductDesktopSelectionCommand.ActivateFocused,
                ProductDesktopSelectionModifiers.Control);

        Assert.Null(invalidCommand);
        Assert.Null(invalidModifiers);
        Assert.Equal(ProductDesktopSelectionAction.MoveNext, noFocusSpace!.Action);
        Assert.Equal(ProductDesktopSelectionModifiers.None,
            noFocusSpace.Modifiers);
    }

    private static ProductDesktopInteractionSelectionController Controller() =>
        ProductDesktopInteractionSelectionController.TryCreate(
            Lease(),
            Items,
            Now)
            .Controller!;

    private static ProductDesktopSelectionSnapshot Apply(
        ProductDesktopInteractionSelectionController controller,
        ProductDesktopSelectionRequest request) =>
        controller.Apply(Lease(), Items, request, Now);

    private static ProductDesktopSelectionRequest Select(
        string id,
        ProductDesktopSelectionModifiers modifiers =
            ProductDesktopSelectionModifiers.None) =>
        new(ProductDesktopSelectionAction.SelectItem, modifiers, id);

    private static ProductDesktopInteractionLease Lease() =>
        new(
            Guid.Parse("84a5a270-7a10-4ced-a0ae-0e1ade42b92b"),
            "container-1",
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            ExpiresAtUtc: Now.AddSeconds(5));
}
