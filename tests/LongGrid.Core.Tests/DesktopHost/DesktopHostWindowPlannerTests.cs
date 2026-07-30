using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class DesktopHostWindowPlannerTests
{
    private static readonly DesktopDisplayPlacement Display =
        new("primary", new PixelRect(100, 200, 1000, 800));

    [Fact]
    public void PerContainerCreatesOneSurfacePerVisibleContainer()
    {
        DesktopContainerPlacement[] containers =
        [
            new("one", "primary", new PixelRect(150, 250, 200, 100)),
            new("two", "primary", new PixelRect(500, 450, 300, 200)),
        ];

        IReadOnlyList<DesktopHostSurfacePlan> result =
            DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerContainer,
                [Display],
                containers);

        Assert.Equal(2, result.Count);
        Assert.All(result, surface => Assert.Single(surface.InteractiveRegions));
        Assert.Equal(containers[0].Bounds, result[0].WindowBounds);
    }

    [Fact]
    public void PerDisplayCreatesOneSurfaceWithRelativeInteractiveRegions()
    {
        DesktopContainerPlacement[] containers =
        [
            new("one", "primary", new PixelRect(150, 250, 200, 100)),
            new("two", "primary", new PixelRect(500, 450, 300, 200)),
        ];

        DesktopHostSurfacePlan result = Assert.Single(
            DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerDisplay,
                [Display],
                containers));

        Assert.Equal(Display.Bounds, result.WindowBounds);
        Assert.Equal(
            new PixelRect(50, 50, 200, 100),
            result.InteractiveRegions[0]);
        Assert.Equal(
            new PixelRect(400, 250, 300, 200),
            result.InteractiveRegions[1]);
    }

    [Fact]
    public void PlannerClipsContainersToTheirDisplay()
    {
        var container = new DesktopContainerPlacement(
            "edge",
            "primary",
            new PixelRect(50, 150, 100, 100));

        DesktopHostSurfacePlan perContainer = Assert.Single(
            DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerContainer,
                [Display],
                [container]));
        DesktopHostSurfacePlan perDisplay = Assert.Single(
            DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerDisplay,
                [Display],
                [container]));

        Assert.Equal(new PixelRect(100, 200, 50, 50), perContainer.WindowBounds);
        Assert.Equal(new PixelRect(0, 0, 50, 50), perDisplay.InteractiveRegions[0]);
    }

    [Fact]
    public void PlannerRejectsContainersAssignedToUnknownDisplays()
    {
        var container = new DesktopContainerPlacement(
            "orphan",
            "missing",
            new PixelRect(0, 0, 100, 100));

        Assert.Throws<ArgumentException>(
            () => DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerDisplay,
                [Display],
                [container]));
    }

    [Fact]
    public void PerDisplayGroupsContainersUsingDisplayRelativeCoordinates()
    {
        DesktopDisplayPlacement[] displays =
        [
            Display,
            new("left", new PixelRect(-900, 100, 800, 600)),
        ];
        DesktopContainerPlacement[] containers =
        [
            new("primary-item", "primary", new PixelRect(150, 250, 200, 100)),
            new("left-item", "left", new PixelRect(-850, 150, 300, 200)),
        ];

        IReadOnlyList<DesktopHostSurfacePlan> result =
            DesktopHostWindowPlanner.Create(
                DesktopHostWindowModel.PerDisplay,
                displays,
                containers);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            new PixelRect(50, 50, 300, 200),
            result[1].InteractiveRegions[0]);
    }
}
