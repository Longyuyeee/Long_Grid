using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerRemovalUndoTests
{
    [Fact]
    public void PrepareAndConfirmBindRevisionTokenAndFingerprints()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState removed) = States();
        ProductWorkspaceContainerRemovalUndoToken token =
            ProductWorkspaceContainerRemovalUndo.Prepare(
                restore,
                removed,
                removalEditRevision: 4,
                Guid.NewGuid())!;

        ProductWorkspaceContainerRemovalUndoResult confirmationRequired =
            ProductWorkspaceContainerRemovalUndo.Confirm(
                removed, restore, 4, token, token, confirmed: false);
        ProductWorkspaceContainerRemovalUndoResult accepted =
            ProductWorkspaceContainerRemovalUndo.Confirm(
                removed, restore, 4, token, token, confirmed: true);

        Assert.Equal(
            ProductWorkspaceContainerRemovalUndoStatus.ConfirmationRequired,
            confirmationRequired.Status);
        Assert.True(accepted.IsAccepted);
        Assert.Equal(64, token.RemovedConfigurationFingerprint.Length);
        Assert.Equal(64, token.RestoreConfigurationFingerprint.Length);
    }

    [Fact]
    public void ConfirmRejectsStaleMismatchedAndChangedEvidence()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState removed) = States();
        ProductWorkspaceContainerRemovalUndoToken token =
            ProductWorkspaceContainerRemovalUndo.Prepare(
                restore, removed, 4, Guid.NewGuid())!;

        Assert.Equal(
            ProductWorkspaceContainerRemovalUndoStatus.EditRevisionChanged,
            ProductWorkspaceContainerRemovalUndo.Confirm(
                removed, restore, 5, token, token, true).Status);
        Assert.Equal(
            ProductWorkspaceContainerRemovalUndoStatus.TokenMismatch,
            ProductWorkspaceContainerRemovalUndo.Confirm(
                removed,
                restore,
                4,
                token with { OperationId = Guid.NewGuid() },
                token,
                true).Status);
        Assert.Equal(
            ProductWorkspaceContainerRemovalUndoStatus
                .CurrentConfigurationChanged,
            ProductWorkspaceContainerRemovalUndo.Confirm(
                restore, restore, 4, token, token, true).Status);
    }

    [Fact]
    public void PrepareRejectsInvalidOrUnchangedRequest()
    {
        (ProductWorkspaceState restore, ProductWorkspaceState removed) = States();

        Assert.Null(ProductWorkspaceContainerRemovalUndo.Prepare(
            restore, restore, 1, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceContainerRemovalUndo.Prepare(
            restore, removed, 0, Guid.NewGuid()));
        Assert.Null(ProductWorkspaceContainerRemovalUndo.Prepare(
            restore, removed, 1, Guid.Empty));
    }

    private static (ProductWorkspaceState Restore, ProductWorkspaceState Removed)
        States()
    {
        ProductContainerState container = new()
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
            Items = Array.Empty<ProductItemReferenceState>(),
        };
        return (
            new() { ProfileId = "default", Containers = [container] },
            new()
            {
                ProfileId = "default",
                Containers = Array.Empty<ProductContainerState>(),
            });
    }
}
