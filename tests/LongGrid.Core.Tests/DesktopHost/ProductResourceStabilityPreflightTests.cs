using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductResourceStabilityPreflightTests
{
    [Fact]
    public async Task AcceleratedChurnClosesEveryOwnedResourceWithoutClaiming24Hours()
    {
        ProductResourceStabilityPreflightResult result =
            await ProductResourceStabilityPreflight.RunAsync();

        Assert.Equal("AcceleratedPass", result.Outcome);
        Assert.Equal(200, result.LifecycleIterations);
        Assert.Equal(200, result.CatalogIterations);
        Assert.Equal(200, result.ClassifierIterations);
        Assert.Equal(600, result.SyntheticSurfacesCreated);
        Assert.Equal(600, result.SyntheticSurfacesReleased);
        Assert.Equal(600, result.CatalogRefreshes);
        Assert.Equal(1_200, result.CatalogNotifications);
        Assert.True(result.SystemEventStateRecoveredEveryIteration);
        Assert.True(result.AllOwnedResourcesReleased);
        Assert.True(result.ThumbnailWorkerIsolationGateRequired);
        Assert.True(result.RealApp24HourSoakRequired);
        Assert.False(result.Real24HourEvidenceCollected);
        Assert.False(result.ReadsRealDesktop);
        Assert.False(result.CreatesNativeWindows);
        Assert.False(result.RealFileOperationsAllowed);
    }
}
