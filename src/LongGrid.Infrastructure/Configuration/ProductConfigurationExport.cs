namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationExportError
{
    ConfirmationRequired,
    ExportNotAvailable,
    DestinationNotUserSelected,
    NonLocalDestination,
    ReparsePointNotAllowed,
    StoreChanged,
    DestinationUnavailable,
    IoFailure,
}

public enum ProductConfigurationExportSourceState
{
    LoadedPrimary,
    RecoveredBackupReadOnly,
}

public sealed record ProductConfigurationExportDestination(
    bool UserSelected,
    bool IsLocalFileSystem,
    bool IsReparsePoint);

public sealed record ProductConfigurationExportPreview(
    int SchemaVersion,
    int ContainerCount,
    int ItemCount,
    ProductConfigurationExportSourceState SourceState);

public sealed class ProductConfigurationExportPlan
{
    private readonly byte[] payload;

    internal ProductConfigurationExportPlan(
        ProductConfigurationExportPreview preview,
        byte[] payload,
        string expectedStoreRevision)
    {
        Preview = preview;
        this.payload = payload.ToArray();
        ExpectedStoreRevision = expectedStoreRevision;
    }

    public ProductConfigurationExportPreview Preview { get; }

    internal string ExpectedStoreRevision { get; }

    internal ReadOnlyMemory<byte> Payload => payload;
}

public sealed record ProductConfigurationExportResult(string FileName);

public sealed class ProductConfigurationExportException(
    ProductConfigurationExportError error)
    : IOException($"Product configuration export failed: {error}.")
{
    public ProductConfigurationExportError Error { get; } = error;
}

public enum ProductConfigurationEvidenceOrigin
{
    DamagedRecovery,
    ImportPrevious,
}

public enum ProductConfigurationEvidenceRole
{
    Primary,
    Backup,
}

public sealed record ProductConfigurationEvidenceItem(
    ProductConfigurationEvidenceOrigin Origin,
    ProductConfigurationEvidenceRole Role,
    long SizeBytes,
    DateTimeOffset ArchivedUtc);

public sealed record ProductConfigurationEvidenceInventory(
    IReadOnlyList<ProductConfigurationEvidenceItem> Items,
    bool Truncated,
    int SkippedUnsafeCount);
