using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopSearchNavigationTests
{
    [Fact]
    public void ContainerResultUsesAssignedDisplayAndTemporaryExpansion()
    {
        ProductWorkspaceState state = CreateState(collapsed: true);
        ProductWorkspaceReadSnapshot snapshot =
            ProductWorkspaceReadModel.Create(state).Snapshot!;

        ProductDesktopSearchNavigationTarget target =
            ProductDesktopSearchNavigation.Resolve(
                5,
                CreateTopology(),
                state,
                snapshot,
                new(5, 8, 1, null));

        Assert.True(target.IsApplied);
        Assert.Equal("display-secondary", target.DisplayId);
        Assert.Equal("container-1", target.ContainerId);
        Assert.Null(target.ItemId);
        Assert.True(target.TemporarilyExpandsContainer);
    }

    [Fact]
    public void RevisionAndTopologyChangesRejectOldResult()
    {
        ProductWorkspaceState state = CreateState(collapsed: false);
        ProductWorkspaceReadSnapshot snapshot =
            ProductWorkspaceReadModel.Create(state).Snapshot!;

        Assert.Equal(
            ProductDesktopSearchNavigationStatus.StaleAuthority,
            ProductDesktopSearchNavigation.Resolve(
                6,
                CreateTopology(),
                state,
                snapshot,
                new(5, 8, 1, null)).Status);
        Assert.Equal(
            ProductDesktopSearchNavigationStatus.StaleAuthority,
            ProductDesktopSearchNavigation.Resolve(
                5,
                CreateTopology(),
                state,
                snapshot,
                new(5, 7, 1, null)).Status);
    }

    [Fact]
    public void InvalidOrMissingOrdinalFailsClosed()
    {
        ProductWorkspaceState state = CreateState(collapsed: false);
        ProductWorkspaceReadSnapshot snapshot =
            ProductWorkspaceReadModel.Create(state).Snapshot!;

        Assert.Equal(
            ProductDesktopSearchNavigationStatus.Invalid,
            ProductDesktopSearchNavigation.Resolve(
                5,
                CreateTopology(),
                state,
                snapshot,
                new(5, 8, 0, null)).Status);
        Assert.Equal(
            ProductDesktopSearchNavigationStatus.TargetUnavailable,
            ProductDesktopSearchNavigation.Resolve(
                5,
                CreateTopology(),
                state,
                snapshot,
                new(5, 8, 2, null)).Status);
    }

    private static ProductWorkspaceState CreateState(bool collapsed) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "资料",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = collapsed,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-secondary",
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };

    private static ProductDisplayTopologySnapshot CreateTopology() =>
        new(
            ProductDisplayTopologyStatus.Ready,
            8,
            [
                new DisplayTopologyNode(
                    "display-primary",
                    new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040),
                    96,
                    DisplayRotation.Landscape,
                    IsPrimary: true),
                new DisplayTopologyNode(
                    "display-secondary",
                    new PixelRect(1920, 0, 1920, 1080),
                    new PixelRect(1920, 0, 1920, 1040),
                    96,
                    DisplayRotation.Landscape,
                    IsPrimary: false),
            ],
            2,
            2,
            1);
}
