using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceRecoveryPreflightTests
{
    [Fact]
    public async Task RecoveryMatrixIsFiniteRestartAwareAndSandboxed()
    {
        ProductWorkspaceRecoveryPreflightResult result =
            await ProductWorkspaceRecoveryPreflight.RunAsync();

        Assert.Equal("Passed", result.Outcome);
        Assert.Equal(5, result.ScenarioCount);
        Assert.True(result.BackupAcceptedAfterRestart);
        Assert.True(result.SafeModeResetAfterRestart);
        Assert.True(result.CatalogRecovered);
        Assert.True(result.ExplicitRetrySucceeded);
        Assert.True(result.CancellationLeftNoRetry);
        Assert.True(result.TemporarySandboxCleaned);
        Assert.False(result.ReadsRealDesktop);
        Assert.False(result.RealFileOperationsAllowed);
    }
}
