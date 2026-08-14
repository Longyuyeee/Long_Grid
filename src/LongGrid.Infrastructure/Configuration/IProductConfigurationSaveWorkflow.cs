using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public interface IProductConfigurationSaveWorkflow
{
    Task<ProductConfigurationSaveAttemptResult> SaveAsync(
        ProductConfigurationDocument document,
        CancellationToken cancellationToken = default);

    Task<ProductConfigurationSaveAttemptResult> RetryAsync(
        CancellationToken cancellationToken = default);

    void DiscardRetry();

    Task CompleteAsync(CancellationToken cancellationToken = default);
}
