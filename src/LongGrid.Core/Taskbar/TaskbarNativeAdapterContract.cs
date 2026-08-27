namespace LongGrid.Core.Taskbar;

public enum TaskbarNativeAdapterAvailability
{
    Unavailable,
    Available,
}

public enum TaskbarNativeRestoreStatus
{
    Restored,
    Failed,
    VerificationFailed,
}

public enum TaskbarNativeRestoreAdmissionStatus
{
    RecoveryJournalInvalid,
    TargetChanged,
    CompatibilityDenied,
    AdapterUnavailable,
    Ready,
}

public sealed record TaskbarNativeRestoreTarget(
    int WindowsBuild,
    int SessionId,
    int JournalExplorerProcessId,
    int CurrentExplorerProcessId,
    bool ExplorerRestartedSinceJournal,
    IReadOnlyList<TaskbarWindowSnapshot> TaskbarWindows);

public sealed record TaskbarNativeRestoreRequest(
    string TransactionId,
    TaskbarAppearancePreset BaselinePreset,
    TaskbarNativeRestoreTarget Target);

public sealed record TaskbarNativeRestoreResult(
    TaskbarNativeRestoreStatus Status,
    string DiagnosticCode,
    bool ModifiedSystemState,
    bool SystemDefaultVerified);

public interface ITaskbarAppearanceNativeAdapter
{
    string AdapterId { get; }

    TaskbarNativeAdapterAvailability Availability { get; }

    TaskbarNativeRestoreResult RestoreSystemDefault(
        TaskbarNativeRestoreRequest request);
}

public sealed record TaskbarNativeRestoreAdmission(
    TaskbarNativeRestoreAdmissionStatus Status,
    string DiagnosticCode,
    TaskbarNativeRestoreRequest? Request)
{
    public bool IsReady =>
        Status == TaskbarNativeRestoreAdmissionStatus.Ready
        && Request is not null;
}

public static class TaskbarNativeRestoreAdmissionPolicy
{
    public static TaskbarNativeRestoreAdmission Evaluate(
        TaskbarAppearanceRecoveryJournal? journal,
        TaskbarCompatibilityReport report,
        ITaskbarAppearanceNativeAdapter? adapter)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!TaskbarAppearanceRecoveryJournalPolicy.IsValid(journal))
        {
            return Denied(
                TaskbarNativeRestoreAdmissionStatus.RecoveryJournalInvalid,
                "RecoveryJournalInvalid");
        }

        TaskbarAppearanceRecoveryJournal validJournal = journal!;
        if (validJournal.WindowsBuild != report.Actual.WindowsBuild)
        {
            return Denied(
                TaskbarNativeRestoreAdmissionStatus.TargetChanged,
                "WindowsBuildChanged");
        }

        TaskbarWindowSnapshot[] windows = report.Actual.TaskbarWindows.ToArray();
        int[] explorerProcessIds = windows
            .Select(window => window.ProcessId)
            .Distinct()
            .ToArray();
        if (windows.Length == 0
            || explorerProcessIds.Length != 1
            || !WindowClassesMatch(
                validJournal.TaskbarWindowClasses,
                windows.Select(window => window.WindowClass)))
        {
            return Denied(
                TaskbarNativeRestoreAdmissionStatus.TargetChanged,
                "TaskbarTargetChanged");
        }

        if (report.RuntimeAdmission != TaskbarRuntimeAdmission.Allowed
            || report.ProbeOutcome != TaskbarProbeOutcome.Pass
            || report.Actual.ModifiedSystemState)
        {
            return Denied(
                TaskbarNativeRestoreAdmissionStatus.CompatibilityDenied,
                report.RuntimeAdmission.ToString());
        }

        if (adapter is null
            || adapter.Availability
                != TaskbarNativeAdapterAvailability.Available
            || !IsSafeAdapterId(adapter.AdapterId))
        {
            return Denied(
                TaskbarNativeRestoreAdmissionStatus.AdapterUnavailable,
                "NativeRestoreAdapterUnavailable");
        }

        int currentExplorerProcessId = explorerProcessIds[0];
        TaskbarNativeRestoreTarget target = new(
            report.Actual.WindowsBuild,
            report.Actual.SessionId,
            validJournal.ExplorerProcessId,
            currentExplorerProcessId,
            validJournal.ExplorerProcessId != currentExplorerProcessId,
            windows);
        return new(
            TaskbarNativeRestoreAdmissionStatus.Ready,
            "None",
            new TaskbarNativeRestoreRequest(
                validJournal.TransactionId,
                validJournal.BaselinePreset,
                target));
    }

    private static bool WindowClassesMatch(
        IEnumerable<string> expected,
        IEnumerable<string> actual) => expected
        .OrderBy(value => value, StringComparer.Ordinal)
        .SequenceEqual(
            actual.OrderBy(value => value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static bool IsSafeAdapterId(string? adapterId) =>
        !string.IsNullOrWhiteSpace(adapterId)
        && adapterId.Length <= 64
        && adapterId.All(character => !char.IsControl(character));

    private static TaskbarNativeRestoreAdmission Denied(
        TaskbarNativeRestoreAdmissionStatus status,
        string diagnosticCode) => new(status, diagnosticCode, null);
}
