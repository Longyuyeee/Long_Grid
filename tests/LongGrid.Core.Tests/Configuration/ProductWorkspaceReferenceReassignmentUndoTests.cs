using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReferenceReassignmentUndoTests
{
    [Fact]
    public void PrepareAndConfirmBindRevisionTokenAndFingerprints()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState reassigned) = States();
        ProductWorkspaceReferenceReassignmentUndoToken token =
            ProductWorkspaceReferenceReassignmentUndo.Prepare(
                restore,
                reassigned,
                reassignmentEditRevision: 4,
                Guid.NewGuid())!;

        ProductWorkspaceReferenceReassignmentUndoResult confirmationRequired =
            ProductWorkspaceReferenceReassignmentUndo.Confirm(
                reassigned, restore, 4, token, token, confirmed: false);
        ProductWorkspaceReferenceReassignmentUndoResult accepted =
            ProductWorkspaceReferenceReassignmentUndo.Confirm(
                reassigned, restore, 4, token, token, confirmed: true);

        Assert.Equal(
            ProductWorkspaceReferenceReassignmentUndoStatus.ConfirmationRequired,
            confirmationRequired.Status);
        Assert.True(accepted.IsAccepted);
        Assert.Equal(64, token.ReassignedConfigurationFingerprint.Length);
        Assert.Equal(64, token.RestoreConfigurationFingerprint.Length);
    }

    [Fact]
    public void ConfirmRejectsStaleMismatchedAndChangedEvidence()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState reassigned) = States();
        ProductWorkspaceReferenceReassignmentUndoToken token =
            ProductWorkspaceReferenceReassignmentUndo.Prepare(
                restore, reassigned, 4, Guid.NewGuid())!;

        Assert.Equal(
            ProductWorkspaceReferenceReassignmentUndoStatus.EditRevisionChanged,
            ProductWorkspaceReferenceReassignmentUndo.Confirm(
                reassigned, restore, 5, token, token, true).Status);
        Assert.Equal(
            ProductWorkspaceReferenceReassignmentUndoStatus.TokenMismatch,
            ProductWorkspaceReferenceReassignmentUndo.Confirm(
                reassigned,
                restore,
                4,
                token with { OperationId = Guid.NewGuid() },
                token,
                true).Status);
        Assert.Equal(
            ProductWorkspaceReferenceReassignmentUndoStatus
                .CurrentConfigurationChanged,
            ProductWorkspaceReferenceReassignmentUndo.Confirm(
                restore, restore, 4, token, token, true).Status);
    }

    [Fact]
    public void PrepareRejectsInvalidOrUnchangedRequest()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState reassigned) = States();

        Assert.Null(ProductWorkspaceReferenceReassignmentUndo.Prepare(
            restore, restore, 1, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceReferenceReassignmentUndo.Prepare(
            restore, reassigned, 0, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceReferenceReassignmentUndo.Prepare(
            restore, reassigned, 1, Guid.Empty));
    }

    private static (ProductWorkspaceState Restore, ProductWorkspaceState Reassigned)
        States()
    {
        DesktopCatalogEntry entry = new(
            new DesktopItemIdentity("filesystem", @"C:\Desktop\keep.txt"),
            "user-desktop",
            "keep.txt",
            DesktopItemKind.File);
        ProductItemReferenceState item =
            ProductItemReferenceState.CreateResolved("item-1", entry);
        ProductContainerState source = Container("container-1", "Source", [item]);
        ProductContainerState target = Container(
            "container-2",
            "Target",
            Array.Empty<ProductItemReferenceState>());
        return (
            State(source, target),
            State(
                source with { Items = Array.Empty<ProductItemReferenceState>() },
                target with { Items = [item] }));
    }

    private static ProductWorkspaceState State(
        params ProductContainerState[] containers) =>
        new() { ProfileId = "default", Containers = containers };

    private static ProductContainerState Container(
        string id,
        string name,
        IReadOnlyList<ProductItemReferenceState> items) =>
        new()
        {
            Id = id,
            Name = name,
            Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
            Placement = new()
            {
                DisplayKey = "display-unassigned",
                WidthDip = 360,
                HeightDip = 240,
            },
            Items = items,
        };
}
