namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationImportError
{
    ConfirmationRequired,
    SourceNotUserSelected,
    UnsupportedFileType,
    NonLocalSource,
    ReparsePointNotAllowed,
    SourceUnavailable,
    EmptyDocument,
    DocumentTooLarge,
    InvalidConfiguration,
    StoreChanged,
    WriteLeaseUnavailable,
    IoFailure,
}

public enum ProductConfigurationImportExistingState
{
    NoSavedConfiguration,
    LoadedPrimary,
    RecoveredBackupReadOnly,
    SafeMode,
}

public sealed record ProductConfigurationImportSource(
    bool UserSelected,
    string FileExtension,
    bool IsLocalFileSystem,
    bool IsReparsePoint);

public sealed record ProductConfigurationImportPreview(
    int SchemaVersion,
    int ContainerCount,
    int ItemCount,
    ProductConfigurationImportExistingState ExistingState);

public sealed class ProductConfigurationImportPlan
{
    private readonly byte[] payload;

    internal ProductConfigurationImportPlan(
        ProductConfigurationImportPreview preview,
        byte[] payload,
        string expectedStoreRevision)
    {
        Preview = preview;
        this.payload = payload.ToArray();
        ExpectedStoreRevision = expectedStoreRevision;
    }

    public ProductConfigurationImportPreview Preview { get; }

    internal string ExpectedStoreRevision { get; }

    internal ReadOnlyMemory<byte> Payload => payload;
}

public sealed record ProductConfigurationImportResult(
    bool PrimaryArchived,
    bool BackupArchived);

public sealed class ProductConfigurationImportException(
    ProductConfigurationImportError error)
    : IOException($"Product configuration import failed: {error}.")
{
    public ProductConfigurationImportError Error { get; } = error;
}
