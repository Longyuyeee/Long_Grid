using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarAppearanceRecoveryClientRealProcessTests
{
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
