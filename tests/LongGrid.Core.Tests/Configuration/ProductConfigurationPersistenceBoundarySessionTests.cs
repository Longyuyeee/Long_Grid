using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationPersistenceBoundarySessionTests
{
    [Fact]
    public async Task PrepareBaselineCreatesDeterministicPrimaryAndBackup()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationPersistenceBoundarySession session = new(directory.Path);

        ProductConfigurationPersistenceBoundaryResult result = await session.ExecuteAsync(
            ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline);

        Assert.Equal(
            ProductConfigurationPersistenceBoundaryOutcome.BaselinePrepared,
            result.Outcome);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, result.LoadStatus);
        Assert.Null(result.SaveError);
        Assert.Equal(64, result.PrimarySha256.Length);
        Assert.Equal(64, result.BackupSha256?.Length);
        Assert.NotEqual(result.PrimarySha256, result.BackupSha256);
        Assert.False(result.TemporaryFilePresent);
    }

    [Fact]
    public async Task PrepareBaselineRefusesToOverwriteExistingSessionData()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "existing.txt"), "preserve");
        ProductConfigurationPersistenceBoundarySession session = new(directory.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(
            ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline));

        Assert.Equal(
            "preserve",
            await File.ReadAllTextAsync(Path.Combine(directory.Path, "existing.txt")));
    }

    [Fact]
    public async Task WritableFailureAttemptIsReportedAsUnexpectedSuccess()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationPersistenceBoundarySession session = new(directory.Path);
        await session.ExecuteAsync(ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline);

        ProductConfigurationPersistenceBoundaryResult result = await session.ExecuteAsync(
            ProductConfigurationPersistenceBoundaryPhase.AttemptFailure);

        Assert.Equal(
            ProductConfigurationPersistenceBoundaryOutcome.UnexpectedSaveSuccess,
            result.Outcome);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, result.LoadStatus);
        Assert.Null(result.SaveError);
        Assert.False(result.TemporaryFilePresent);
    }

    [Fact]
    public async Task RecoveryRetryPublishesCandidateAfterBaseline()
    {
        using TemporaryDirectory directory = new(create: false);
        ProductConfigurationPersistenceBoundarySession session = new(directory.Path);
        ProductConfigurationPersistenceBoundaryResult baseline = await session.ExecuteAsync(
            ProductConfigurationPersistenceBoundaryPhase.PrepareBaseline);

        ProductConfigurationPersistenceBoundaryResult result = await session.ExecuteAsync(
            ProductConfigurationPersistenceBoundaryPhase.RecoverAndRetry);

        Assert.Equal(
            ProductConfigurationPersistenceBoundaryOutcome.RecoverySucceeded,
            result.Outcome);
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, result.LoadStatus);
        Assert.Null(result.SaveError);
        Assert.Equal(baseline.PrimarySha256, result.BackupSha256);
        Assert.NotEqual(baseline.PrimarySha256, result.PrimarySha256);
        Assert.False(result.TemporaryFilePresent);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "long-grid-persistence-boundary-tests",
                Guid.NewGuid().ToString("N"));
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
