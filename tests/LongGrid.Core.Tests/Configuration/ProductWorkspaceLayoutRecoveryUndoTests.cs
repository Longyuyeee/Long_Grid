using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceLayoutRecoveryUndoTests
{
    private static readonly Guid OperationId =
        Guid.Parse("8ea5bf0f-2c80-4dd7-a86b-4aa3d67ab7f2");

    [Fact]
    public void PrepareBindsOperationRevisionFingerprintsAndCount()
    {
        (ProductWorkspaceState before, ProductWorkspaceState recovered) = Fixture();

        ProductWorkspaceLayoutRecoveryUndoToken? token =
            ProductWorkspaceLayoutRecoveryUndo.Prepare(
                before,
                recovered,
                7,
                OperationId);

        Assert.NotNull(token);
        Assert.Equal(OperationId, token.OperationId);
        Assert.Equal(7, token.RecoveryEditRevision);
        Assert.Equal(1, token.ContainerCount);
        Assert.NotEqual(
            token.RestoreConfigurationFingerprint,
            token.RecoveredConfigurationFingerprint);
        Assert.Equal(64, token.RestoreConfigurationFingerprint.Length);
        Assert.Equal(64, token.RecoveredConfigurationFingerprint.Length);
    }

    [Fact]
    public void ConfirmRestoresOnlyBoundConfigurationAfterExplicitApproval()
    {
        (ProductWorkspaceState before, ProductWorkspaceState recovered) = Fixture();
        ProductWorkspaceLayoutRecoveryUndoToken token = Prepare(before, recovered);

        ProductWorkspaceLayoutRecoveryUndoResult result =
            ProductWorkspaceLayoutRecoveryUndo.Confirm(
                recovered,
                before,
                7,
                token,
                token,
                confirmed: true);

        Assert.True(result.IsAccepted);
        Assert.Same(before, result.Edit!.State);
        Assert.True(result.Edit.Changed);
    }

    [Fact]
    public void ConfirmationRevisionTokenAndCurrentConfigurationAreGated()
    {
        (ProductWorkspaceState before, ProductWorkspaceState recovered) = Fixture();
        ProductWorkspaceLayoutRecoveryUndoToken token = Prepare(before, recovered);

        Assert.Equal(
            ProductWorkspaceLayoutRecoveryUndoStatus.ConfirmationRequired,
            Confirm(recovered, before, 7, token, token, false).Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryUndoStatus.EditRevisionChanged,
            Confirm(recovered, before, 8, token, token, true).Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryUndoStatus.TokenMismatch,
            Confirm(
                recovered,
                before,
                7,
                token with { OperationId = Guid.NewGuid() },
                token,
                true).Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryUndoStatus.CurrentConfigurationChanged,
            Confirm(
                recovered with { ProfileId = "changed" },
                before,
                7,
                token,
                token,
                true).Status);
        Assert.Equal(
            ProductWorkspaceLayoutRecoveryUndoStatus.TokenMismatch,
            Confirm(
                recovered,
                before with { ProfileId = "changed" },
                7,
                token,
                token,
                true).Status);
    }

    [Fact]
    public void InvalidPreparationAndInputsAreRejected()
    {
        (ProductWorkspaceState before, ProductWorkspaceState recovered) = Fixture();
        Assert.Null(ProductWorkspaceLayoutRecoveryUndo.Prepare(
            before, before, 7, OperationId));
        Assert.Null(ProductWorkspaceLayoutRecoveryUndo.Prepare(
            before, recovered, 0, OperationId));
        Assert.Null(ProductWorkspaceLayoutRecoveryUndo.Prepare(
            before, recovered, 7, Guid.Empty));
        Assert.Throws<ArgumentNullException>(
            () => ProductWorkspaceLayoutRecoveryUndo.Prepare(
                null!, recovered, 7, OperationId));
        ProductWorkspaceLayoutRecoveryUndoToken token = Prepare(before, recovered);
        Assert.Throws<ArgumentNullException>(
            () => ProductWorkspaceLayoutRecoveryUndo.Confirm(
                recovered, before, 7, null!, token, true));
    }

    private static ProductWorkspaceLayoutRecoveryUndoResult Confirm(
        ProductWorkspaceState current,
        ProductWorkspaceState restore,
        long revision,
        ProductWorkspaceLayoutRecoveryUndoToken token,
        ProductWorkspaceLayoutRecoveryUndoToken expected,
        bool confirmed) =>
        ProductWorkspaceLayoutRecoveryUndo.Confirm(
            current,
            restore,
            revision,
            token,
            expected,
            confirmed);

    private static ProductWorkspaceLayoutRecoveryUndoToken Prepare(
        ProductWorkspaceState before,
        ProductWorkspaceState recovered) =>
        ProductWorkspaceLayoutRecoveryUndo.Prepare(
            before,
            recovered,
            7,
            OperationId)!;

    private static (ProductWorkspaceState Before, ProductWorkspaceState Recovered)
        Fixture()
    {
        ProductWorkspaceState before = new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };
        ProductWorkspaceState recovered = before with
        {
            Containers =
            [
                before.Containers[0] with
                {
                    Placement = before.Containers[0].Placement with
                    {
                        DisplayKey = "display-current",
                        XDip = 21,
                        YDip = 32,
                    },
                },
            ],
        };
        return (before, recovered);
    }
}
