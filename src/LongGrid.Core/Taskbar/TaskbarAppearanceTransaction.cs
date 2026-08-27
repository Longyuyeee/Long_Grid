namespace LongGrid.Core.Taskbar;

public enum TaskbarAppearancePreset
{
    SystemDefault,
    Clear,
}

public enum TaskbarAppearanceTransactionStatus
{
    Idle,
    AdmissionDenied,
    ReadyToStage,
    AwaitingConfirmation,
    Confirmed,
    RollbackRequired,
    RolledBack,
    RollbackFailed,
}

public enum TaskbarAppearanceTransactionAction
{
    None,
    StageRecoveryThenApply,
    WaitForConfirmation,
    RestoreSystemDefault,
    ClearRecoveryJournal,
    PreserveRecoveryJournal,
}

public enum TaskbarAppearanceRollbackReason
{
    None,
    ConfirmationExpired,
    UserRejected,
    ParentExited,
    StartupRecovery,
    ApplyFailed,
    VerificationFailed,
}

public sealed record TaskbarAppearanceTransactionSnapshot(
    TaskbarAppearanceTransactionStatus Status,
    TaskbarAppearanceTransactionAction NextAction,
    string? TransactionId,
    TaskbarAppearancePreset RequestedPreset,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? ConfirmationDeadlineUtc,
    TaskbarAppearanceRollbackReason RollbackReason,
    long Revision)
{
    public static TaskbarAppearanceTransactionSnapshot Idle { get; } = new(
        TaskbarAppearanceTransactionStatus.Idle,
        TaskbarAppearanceTransactionAction.None,
        TransactionId: null,
        TaskbarAppearancePreset.SystemDefault,
        StartedUtc: null,
        ConfirmationDeadlineUtc: null,
        TaskbarAppearanceRollbackReason.None,
        Revision: 0);
}

public static class TaskbarAppearanceTransactionPolicy
{
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(15);

    public static TaskbarAppearanceTransactionSnapshot Begin(
        TaskbarCompatibilityReport compatibility,
        TaskbarAppearancePreset requestedPreset,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        if (requestedPreset == TaskbarAppearancePreset.SystemDefault
            || compatibility.RuntimeAdmission != TaskbarRuntimeAdmission.Allowed
            || compatibility.ProbeOutcome != TaskbarProbeOutcome.Pass
            || compatibility.Actual.ModifiedSystemState)
        {
            return TaskbarAppearanceTransactionSnapshot.Idle with
            {
                Status = TaskbarAppearanceTransactionStatus.AdmissionDenied,
            };
        }

        return new(
            TaskbarAppearanceTransactionStatus.ReadyToStage,
            TaskbarAppearanceTransactionAction.StageRecoveryThenApply,
            Guid.NewGuid().ToString("N"),
            requestedPreset,
            nowUtc,
            nowUtc + ConfirmationWindow,
            TaskbarAppearanceRollbackReason.None,
            Revision: 1);
    }

    public static TaskbarAppearanceTransactionSnapshot Applied(
        TaskbarAppearanceTransactionSnapshot current,
        bool applySucceeded,
        bool verificationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status != TaskbarAppearanceTransactionStatus.ReadyToStage)
        {
            return current;
        }

        if (!applySucceeded || !verificationSucceeded)
        {
            return current with
            {
                Status = TaskbarAppearanceTransactionStatus.RollbackRequired,
                NextAction = TaskbarAppearanceTransactionAction.RestoreSystemDefault,
                RollbackReason = !applySucceeded
                    ? TaskbarAppearanceRollbackReason.ApplyFailed
                    : TaskbarAppearanceRollbackReason.VerificationFailed,
                Revision = checked(current.Revision + 1),
            };
        }

