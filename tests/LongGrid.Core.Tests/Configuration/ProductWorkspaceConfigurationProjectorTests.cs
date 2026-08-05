using System.Text;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceConfigurationProjectorTests
{
    [Fact]
    public void ResolvedCatalogEntriesProjectToCurrentReferenceSchema()
    {
        DesktopItemKind[] kinds = Enum.GetValues<DesktopItemKind>();
        ProductWorkspaceState state = CreateState(
            kinds.Select(
                    (kind, index) => CreateItem(
                        $"item-{index}",
                        kind,
                        $"Item-{index}.dat"))
                .ToArray());

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(state);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductWorkspaceProjectionError.None, result.Error);
        Assert.Equal(ProductConfigurationError.None, result.ConfigurationError);
        Assert.Equal(
            new[]
            {
                ConfigurationItemKind.File,
                ConfigurationItemKind.Folder,
                ConfigurationItemKind.Shortcut,
                ConfigurationItemKind.Url,
            },
            result.Document!.Containers[0].Items.Select(item => item.Kind));
        Assert.All(
            result.Document.Containers[0].Items,
            item => Assert.Equal(ConfigurationItemBehavior.Reference, item.Behavior));
    }

    [Fact]
    public void ProjectionUsesCanonicalIdentityTargetAndOmitsCatalogMetadata()
    {
        string target = CreateAbsoluteTarget("Project");
        DesktopCatalogEntry entry = new(
            new DesktopItemIdentity(
                "filesystem",
                target,
                VolumeId: "volume-private",
                FileId: "file-private",
                ParsingName: "parsing-private"),
            SourceId: "source-private",
            DisplayName: "display-private",
            DesktopItemKind.Directory);
        ProductWorkspaceState state = CreateState(
            [new ProductItemReferenceState { Id = "item-1", CatalogEntry = entry }]);

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(state);
        string json = Encoding.UTF8.GetString(
            ProductConfigurationJson.SerializeToUtf8Bytes(result.Document!));

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(target), result.Document!.Containers[0].Items[0].Target);
        Assert.DoesNotContain("display-private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("source-private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("volume-private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("file-private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("parsing-private", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionReturnsDetachedValidatedSnapshot()
    {
        var containers = new List<ProductContainerState>();
        var rootExtensions = new Dictionary<string, JsonElement>
        {
            ["future"] = JsonSerializer.SerializeToElement(1),
        };
        ProductWorkspaceState source = CreateState([CreateItem()]) with
        {
            Containers = containers,
            ExtensionData = rootExtensions,
        };
        containers.Add(CreateState([CreateItem()]).Containers[0]);

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(source);
        containers.Clear();
        rootExtensions["future"] = JsonSerializer.SerializeToElement(2);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Document!.Containers);
        Assert.Equal(1, result.Document.ExtensionData!["future"].GetInt32());
        Assert.True(ProductConfigurationValidator.Validate(result.Document).IsValid);
    }

    [Fact]
    public void ExtensionDataSurvivesAtEveryWorkspaceLevel()
    {
        ProductWorkspaceState template = CreateState([CreateItem()]);
        ProductContainerState container = template.Containers[0];
        ProductWorkspaceState state = template with
        {
            ExtensionData = Extension("rootFuture", 1),
            Containers =
            [
                container with
                {
                    ExtensionData = Extension("containerFuture", 2),
                    Appearance = container.Appearance with
                    {
                        ExtensionData = Extension("appearanceFuture", 3),
                    },
                    Placement = container.Placement with
                    {
                        ExtensionData = Extension("placementFuture", 4),
                    },
                    Items =
                    [
                        container.Items[0] with
                        {
                            ExtensionData = Extension("itemFuture", 5),
                        },
                    ],
                },
            ],
        };

        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(state).Document!;

        Assert.Equal(1, document.ExtensionData!["rootFuture"].GetInt32());
        ContainerConfiguration projectedContainer = document.Containers[0];
        Assert.Equal(
            2,
            projectedContainer.ExtensionData!["containerFuture"].GetInt32());
        Assert.Equal(
            3,
            projectedContainer.Appearance.ExtensionData!["appearanceFuture"].GetInt32());
        Assert.Equal(
            4,
            projectedContainer.Placement.ExtensionData!["placementFuture"].GetInt32());
        Assert.Equal(
            5,
            projectedContainer.Items[0].ExtensionData!["itemFuture"].GetInt32());
    }

    [Fact]
    public void RelativeOrDisplayTextTargetIsRejectedWithoutDocumentContent()
    {
        ProductItemReferenceState item = CreateItem() with
        {
            CatalogEntry = CreateItem().CatalogEntry with
            {
                Identity = new DesktopItemIdentity("filesystem", "Project folder"),
            },
        };

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(CreateState([item]));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ProductWorkspaceProjectionError.InvalidCanonicalTarget,
            result.Error);
        Assert.Null(result.Document);
    }

    [Fact]
    public void UnsupportedProviderIsRejectedWithoutFallingBackToDisplayText()
    {
        ProductItemReferenceState item = CreateItem() with
        {
            CatalogEntry = CreateItem().CatalogEntry with
            {
                Identity = new DesktopItemIdentity(
                    "shell-display-name",
                    CreateAbsoluteTarget("Project")),
            },
        };

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(CreateState([item]));

        Assert.Equal(
            ProductWorkspaceProjectionError.UnsupportedIdentityProvider,
            result.Error);
        Assert.Null(result.Document);
    }

    [Fact]
    public void PartialStableFileIdentityIsRejectedAsInvalidState()
    {
        ProductItemReferenceState item = CreateItem() with
        {
            CatalogEntry = CreateItem().CatalogEntry with
            {
                Identity = new DesktopItemIdentity(
                    "filesystem",
                    CreateAbsoluteTarget("Project"),
                    VolumeId: "volume-only"),
            },
        };

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(CreateState([item]));

        Assert.Equal(ProductWorkspaceProjectionError.InvalidState, result.Error);
        Assert.Null(result.Document);
    }

    [Fact]
    public void V1ContractRejectionRemainsFinite()
    {
        ProductItemReferenceState item = CreateItem("container-1");

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(CreateState([item]));

        Assert.Equal(
            ProductWorkspaceProjectionError.ConfigurationRejected,
            result.Error);
        Assert.Equal(
            ProductConfigurationError.DuplicateObjectId,
            result.ConfigurationError);
        Assert.Null(result.Document);
    }

    [Fact]
    public void InvalidStructuralStateReturnsFiniteFailure()
    {
        ProductWorkspaceState state = new()
        {
            ProfileId = "default",
            Containers = null!,
        };

        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(state);

        Assert.Equal(ProductWorkspaceProjectionError.InvalidState, result.Error);
        Assert.Equal(ProductConfigurationError.None, result.ConfigurationError);
        Assert.Null(result.Document);
    }

    private static ProductWorkspaceState CreateState(
        IReadOnlyList<ProductItemReferenceState> items) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = false,
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = false,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = items,
                },
            ],
        };

    private static ProductItemReferenceState CreateItem(
        string id = "item-1",
        DesktopItemKind kind = DesktopItemKind.Directory,
        string name = "Project") =>
        new()
        {
            Id = id,
            CatalogEntry = new DesktopCatalogEntry(
                new DesktopItemIdentity(
                    "filesystem",
                    CreateAbsoluteTarget(name)),
                "user-desktop",
                name,
                kind),
        };

    private static string CreateAbsoluteTarget(string name) =>
        Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ProductState.Tests",
            name);

    private static Dictionary<string, JsonElement> Extension(
        string name,
        int value) =>
        new()
        {
            [name] = JsonSerializer.SerializeToElement(value),
        };
}
