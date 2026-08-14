using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostRecoveryPreflightTests
{
    [Fact]
    public async Task NativeLifecycleMatrixIsFiniteSyntheticAndFullyReleased()
    {
        ProductDesktopHostRecoveryPreflightResult result =
            await ProductDesktopHostRecoveryPreflight.RunAsync();

        Assert.Equal("Passed", result.Outcome);
        Assert.Equal(5, result.ScenarioCount);
        Assert.True(result.ExplorerRestartRecovered);
        Assert.True(result.SessionUnavailableRecovered);
        Assert.True(result.TopologyUnavailableRecovered);
        Assert.True(result.DisplayReplacementReleasedOldSurfaces);
        Assert.True(result.HostRestartRejectedStaleIdentity);
        Assert.True(result.AllSyntheticSurfacesReleased);
        Assert.False(result.ReadsRealDesktop);
        Assert.False(result.CreatesNativeWindows);
        Assert.False(result.RealFileOperationsAllowed);
    }
}
