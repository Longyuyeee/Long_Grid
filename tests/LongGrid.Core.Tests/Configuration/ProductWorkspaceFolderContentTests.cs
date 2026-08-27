using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceFolderContentTests
{
    [Theory]
    [InlineData(
        ProductContainerFolderBindingResolution.Resolved,
        ProductWorkspaceFolderContentStatus.AwaitingRefresh)]
    [InlineData(
        ProductContainerFolderBindingResolution.AccessDenied,
        ProductWorkspaceFolderContentStatus.AccessDenied)]
    [InlineData(
        ProductContainerFolderBindingResolution.InvalidTarget,
        ProductWorkspaceFolderContentStatus.InvalidTarget)]
    [InlineData(
        ProductContainerFolderBindingResolution.Missing,
        ProductWorkspaceFolderContentStatus.BindingUnavailable)]
    [InlineData(
        ProductContainerFolderBindingResolution.Replaced,
        ProductWorkspaceFolderContentStatus.BindingUnavailable)]
    [InlineData(
        ProductContainerFolderBindingResolution.Unavailable,
        ProductWorkspaceFolderContentStatus.BindingUnavailable)]
    public void PendingSetPublishesFinitePathFreeStatus(
        ProductContainerFolderBindingResolution resolution,
        ProductWorkspaceFolderContentStatus expectedStatus)
    {
        ProductWorkspaceState state = State(resolution);

        ProductWorkspaceFolderContentSet pending =
            ProductWorkspaceFolderContentSet.CreatePending(state, generation: 7);
        ProductWorkspaceContainerFolderContent actual = Assert.IsType<
            ProductWorkspaceContainerFolderContent>(pending.Find("container-1"));

        Assert.Equal(7, pending.Generation);
        Assert.Equal(expectedStatus, actual.Status);
        Assert.Equal(resolution, actual.BindingResolution);
        Assert.Empty(actual.Items);
        Assert.True(actual.HasValidShape);
        Assert.False(actual.HasUsableProjection);
        Assert.DoesNotContain("C:\\private", actual.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AwaitingRefreshKeepsResolvedBindingHealthyWithoutPublishingItems()
    {
        ProductWorkspaceState state = State(
            ProductContainerFolderBindingResolution.Resolved);
        ProductWorkspaceFolderContentSet pending =
            ProductWorkspaceFolderContentSet.CreatePending(state, generation: 3);

        ProductWorkspaceReadResult read = ProductWorkspaceReadModel.Create(
            state,
            pending);

        Assert.True(read.IsSuccess);
        ProductWorkspaceReadContainer container = Assert.Single(
            read.Snapshot!.Containers);
        Assert.Equal(ProductWorkspaceFolderContentStatus.AwaitingRefresh,
            container.FolderContentStatus);
        Assert.Equal(ProductWorkspaceContainerHealth.Ready, container.Health);
        Assert.Equal(0, container.FolderContentItemCount);
        Assert.Empty(container.Items);
    }

    [Fact]
    public void UndefinedStatusAndNegativeSkippedCountAreInvalidShapes()
    {
        var undefined = new ProductWorkspaceContainerFolderContent(
            "container-1",
            1,
            (ProductWorkspaceFolderContentStatus)int.MaxValue,
            Array.Empty<ProductWorkspaceFolderContentItem>());
        var negativeSkipped = new ProductWorkspaceContainerFolderContent(
            "container-1",
            1,
            ProductWorkspaceFolderContentStatus.ReadyWithSkippedEntries,
            Array.Empty<ProductWorkspaceFolderContentItem>(),
            SkippedReparsePointCount: -1);

        Assert.False(undefined.HasValidShape);
        Assert.False(negativeSkipped.HasValidShape);
    }

    private static ProductWorkspaceState State(
        ProductContainerFolderBindingResolution resolution) => new()
        {
            ProfileId = "default",
            Containers =
        [
            new ProductContainerState
            {
                Id = "container-1",
                Name = "Work",
                Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                Placement = new()
                {
                    DisplayKey = "display-1",
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = Array.Empty<ProductItemReferenceState>(),
                FolderBinding = new()
                {
                    PersistedTarget = "C:\\private\\bound",
                    VolumeSerialNumber = 1,
                    FileId = new string('A', 32),
                    Resolution = resolution,
                    ResolvedTarget = resolution ==
                        ProductContainerFolderBindingResolution.Resolved
                            ? "C:\\private\\bound"
                            : null,
                },
            },
        ],
        };
}
