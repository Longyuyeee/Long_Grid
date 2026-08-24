using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostProjectionBuilderTests
{
    private static readonly DisplayTopologyNode Primary = new(
        "display-primary",
        new(0, 0, 1920, 1080),
        new(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        true);

    private static readonly DisplayTopologyNode Secondary = new(
        "display-secondary",
        new(-1280, 0, 1280, 1024),
        new(-1280, 0, 1280, 984),
        120,
        DisplayRotation.Landscape,
        false);

    [Fact]
    public void BuildGroupsContainersPerDisplayAndFallsBackToPrimary()
    {
        ProductWorkspaceState state = CreateState();
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            9,
            [Primary, Secondary],
            2,
            2,
            1);

        ProductDesktopHostProjectionBatch batch =
            ProductDesktopHostProjectionBuilder.Build(
                state,
                read,
                topology,
                workspaceRevision: 14)!;

        Assert.Equal(14, batch.WorkspaceRevision);
        Assert.Equal(9, batch.TopologyGeneration);
        Assert.Equal(64, batch.TopologyFingerprint.Length);
        Assert.Equal(2, batch.Displays.Count);
        Assert.Equal(
            ["container-fallback"],
            batch.Displays[0].Containers.Select(container => container.ContainerId));
        Assert.Equal(
            ["container-secondary"],
            batch.Displays[1].Containers.Select(container => container.ContainerId));
        Assert.False(batch.Displays[0].Containers[0].IsLocked);
        Assert.True(batch.Displays[1].Containers[0].IsLocked);
        Assert.Equal(120u, batch.Displays[1].EffectiveDpi);
        Assert.Equal(
            ["item:1", "item:2"],
            batch.Displays[0].Containers[0].ItemIds);
        Assert.All(
            batch.Displays[0].Containers[0].ItemVisuals,
            visual =>
            {
                Assert.Equal(ProductDesktopItemTypeIconKind.File, visual.TypeIcon);
                Assert.Equal(
                    ProductDesktopItemVisualStatus.ReadyTypeIcon,
                    visual.Status);
            });
        Assert.DoesNotContain(
            batch.Displays[0].Containers[0].ItemIds,
            id => id.Contains("persisted", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildKeepsAuthoritativeDisplaysWithoutContainersForCreateEntry()
    {
        ProductWorkspaceState state = CreateState() with
        {
            Containers = [CreateContainer(
                "container-secondary",
                Secondary.StableId,
                48)],
        };
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            9,
            [Primary, Secondary],
            2,
            2,
            1);

        ProductDesktopHostProjectionBatch batch = Assert.IsType<
            ProductDesktopHostProjectionBatch>(
                ProductDesktopHostProjectionBuilder.Build(
                    state,
                    read,
                    topology,
                    workspaceRevision: 14));

        Assert.Equal(2, batch.Displays.Count);
        Assert.True(batch.Displays[0].IsPrimary);
        Assert.Empty(batch.Displays[0].Containers);
        Assert.False(batch.Displays[0].WorkspaceIsEmpty);
        Assert.False(batch.Displays[1].IsPrimary);
        Assert.Single(batch.Displays[1].Containers);
    }

    [Fact]
    public void BuildRejectsNonAuthoritativeTopologyWithoutCreatingBatch()
    {
        ProductWorkspaceState state = CreateState();
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Degraded,
            9,
            [Primary, Secondary],
            2,
            1,
            1);

        Assert.Null(ProductDesktopHostProjectionBuilder.Build(
            state,
            read,
            topology,
            workspaceRevision: 14));
    }

    [Fact]
    public void ThumbnailResultsProjectLoadingReadyAndFiniteFallbackStates()
    {
        ProductWorkspaceState state = CreateState();
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            9,
            [Primary, Secondary],
            2,
            2,
            1);
        ProductDesktopThumbnailFrame frame = ProductDesktopThumbnailFrame.Create(
            2,
            2,
            8,
            new byte[16]);
        var results = new Dictionary<string, ProductDesktopThumbnailResult>
        {
            [ProductDesktopThumbnailItemKey.Create("container-fallback", 1)] =
                new("loading", ProductDesktopThumbnailStatus.LoadingThumbnail,
                    false, null),
            [ProductDesktopThumbnailItemKey.Create("container-fallback", 2)] =
                new("ready", ProductDesktopThumbnailStatus.ReadyThumbnail,
                    false, frame),
        };

        ProductDesktopHostProjectionBatch batch = Assert.IsType<
            ProductDesktopHostProjectionBatch>(
                ProductDesktopHostProjectionBuilder.Build(
                    state, read, topology, 14, results));
        ProductDesktopHostReadOnlyProjection container =
            batch.Displays[0].Containers[0];

        Assert.Equal(
            ProductDesktopItemVisualStatus.LoadingThumbnail,
            container.ItemVisuals[0].Status);
        Assert.Equal(
            ProductDesktopItemVisualStatus.ReadyThumbnail,
            container.ItemVisuals[1].Status);
        Assert.Same(frame, container.ItemVisuals[1].Thumbnail);

        results[ProductDesktopThumbnailItemKey.Create("container-fallback", 1)] =
            new("failed", ProductDesktopThumbnailStatus.Unsupported, false, null);
        ProductDesktopHostProjectionBatch fallbackBatch = Assert.IsType<
            ProductDesktopHostProjectionBatch>(
                ProductDesktopHostProjectionBuilder.Build(
                    state, read, topology, 14, results));
        Assert.Equal(
            ProductDesktopItemVisualStatus.FailedFallback,
            fallbackBatch.Displays[0].Containers[0].ItemVisuals[0].Status);
    }

    [Fact]
    public void FiveHundredItemViewportProjectsOnlyRequestedLastTwelveItems()
    {
        ProductItemReferenceState[] items = Enumerable.Range(1, 500)
            .Select(index => CreateResolvedItem(
                $"persisted-{index}",
                $"项目 {index}"))
            .ToArray();
        ProductWorkspaceState state = CreateState() with
        {
            Containers =
            [
                CreateContainer(
                    "container-fallback",
                    "unknown-display",
                    24,
                    items: items),
            ],
        };
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            9,
            [Primary],
            1,
            1,
            1);
        var viewports = new Dictionary<string, int>
        {
            ["container-fallback"] = 488,
        };

        ProductDesktopHostProjectionBatch batch = Assert.IsType<
            ProductDesktopHostProjectionBatch>(
                ProductDesktopHostProjectionBuilder.Build(
                    state,
                    read,
                    topology,
                    14,
                    viewportStarts: viewports,
                    presentationGeneration: 3));
        ProductDesktopHostReadOnlyProjection actual =
            Assert.Single(batch.Displays[0].Containers);

        Assert.Equal(489, actual.VisibleItemStartOrdinal);
        Assert.Equal(500, actual.TotalItemCount);
        Assert.Equal(12, actual.ItemIds.Count);
        Assert.Equal("item:489", actual.ItemIds[0]);
        Assert.Equal("item:500", actual.ItemIds[^1]);
        Assert.Equal(3, batch.PresentationGeneration);
    }

    [Fact]
    public void BuildFailsClosedWhenDisplayCountExceedsSurfaceBudget()
    {
        ProductWorkspaceState state = CreateState();
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        DisplayTopologyNode[] displays = Enumerable.Range(
                0,
                ProductDesktopHostProjectionBatch.MaximumDisplays + 1)
            .Select(index => new DisplayTopologyNode(
                $"display-{index}",
                new(index * 100, 0, 100, 100),
                new(index * 100, 0, 100, 90),
                96,
                DisplayRotation.Landscape,
                index == 0))
            .ToArray();
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            9,
            displays,
            displays.Length,
            displays.Length,
            1);

        Assert.Null(ProductDesktopHostProjectionBuilder.Build(
            state,
            read,
            topology,
            workspaceRevision: 14));
    }

    [Theory]
    [InlineData(
        ProductDisplayTopologyStatus.Refreshing,
        ProductDesktopHostProjectionDisposition.TopologyRefreshing)]
    [InlineData(
        ProductDisplayTopologyStatus.Degraded,
        ProductDesktopHostProjectionDisposition.TopologyUnavailable)]
    public void BuildUpdateDistinguishesUnsafeTopologyStates(
        ProductDisplayTopologyStatus status,
        ProductDesktopHostProjectionDisposition expected)
    {
        ProductWorkspaceState state = CreateState();
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            status,
            10,
            Array.Empty<DisplayTopologyNode>(),
            0,
            0,
            0);

        ProductDesktopHostProjectionUpdate update =
            ProductDesktopHostProjectionBuilder.BuildUpdate(
                state,
                read,
                topology,
                workspaceRevision: 15);

        Assert.Equal(expected, update.Disposition);
        Assert.Null(update.Batch);
        Assert.Equal(15, update.WorkspaceRevision);
        Assert.Equal(10, update.TopologyGeneration);
    }

    [Fact]
    public void BuildUpdateCreatesPrimaryDisplaySurfaceForKnownEmptyWorkspace()
    {
        ProductWorkspaceState state = CreateState() with
        {
            Containers = Array.Empty<ProductContainerState>(),
        };
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state).Snapshot!;
        var topology = new ProductDisplayTopologySnapshot(
            ProductDisplayTopologyStatus.Ready,
            10,
            [Primary, Secondary],
            2,
            2,
            1);

        ProductDesktopHostProjectionUpdate update =
            ProductDesktopHostProjectionBuilder.BuildUpdate(
                state,
                read,
                topology,
                workspaceRevision: 15);

        Assert.Equal(
            ProductDesktopHostProjectionDisposition.EmptyWorkspace,
            update.Disposition);
        ProductDesktopHostProjectionBatch batch = Assert.IsType<
            ProductDesktopHostProjectionBatch>(update.Batch);
        Assert.Equal(0, batch.ContainerCount);
        ProductDesktopHostDisplayProjection display = Assert.Single(batch.Displays);
        Assert.Equal(Primary.StableId, display.DisplayId);
        Assert.Empty(display.Containers);
        Assert.Equal(Primary.WorkArea, display.WorkArea);
    }

    private static ProductWorkspaceState CreateState() => new()
    {
        ProfileId = "profile",
        Containers =
        [
            CreateContainer(
                "container-fallback",
                "unknown-display",
                24,
                items:
                [
                    CreateResolvedItem("persisted-secret-1", "Visible One"),
                    CreateResolvedItem("persisted-secret-2", "Visible Two"),
                ]),
            CreateContainer(
                "container-secondary",
                Secondary.StableId,
                48,
                isLocked: true),
        ],
        SavedDisplayTopology = null,
        ExtensionData = null,
    };

    private static ProductContainerState CreateContainer(
        string id,
        string displayKey,
        double xDip,
        bool isLocked = false,
        IReadOnlyList<ProductItemReferenceState>? items = null) => new()
        {
            Id = id,
            Name = id,
            IsLocked = isLocked,
            Appearance = new()
            {
                Color = "#2457D6",
                Opacity = 0.82,
                Collapsed = false,
                ExtensionData = null,
            },
            Placement = new()
            {
                DisplayKey = displayKey,
                XDip = xDip,
                YDip = 36,
                WidthDip = 320,
                HeightDip = 240,
                ExtensionData = null,
            },
            Items = items ?? Array.Empty<ProductItemReferenceState>(),
            ExtensionData = null,
        };

    private static ProductItemReferenceState CreateResolvedItem(
        string id,
        string name) =>
        ProductItemReferenceState.CreateResolved(
            id,
            new DesktopCatalogEntry(
                new DesktopItemIdentity(
                    "filesystem",
                    Path.Combine(Path.GetTempPath(), "LongGrid.E2a", name)),
                "user-desktop",
                name,
                DesktopItemKind.File));
}
