namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationExportError
{
    ConfirmationRequired,
    ExportNotAvailable,
    DestinationNotUserSelected,
    NonLocalDestination,
    ReparsePointNotAllowed,
    StoreChanged,
    EvidenceNotAvailable,
    EvidenceChanged,
    EvidenceTooLarge,
    EvidenceVerificationFailed,
    WriteLeaseUnavailable,
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

public sealed class ProductConfigurationEvidenceItem
{
    internal ProductConfigurationEvidenceItem(
        ProductConfigurationEvidenceOrigin origin,
        ProductConfigurationEvidenceRole role,
        long sizeBytes,
        DateTimeOffset archivedUtc,
        string sourcePath)
    {
        Origin = origin;
        Role = role;
        SizeBytes = sizeBytes;
        ArchivedUtc = archivedUtc;
        SourcePath = sourcePath;
    }

    public ProductConfigurationEvidenceOrigin Origin { get; }

    public ProductConfigurationEvidenceRole Role { get; }

    public long SizeBytes { get; }

    public DateTimeOffset ArchivedUtc { get; }

    internal string SourcePath { get; }
}

public sealed record ProductConfigurationEvidenceInventory(
    IReadOnlyList<ProductConfigurationEvidenceItem> Items,
    bool Truncated,
    int SkippedUnsafeCount,
    int ObservedItemCount,
    long ObservedSizeBytes,
    DateTimeOffset? OldestObservedArchivedUtc);

public sealed record ProductConfigurationEvidenceRemovalResult(
    ProductConfigurationEvidenceOrigin Origin,
    ProductConfigurationEvidenceRole Role,
    long SizeBytes);