        return current with
        {
            Status = TaskbarAppearanceTransactionStatus.AwaitingConfirmation,
            NextAction = TaskbarAppearanceTransactionAction.WaitForConfirmation,
            Revision = checked(current.Revision + 1),
        };
    }

    public static TaskbarAppearanceTransactionSnapshot Confirm(
        TaskbarAppearanceTransactionSnapshot current,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status != TaskbarAppearanceTransactionStatus
                .AwaitingConfirmation)
        {
            return current;
        }

        if (nowUtc >= current.ConfirmationDeadlineUtc)
        {
            return RequireRollback(
                current,
                TaskbarAppearanceRollbackReason.ConfirmationExpired);
        }

        return current with
        {
            Status = TaskbarAppearanceTransactionStatus.Confirmed,
            NextAction = TaskbarAppearanceTransactionAction.PreserveRecoveryJournal,
            Revision = checked(current.Revision + 1),
        };
    }

    public static TaskbarAppearanceTransactionSnapshot EvaluateRollback(
        TaskbarAppearanceTransactionSnapshot current,
        DateTimeOffset nowUtc,
        bool parentExited = false,
        bool startupRecovery = false,
        bool userRejected = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status is not (
                TaskbarAppearanceTransactionStatus.ReadyToStage
                or TaskbarAppearanceTransactionStatus.AwaitingConfirmation))
        {
            return current;
        }

        TaskbarAppearanceRollbackReason reason = startupRecovery
            ? TaskbarAppearanceRollbackReason.StartupRecovery
            : parentExited
                ? TaskbarAppearanceRollbackReason.ParentExited
                : userRejected
                    ? TaskbarAppearanceRollbackReason.UserRejected
                    : nowUtc >= current.ConfirmationDeadlineUtc
                        ? TaskbarAppearanceRollbackReason.ConfirmationExpired
                        : TaskbarAppearanceRollbackReason.None;
        return reason == TaskbarAppearanceRollbackReason.None
            ? current
            : RequireRollback(current, reason);
    }

    public static TaskbarAppearanceTransactionSnapshot CompleteRollback(
        TaskbarAppearanceTransactionSnapshot current,
        bool restoreSucceeded,
        bool verificationSucceeded)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status != TaskbarAppearanceTransactionStatus.RollbackRequired)
        {
            return current;
        }

        bool restored = restoreSucceeded && verificationSucceeded;
        return current with
        {
            Status = restored
                ? TaskbarAppearanceTransactionStatus.RolledBack
                : TaskbarAppearanceTransactionStatus.RollbackFailed,
            NextAction = restored
                ? TaskbarAppearanceTransactionAction.ClearRecoveryJournal
                : TaskbarAppearanceTransactionAction.PreserveRecoveryJournal,
            Revision = checked(current.Revision + 1),
        };
    }

    private static TaskbarAppearanceTransactionSnapshot RequireRollback(
        TaskbarAppearanceTransactionSnapshot current,
        TaskbarAppearanceRollbackReason reason) => current with
        {
            Status = TaskbarAppearanceTransactionStatus.RollbackRequired,
            NextAction = TaskbarAppearanceTransactionAction.RestoreSystemDefault,
            RollbackReason = reason,
            Revision = checked(current.Revision + 1),
        };
}

public sealed record TaskbarAppearanceRecoveryJournal(
    int SchemaVersion,
    string TransactionId,
    TaskbarAppearanceRecoveryPhase Phase,
    TaskbarAppearancePreset RequestedPreset,
    TaskbarAppearancePreset BaselinePreset,
    int WindowsBuild,
    int ExplorerProcessId,
    IReadOnlyList<string> TaskbarWindowClasses,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ConfirmationDeadlineUtc);

public enum TaskbarAppearanceRecoveryPhase
{
    Staged,
    Applied,
    Confirmed,
}

public static class TaskbarAppearanceRecoveryJournalPolicy
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumJournalBytes = 16 * 1024;
    public const int MaximumTaskbarWindows = 16;

    public static bool IsValid(TaskbarAppearanceRecoveryJournal? journal)
    {
        if (journal is null
            || journal.SchemaVersion != CurrentSchemaVersion
            || !Guid.TryParseExact(journal.TransactionId, "N", out _)
            || !Enum.IsDefined(journal.Phase)
            || journal.RequestedPreset == TaskbarAppearancePreset.SystemDefault
            || journal.BaselinePreset != TaskbarAppearancePreset.SystemDefault
            || journal.WindowsBuild <= 0
            || journal.ExplorerProcessId <= 0
            || journal.TaskbarWindowClasses is null
            || journal.TaskbarWindowClasses.Count is < 1 or > MaximumTaskbarWindows
            || journal.CreatedUtc == default
            || journal.ConfirmationDeadlineUtc
                != journal.CreatedUtc
                    + TaskbarAppearanceTransactionPolicy.ConfirmationWindow)
        {
            return false;
        }

        return journal.TaskbarWindowClasses.All(windowClass =>
            string.Equals(windowClass, "Shell_TrayWnd", StringComparison.Ordinal)
            || string.Equals(
                windowClass,
                "Shell_SecondaryTrayWnd",
                StringComparison.Ordinal));
    }
}
