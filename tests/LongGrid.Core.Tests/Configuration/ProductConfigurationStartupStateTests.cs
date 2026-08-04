using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationStartupStateTests
{
    public static TheoryData<
        ProductConfigurationLoadStatus,
        ProductConfigurationStartupMode,
        bool> Modes => new()
        {
            {
                ProductConfigurationLoadStatus.Missing,
                ProductConfigurationStartupMode.NoSavedConfiguration,
                false
            },
            {
                ProductConfigurationLoadStatus.LoadedPrimary,
                ProductConfigurationStartupMode.LoadedPrimary,
                false
            },
            {
                ProductConfigurationLoadStatus.RecoveredFromBackup,
                ProductConfigurationStartupMode.RecoveredBackupReadOnly,
                true
            },
            {
                ProductConfigurationLoadStatus.SafeMode,
                ProductConfigurationStartupMode.SafeMode,
                true
            },
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public void LoadStatusMapsToFiniteStartupMode(
        ProductConfigurationLoadStatus loadStatus,
        ProductConfigurationStartupMode expectedMode,
        bool requiresNotice)
    {
        ProductConfigurationLoadResult result = new(
            loadStatus,
            null,
            ProductConfigurationStorageFailure.InvalidConfiguration,
            ProductConfigurationStorageFailure.Missing,
            ProductConfigurationError.MalformedJson,
            ProductConfigurationError.None);

        ProductConfigurationStartupState state =
            ProductConfigurationStartupState.FromLoadResult(result);

        Assert.Equal(expectedMode, state.Mode);
        Assert.Equal(requiresNotice, state.RequiresRecoveryNotice);
        Assert.Equal(
            ProductConfigurationStorageFailure.InvalidConfiguration,
            state.PrimaryFailure);
        Assert.Equal(
            ProductConfigurationStorageFailure.Missing,
            state.BackupFailure);
    }

    [Fact]
    public void NullLoadResultIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => ProductConfigurationStartupState.FromLoadResult(null!));
    }
}
