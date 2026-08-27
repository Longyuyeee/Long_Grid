using LongGrid.Core.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarPresetAvailabilityTests
{
    [Fact]
    public void MissingOrFailedProbeFailsClosed()
    {
        TaskbarPresetAvailability missing =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                report: null,
                TaskbarNativeAdapterAvailability.Available,
                recoveryPending: true);
        TaskbarPresetAvailability failed =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                CreateReport(
                    TaskbarRuntimeAdmission.DeniedProbeFailure,
                    TaskbarProbeOutcome.Fail),
                TaskbarNativeAdapterAvailability.Available,
                recoveryPending: true);

        Assert.Equal(
            TaskbarPresetAvailabilityStatus.ProbeUnavailable,
            missing.Status);
        Assert.Equal(TaskbarPresetAvailabilityStatus.ProbeFailed, failed.Status);
        Assert.False(missing.ClearEnabled);
        Assert.False(missing.RestoreSystemDefaultEnabled);
        Assert.False(failed.ClearEnabled);
        Assert.False(failed.RestoreSystemDefaultEnabled);
    }

    [Theory]
    [InlineData(
        TaskbarRuntimeAdmission.DeniedConflictDetected,
        TaskbarPresetAvailabilityStatus.ConflictDetected)]
    [InlineData(
        TaskbarRuntimeAdmission.DeniedNoCertifiedBuild,
        TaskbarPresetAvailabilityStatus.BuildNotCertified)]
    public void CompatibilityDenialKeepsBothActionsDisabled(
        TaskbarRuntimeAdmission admission,
        TaskbarPresetAvailabilityStatus expectedStatus)
    {
        TaskbarPresetAvailability result =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                CreateReport(admission),
                TaskbarNativeAdapterAvailability.Available,
                recoveryPending: true);

        Assert.Equal(expectedStatus, result.Status);
        Assert.False(result.ClearEnabled);
        Assert.False(result.RestoreSystemDefaultEnabled);
    }

    [Fact]
    public void AllowedProbeStillRequiresNativeAdapter()
    {
        TaskbarPresetAvailability result =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                CreateReport(TaskbarRuntimeAdmission.Allowed),
                TaskbarNativeAdapterAvailability.Unavailable,
                recoveryPending: true);

        Assert.Equal(
            TaskbarPresetAvailabilityStatus.AdapterUnavailable,
            result.Status);
        Assert.False(result.ClearEnabled);
        Assert.False(result.RestoreSystemDefaultEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CertifiedAdapterEnablesClearAndOnlyPendingRestore(
        bool recoveryPending,
        bool restoreEnabled)
    {
        TaskbarPresetAvailability result =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                CreateReport(TaskbarRuntimeAdmission.Allowed),
                TaskbarNativeAdapterAvailability.Available,
                recoveryPending);

        Assert.Equal(TaskbarPresetAvailabilityStatus.Ready, result.Status);
        Assert.True(result.ClearEnabled);
        Assert.Equal(restoreEnabled, result.RestoreSystemDefaultEnabled);
        Assert.Equal("None", result.DiagnosticCode);
    }

    [Fact]
    public void MutationEvidenceFailsClosedEvenWhenAdmissionClaimsAllowed()
    {
        TaskbarCompatibilityReport report =
            CreateReport(TaskbarRuntimeAdmission.Allowed) with
            {
                Actual = CreateActual() with { ModifiedSystemState = true },
            };

        TaskbarPresetAvailability result =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                report,
                TaskbarNativeAdapterAvailability.Available,
                recoveryPending: true);

        Assert.Equal(TaskbarPresetAvailabilityStatus.ProbeFailed, result.Status);
        Assert.False(result.ClearEnabled);
        Assert.False(result.RestoreSystemDefaultEnabled);
    }

    private static TaskbarCompatibilityReport CreateReport(
        TaskbarRuntimeAdmission admission,
        TaskbarProbeOutcome outcome = TaskbarProbeOutcome.Pass) => new(
            TaskbarWorkerProtocol.CurrentVersion,
            TaskbarWorkerProtocol.ProbePurpose,
            new TaskbarCompatibilityExpected(
                Windows: true,
                PrimaryTaskbarCount: 1,
                RequiredOwnerProcess: "explorer",
                ConflictingProcesses: Array.Empty<string>(),
                ModifiedSystemState: false),
            CreateActual(),
            Difference: Array.Empty<string>(),
            outcome,
            admission);

    private static TaskbarCompatibilityActual CreateActual() => new(
        OperatingSystemVersion: "10.0.26100.0",
        WindowsBuild: 26100,
        SessionId: 1,
        TaskbarWindows:
        [
            new TaskbarWindowSnapshot(1, "Shell_TrayWnd", 42, "explorer"),
        ],
        ConflictingProcesses: Array.Empty<string>(),
        ModifiedSystemState: false,
        ProbeMilliseconds: 1);
}
