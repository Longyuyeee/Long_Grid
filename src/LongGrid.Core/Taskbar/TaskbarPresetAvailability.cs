namespace LongGrid.Core.Taskbar;

public enum TaskbarPresetAvailabilityStatus
{
    ProbeUnavailable,
    ProbeFailed,
    ConflictDetected,
    BuildNotCertified,
    AdapterUnavailable,
    Ready,
}

public sealed record TaskbarPresetAvailability(
    TaskbarPresetAvailabilityStatus Status,
    bool ClearEnabled,
    bool RestoreSystemDefaultEnabled,
    string DiagnosticCode);

public static class TaskbarPresetAvailabilityPolicy
{
    public static TaskbarPresetAvailability Evaluate(
        TaskbarCompatibilityReport? report,
        TaskbarNativeAdapterAvailability adapterAvailability,
        bool recoveryPending)
    {
        if (report is null)
        {
            return Denied(
                TaskbarPresetAvailabilityStatus.ProbeUnavailable,
                "CompatibilityProbeUnavailable");
        }

        if (report.ProbeOutcome != TaskbarProbeOutcome.Pass
            || report.RuntimeAdmission == TaskbarRuntimeAdmission.DeniedProbeFailure
            || report.Actual.ModifiedSystemState)
        {
            return Denied(
                TaskbarPresetAvailabilityStatus.ProbeFailed,
                "CompatibilityProbeFailed");
        }

        if (report.RuntimeAdmission
            == TaskbarRuntimeAdmission.DeniedConflictDetected)
        {
            return Denied(
                TaskbarPresetAvailabilityStatus.ConflictDetected,
                "TaskbarConflictDetected");
        }

        if (report.RuntimeAdmission
            == TaskbarRuntimeAdmission.DeniedNoCertifiedBuild)
        {
            return Denied(
                TaskbarPresetAvailabilityStatus.BuildNotCertified,
                "WindowsBuildNotCertified");
        }

        if (adapterAvailability != TaskbarNativeAdapterAvailability.Available)
        {
            return Denied(
                TaskbarPresetAvailabilityStatus.AdapterUnavailable,
                "NativeAdapterUnavailable");
        }

        return new(
            TaskbarPresetAvailabilityStatus.Ready,
            ClearEnabled: true,
            RestoreSystemDefaultEnabled: recoveryPending,
            DiagnosticCode: "None");
    }

    private static TaskbarPresetAvailability Denied(
        TaskbarPresetAvailabilityStatus status,
        string diagnosticCode) => new(
            status,
            ClearEnabled: false,
            RestoreSystemDefaultEnabled: false,
            diagnosticCode);
}
