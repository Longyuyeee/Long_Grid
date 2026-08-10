using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReferenceRemovalUndoTests
{
    [Fact]
    public void PrepareAndConfirmBindRevisionTokenAndConfigurationFingerprints()
    {
        ProductWorkspaceState restore = State(withItem: true);
        ProductWorkspaceState removed = State(withItem: false);
        ProductWorkspaceReferenceRemovalUndoToken token =
            ProductWorkspaceReferenceRemovalUndo.Prepare(
                restore,
                removed,
                removalEditRevision: 4,
                Guid.NewGuid())!;

        ProductWorkspaceReferenceRemovalUndoResult confirmationRequired =
            ProductWorkspaceReferenceRemovalUndo.Confirm(
                removed, restore, 4, token, token, confirmed: false);
        ProductWorkspaceReferenceRemovalUndoResult accepted =
            ProductWorkspaceReferenceRemovalUndo.Confirm(
                removed, restore, 4, token, token, confirmed: true);

        Assert.Equal(
            ProductWorkspaceReferenceRemovalUndoStatus.ConfirmationRequired,
            confirmationRequired.Status);
        Assert.True(accepted.IsAccepted);
        Assert.Equal(64, token.RemovedConfigurationFingerprint.Length);
        Assert.Equal(64, token.RestoreConfigurationFingerprint.Length);
    }

    [Fact]
    public void ConfirmRejectsStaleMismatchedAndChangedEvidence()
    {
        ProductWorkspaceState restore = State(withItem: true);
        ProductWorkspaceState removed = State(withItem: false);
        ProductWorkspaceReferenceRemovalUndoToken token =
            ProductWorkspaceReferenceRemovalUndo.Prepare(
                restore, removed, 4, Guid.NewGuid())!;

        Assert.Equal(
            ProductWorkspaceReferenceRemovalUndoStatus.EditRevisionChanged,
            ProductWorkspaceReferenceRemovalUndo.Confirm(
                removed, restore, 5, token, token, true).Status);
        Assert.Equal(
            ProductWorkspaceReferenceRemovalUndoStatus.TokenMismatch,
            ProductWorkspaceReferenceRemovalUndo.Confirm(
                removed,
                restore,
                4,
                token with { OperationId = Guid.NewGuid() },
                token,
                true).Status);
        Assert.Equal(
            ProductWorkspaceReferenceRemovalUndoStatus.CurrentConfigurationChanged,
            ProductWorkspaceReferenceRemovalUndo.Confirm(
                restore, restore, 4, token, token, true).Status);
    }

    [Fact]
    public void PrepareRejectsInvalidOrUnchangedRequest()
    {
        ProductWorkspaceState state = State(withItem: false);

        Assert.Null(ProductWorkspaceReferenceRemovalUndo.Prepare(
            state, state, 1, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceReferenceRemovalUndo.Prepare(
            State(withItem: true), state, 0, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceReferenceRemovalUndo.Prepare(
            State(withItem: true), state, 1, Guid.Empty));
    }

    private static ProductWorkspaceState State(bool withItem)
    {
        DesktopCatalogEntry entry = new(
            new DesktopItemIdentity("filesystem", @"C:\Desktop\keep.txt"),
            "user-desktop",
            "keep.txt",
            DesktopItemKind.File);
        return new()
        {
            ProfileId = "default",
            Containers =
            [
                new()
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-unassigned",
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = withItem
                        ? [ProductItemReferenceState.CreateResolved("item-1", entry)]
                        : Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };
    }
}
