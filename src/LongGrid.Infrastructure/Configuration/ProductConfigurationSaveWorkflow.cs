using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationSaveAttemptStatus
{
    Saved,
    Failed,
    NoRetryAvailable,
    Completed,
}

public sealed record ProductConfigurationSaveAttemptResult(
    ProductConfigurationSaveAttemptStatus Status,
    ProductConfigurationSaveError? Error,
    bool CanRetry);

public sealed class ProductConfigurationSaveWorkflow
    : IProductConfigurationSaveWorkflow
{
    private readonly object gate = new();
    private readonly ProductConfigurationSaveCoordinator coordinator;
    private ProductConfigurationDocument? retryDocument;
    private long latestAttempt;
    private bool accepting = true;

    public ProductConfigurationSaveWorkflow(
        ProductConfigurationSaveCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        this.coordinator = coordinator;
    }

    public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
        ProductWorkspaceState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        return projection.IsSuccess
            ? SaveAsync(projection.Document!, cancellationToken)
            : RejectInvalidSave();
    }

    public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
        ProductConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (gate)
        {
            if (!accepting)
            {
                return Task.FromResult(CompletedResult());
            }

            long attempt = ++latestAttempt;
            retryDocument = null;
            ProductConfigurationDocument captured;
            try
            {
                captured = Snapshot(document);
            }
            catch (ProductConfigurationContractException)
            {
                return Task.FromResult(InvalidConfigurationResult());
            }

            Task save = coordinator.EnqueueAsync(captured, cancellationToken);
            return ObserveAsync(save, captured, attempt);
        }
    }

    public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!accepting)
            {
                return Task.FromResult(CompletedResult());
            }

            if (retryDocument is null)
            {
                return Task.FromResult(
                    new ProductConfigurationSaveAttemptResult(
                        ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                        null,
                        CanRetry: false));
            }

            ProductConfigurationDocument captured = Snapshot(retryDocument);
            long attempt = ++latestAttempt;
            Task save = coordinator.EnqueueAsync(captured, cancellationToken);
            return ObserveAsync(save, captured, attempt);
        }
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            accepting = false;
            retryDocument = null;
            return coordinator.CompleteAsync(cancellationToken);
        }
    }

    private async Task<ProductConfigurationSaveAttemptResult> ObserveAsync(
        Task save,
        ProductConfigurationDocument captured,
        long attempt)
    {
        try
        {
            await save.ConfigureAwait(false);
            lock (gate)
            {
                if (attempt == latestAttempt)
                {
                    retryDocument = null;
                }
            }

            return new(
                ProductConfigurationSaveAttemptStatus.Saved,
                null,
                CanRetry: false);
        }
        catch (ProductConfigurationSaveException exception)
        {
            bool retainedForRetry = false;
            lock (gate)
            {
                if (attempt == latestAttempt && IsRetryable(exception.Error))
                {
                    retryDocument = captured;
                    retainedForRetry = true;
                }
            }

            return new(
                ProductConfigurationSaveAttemptStatus.Failed,
                exception.Error,
                retainedForRetry);
        }
    }

    private static bool IsRetryable(ProductConfigurationSaveError error) =>
        error is ProductConfigurationSaveError.DamagedEvidence
            or ProductConfigurationSaveError.WriteLeaseUnavailable
            or ProductConfigurationSaveError.IoFailure;

    private static ProductConfigurationDocument Snapshot(
        ProductConfigurationDocument document) =>
        ProductConfigurationJson.Deserialize(
            ProductConfigurationJson.SerializeToUtf8Bytes(document));

    private static ProductConfigurationSaveAttemptResult CompletedResult() =>
        new(
            ProductConfigurationSaveAttemptStatus.Completed,
            null,
            CanRetry: false);

    private Task<ProductConfigurationSaveAttemptResult> RejectInvalidSave()
    {
        lock (gate)
        {
            if (!accepting)
            {
                return Task.FromResult(CompletedResult());
            }

            ++latestAttempt;
            retryDocument = null;
            return Task.FromResult(InvalidConfigurationResult());
        }
    }

    private static ProductConfigurationSaveAttemptResult InvalidConfigurationResult() =>
        new(
            ProductConfigurationSaveAttemptStatus.Failed,
            ProductConfigurationSaveError.InvalidConfiguration,
            CanRetry: false);
}
