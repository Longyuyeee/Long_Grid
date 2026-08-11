using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public interface IProductWorkspaceSaveScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    Task YieldAsync(CancellationToken cancellationToken);
}

public enum ProductWorkspaceSaveSubmissionStatus
{
    Accepted,
    NoChange,
    RejectedEdit,
    InvalidState,
    Completed,
}

public sealed record ProductWorkspaceSaveSubmissionResult(
    ProductWorkspaceSaveSubmissionStatus Status,
    ProductWorkspaceSaveSnapshot Snapshot,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceProjectionError ProjectionError,
    ProductConfigurationError ConfigurationError)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceSaveSubmissionStatus.Accepted;
}

public enum ProductWorkspaceSaveRetryStatus
{
    Accepted,
    NotAvailable,
    Completed,
}

public sealed record ProductWorkspaceSaveRetryResult(
    ProductWorkspaceSaveRetryStatus Status,
    ProductWorkspaceSaveSnapshot Snapshot);

public enum ProductWorkspaceSaveCompletionStatus
{
    Completed,
    BlockedByFailure,
}

public sealed record ProductWorkspaceSaveCompletionResult(
    ProductWorkspaceSaveCompletionStatus Status,
    ProductWorkspaceSaveSnapshot Snapshot);

public sealed class ProductWorkspaceSaveController : IAsyncDisposable
{
    public static readonly TimeSpan DefaultDebounceDelay =
        TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan MaximumDebounceDelay =
        TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private readonly SemaphoreSlim completionGate = new(1, 1);
    private readonly SemaphoreSlim saveSubmissionGate = new(1, 1);
    private readonly IProductConfigurationSaveWorkflow workflow;
    private readonly IProductWorkspaceSaveScheduler scheduler;
    private readonly TimeSpan debounceDelay;
    private readonly HashSet<Task> operations = [];
    private ProductWorkspaceSaveSnapshot snapshot =
        ProductWorkspaceSaveSnapshot.Initial;
    private ProductConfigurationDocument? pendingDocument;
    private CancellationTokenSource? debounceCancellation;
    private bool accepting = true;
    private bool completed;
    private bool resourcesDisposed;

    public ProductWorkspaceSaveController(
        IProductConfigurationSaveWorkflow workflow,
        IProductWorkspaceSaveScheduler? scheduler = null,
        TimeSpan? debounceDelay = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        this.workflow = workflow;
        this.scheduler = scheduler ?? new SystemProductWorkspaceSaveScheduler();
        this.debounceDelay = debounceDelay ?? DefaultDebounceDelay;
        if (this.debounceDelay <= TimeSpan.Zero
            || this.debounceDelay > MaximumDebounceDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }
    }

    public event EventHandler<ProductWorkspaceSaveSnapshot>? SnapshotChanged;

    public ProductWorkspaceSaveSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public ProductWorkspaceSaveSubmissionResult Submit(
        ProductWorkspaceEditResult edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.IsSuccess)
        {
            return Submission(
                ProductWorkspaceSaveSubmissionStatus.RejectedEdit,
                edit.Error,
                edit.ProjectionError,
                edit.ConfigurationError);
        }

        if (!edit.Changed)
        {
            return Submission(ProductWorkspaceSaveSubmissionStatus.NoChange);
        }

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(edit.State!);
        if (!projection.IsSuccess)
        {
            return Submission(
                ProductWorkspaceSaveSubmissionStatus.InvalidState,
                ProductWorkspaceEditError.InvalidState,
                projection.Error,
                projection.ConfigurationError);
        }

        CancellationTokenSource? previousCancellation;
        ProductWorkspaceSaveSnapshot published;
        lock (gate)
        {
            if (!accepting)
            {
                return SubmissionUnsafe(
                    ProductWorkspaceSaveSubmissionStatus.Completed);
            }

            ProductWorkspaceSaveTransition transition =
                ProductWorkspaceSaveStateMachine.AcceptEdit(snapshot);
            snapshot = transition.Snapshot;
            pendingDocument = projection.Document!;
            previousCancellation = debounceCancellation;
            debounceCancellation = new();
            TrackUnsafe(
                RunDebouncedSaveAsync(
                    transition.Command.Revision,
                    projection.Document!,
                    debounceCancellation.Token));
            published = snapshot;
        }

