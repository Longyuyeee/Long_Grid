namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceSaveStatus
{
    Clean,
    WaitingForDebounce,
    Saving,
    Saved,
    Failed,
}

public enum ProductWorkspaceSaveCommandKind
{
    None,
    ScheduleDebounce,
    Save,
    Retry,
}

public enum ProductWorkspaceSaveFailure
{
    None,
    InvalidConfiguration,
    DamagedEvidence,
    WriteLeaseUnavailable,
    IoFailure,
    RetryUnavailable,
}

public sealed record ProductWorkspaceSaveSnapshot(
    ProductWorkspaceSaveStatus Status,
    long CurrentRevision,
    long SavedRevision,
    long? ActiveSaveRevision,
    ProductWorkspaceSaveFailure Failure,
    bool CanRetry)
{
    public static ProductWorkspaceSaveSnapshot Initial { get; } =
        new(
            ProductWorkspaceSaveStatus.Clean,
            CurrentRevision: 0,
            SavedRevision: 0,
            ActiveSaveRevision: null,
            ProductWorkspaceSaveFailure.None,
            CanRetry: false);
}

public sealed record ProductWorkspaceSaveCommand(
    ProductWorkspaceSaveCommandKind Kind,
    long Revision)
{
    public static ProductWorkspaceSaveCommand None { get; } =
        new(ProductWorkspaceSaveCommandKind.None, Revision: 0);
}

public sealed record ProductWorkspaceSaveTransition(
    ProductWorkspaceSaveSnapshot Snapshot,
    ProductWorkspaceSaveCommand Command);

public static class ProductWorkspaceSaveStateMachine
{
    public static ProductWorkspaceSaveTransition AcceptEdit(
        ProductWorkspaceSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        long revision = checked(snapshot.CurrentRevision + 1);
        return new(
            snapshot with
            {
                Status = ProductWorkspaceSaveStatus.WaitingForDebounce,
                CurrentRevision = revision,
                Failure = ProductWorkspaceSaveFailure.None,
                CanRetry = false,
            },
            new(ProductWorkspaceSaveCommandKind.ScheduleDebounce, revision));
    }

    public static ProductWorkspaceSaveTransition DebounceElapsed(
        ProductWorkspaceSaveSnapshot snapshot,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status != ProductWorkspaceSaveStatus.WaitingForDebounce
            || revision != snapshot.CurrentRevision)
        {
            return NoCommand(snapshot);
        }

        return new(
            snapshot with
            {
                Status = ProductWorkspaceSaveStatus.Saving,
                ActiveSaveRevision = revision,
            },
            new(ProductWorkspaceSaveCommandKind.Save, revision));
    }

    public static ProductWorkspaceSaveTransition SaveCompleted(
        ProductWorkspaceSaveSnapshot snapshot,
        long revision,
        ProductWorkspaceSaveFailure failure = ProductWorkspaceSaveFailure.None)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        if (snapshot.ActiveSaveRevision != revision)
        {
            return NoCommand(snapshot);
        }

        if (revision != snapshot.CurrentRevision)
        {
            return NoCommand(snapshot with { ActiveSaveRevision = null });
        }

        if (failure == ProductWorkspaceSaveFailure.None)
        {
            return NoCommand(
                snapshot with
                {
                    Status = ProductWorkspaceSaveStatus.Saved,
                    SavedRevision = revision,
                    ActiveSaveRevision = null,
                    Failure = ProductWorkspaceSaveFailure.None,
                    CanRetry = false,
                });
        }

        return NoCommand(
            snapshot with
            {
                Status = ProductWorkspaceSaveStatus.Failed,
                ActiveSaveRevision = null,
                Failure = failure,
                CanRetry = IsRetryable(failure),
            });
    }

    public static ProductWorkspaceSaveTransition RetryRequested(
        ProductWorkspaceSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status != ProductWorkspaceSaveStatus.Failed
            || !snapshot.CanRetry)
        {
            return NoCommand(snapshot);
        }

        return new(
            snapshot with
            {
                Status = ProductWorkspaceSaveStatus.Saving,
                ActiveSaveRevision = snapshot.CurrentRevision,
                Failure = ProductWorkspaceSaveFailure.None,
                CanRetry = false,
            },
            new(
                ProductWorkspaceSaveCommandKind.Retry,
                snapshot.CurrentRevision));
    }

    private static ProductWorkspaceSaveTransition NoCommand(
        ProductWorkspaceSaveSnapshot snapshot) =>
        new(snapshot, ProductWorkspaceSaveCommand.None);

    private static bool IsRetryable(ProductWorkspaceSaveFailure failure) =>
        failure is ProductWorkspaceSaveFailure.DamagedEvidence
            or ProductWorkspaceSaveFailure.WriteLeaseUnavailable
            or ProductWorkspaceSaveFailure.IoFailure;
}
