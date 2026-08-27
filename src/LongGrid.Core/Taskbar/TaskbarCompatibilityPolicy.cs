namespace LongGrid.Core.Taskbar;

public enum TaskbarProbeOutcome
{
    Pass,
    Fail,
}

public enum TaskbarRuntimeAdmission
{
    DeniedProbeFailure,
    DeniedConflictDetected,
    DeniedNoCertifiedBuild,
    Allowed,
}

public sealed record TaskbarWindowSnapshot(
    long Handle,
    string WindowClass,
    int ProcessId,
    string ProcessName);

public sealed record TaskbarCompatibilityActual(
    string OperatingSystemVersion,
    int WindowsBuild,
    int SessionId,
    IReadOnlyList<TaskbarWindowSnapshot> TaskbarWindows,
    IReadOnlyList<string> ConflictingProcesses,
    bool ModifiedSystemState,
    double ProbeMilliseconds);

public sealed record TaskbarCompatibilityExpected(
    bool Windows,
    int PrimaryTaskbarCount,
    string RequiredOwnerProcess,
    IReadOnlyList<string> ConflictingProcesses,
    bool ModifiedSystemState);

public sealed record TaskbarCompatibilityReport(
    int SchemaVersion,
    string Purpose,
    TaskbarCompatibilityExpected Expected,
    TaskbarCompatibilityActual Actual,
    IReadOnlyList<string> Difference,
    TaskbarProbeOutcome ProbeOutcome,
    TaskbarRuntimeAdmission RuntimeAdmission);

public static class TaskbarCompatibilityPolicy
{
    // R1A intentionally has no certified build. A build may only be added after the
    // R4 physical matrix proves apply, rollback, Explorer restart, and uninstall.
    private static readonly HashSet<int> CertifiedBuilds = [];

    public static TaskbarCompatibilityReport Evaluate(
        TaskbarCompatibilityActual actual,
        bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(actual);

        List<string> difference = [];
        TaskbarWindowSnapshot[] primary = actual.TaskbarWindows
            .Where(window => string.Equals(
                window.WindowClass,
                "Shell_TrayWnd",
                StringComparison.Ordinal))
            .ToArray();

        if (!isWindows)
        {
            difference.Add("NotWindows");
        }

        if (primary.Length != 1)
        {
            difference.Add("PrimaryTaskbarCountMismatch");
        }

        if (actual.TaskbarWindows.Count == 0)
        {
            difference.Add("NoTaskbarWindowFound");
        }
        else if (actual.TaskbarWindows.Any(window => !string.Equals(
                     window.ProcessName,
                     "explorer",
                     StringComparison.OrdinalIgnoreCase)))
        {
            difference.Add("TaskbarOwnerIsNotExplorer");
        }

        if (actual.ModifiedSystemState)
        {
            difference.Add("ProbeModifiedSystemState");
        }

        TaskbarProbeOutcome outcome = difference.Count == 0
            ? TaskbarProbeOutcome.Pass
            : TaskbarProbeOutcome.Fail;
        TaskbarRuntimeAdmission admission = outcome == TaskbarProbeOutcome.Fail
            ? TaskbarRuntimeAdmission.DeniedProbeFailure
            : actual.ConflictingProcesses.Count != 0
                ? TaskbarRuntimeAdmission.DeniedConflictDetected
                : !CertifiedBuilds.Contains(actual.WindowsBuild)
                    ? TaskbarRuntimeAdmission.DeniedNoCertifiedBuild
                    : TaskbarRuntimeAdmission.Allowed;

        return new TaskbarCompatibilityReport(
            SchemaVersion: TaskbarWorkerProtocol.CurrentVersion,
            Purpose: TaskbarWorkerProtocol.ProbePurpose,
            Expected: new TaskbarCompatibilityExpected(
                Windows: true,
                PrimaryTaskbarCount: 1,
                RequiredOwnerProcess: "explorer",
                ConflictingProcesses: Array.Empty<string>(),
                ModifiedSystemState: false),
            Actual: actual,
            Difference: difference,
            ProbeOutcome: outcome,
            RuntimeAdmission: admission);
    }
}
