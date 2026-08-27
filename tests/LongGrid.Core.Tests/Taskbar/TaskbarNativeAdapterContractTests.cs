using LongGrid.Core.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarNativeAdapterContractTests
{
    [Fact]
    public void ReadyAdmissionCarriesAttestedCurrentTarget()
    {
        FakeAdapter adapter = new();
        TaskbarAppearanceRecoveryJournal journal = Journal();
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed,
            explorerProcessId: journal.ExplorerProcessId);

        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                journal,
                report,
                adapter);

        Assert.True(admission.IsReady);
        Assert.Equal("None", admission.DiagnosticCode);
        Assert.Equal(journal.TransactionId, admission.Request!.TransactionId);
        Assert.Equal(
            TaskbarAppearancePreset.SystemDefault,
            admission.Request.BaselinePreset);
        Assert.Equal(report.Actual.WindowsBuild, admission.Request.Target.WindowsBuild);
        Assert.Equal(report.Actual.SessionId, admission.Request.Target.SessionId);
        Assert.False(admission.Request.Target.ExplorerRestartedSinceJournal);
        Assert.Equal(2, admission.Request.Target.TaskbarWindows.Count);
    }

    [Fact]
    public void ExplorerRestartIsAttestedWithoutReusingJournalProcessId()
    {
        TaskbarAppearanceRecoveryJournal journal = Journal();
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed,
            explorerProcessId: journal.ExplorerProcessId + 1);

        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                journal,
                report,
                new FakeAdapter());

        Assert.True(admission.IsReady);
        Assert.True(admission.Request!.Target.ExplorerRestartedSinceJournal);
        Assert.Equal(
            journal.ExplorerProcessId + 1,
            admission.Request.Target.CurrentExplorerProcessId);
    }

    [Theory]
    [InlineData(TaskbarRuntimeAdmission.DeniedProbeFailure)]
    [InlineData(TaskbarRuntimeAdmission.DeniedConflictDetected)]
    [InlineData(TaskbarRuntimeAdmission.DeniedNoCertifiedBuild)]
    public void CompatibilityDenialNeverProducesNativeRequest(
        TaskbarRuntimeAdmission runtimeAdmission)
    {
        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                Journal(),
                Report(runtimeAdmission),
                new FakeAdapter());

        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.CompatibilityDenied,
            admission.Status);
        Assert.Equal(runtimeAdmission.ToString(), admission.DiagnosticCode);
        Assert.Null(admission.Request);
    }

    [Fact]
    public void ModifiedProbeIsDeniedEvenIfAdmissionClaimsAllowed()
    {
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed) with
        {
            Actual = Report(TaskbarRuntimeAdmission.Allowed).Actual with
            {
                ModifiedSystemState = true,
            },
        };

        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                Journal(),
                report,
                new FakeAdapter());

        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.CompatibilityDenied,
            admission.Status);
        Assert.Null(admission.Request);
    }

    [Fact]
    public void BuildAndTaskbarTargetChangesFailClosedBeforeAdapterSelection()
    {
        TaskbarAppearanceRecoveryJournal journal = Journal();
        TaskbarNativeRestoreAdmission buildChanged =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                journal with { WindowsBuild = journal.WindowsBuild + 1 },
                Report(TaskbarRuntimeAdmission.Allowed),
                new FakeAdapter());
        TaskbarNativeRestoreAdmission classesChanged =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                journal with
                {
                    TaskbarWindowClasses = ["Shell_TrayWnd"],
                },
                Report(TaskbarRuntimeAdmission.Allowed),
                new FakeAdapter());

        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.TargetChanged,
            buildChanged.Status);
        Assert.Equal("WindowsBuildChanged", buildChanged.DiagnosticCode);
        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.TargetChanged,
            classesChanged.Status);
        Assert.Equal("TaskbarTargetChanged", classesChanged.DiagnosticCode);
    }

    [Fact]
    public void SplitExplorerOwnershipFailsClosed()
    {
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed);
        report = report with
        {
            Actual = report.Actual with
            {
                TaskbarWindows =
                [
                    report.Actual.TaskbarWindows[0],
                    report.Actual.TaskbarWindows[1] with { ProcessId = 4040 },
                ],
            },
        };

        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                Journal(),
                report,
                new FakeAdapter());

        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.TargetChanged,
            admission.Status);
        Assert.Null(admission.Request);
    }

    [Fact]
    public void MissingUnavailableAndUnsafeAdaptersAllRemainUnavailable()
    {
        TaskbarAppearanceRecoveryJournal journal = Journal();
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed);
        ITaskbarAppearanceNativeAdapter?[] adapters =
        [
            null,
            new FakeAdapter
            {
                Availability = TaskbarNativeAdapterAvailability.Unavailable,
            },
            new FakeAdapter { AdapterId = "" },
            new FakeAdapter { AdapterId = new string('x', 65) },
            new FakeAdapter { AdapterId = "bad\rvalue" },
        ];

        foreach (ITaskbarAppearanceNativeAdapter? adapter in adapters)
        {
            TaskbarNativeRestoreAdmission admission =
                TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                    journal,
                    report,
                    adapter);
            Assert.Equal(
                TaskbarNativeRestoreAdmissionStatus.AdapterUnavailable,
                admission.Status);
            Assert.Equal(
                "NativeRestoreAdapterUnavailable",
                admission.DiagnosticCode);
            Assert.Null(admission.Request);
        }
    }

    [Fact]
    public void InvalidJournalAndNullReportAreRejected()
    {
        TaskbarCompatibilityReport report = Report(
            TaskbarRuntimeAdmission.Allowed);
        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                null,
                report,
                new FakeAdapter());

        Assert.Equal(
            TaskbarNativeRestoreAdmissionStatus.RecoveryJournalInvalid,
            admission.Status);
        Assert.False(admission.IsReady);
        Assert.Throws<ArgumentNullException>(() =>
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                Journal(),
                null!,
                new FakeAdapter()));
    }

    [Fact]
    public void NativeResultContractSeparatesMutationFromVerification()
    {
        FakeAdapter adapter = new();
        TaskbarNativeRestoreAdmission admission =
            TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                Journal(),
                Report(TaskbarRuntimeAdmission.Allowed),
                adapter);

        TaskbarNativeRestoreResult result = adapter.RestoreSystemDefault(
            admission.Request!);

        Assert.Equal(TaskbarNativeRestoreStatus.Restored, result.Status);
        Assert.True(result.ModifiedSystemState);
        Assert.True(result.SystemDefaultVerified);
    }

    private static TaskbarAppearanceRecoveryJournal Journal()
    {
        DateTimeOffset created = new(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        return new(
            TaskbarAppearanceRecoveryJournalPolicy.CurrentSchemaVersion,
            Guid.Parse("93247546637f44dabf22af1c1a238490")
                .ToString("N"),
            TaskbarAppearanceRecoveryPhase.Applied,
            TaskbarAppearancePreset.Clear,
            TaskbarAppearancePreset.SystemDefault,
            26100,
            3030,
            ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"],
            created,
            created + TaskbarAppearanceTransactionPolicy.ConfirmationWindow);
    }

    private static TaskbarCompatibilityReport Report(
        TaskbarRuntimeAdmission runtimeAdmission,
        int explorerProcessId = 3030)
    {
        TaskbarCompatibilityActual actual = new(
            "10.0.26100.0",
            26100,
            1,
            [
                new(100, "Shell_TrayWnd", explorerProcessId, "explorer"),
                new(
                    101,
                    "Shell_SecondaryTrayWnd",
                    explorerProcessId,
                    "explorer"),
            ],
            Array.Empty<string>(),
            ModifiedSystemState: false,
            ProbeMilliseconds: 1);
        return new(
            TaskbarWorkerProtocol.CurrentVersion,
            TaskbarWorkerProtocol.ProbePurpose,
            new(
                Windows: true,
                PrimaryTaskbarCount: 1,
                RequiredOwnerProcess: "explorer",
                ConflictingProcesses: Array.Empty<string>(),
                ModifiedSystemState: false),
            actual,
            Array.Empty<string>(),
            TaskbarProbeOutcome.Pass,
            runtimeAdmission);
    }

    private sealed class FakeAdapter : ITaskbarAppearanceNativeAdapter
    {
        public string AdapterId { get; init; } = "FakeCertifiedAdapter";

        public TaskbarNativeAdapterAvailability Availability { get; init; } =
            TaskbarNativeAdapterAvailability.Available;

        public TaskbarNativeRestoreResult RestoreSystemDefault(
            TaskbarNativeRestoreRequest request)
        {
            Assert.NotNull(request);
            return new(
                TaskbarNativeRestoreStatus.Restored,
                "None",
                ModifiedSystemState: true,
                SystemDefaultVerified: true);
        }
    }
}
