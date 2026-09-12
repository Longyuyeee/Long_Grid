using LongGrid.App;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCreateAdmissionTests
{
    [Theory]
    [InlineData(ProductWorkspaceSessionStatus.Loading)]
    [InlineData(ProductWorkspaceSessionStatus.SafeMode)]
    [InlineData(ProductWorkspaceSessionStatus.Failed)]
    [InlineData(ProductWorkspaceSessionStatus.RecoveredBackupReadOnly)]
    [InlineData(ProductWorkspaceSessionStatus.AwaitingCatalog)]
    public void ProtectedSessionCannotCreateEvenWithState(ProductWorkspaceSessionStatus status)
    {
        var session = ProductWorkspaceSessionSnapshot.Initial with
        {
            Status = status,
            State = ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(), []).State,
        };
        Assert.Null(ProductWorkspaceCreateAdmission.Resolve(
            session, ProductConfigurationLoadStatus.Missing));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ProductConfigurationLoadStatus.SafeMode)]
    [InlineData(ProductConfigurationLoadStatus.LoadedPrimary)]
    [InlineData(ProductConfigurationLoadStatus.RecoveredFromBackup)]
    public void FirstRunRequiresConfirmedMissingLoad(ProductConfigurationLoadStatus? status)
    {
        var session = ProductWorkspaceSessionSnapshot.Initial with
        {
            Status = ProductWorkspaceSessionStatus.NoSavedConfiguration,
        };
        Assert.Null(ProductWorkspaceCreateAdmission.Resolve(session, status));
    }

    [Fact]
    public void ReadySessionPreservesExistingState()
    {
        var session = ProductWorkspaceSessionSnapshot.Initial with
        {
            Status = ProductWorkspaceSessionStatus.Ready,
            IsReadOnly = false,
            State = ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(), []).State,
        };
        Assert.Same(session.State, ProductWorkspaceCreateAdmission.Resolve(
            session, ProductConfigurationLoadStatus.LoadedPrimary));
    }
}
