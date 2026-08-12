using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
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
        Assert.Equal(120u, batch.Displays[1].EffectiveDpi);
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

    private static ProductWorkspaceState CreateState() => new()
    {
        ProfileId = "profile",
        Containers =
        [
            CreateContainer("container-fallback", "unknown-display", 24),
            CreateContainer("container-secondary", Secondary.StableId, 48),
        ],
        SavedDisplayTopology = null,
        ExtensionData = null,
    };

    private static ProductContainerState CreateContainer(
        string id,
        string displayKey,
        double xDip) => new()
        {
            Id = id,
            Name = id,
            IsLocked = false,
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
            Items = Array.Empty<ProductItemReferenceState>(),
            ExtensionData = null,
        };
}
