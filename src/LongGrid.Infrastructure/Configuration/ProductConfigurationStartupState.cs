namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationStartupMode
{
    NoSavedConfiguration,
    LoadedPrimary,
    RecoveredBackupReadOnly,
    SafeMode,
}

public sealed record ProductConfigurationStartupState(
    ProductConfigurationStartupMode Mode,
    ProductConfigurationStorageFailure PrimaryFailure,
    ProductConfigurationStorageFailure BackupFailure)
{
    public bool RequiresRecoveryNotice => Mode is
        ProductConfigurationStartupMode.RecoveredBackupReadOnly or
        ProductConfigurationStartupMode.SafeMode;

    public static ProductConfigurationStartupState FromLoadResult(
        ProductConfigurationLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ProductConfigurationStartupMode mode = result.Status switch
        {
            ProductConfigurationLoadStatus.Missing =>
                ProductConfigurationStartupMode.NoSavedConfiguration,
            ProductConfigurationLoadStatus.LoadedPrimary =>
                ProductConfigurationStartupMode.LoadedPrimary,
            ProductConfigurationLoadStatus.RecoveredFromBackup =>
                ProductConfigurationStartupMode.RecoveredBackupReadOnly,
            ProductConfigurationLoadStatus.SafeMode =>
                ProductConfigurationStartupMode.SafeMode,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Status,
                "Unknown product configuration load status."),
        };

        return new(mode, result.PrimaryFailure, result.BackupFailure);
    }
}
