using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarAppearanceRecoveryClientRealProcessTests
{
    [Fact]
    public async Task FormalStartupOverloadUsesBoundedDefaultRecoveryPath()
    {
        string path = TaskbarAppearanceRecoveryPath.ResolveDefaultDirectory();
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.EndsWith(
            Path.Combine("LongGrid", "TaskbarRecovery"),
            path,
            StringComparison.OrdinalIgnoreCase);

        TaskbarStartupRecoveryClientResult result =
            await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                TimeSpan.FromSeconds(3));

        Assert.True(result.IsCompleted);
        Assert.NotNull(result.Response);
        Assert.False(result.Response.ModifiedSystemState);
    }

    [Fact]
    public async Task RealWorkerReportsNoRecoveryWithoutCreatingJournal()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.Completed,
                result.Status);
            Assert.NotNull(result.Response);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.NoRecoveryRequired,
                result.Response.Status);
            Assert.False(result.Response.JournalPreserved);
            Assert.False(result.Response.ModifiedSystemState);
            Assert.Null(result.Response.Report);
            AssertJournalArtifacts(directory, expectedJournal: false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerDefersUncertifiedRecoveryAndPreservesExactJournal()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarCompatibilityClientResult before =
                await TaskbarCompatibilityClient.ProbeAsync(
                    TimeSpan.FromSeconds(3));
            Assert.True(before.IsCompleted);
            Assert.NotNull(before.Report);
            Assert.NotEqual(
                TaskbarRuntimeAdmission.Allowed,
                before.Report.RuntimeAdmission);

            TaskbarAppearanceRecoveryJournal journal = CreateJournal(before.Report);
            using (TaskbarAppearanceRecoveryLease lease = AcquireLease(directory))
            {
                TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
                Assert.True(await store.StageAsync(journal));
            }

            string journalPath = JournalPath(directory);
            byte[] expectedBytes = await File.ReadAllBytesAsync(journalPath);
            TaskbarStartupRecoveryClientResult recovery =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);
            TaskbarCompatibilityClientResult after =
                await TaskbarCompatibilityClient.ProbeAsync(
                    TimeSpan.FromSeconds(3));

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.Completed,
                recovery.Status);
            Assert.NotNull(recovery.Response);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.RecoveryDeferredCompatibility,
                recovery.Response.Status);
            Assert.Equal(
                recovery.Response.Report!.RuntimeAdmission.ToString(),
                recovery.Response.DiagnosticCode);
            Assert.Equal(journal.Phase, recovery.Response.RecoveryPhase);
            Assert.True(recovery.Response.JournalPreserved);
            Assert.False(recovery.Response.ModifiedSystemState);
            Assert.False(recovery.Response.Report.Actual.ModifiedSystemState);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(journalPath));
            AssertJournalArtifacts(directory, expectedJournal: true);

            Assert.True(after.IsCompleted);
            Assert.NotNull(after.Report);
            Assert.Equal(
                CaptureWindowIdentity(before.Report),
                CaptureWindowIdentity(after.Report));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerFailsClosedWhenRecoveryLeaseIsContended()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarCompatibilityClientResult probe =
                await TaskbarCompatibilityClient.ProbeAsync(
                    TimeSpan.FromSeconds(3));
            Assert.True(probe.IsCompleted);
            using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
            TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
            Assert.True(await store.StageAsync(CreateJournal(probe.Report!)));
            byte[] expectedBytes = await File.ReadAllBytesAsync(
                JournalPath(directory));

            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.Completed,
                result.Status);
            Assert.NotNull(result.Response);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.LeaseContended,
                result.Response.Status);
            Assert.True(result.Response.JournalPreserved);
            Assert.False(result.Response.ModifiedSystemState);
            Assert.Equal(
                expectedBytes,
                await File.ReadAllBytesAsync(JournalPath(directory)));
            AssertJournalArtifacts(directory, expectedJournal: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerPreservesMalformedRecoveryJournal()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            byte[] malformed = "{malformed"u8.ToArray();
            await File.WriteAllBytesAsync(JournalPath(directory), malformed);

            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.Completed,
                result.Status);
            Assert.NotNull(result.Response);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.RecoveryJournalInvalid,
                result.Response.Status);
            Assert.Equal("MalformedJson", result.Response.DiagnosticCode);
            Assert.True(result.Response.JournalPreserved);
            Assert.False(result.Response.ModifiedSystemState);
            Assert.Equal(
                malformed,
                await File.ReadAllBytesAsync(JournalPath(directory)));
            AssertJournalArtifacts(directory, expectedJournal: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "Hang",
        TaskbarStartupRecoveryClientStatus.TimedOut,
        "WorkerTimeout")]
    [InlineData(
        "Exit",
        TaskbarStartupRecoveryClientStatus.WorkerExited,
        "WorkerExit71")]
    [InlineData(
        "Malformed",
        TaskbarStartupRecoveryClientStatus.ProtocolError,
        "MalformedResponse")]
    [InlineData(
        "WrongVersion",
        TaskbarStartupRecoveryClientStatus.ProtocolError,
        "InvalidRecoveryResponse")]
    [InlineData(
        "Oversized",
        TaskbarStartupRecoveryClientStatus.ProtocolError,
        "ResponseTooLarge")]
    public async Task RealWorkerFailsClosedForRecoveryProtocolFaults(
        string faultName,
        TaskbarStartupRecoveryClientStatus expectedStatus,
        string expectedDiagnostic)
    {
        TaskbarWorkerEvidenceFault fault = Enum.Parse<TaskbarWorkerEvidenceFault>(
            faultName);
        TimeSpan timeout = fault == TaskbarWorkerEvidenceFault.Hang
            ? TimeSpan.FromMilliseconds(350)
            : TimeSpan.FromSeconds(3);

        TaskbarStartupRecoveryClientResult result =
            await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                timeout,
                directoryPath: null,
                fault,
                Environment.ProcessId);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(expectedDiagnostic, result.DiagnosticCode);
    }

    [Fact]
    public async Task RealWorkerAcceptsCombinedEvidenceDirectoryAndFault()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.Exit,
                    Environment.ProcessId);

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.WorkerExited,
                result.Status);
            Assert.Equal("WorkerExit71", result.DiagnosticCode);
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerDefersRecoveryWhenWindowsBuildChanged()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarCompatibilityClientResult probe =
                await TaskbarCompatibilityClient.ProbeAsync(
                    TimeSpan.FromSeconds(3));
            Assert.True(probe.IsCompleted);
            TaskbarAppearanceRecoveryJournal changedBuild =
                CreateJournal(probe.Report!) with
                {
                    WindowsBuild = checked(probe.Report!.Actual.WindowsBuild + 1),
                };
            using (TaskbarAppearanceRecoveryLease lease = AcquireLease(directory))
            {
                TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
                Assert.True(await store.StageAsync(changedBuild));
            }

            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    directory,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);

            Assert.True(result.IsCompleted);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.RecoveryDeferredTargetChanged,
                result.Response!.Status);
            Assert.Equal("WindowsBuildChanged", result.Response.DiagnosticCode);
            Assert.True(result.Response.JournalPreserved);
            Assert.True(File.Exists(JournalPath(directory)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerFailsClosedWhenRecoveryPathIsRegularFile()
    {
        string parent = CreateTemporaryDirectory();
        string filePath = Path.Combine(parent, "not-a-directory.bin");
        try
        {
            await File.WriteAllTextAsync(filePath, "preserve-me");

            TaskbarStartupRecoveryClientResult result =
                await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                    TimeSpan.FromSeconds(3),
                    filePath,
                    TaskbarWorkerEvidenceFault.None,
                    Environment.ProcessId);

            Assert.True(result.IsCompleted);
            Assert.Equal(
                TaskbarStartupRecoveryStatus.RecoveryJournalIoFailure,
                result.Response!.Status);
            Assert.Equal(
                "RecoveryLeaseIoFailure",
                result.Response.DiagnosticCode);
            Assert.True(result.Response.JournalPreserved);
            Assert.Equal("preserve-me", await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task RealRecoveryWorkerExitsWhenBoundParentExits()
    {
        string directory = CreateTemporaryDirectory();
        await using TaskbarReadyParentProcess parent =
            await TaskbarReadyParentProcess.StartAsync();
        try
        {
            Task<TaskbarStartupRecoveryClientResult> recovery =
                TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                TimeSpan.FromSeconds(4),
                directory,
                TaskbarWorkerEvidenceFault.Hang,
                parent.Id);
            string readinessPath = Path.Combine(
                directory,
                TaskbarWorkerProtocol.ParentMonitorReadyEvidenceFileName);
            await WaitForFileAsync(readinessPath, TimeSpan.FromSeconds(4));
            Assert.Equal(
                "ParentMonitorReady",
                await File.ReadAllTextAsync(readinessPath));
            await parent.ReleaseAsync();
            TaskbarStartupRecoveryClientResult result = await recovery;

            Assert.Equal(
                TaskbarStartupRecoveryClientStatus.WorkerExited,
                result.Status);
            Assert.Null(result.Response);
            Assert.Equal("WorkerExit72", result.DiagnosticCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryClientCancellationTerminatesHangingWorker()
    {
        using CancellationTokenSource cancellation =
            new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                TimeSpan.FromSeconds(3),
                directoryPath: null,
                TaskbarWorkerEvidenceFault.Hang,
                Environment.ProcessId,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task RecoveryClientReportsRealWorkerStartFailure()
    {
        string missingWorker = Path.Combine(
            Path.GetTempPath(),
            "LongGrid-MissingWorker-" + Guid.NewGuid().ToString("N") + ".exe");

        TaskbarStartupRecoveryClientResult result =
            await TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                TimeSpan.FromSeconds(3),
                directoryPath: null,
                TaskbarWorkerEvidenceFault.None,
                Environment.ProcessId,
                workerPath: missingWorker,
                cancellationToken: default);

        Assert.Equal(
            TaskbarStartupRecoveryClientStatus.StartFailed,
            result.Status);
        Assert.Null(result.Response);
        Assert.Equal("WorkerStartFailed", result.DiagnosticCode);
    }

    [Fact]
    public async Task RecoveryClientRejectsUnknownEvidenceFault()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            TaskbarAppearanceRecoveryClient.RecoverAtStartupAsync(
                TimeSpan.FromSeconds(3),
                directoryPath: null,
                (TaskbarWorkerEvidenceFault)int.MaxValue,
                Environment.ProcessId));
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            while (!File.Exists(path))
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    timeoutSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Worker readiness evidence was not created: {path}");
        }
    }

    private static TaskbarAppearanceRecoveryJournal CreateJournal(
        TaskbarCompatibilityReport report)
    {
        DateTimeOffset created = DateTimeOffset.UtcNow;
        TaskbarWindowSnapshot primary = report.Actual.TaskbarWindows.Single(
            window => string.Equals(
                window.WindowClass,
                "Shell_TrayWnd",
                StringComparison.Ordinal));
        return new(
            TaskbarAppearanceRecoveryJournalPolicy.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            TaskbarAppearanceRecoveryPhase.Applied,
            TaskbarAppearancePreset.Clear,
            TaskbarAppearancePreset.SystemDefault,
            report.Actual.WindowsBuild,
            primary.ProcessId,
            report.Actual.TaskbarWindows
                .Select(window => window.WindowClass)
                .ToArray(),
            created,
            created + TaskbarAppearanceTransactionPolicy.ConfirmationWindow);
    }

    private static TaskbarAppearanceRecoveryLease AcquireLease(string directory)
    {
        TaskbarAppearanceRecoveryLeaseResult result =
            TaskbarAppearanceRecoveryLease.TryAcquire(directory);
        Assert.Equal(TaskbarAppearanceRecoveryLeaseStatus.Acquired, result.Status);
        return result.Lease!;
    }

    private static string CaptureWindowIdentity(TaskbarCompatibilityReport report) =>
        string.Join(
            "|",
            report.Actual.TaskbarWindows.Select(window =>
                $"{window.Handle}:{window.WindowClass}:{window.ProcessId}"));

    private static void AssertJournalArtifacts(
        string directory,
        bool expectedJournal)
    {
        Assert.Equal(expectedJournal, File.Exists(JournalPath(directory)));
        Assert.False(File.Exists(JournalPath(directory) + ".new"));
    }

    private static string JournalPath(string directory) => Path.Combine(
        directory,
        "taskbar-appearance-recovery.json");

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "LongGrid-TaskbarRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
