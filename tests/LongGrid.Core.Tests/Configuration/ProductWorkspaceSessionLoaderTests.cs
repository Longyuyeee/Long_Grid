using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSessionLoaderTests
{
    [Fact]
    public void MissingConfigurationDoesNotRequireCatalogOrCreateState()
    {
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(ProductConfigurationLoadStatus.Missing),
            ProductWorkspaceCatalogSnapshot.Unavailable);

        Assert.Equal(
            ProductWorkspaceSessionStatus.NoSavedConfiguration,
            snapshot.Status);
        Assert.False(snapshot.HasResolvedState);
        Assert.True(snapshot.IsReadOnly);
    }

    [Fact]
    public void LoadedConfigurationWaitsForCatalogWithoutClassifyingReferencesMissing()
    {
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(
                ProductConfigurationLoadStatus.LoadedPrimary,
                CreateDocument()),
            ProductWorkspaceCatalogSnapshot.Unavailable);

        Assert.Equal(ProductWorkspaceSessionStatus.AwaitingCatalog, snapshot.Status);
        Assert.Equal(ProductWorkspaceSessionSource.Primary, snapshot.Source);
        Assert.Equal(0, snapshot.Summary.Total);
        Assert.False(snapshot.HasResolvedState);
        Assert.False(snapshot.IsReadOnly);
    }

    [Fact]
    public void AuthoritativeEmptyCatalogProducesExplicitMissingReference()
    {
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(
                ProductConfigurationLoadStatus.LoadedPrimary,
                CreateDocument()),
            ProductWorkspaceCatalogSnapshot.Available([]));

        Assert.Equal(ProductWorkspaceSessionStatus.Ready, snapshot.Status);
        Assert.True(snapshot.HasResolvedState);
        Assert.Equal(1, snapshot.Summary.Missing);
        Assert.Equal(
            ProductItemReferenceResolution.Missing,
            snapshot.State!.Containers[0].Items[0].Resolution);
    }

    [Fact]
    public void AvailableCatalogResolvesPrimaryConfigurationForProductSession()
    {
        string target = Target();
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(
                ProductConfigurationLoadStatus.LoadedPrimary,
                CreateDocument(target)),
            ProductWorkspaceCatalogSnapshot.Available(
                [CreateEntry(target)]));

        Assert.Equal(ProductWorkspaceSessionStatus.Ready, snapshot.Status);
        Assert.Equal(ProductWorkspaceSessionSource.Primary, snapshot.Source);
        Assert.Equal(ProductWorkspaceCatalogAvailability.Available, snapshot.CatalogAvailability);
        Assert.False(snapshot.IsReadOnly);
        Assert.Equal(1, snapshot.Summary.Resolved);
    }

    [Fact]
    public void RecoveredBackupSessionRemainsReadOnlyAfterResolution()
    {
        string target = Target();
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(
                ProductConfigurationLoadStatus.RecoveredFromBackup,
                CreateDocument(target)),
            ProductWorkspaceCatalogSnapshot.Available(
                [CreateEntry(target)]));

        Assert.Equal(
            ProductWorkspaceSessionStatus.RecoveredBackupReadOnly,
            snapshot.Status);
        Assert.Equal(ProductWorkspaceSessionSource.RecoveredBackup, snapshot.Source);
        Assert.True(snapshot.HasResolvedState);
        Assert.True(snapshot.IsReadOnly);
    }

    [Fact]
    public void SafeModeNeverCreatesProductState()
    {
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(ProductConfigurationLoadStatus.SafeMode),
            ProductWorkspaceCatalogSnapshot.Available([]));

        Assert.Equal(ProductWorkspaceSessionStatus.SafeMode, snapshot.Status);
        Assert.False(snapshot.HasResolvedState);
        Assert.True(snapshot.IsReadOnly);
    }

    [Fact]
    public void InconsistentLoadResultFailsClosed()
    {
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(ProductConfigurationLoadStatus.LoadedPrimary),
            ProductWorkspaceCatalogSnapshot.Available([]));

        Assert.Equal(ProductWorkspaceSessionStatus.Failed, snapshot.Status);
        Assert.Equal(
            ProductWorkspaceSessionFailure.InconsistentLoadResult,
            snapshot.Failure);
        Assert.True(snapshot.IsReadOnly);
    }

    [Fact]
    public void InvalidConfigurationFailsBeforeCatalogBecomesAvailable()
    {
        ProductConfigurationDocument invalid = CreateDocument() with
        {
            ProfileId = string.Empty,
        };
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(ProductConfigurationLoadStatus.LoadedPrimary, invalid),
            ProductWorkspaceCatalogSnapshot.Unavailable);

        Assert.Equal(ProductWorkspaceSessionStatus.Failed, snapshot.Status);
        Assert.Equal(
            ProductWorkspaceSessionFailure.InvalidConfiguration,
            snapshot.Failure);
        Assert.False(snapshot.HasResolvedState);
    }

    [Fact]
    public void InvalidCatalogFailsWithFiniteError()
    {
        DesktopCatalogEntry invalidEntry = CreateEntry(Target()) with
        {
            SourceId = string.Empty,
        };
        ProductWorkspaceSessionSnapshot snapshot = ProductWorkspaceSessionLoader.Load(
            LoadResult(
                ProductConfigurationLoadStatus.LoadedPrimary,
                CreateDocument()),
            ProductWorkspaceCatalogSnapshot.Available([invalidEntry]));

        Assert.Equal(ProductWorkspaceSessionStatus.Failed, snapshot.Status);
        Assert.Equal(ProductWorkspaceSessionFailure.InvalidCatalog, snapshot.Failure);
        Assert.False(snapshot.HasResolvedState);
    }

    private static ProductConfigurationLoadResult LoadResult(
        ProductConfigurationLoadStatus status,
        ProductConfigurationDocument? document = null) =>
        new(
            status,
            document,
            ProductConfigurationStorageFailure.None,
            ProductConfigurationStorageFailure.None,
            ProductConfigurationError.None,
            ProductConfigurationError.None);

    private static ProductConfigurationDocument CreateDocument(string? target = null) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "session-profile",
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "container-1",
                    Name = "Workspace",
                    Appearance = new ContainerAppearanceConfiguration
                    {
                        Color = "#334155",
                        Opacity = 0.8,
                    },
                    Placement = new ContainerPlacementConfiguration
                    {
                        DisplayKey = "display-1",
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        new DesktopItemReferenceConfiguration
                        {
                            Id = "item-1",
                            Kind = ConfigurationItemKind.File,
                            Target = target ?? Target(),
                            Behavior = ConfigurationItemBehavior.Reference,
                        },
                    ],
                },
            ],
        };

    private static DesktopCatalogEntry CreateEntry(string target) =>
        new(
            new DesktopItemIdentity("filesystem", target),
            "user",
            "Anonymous item",
            DesktopItemKind.File);

    private static string Target() =>
        Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ProductWorkspaceSession.Tests",
            "item.txt");
}