        CancelAndDispose(previousCancellation);
        Publish(published);
        return Submission(ProductWorkspaceSaveSubmissionStatus.Accepted);
    }

    public ProductWorkspaceSaveRetryResult Retry()
    {
        ProductWorkspaceSaveSnapshot published;
        lock (gate)
        {
            if (!accepting)
            {
                return new(
                    ProductWorkspaceSaveRetryStatus.Completed,
                    snapshot);
            }

            ProductWorkspaceSaveTransition transition =
                ProductWorkspaceSaveStateMachine.RetryRequested(snapshot);
            if (transition.Command.Kind != ProductWorkspaceSaveCommandKind.Retry)
            {
                return new(
                    ProductWorkspaceSaveRetryStatus.NotAvailable,
                    snapshot);
            }

            snapshot = transition.Snapshot;
            TrackUnsafe(RunRetryAsync(transition.Command.Revision));
            published = snapshot;
        }

        Publish(published);
        return new(ProductWorkspaceSaveRetryStatus.Accepted, published);
    }

    public async Task<ProductWorkspaceSaveCompletionResult> CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (resourcesDisposed)
            {
                return new(
                    ProductWorkspaceSaveCompletionStatus.Completed,
                    snapshot);
            }
        }

        await completionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource? delayToCancel = null;
            ProductWorkspaceSaveSnapshot? published = null;
            Task[] acceptedOperations;
            lock (gate)
            {
                if (completed)
                {
                    return new(
                        ProductWorkspaceSaveCompletionStatus.Completed,
                        snapshot);
                }

                if (snapshot.Status == ProductWorkspaceSaveStatus.Failed)
                {
                    return new(
                        ProductWorkspaceSaveCompletionStatus.BlockedByFailure,
                        snapshot);
                }

                accepting = false;
                if (snapshot.Status == ProductWorkspaceSaveStatus.WaitingForDebounce
                    && pendingDocument is not null)
                {
                    delayToCancel = debounceCancellation;
                    debounceCancellation = null;
                    ProductWorkspaceSaveTransition transition =
                        ProductWorkspaceSaveStateMachine.DebounceElapsed(
                            snapshot,
                            snapshot.CurrentRevision);
                    snapshot = transition.Snapshot;
                    TrackUnsafe(
                        RunSaveAsync(
                            transition.Command.Revision,
                            pendingDocument));
                    published = snapshot;
                }

                acceptedOperations = operations.ToArray();
            }

            CancelAndDispose(delayToCancel);
            if (published is not null)
            {
                Publish(published);
            }

            try
            {
                await Task.WhenAll(acceptedOperations)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                lock (gate)
                {
                    if (!completed)
                    {
                        accepting = true;
                    }
                }

                throw;
            }

            lock (gate)
            {
                if (snapshot.Status == ProductWorkspaceSaveStatus.Failed)
                {
                    accepting = true;
                    return new(
                        ProductWorkspaceSaveCompletionStatus.BlockedByFailure,
                        snapshot);
                }
            }

            await workflow.CompleteAsync(CancellationToken.None)
                .ConfigureAwait(false);
            lock (gate)
            {
                completed = true;
                pendingDocument = null;
                CancelAndDispose(debounceCancellation);
                debounceCancellation = null;
                return new(
                    ProductWorkspaceSaveCompletionStatus.Completed,
                    snapshot);
            }
        }
        finally
        {
            completionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (resourcesDisposed)
            {
                return;
            }
        }

        ProductWorkspaceSaveCompletionResult completion =
            await CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        if (completion.Status == ProductWorkspaceSaveCompletionStatus.BlockedByFailure)
        {
            throw new InvalidOperationException(
                "The latest product workspace state must be retried or edited before disposal.");
        }

        lock (gate)
        {
            if (resourcesDisposed)
            {
                return;
            }

            resourcesDisposed = true;
        }

        completionGate.Dispose();
        saveSubmissionGate.Dispose();
    }

    private async Task RunDebouncedSaveAsync(
        long revision,
        ProductConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await scheduler.DelayAsync(debounceDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ProductWorkspaceSaveSnapshot published;
        lock (gate)
        {
            ProductWorkspaceSaveTransition transition =
                ProductWorkspaceSaveStateMachine.DebounceElapsed(
                    snapshot,
                    revision);
            if (transition.Command.Kind != ProductWorkspaceSaveCommandKind.Save)
            {
                return;
            }

            snapshot = transition.Snapshot;
            published = snapshot;
        }

        Publish(published);
        await RunSaveAsync(revision, document).ConfigureAwait(false);
    }

    private async Task RunSaveAsync(
        long revision,
        ProductConfigurationDocument document)
    {
        await scheduler.YieldAsync(CancellationToken.None).ConfigureAwait(false);
        Task<ProductConfigurationSaveAttemptResult>? saveOperation = null;
        ProductConfigurationSaveAttemptResult? result = null;
        await saveSubmissionGate.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (!ProductWorkspaceSaveStateMachine.CanSubmitSave(
                        snapshot,
                        revision))
                {
                    return;
                }
            }

            try
            {
                saveOperation = workflow.SaveAsync(
                    document,
                    CancellationToken.None);
            }
            catch (ProductConfigurationSaveException exception)
            {
                result = new(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    exception.Error,
                    CanRetry: false);
            }
            catch (IOException)
            {
                result = new(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    ProductConfigurationSaveError.IoFailure,
                    CanRetry: false);
            }
        }
        finally
        {
            saveSubmissionGate.Release();
        }

        if (saveOperation is not null)
        {
            try
            {
                result = await saveOperation.ConfigureAwait(false);
            }
            catch (ProductConfigurationSaveException exception)
            {
                result = new(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    exception.Error,
                    CanRetry: false);
            }
            catch (IOException)
            {
                result = new(
                    ProductConfigurationSaveAttemptStatus.Failed,
                    ProductConfigurationSaveError.IoFailure,
                    CanRetry: false);
            }
        }

        CompleteRevision(revision, MapFailure(result!));
    }

    private async Task RunRetryAsync(long revision)
    {
        await Task.Yield();
        ProductConfigurationSaveAttemptResult result;
        try
        {
            result = await workflow.RetryAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ProductConfigurationSaveException exception)
        {
            result = new(
                ProductConfigurationSaveAttemptStatus.Failed,
                exception.Error,
                CanRetry: false);
        }
        catch (IOException)
        {
            result = new(
                ProductConfigurationSaveAttemptStatus.Failed,
                ProductConfigurationSaveError.IoFailure,
                CanRetry: false);
        }

        ProductWorkspaceSaveFailure failure = result.Status switch
        {
            ProductConfigurationSaveAttemptStatus.Saved =>
                ProductWorkspaceSaveFailure.None,
            ProductConfigurationSaveAttemptStatus.Failed =>
                MapFailure(result),
            _ => ProductWorkspaceSaveFailure.RetryUnavailable,
        };
        CompleteRevision(revision, failure);
    }

    private void CompleteRevision(
        long revision,
        ProductWorkspaceSaveFailure failure)
    {
        ProductWorkspaceSaveSnapshot? published = null;
        lock (gate)
        {
            ProductWorkspaceSaveTransition transition =
                ProductWorkspaceSaveStateMachine.SaveCompleted(
                    snapshot,
                    revision,
                    failure);
            if (transition.Snapshot == snapshot)
            {
                return;
            }

            snapshot = transition.Snapshot;
            if (revision == snapshot.CurrentRevision
                && snapshot.Status is ProductWorkspaceSaveStatus.Saved
                    or ProductWorkspaceSaveStatus.Failed)
            {
                pendingDocument = null;
            }

            published = snapshot;
        }

        Publish(published!);
    }

    private static ProductWorkspaceSaveFailure MapFailure(
        ProductConfigurationSaveAttemptResult result)
    {
        if (result.Status == ProductConfigurationSaveAttemptStatus.Saved)
        {
            return ProductWorkspaceSaveFailure.None;
        }

        return result.Error switch
        {
            ProductConfigurationSaveError.InvalidConfiguration =>
                ProductWorkspaceSaveFailure.InvalidConfiguration,
            ProductConfigurationSaveError.DamagedEvidence =>
                ProductWorkspaceSaveFailure.DamagedEvidence,
            ProductConfigurationSaveError.WriteLeaseUnavailable =>
                ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
            ProductConfigurationSaveError.IoFailure =>
                ProductWorkspaceSaveFailure.IoFailure,
            _ => ProductWorkspaceSaveFailure.RetryUnavailable,
        };
    }

    private ProductWorkspaceSaveSubmissionResult Submission(
        ProductWorkspaceSaveSubmissionStatus status,
        ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
        ProductWorkspaceProjectionError projectionError =
            ProductWorkspaceProjectionError.None,
        ProductConfigurationError configurationError =
            ProductConfigurationError.None)
    {
        lock (gate)
        {
            return SubmissionUnsafe(
                status,
                editError,
                projectionError,
                configurationError);
        }
    }

    private ProductWorkspaceSaveSubmissionResult SubmissionUnsafe(
        ProductWorkspaceSaveSubmissionStatus status,
        ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
        ProductWorkspaceProjectionError projectionError =
            ProductWorkspaceProjectionError.None,
        ProductConfigurationError configurationError =
            ProductConfigurationError.None) =>
        new(status, snapshot, editError, projectionError, configurationError);

    private void TrackUnsafe(Task operation)
    {
        operations.Add(operation);
        _ = operation.ContinueWith(
            completedOperation =>
            {
                lock (gate)
                {
                    operations.Remove(completedOperation);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Publish(ProductWorkspaceSaveSnapshot value)
    {
        EventHandler<ProductWorkspaceSaveSnapshot>? handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<ProductWorkspaceSaveSnapshot> handler in
            handlers.GetInvocationList())
        {
            try
            {
                handler(this, value);
            }
            catch
            {
                // Observers cannot change persistence ordering or completion.
            }
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private sealed class SystemProductWorkspaceSaveScheduler
        : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken);

        public async Task YieldAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
