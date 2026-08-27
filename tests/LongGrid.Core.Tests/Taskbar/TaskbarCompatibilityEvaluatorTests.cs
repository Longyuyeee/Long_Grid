using LongGrid.Core.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarCompatibilityPolicyTests
{
    [Fact]
    public void EvaluateDeniesUncertifiedBuildAfterSuccessfulProbe()
    {
        TaskbarCompatibilityReport report = TaskbarCompatibilityPolicy.Evaluate(
            CreateActual(),
            isWindows: true);

        Assert.Equal(TaskbarProbeOutcome.Pass, report.ProbeOutcome);
        Assert.Empty(report.Difference);
        Assert.Equal(
            TaskbarRuntimeAdmission.DeniedNoCertifiedBuild,
            report.RuntimeAdmission);
    }

    [Fact]
    public void EvaluateDeniesKnownConflictWithoutFailingReadOnlyProbe()
    {
        TaskbarCompatibilityActual actual = CreateActual() with
        {
            ConflictingProcesses = new[] { "TranslucentTB" },
        };

        TaskbarCompatibilityReport report = TaskbarCompatibilityPolicy.Evaluate(
            actual,
            isWindows: true);

        Assert.Equal(TaskbarProbeOutcome.Pass, report.ProbeOutcome);
        Assert.Equal(
            TaskbarRuntimeAdmission.DeniedConflictDetected,
            report.RuntimeAdmission);
    }

    [Fact]
    public void EvaluateFailsClosedWhenPrimaryTaskbarIsMissing()
    {
        TaskbarCompatibilityActual actual = CreateActual() with
        {
            TaskbarWindows = Array.Empty<TaskbarWindowSnapshot>(),
        };

        TaskbarCompatibilityReport report = TaskbarCompatibilityPolicy.Evaluate(
            actual,
            isWindows: true);

        Assert.Equal(TaskbarProbeOutcome.Fail, report.ProbeOutcome);
        Assert.Contains("PrimaryTaskbarCountMismatch", report.Difference);
        Assert.Contains("NoTaskbarWindowFound", report.Difference);
        Assert.Equal(
            TaskbarRuntimeAdmission.DeniedProbeFailure,
            report.RuntimeAdmission);
    }

    [Fact]
    public void EvaluateFailsClosedWhenTaskbarOwnerIsNotExplorer()
    {
        TaskbarCompatibilityActual actual = CreateActual() with
        {
            TaskbarWindows = new[]
            {
                new TaskbarWindowSnapshot(1, "Shell_TrayWnd", 42, "replacement"),
            },
        };

        TaskbarCompatibilityReport report = TaskbarCompatibilityPolicy.Evaluate(
            actual,
            isWindows: true);

        Assert.Equal(TaskbarProbeOutcome.Fail, report.ProbeOutcome);
        Assert.Contains("TaskbarOwnerIsNotExplorer", report.Difference);
    }

    [Fact]
    public void EvaluateFailsWhenProbeReportsMutation()
    {
        TaskbarCompatibilityReport report = TaskbarCompatibilityPolicy.Evaluate(
            CreateActual() with { ModifiedSystemState = true },
            isWindows: true);

        Assert.Equal(TaskbarProbeOutcome.Fail, report.ProbeOutcome);
        Assert.Contains("ProbeModifiedSystemState", report.Difference);
    }

    private static TaskbarCompatibilityActual CreateActual()
    {
        return new TaskbarCompatibilityActual(
            OperatingSystemVersion: "10.0.26100.0",
            WindowsBuild: 26100,
            SessionId: 1,
            TaskbarWindows: new[]
            {
                new TaskbarWindowSnapshot(1, "Shell_TrayWnd", 42, "explorer"),
            },
            ConflictingProcesses: Array.Empty<string>(),
            ModifiedSystemState: false,
            ProbeMilliseconds: 1);
    }
}
