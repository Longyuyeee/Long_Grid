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

    [Theory]
    [InlineData(ProductContainerFolderBindingResolution.Missing)]
    [InlineData(ProductContainerFolderBindingResolution.AccessDenied)]
    [InlineData(ProductContainerFolderBindingResolution.Replaced)]
    [InlineData(ProductContainerFolderBindingResolution.InvalidTarget)]
    [InlineData(ProductContainerFolderBindingResolution.Unavailable)]
    public void LaterUsableGenerationPreservesFiniteRecoveryOrigin(
        ProductContainerFolderBindingResolution previousResolution)
    {
        ProductWorkspaceFolderContentSet previous = ContentSet(
            generation: 4,
            previousResolution switch
            {
                ProductContainerFolderBindingResolution.AccessDenied =>
                    ProductWorkspaceFolderContentStatus.AccessDenied,
                ProductContainerFolderBindingResolution.InvalidTarget =>
                    ProductWorkspaceFolderContentStatus.InvalidTarget,
                _ => ProductWorkspaceFolderContentStatus.BindingUnavailable,
            },
            previousResolution);
        ProductWorkspaceFolderContentSet current = ContentSet(
            generation: 5,
            ProductWorkspaceFolderContentStatus.Empty,
            ProductContainerFolderBindingResolution.Resolved);

        ProductWorkspaceFolderContentSet recovered =
            current.MarkRecoveriesFrom(previous);
        ProductWorkspaceContainerFolderContent content = Assert.IsType<
            ProductWorkspaceContainerFolderContent>(recovered.Find("container-1"));
        ProductWorkspaceReadContainer readContainer = Assert.Single(
            ProductWorkspaceReadModel.Create(
                State(ProductContainerFolderBindingResolution.Resolved),
                recovered).Snapshot!.Containers);

        Assert.Equal(ProductWorkspaceFolderContentStatus.Empty, content.Status);
        Assert.Equal(previousResolution,
            content.RecoveredFromBindingResolution);
        Assert.True(content.HasValidShape);
        Assert.True(content.HasUsableProjection);
        Assert.Equal(previousResolution,
            readContainer.FolderBindingRecoveredFrom);
        Assert.Equal(ProductWorkspaceContainerHealth.Ready, readContainer.Health);
        Assert.DoesNotContain("C:\\private", content.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoveryIsNotReplayedWithoutANewerFailureTransition()
    {
        ProductWorkspaceFolderContentSet resolved = ContentSet(
            generation: 5,
            ProductWorkspaceFolderContentStatus.Ready,
            ProductContainerFolderBindingResolution.Resolved);
        ProductWorkspaceFolderContentSet later = ContentSet(
            generation: 6,
            ProductWorkspaceFolderContentStatus.Ready,
            ProductContainerFolderBindingResolution.Resolved);
        ProductWorkspaceFolderContentSet sameGenerationFailure = ContentSet(
            generation: 6,
            ProductWorkspaceFolderContentStatus.BindingUnavailable,
            ProductContainerFolderBindingResolution.Missing);
        ProductWorkspaceFolderContentSet earlierFailure = ContentSet(
            generation: 5,
            ProductWorkspaceFolderContentStatus.BindingUnavailable,
            ProductContainerFolderBindingResolution.Missing);
        ProductWorkspaceFolderContentSet recovered =
            later.MarkRecoveriesFrom(earlierFailure);

        Assert.Null(later.MarkRecoveriesFrom(resolved)
            .Find("container-1")!.RecoveredFromBindingResolution);
        Assert.Equal(ProductContainerFolderBindingResolution.Missing,
            recovered.Find("container-1")!.RecoveredFromBindingResolution);
        Assert.Null(recovered.MarkRecoveriesFrom(sameGenerationFailure)
            .Find("container-1")!.RecoveredFromBindingResolution);
    }

    [Fact]
    public void RecoveryMetadataRequiresUsableResolvedContentAndFiniteOrigin()
    {
        var resolvedOrigin = new ProductWorkspaceContainerFolderContent(
            "container-1",
            2,
            ProductWorkspaceFolderContentStatus.Empty,
            Array.Empty<ProductWorkspaceFolderContentItem>(),
            BindingResolution: ProductContainerFolderBindingResolution.Resolved,
            RecoveredFromBindingResolution:
                ProductContainerFolderBindingResolution.Resolved);
        var unresolvedCurrent = new ProductWorkspaceContainerFolderContent(
            "container-1",
            2,
            ProductWorkspaceFolderContentStatus.BindingUnavailable,
            Array.Empty<ProductWorkspaceFolderContentItem>(),
            BindingResolution: ProductContainerFolderBindingResolution.Missing,
            RecoveredFromBindingResolution:
                ProductContainerFolderBindingResolution.AccessDenied);

        Assert.False(resolvedOrigin.HasValidShape);
        Assert.False(unresolvedCurrent.HasValidShape);
    }

    private static ProductWorkspaceFolderContentSet ContentSet(
        long generation,
        ProductWorkspaceFolderContentStatus status,
        ProductContainerFolderBindingResolution resolution) => new(
            generation,
            new Dictionary<string, ProductWorkspaceContainerFolderContent>(
                StringComparer.Ordinal)
            {
                ["container-1"] = new(
                    "container-1",
                    generation,
                    status,
                    Array.Empty<ProductWorkspaceFolderContentItem>(),
                    BindingResolution: resolution),
            });

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
