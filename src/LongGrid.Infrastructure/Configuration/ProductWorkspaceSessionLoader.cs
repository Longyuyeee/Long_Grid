using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductWorkspaceCatalogAvailability
{
    Unavailable,
    Available,
}

public sealed record ProductWorkspaceCatalogSnapshot
{
    private ProductWorkspaceCatalogSnapshot(
        ProductWorkspaceCatalogAvailability availability,
        IReadOnlyList<DesktopCatalogEntry> entries)
    {
        Availability = availability;
        Entries = entries;
    }

    public ProductWorkspaceCatalogAvailability Availability { get; }

    public IReadOnlyList<DesktopCatalogEntry> Entries { get; }

    public static ProductWorkspaceCatalogSnapshot Unavailable { get; } =
        new(
            ProductWorkspaceCatalogAvailability.Unavailable,
            Array.Empty<DesktopCatalogEntry>());

    public static ProductWorkspaceCatalogSnapshot Available(
        IReadOnlyList<DesktopCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new(
            ProductWorkspaceCatalogAvailability.Available,
            Array.AsReadOnly(entries.ToArray()));
    }
}

public enum ProductWorkspaceSessionStatus
{
    Loading,
    NoSavedConfiguration,
    AwaitingCatalog,
    Ready,
    RecoveredBackupReadOnly,
    SafeMode,
    Failed,
}

public enum ProductWorkspaceSessionSource
{
    None,
    Primary,
    RecoveredBackup,
}

public enum ProductWorkspaceSessionFailure
{
    None,
    InconsistentLoadResult,
    InvalidConfiguration,
    InvalidCatalog,
}

public sealed record ProductWorkspaceSessionSnapshot(
    ProductWorkspaceSessionStatus Status,
    ProductWorkspaceSessionSource Source,
    ProductWorkspaceCatalogAvailability CatalogAvailability,
    ProductWorkspaceState? State,
    ProductWorkspaceResolutionSummary Summary,
    ProductWorkspaceSessionFailure Failure,
    bool IsReadOnly)
{
    public static ProductWorkspaceSessionSnapshot Initial { get; } = new(
        ProductWorkspaceSessionStatus.Loading,
        ProductWorkspaceSessionSource.None,
        ProductWorkspaceCatalogAvailability.Unavailable,
        null,
        new(0, 0, 0, 0, 0),
        ProductWorkspaceSessionFailure.None,
        IsReadOnly: true);

    public bool HasResolvedState => State is not null;
}

public static class ProductWorkspaceSessionLoader
{
    public static ProductWorkspaceSessionSnapshot Load(
        ProductConfigurationLoadResult loadResult,
        ProductWorkspaceCatalogSnapshot catalogSnapshot)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        ArgumentNullException.ThrowIfNull(catalogSnapshot);

        return loadResult.Status switch
        {
            ProductConfigurationLoadStatus.Missing =>
                loadResult.Document is null
                    ? CreateWithoutState(
                        ProductWorkspaceSessionStatus.NoSavedConfiguration,
                        catalogSnapshot.Availability)
                    : Failure(
                        ProductWorkspaceSessionFailure.InconsistentLoadResult,
                        catalogSnapshot.Availability),
            ProductConfigurationLoadStatus.SafeMode =>
                loadResult.Document is null
                    ? CreateWithoutState(
                        ProductWorkspaceSessionStatus.SafeMode,
                        catalogSnapshot.Availability)
                    : Failure(
                        ProductWorkspaceSessionFailure.InconsistentLoadResult,
                        catalogSnapshot.Availability),
            ProductConfigurationLoadStatus.LoadedPrimary => LoadDocument(
                loadResult.Document,
                ProductWorkspaceSessionSource.Primary,
                isReadOnly: false,
                catalogSnapshot),
            ProductConfigurationLoadStatus.RecoveredFromBackup => LoadDocument(
                loadResult.Document,
                ProductWorkspaceSessionSource.RecoveredBackup,
                isReadOnly: true,
                catalogSnapshot),
            _ => Failure(
                ProductWorkspaceSessionFailure.InconsistentLoadResult,
                catalogSnapshot.Availability),
        };
    }

    private static ProductWorkspaceSessionSnapshot LoadDocument(
        ProductConfigurationDocument? document,
        ProductWorkspaceSessionSource source,
        bool isReadOnly,
        ProductWorkspaceCatalogSnapshot catalogSnapshot)
    {
        if (document is null)
        {
            return Failure(
                ProductWorkspaceSessionFailure.InconsistentLoadResult,
                catalogSnapshot.Availability,
                source);
        }

        ProductConfigurationValidationResult validation =
            ProductConfigurationValidator.Validate(document);
        if (!validation.IsValid)
        {
            return Failure(
                ProductWorkspaceSessionFailure.InvalidConfiguration,
                catalogSnapshot.Availability,
                source);
        }

        bool hasReferences = document.Containers.Any(
            container => container.Items.Count > 0);
        if (catalogSnapshot.Availability == ProductWorkspaceCatalogAvailability.Unavailable
            && hasReferences)
        {
            return new(
                ProductWorkspaceSessionStatus.AwaitingCatalog,
                source,
                catalogSnapshot.Availability,
                null,
                new(0, 0, 0, 0, 0),
                ProductWorkspaceSessionFailure.None,
                isReadOnly);
        }

        ProductWorkspaceResolutionResult resolution =
            ProductWorkspaceConfigurationResolver.Resolve(
                document,
                catalogSnapshot.Availability ==
                    ProductWorkspaceCatalogAvailability.Available
                    ? catalogSnapshot.Entries
                    : Array.Empty<DesktopCatalogEntry>());
        if (!resolution.IsSuccess)
        {
            ProductWorkspaceSessionFailure failure = resolution.Error switch
            {
                ProductWorkspaceResolutionError.InvalidConfiguration =>
                    ProductWorkspaceSessionFailure.InvalidConfiguration,
                ProductWorkspaceResolutionError.InvalidCatalog =>
                    ProductWorkspaceSessionFailure.InvalidCatalog,
                _ => ProductWorkspaceSessionFailure.InconsistentLoadResult,
            };
            return Failure(
                failure,
                catalogSnapshot.Availability,
                source);
        }

        ProductWorkspaceState state =
            WindowsProductContainerFolderBinding.ResolveWorkspace(
                resolution.State!);
        return new(
            isReadOnly
                ? ProductWorkspaceSessionStatus.RecoveredBackupReadOnly
                : ProductWorkspaceSessionStatus.Ready,
            source,
            catalogSnapshot.Availability,
            state,
            resolution.Summary,
            ProductWorkspaceSessionFailure.None,
            isReadOnly);
    }

    private static ProductWorkspaceSessionSnapshot CreateWithoutState(
        ProductWorkspaceSessionStatus status,
        ProductWorkspaceCatalogAvailability catalogAvailability) =>
        new(
            status,
            ProductWorkspaceSessionSource.None,
            catalogAvailability,
            null,
            new(0, 0, 0, 0, 0),
            ProductWorkspaceSessionFailure.None,
            IsReadOnly: true);

    private static ProductWorkspaceSessionSnapshot Failure(
        ProductWorkspaceSessionFailure failure,
        ProductWorkspaceCatalogAvailability catalogAvailability,
        ProductWorkspaceSessionSource source = ProductWorkspaceSessionSource.None) =>
        new(
            ProductWorkspaceSessionStatus.Failed,
            source,
            catalogAvailability,
            null,
            new(0, 0, 0, 0, 0),
            failure,
            IsReadOnly: true);
}
