using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceScalePreflightTests
{
    [Fact]
    public async Task MaximumProductScalePipelineRemainsBoundedAndReadOnly()
    {
        ProductWorkspaceScalePreflightResult result =
            await ProductWorkspaceScalePreflight.RunAsync();

        Assert.Equal(ProductWorkspaceScalePreflightOutcome.Passed, result.Outcome);
        Assert.Equal(ProductConfigurationLimits.MaximumContainers, result.ContainerCount);
        Assert.Equal(ProductConfigurationLimits.MaximumItems, result.ItemCount);
        Assert.Equal(result.ItemCount, result.ResolvedItemCount);
        Assert.Equal(result.ItemCount, result.ProjectedItemCount);
        Assert.Equal(result.ItemCount, result.SelectionActionCount);
        Assert.Equal(1, result.SearchMatchCount);
        Assert.Equal(result.ContainerCount, result.SortedContainerCount);
        Assert.Equal(result.ContainerCount, result.ReadyContainerCount);
        Assert.All(result.Metrics, metric => Assert.True(metric.Passed));
        Assert.True(result.TemporarySandboxCleaned);
        Assert.False(result.ReadsRealDesktop);
        Assert.False(result.RealFileOperationsAllowed);
    }
}
