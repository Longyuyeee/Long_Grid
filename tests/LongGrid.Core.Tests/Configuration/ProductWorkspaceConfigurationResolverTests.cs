using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceConfigurationResolverTests
{
    [Fact]
    public void ResolveClassifiesEveryFiniteItemStateWithoutDroppingReferences()
    {
        string resolvedTarget = Target("resolved.txt");
        string missingTarget = Target("missing.txt");
        string changedTarget = Target("changed");
        string ambiguousTarget = Target("ambiguous.lnk");
        ProductConfigurationDocument document = CreateDocument(
            CreateItem("resolved", ConfigurationItemKind.File, resolvedTarget),
            CreateItem("missing", ConfigurationItemKind.File, missingTarget),
            CreateItem("changed", ConfigurationItemKind.Folder, changedTarget),
            CreateItem("ambiguous", ConfigurationItemKind.Shortcut, ambiguousTarget),
            CreateItem("unsupported", ConfigurationItemKind.File, "relative-target"));
        DesktopCatalogEntry[] catalog =
        [
            CreateEntry("user", resolvedTarget, DesktopItemKind.File),
            CreateEntry("user", changedTarget, DesktopItemKind.File),
            CreateEntry("user", ambiguousTarget, DesktopItemKind.Shortcut),
            CreateEntry("public", ambiguousTarget, DesktopItemKind.Shortcut),
        ];

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(document, catalog);

        Assert.True(result.IsSuccess);
        Assert.Equal(new(1, 1, 1, 1, 1), result.Summary);
        Assert.Equal(5, result.Summary.Total);
        IReadOnlyList<ProductItemReferenceState> items =
            result.State!.Containers[0].Items;
        Assert.Equal(
            new[]
            {
                ProductItemReferenceResolution.Resolved,
                ProductItemReferenceResolution.Missing,
                ProductItemReferenceResolution.TypeChanged,
                ProductItemReferenceResolution.Ambiguous,
                ProductItemReferenceResolution.UnsupportedTarget,
            },
            items.Select(item => item.Resolution));
        Assert.NotNull(items[0].CatalogEntry);
        Assert.All(items.Skip(1), item => Assert.Null(item.CatalogEntry));
        Assert.Equal(missingTarget, items[1].PersistedTarget);
        Assert.Equal(ConfigurationItemKind.Folder, items[2].PersistedKind);
    }

    [Fact]
    public void ResolvedAndUnresolvedStateReprojectsWithoutLosingConfiguration()
    {
        string resolvedTarget = Target("resolved.txt");
        string missingTarget = Target("missing.txt");
        ProductConfigurationDocument source = CreateDocument(
            CreateItem(
                "resolved",
                ConfigurationItemKind.File,
                resolvedTarget,
                Extension("resolvedFuture", 7)),
            CreateItem(
                "missing",
                ConfigurationItemKind.File,
                missingTarget,
                Extension("missingFuture", 8))) with
        {
            ExtensionData = Extension("rootFuture", 1),
        };
        ContainerConfiguration container = source.Containers[0];
        source = source with
        {
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
                },
            ],
        };
        ProductWorkspaceResolutionResult resolved =
            ProductWorkspaceConfigurationResolver.Resolve(
                source,
                [CreateEntry("user", resolvedTarget, DesktopItemKind.File)]);

        ProductWorkspaceProjectionResult projected =
            ProductWorkspaceConfigurationProjector.Project(resolved.State!);

        Assert.True(projected.IsSuccess);
        Assert.Equivalent(source, projected.Document, strict: true);
    }

    [Fact]
    public void ResolutionUsesCaseInsensitiveCanonicalTargetMatching()
    {
        string target = Target("CaseSensitiveName.txt");
        ProductConfigurationDocument document = CreateDocument(
            CreateItem(
                "item-1",
                ConfigurationItemKind.File,
                target.ToUpperInvariant()));

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(
                document,
                [CreateEntry("user", target, DesktopItemKind.File)]);

        ProductItemReferenceState item =
            Assert.Single(result.State!.Containers[0].Items);
        Assert.Equal(ProductItemReferenceResolution.Resolved, item.Resolution);
        Assert.Equal(target, item.CatalogEntry!.Identity.CanonicalTarget);
    }

    [Fact]
    public void EmptyCatalogMarksEverySupportedTargetMissing()
    {
        ProductConfigurationDocument document = CreateDocument(
            CreateItem("item-1", ConfigurationItemKind.File, Target("one.txt")),
            CreateItem("item-2", ConfigurationItemKind.Folder, Target("two")));

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(document, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(new(0, 2, 0, 0, 0), result.Summary);
        Assert.All(
            result.State!.Containers[0].Items,
            item => Assert.Equal(
                ProductItemReferenceResolution.Missing,
                item.Resolution));
    }

    [Fact]
    public void InvalidConfigurationReturnsFiniteFailureBeforeCatalogUse()
    {
        ProductConfigurationDocument document = CreateDocument() with
        {
            ProfileId = string.Empty,
        };
        DesktopCatalogEntry invalidCatalog = CreateEntry(
            "user",
            "relative-target",
            DesktopItemKind.File);

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(
                document,
                [invalidCatalog]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ProductWorkspaceResolutionError.InvalidConfiguration,
            result.Error);
        Assert.Equal(ProductConfigurationError.InvalidProfile, result.ConfigurationError);
        Assert.Null(result.State);
        Assert.Equal(0, result.Summary.Total);
    }

    [Theory]
    [InlineData("shell", true, false)]
    [InlineData("filesystem", false, false)]
    [InlineData("filesystem", true, true)]
    public void InvalidCatalogReturnsFiniteFailure(
        string provider,
        bool absoluteTarget,
        bool partialStableIdentity)
    {
        string target = absoluteTarget ? Target("item.txt") : "relative-target";
        DesktopCatalogEntry entry = new(
            new DesktopItemIdentity(
                provider,
                target,
                VolumeId: partialStableIdentity ? "volume-only" : null),
            "user",
            "item.txt",
            DesktopItemKind.File);

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(
                CreateDocument(),
                [entry]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProductWorkspaceResolutionError.InvalidCatalog, result.Error);
        Assert.Equal(ProductConfigurationError.None, result.ConfigurationError);
        Assert.Null(result.State);
    }

    [Fact]
    public void DuplicateCatalogTargetIsAmbiguousEvenWhenEntriesAreEquivalent()
    {
        string target = Target("duplicate.txt");
        DesktopCatalogEntry entry = CreateEntry(
            "user",
            target,
            DesktopItemKind.File);
        ProductConfigurationDocument document = CreateDocument(
            CreateItem("item-1", ConfigurationItemKind.File, target));

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(document, [entry, entry]);

        ProductItemReferenceState item =
            Assert.Single(result.State!.Containers[0].Items);
        Assert.Equal(ProductItemReferenceResolution.Ambiguous, item.Resolution);
        Assert.Null(item.CatalogEntry);
        Assert.Equal(new(0, 0, 0, 1, 0), result.Summary);
    }

    [Fact]
    public void ResolverDeepSnapshotsConfigurationBeforeReturningState()
    {
        var extensionData = Extension("future", 1);
        ProductConfigurationDocument document = CreateDocument(
            CreateItem(
                "item-1",
                ConfigurationItemKind.File,
                Target("missing.txt"),
                extensionData));

        ProductWorkspaceResolutionResult result =
            ProductWorkspaceConfigurationResolver.Resolve(document, []);
        extensionData["future"] = JsonSerializer.SerializeToElement(2);

        ProductItemReferenceState item =
            Assert.Single(result.State!.Containers[0].Items);
        Assert.Equal(1, item.ExtensionData!["future"].GetInt32());
    }

    private static ProductConfigurationDocument CreateDocument(
        params DesktopItemReferenceConfiguration[] items) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "default",
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "container-1",
                    Name = "Current project",
                    Appearance = new ContainerAppearanceConfiguration
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ContainerPlacementConfiguration
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

    private static DesktopItemReferenceConfiguration CreateItem(
        string id,
        ConfigurationItemKind kind,
        string target,
        IDictionary<string, JsonElement>? extensionData = null) =>
        new()
        {
            Id = id,
            Kind = kind,
            Target = target,
            Behavior = ConfigurationItemBehavior.Reference,
            ExtensionData = extensionData,
        };

    private static DesktopCatalogEntry CreateEntry(
        string sourceId,
        string target,
        DesktopItemKind kind) =>
        new(
            new DesktopItemIdentity("filesystem", target),
            sourceId,
            Path.GetFileName(target),
            kind);

    private static string Target(string name) =>
        Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ConfigurationResolver.Tests",
            name);

    private static Dictionary<string, JsonElement> Extension(
        string name,
        int value) =>
        new()
        {
            [name] = JsonSerializer.SerializeToElement(value),
        };
}
