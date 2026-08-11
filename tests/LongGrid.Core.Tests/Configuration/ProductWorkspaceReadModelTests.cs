using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReadModelTests
{
    [Fact]
    public void ReadModelPreservesStableOrderAndFiniteCounts()
    {
        string firstTarget = CreateTarget("first");
        string missingTarget = CreateTarget("missing-private");
        ProductConfigurationDocument document = CreateDocument(
            [
                CreateItem("item-first", firstTarget),
                CreateItem("item-missing-private", missingTarget),
            ]);
        ProductWorkspaceState state = ProductWorkspaceConfigurationResolver.Resolve(
            document,
            [CreateCatalogEntry(firstTarget, "Visible first")]).State!;

        ProductWorkspaceReadResult result = ProductWorkspaceReadModel.Create(state);

        Assert.True(result.IsSuccess);
        ProductWorkspaceReadSnapshot snapshot = result.Snapshot!;
        Assert.Equal(2, snapshot.ItemCount);
        Assert.Equal(1, snapshot.ResolvedCount);
        Assert.Equal(1, snapshot.UnresolvedCount);
        Assert.Equal(0, snapshot.EmptyContainerCount);
        Assert.Equal(1, snapshot.NeedsReviewContainerCount);
        Assert.Equal(
            ProductWorkspaceContainerHealth.NeedsReview,
            snapshot.Containers[0].Health);
        Assert.Equal("Visible first", snapshot.Containers[0].Items[0].UserVisibleName);
        Assert.Null(snapshot.Containers[0].Items[1].UserVisibleName);
        Assert.Equal([1, 2], snapshot.Containers[0].Items.Select(item => item.Ordinal));
    }

    [Fact]
    public void UnresolvedReadModelOmitsPersistedIdentityAndTarget()
    {
        string target = CreateTarget("secret-target");
        ProductConfigurationDocument document = CreateDocument(
            [CreateItem("secret-item-id", target)]) with
        {
            ProfileId = "secret-profile-id",
            Containers =
            [
                CreateDocument([CreateItem("secret-item-id", target)]).Containers[0]
                    with { Id = "secret-container-id" },
            ],
        };
        ProductWorkspaceState state = ProductWorkspaceConfigurationResolver.Resolve(
            document,
            Array.Empty<DesktopCatalogEntry>()).State!;

        string json = JsonSerializer.Serialize(
            ProductWorkspaceReadModel.Create(state).Snapshot);

        Assert.DoesNotContain(target, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-item-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-container-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-profile-id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerPresentationStateIsCopiedWithoutPlacementIdentity()
    {
        ProductWorkspaceState state = ProductWorkspaceConfigurationResolver.Resolve(
            CreateDocument([]),
            Array.Empty<DesktopCatalogEntry>()).State!;

        ProductWorkspaceReadContainer container =
            ProductWorkspaceReadModel.Create(state).Snapshot!.Containers[0];

        Assert.Equal("Work", container.UserVisibleName);
        Assert.True(container.IsLocked);
        Assert.True(container.IsCollapsed);
        Assert.Equal("#334155", container.Color);
        Assert.Equal(0.72, container.Opacity);
        Assert.Equal(32, container.XDip);
        Assert.Equal(48, container.YDip);
        Assert.Equal(420, container.WidthDip);
        Assert.Equal(300, container.HeightDip);
        Assert.Equal(ProductWorkspaceContainerHealth.Empty, container.Health);
    }

    [Fact]
    public void InvalidStateReturnsFiniteFailureWithoutSnapshot()
    {
        ProductWorkspaceState state = new()
        {
            ProfileId = "default",
            Containers = null!,
        };

        ProductWorkspaceReadResult result = ProductWorkspaceReadModel.Create(state);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProductWorkspaceProjectionError.InvalidState, result.Error);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void EmptyWorkspaceIsAValidEmptyReadModel()
    {
        ProductWorkspaceState state = new()
        {
            ProfileId = "default",
            Containers = Array.Empty<ProductContainerState>(),
        };

        ProductWorkspaceReadResult result = ProductWorkspaceReadModel.Create(state);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Snapshot!.Containers);
        Assert.Equal(0, result.Snapshot.ItemCount);
        Assert.Equal(0, result.Snapshot.EmptyContainerCount);
        Assert.Equal(0, result.Snapshot.NeedsReviewContainerCount);
    }

    [Fact]
    public void FullyResolvedContainerHasReadyHealth()
    {
        string target = CreateTarget("ready");
        ProductWorkspaceState state = ProductWorkspaceConfigurationResolver.Resolve(
            CreateDocument([CreateItem("ready-item", target)]),
            [CreateCatalogEntry(target, "Ready item")]).State!;

        ProductWorkspaceReadSnapshot snapshot =
            ProductWorkspaceReadModel.Create(state).Snapshot!;

        Assert.Equal(ProductWorkspaceContainerHealth.Ready, snapshot.Containers[0].Health);
        Assert.Equal(0, snapshot.EmptyContainerCount);
        Assert.Equal(0, snapshot.NeedsReviewContainerCount);
    }

    [Fact]
    public void HealthFilterPolicyIncludesOnlyTheRequestedFiniteState()
    {
        ProductWorkspaceContainerHealth[] health =
        [
            ProductWorkspaceContainerHealth.Empty,
            ProductWorkspaceContainerHealth.Ready,
            ProductWorkspaceContainerHealth.NeedsReview,
        ];

        Assert.Equal(
            3,
            health.Count(value => ProductWorkspaceContainerHealthFilterPolicy.Includes(
                ProductWorkspaceContainerHealthFilter.All,
                value)));
        Assert.Single(health, value =>
            ProductWorkspaceContainerHealthFilterPolicy.Includes(
                ProductWorkspaceContainerHealthFilter.Empty,
                value));
        Assert.Single(health, value =>
            ProductWorkspaceContainerHealthFilterPolicy.Includes(
                ProductWorkspaceContainerHealthFilter.Ready,
                value));
        Assert.Single(health, value =>
            ProductWorkspaceContainerHealthFilterPolicy.Includes(
                ProductWorkspaceContainerHealthFilter.NeedsReview,
                value));
        Assert.False(ProductWorkspaceContainerHealthFilterPolicy.IsSupported(
            ProductWorkspaceContainerHealthFilter.Invalid));
        Assert.False(ProductWorkspaceContainerHealthFilterPolicy.Includes(
            (ProductWorkspaceContainerHealthFilter)99,
            ProductWorkspaceContainerHealth.Ready));
    }

    private static ProductConfigurationDocument CreateDocument(
        IReadOnlyList<DesktopItemReferenceConfiguration> items) =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "default",
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "container-1",
                    Name = "Work",
                    IsLocked = true,
                    Appearance = new()
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = true,
                    },
                    Placement = new()
                    {
                        DisplayKey = "display-private",
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
        string target) =>
        new()
        {
            Id = id,
            Kind = ConfigurationItemKind.Folder,
            Target = target,
            Behavior = ConfigurationItemBehavior.Reference,
        };

    private static DesktopCatalogEntry CreateCatalogEntry(
        string target,
        string displayName) =>
        new(
            new DesktopItemIdentity("filesystem", target),
            "user-desktop",
            displayName,
            DesktopItemKind.Directory);

    private static string CreateTarget(string name) =>
        Path.Combine(Path.GetTempPath(), "LongGrid.ReadModel.Tests", name);
}
