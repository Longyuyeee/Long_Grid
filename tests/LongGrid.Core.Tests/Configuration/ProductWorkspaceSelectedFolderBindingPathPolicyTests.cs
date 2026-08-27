using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSelectedFolderBindingPathPolicyTests
{
    [Theory]
    [InlineData(ProductContainerFolderBindingResolution.Resolved)]
    [InlineData(ProductContainerFolderBindingResolution.Missing)]
    [InlineData(ProductContainerFolderBindingResolution.AccessDenied)]
    [InlineData(ProductContainerFolderBindingResolution.Replaced)]
    public void CurrentSelectedBindingExposesOnlyConfiguredDisplayPath(
        ProductContainerFolderBindingResolution resolution)
    {
        string target = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "LongGrid.当前绑定文件夹"));
        ProductWorkspaceSelectedFolderBindingPathResult result =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                State(Binding(target, resolution)),
                currentEditRevision: 7,
                requestedEditRevision: 7,
                containerOrdinal: 1);

        Assert.True(result.IsAvailable);
        Assert.Equal(
            ProductWorkspaceSelectedFolderBindingPathStatus.Available,
            result.Status);
        Assert.Equal(target, result.DisplayPath);
    }

    [Theory]
    [InlineData(6, 7, 1,
        ProductWorkspaceSelectedFolderBindingPathStatus.StaleEditRevision)]
    [InlineData(7, 7, 0,
        ProductWorkspaceSelectedFolderBindingPathStatus.InvalidOrdinal)]
    [InlineData(7, 7, 2,
        ProductWorkspaceSelectedFolderBindingPathStatus.InvalidOrdinal)]
    public void StaleOrInvalidSelectionFailsClosed(
        long requestedRevision,
        long currentRevision,
        int ordinal,
        ProductWorkspaceSelectedFolderBindingPathStatus expected)
    {
        ProductWorkspaceSelectedFolderBindingPathResult result =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                State(Binding(Path.GetTempPath())),
                currentRevision,
                requestedRevision,
                ordinal);

        Assert.False(result.IsAvailable);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.DisplayPath);
    }

    [Fact]
    public void MissingStateAndUnboundContainerExposeNoPath()
    {
        ProductWorkspaceSelectedFolderBindingPathResult missing =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                state: null,
                currentEditRevision: 1,
                requestedEditRevision: 1,
                containerOrdinal: 1);
        ProductWorkspaceSelectedFolderBindingPathResult unbound =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                State(binding: null),
                currentEditRevision: 1,
                requestedEditRevision: 1,
                containerOrdinal: 1);

        Assert.Equal(
            ProductWorkspaceSelectedFolderBindingPathStatus.UnavailableState,
            missing.Status);
        Assert.Null(missing.DisplayPath);
        Assert.Equal(
            ProductWorkspaceSelectedFolderBindingPathStatus.Unbound,
            unbound.Status);
        Assert.Null(unbound.DisplayPath);
    }

    [Fact]
    public void ControlCharactersInPersistedTargetFailClosed()
    {
        ProductWorkspaceSelectedFolderBindingPathResult result =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                State(Binding("C:\\folder\nprivate")),
                currentEditRevision: 1,
                requestedEditRevision: 1,
                containerOrdinal: 1);

        Assert.Equal(
            ProductWorkspaceSelectedFolderBindingPathStatus.InvalidPath,
            result.Status);
        Assert.Null(result.DisplayPath);
    }

    private static ProductWorkspaceState State(
        ProductContainerFolderBindingState? binding) => new()
        {
            ProfileId = "profile",
            Containers =
            [
                new()
                {
                    Id = "container-1",
                    Name = "工作",
                    Appearance = new()
                    {
                        Color = "#FF446688",
                        Opacity = 0.8,
                    },
                    Placement = new()
                    {
                        DisplayKey = "display-1",
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                    FolderBinding = binding,
                },
            ],
        };

    private static ProductContainerFolderBindingState Binding(
        string target,
        ProductContainerFolderBindingResolution resolution =
            ProductContainerFolderBindingResolution.Resolved) => new()
            {
                PersistedTarget = target,
                VolumeSerialNumber = 1,
                FileId = "00112233445566778899AABBCCDDEEFF",
                Resolution = resolution,
                ResolvedTarget = resolution ==
                ProductContainerFolderBindingResolution.Resolved
                ? target
                : null,
            };
}
