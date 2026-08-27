using System.Diagnostics;
using System.Text.Json;
using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarAppearanceRecoveryLeaseRealProcessTests
{
    [Fact]
    public void RealFileLeaseRejectsCompetitionAndCanBeReacquired()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryLeaseResult first =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            using TaskbarAppearanceRecoveryLease? firstLease = first.Lease;
            TaskbarAppearanceRecoveryLeaseResult competing =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);

            Assert.True(first.IsAcquired);
            Assert.Equal(
                TaskbarAppearanceRecoveryLeaseStatus.Contended,
                competing.Status);
            Assert.Null(competing.Lease);
            Assert.Equal("RecoveryLeaseContended", competing.DiagnosticCode);

            firstLease!.Dispose();
            TaskbarAppearanceRecoveryLeaseResult reacquired =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            using TaskbarAppearanceRecoveryLease? reacquiredLease =
                reacquired.Lease;

            Assert.True(reacquired.IsAcquired);
            Assert.True(File.Exists(Path.Combine(
                directory,
                TaskbarAppearanceRecoveryLease.LeaseFileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerCrashReleasesKernelLeaseForRecovery()
    {
        string directory = CreateTemporaryDirectory();
        Process? worker = null;
        try
        {
            worker = StartEvidenceWorker(directory, "hold");
            string response = await worker.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(3))
                ?? throw new InvalidOperationException(
                    "Worker closed stdout before publishing lease evidence.");
            using JsonDocument document = JsonDocument.Parse(response);

            Assert.Equal(
                nameof(TaskbarAppearanceRecoveryLeaseStatus.Acquired),
                document.RootElement.GetProperty("Status").GetString());
            Assert.Equal(
                "None",
                document.RootElement.GetProperty("DiagnosticCode").GetString());

            TaskbarAppearanceRecoveryLeaseResult whileWorkerOwns =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            Assert.Equal(
                TaskbarAppearanceRecoveryLeaseStatus.Contended,
                whileWorkerOwns.Status);

            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            worker.Dispose();
            worker = null;

            TaskbarAppearanceRecoveryLeaseResult afterCrash =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            using TaskbarAppearanceRecoveryLease? recoveryLease =
                afterCrash.Lease;
            Assert.True(afterCrash.IsAcquired);
        }
        finally
        {
            if (worker is not null)
            {
                if (!worker.HasExited)
                {
                    worker.Kill(entireProcessTree: true);
                    worker.WaitForExit(3000);
                }

                worker.Dispose();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EvidenceLeaseCommandIsUnavailableWithoutExplicitOptIn()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            using Process worker = StartEvidenceWorker(
                directory,
                "try",
                enableEvidence: false);
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(65, worker.ExitCode);
            Assert.False(File.Exists(Path.Combine(
                directory,
                TaskbarAppearanceRecoveryLease.LeaseFileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JournalMutationsRequireMatchingLiveLease()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryJournal journal = Journal();
            TaskbarAppearanceRecoveryJournalStore unowned = new(directory);

            Assert.False(await unowned.StageAsync(journal));
            Assert.False(File.Exists(Path.Combine(
                directory,
                "taskbar-appearance-recovery.json")));

            TaskbarAppearanceRecoveryLeaseResult acquired =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            Assert.True(acquired.IsAcquired);
            using TaskbarAppearanceRecoveryLease lease = acquired.Lease!;
            TaskbarAppearanceRecoveryJournalStore owned = new(directory, lease);
            lease.Dispose();

            Assert.False(await owned.StageAsync(journal));
            TaskbarAppearanceRecoveryLoadResult loaded =
                await owned.LoadAsync();
            Assert.Equal(TaskbarAppearanceRecoveryLoadStatus.Missing, loaded.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeWaitsForPinnedMutationBeforeReleasingKernelLease()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryLeaseResult acquired =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            Assert.True(acquired.IsAcquired);
            TaskbarAppearanceRecoveryLease lease = acquired.Lease!;
            using TaskbarAppearanceRecoveryLeaseOperation operation =
                lease.TryBeginOperation()
                ?? throw new InvalidOperationException(
                    "Live lease did not provide an operation token.");

            Task disposeTask = Task.Run(lease.Dispose);
            await Task.Delay(100);
            TaskbarAppearanceRecoveryLeaseResult whilePinned =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);

            Assert.False(disposeTask.IsCompleted);
            Assert.Equal(
                TaskbarAppearanceRecoveryLeaseStatus.Contended,
                whilePinned.Status);

            operation.Dispose();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));
            TaskbarAppearanceRecoveryLeaseResult afterDispose =
                TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            using TaskbarAppearanceRecoveryLease? nextLease = afterDispose.Lease;
            Assert.True(afterDispose.IsAcquired);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JournalStoreRejectsLeaseOwnedByAnotherDirectory()
    {
        string ownedDirectory = CreateTemporaryDirectory();
        string otherDirectory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryLeaseResult acquired =
                TaskbarAppearanceRecoveryLease.TryAcquire(ownedDirectory);
            Assert.True(acquired.IsAcquired);
            using TaskbarAppearanceRecoveryLease lease = acquired.Lease!;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new TaskbarAppearanceRecoveryJournalStore(
                    otherDirectory,
                    lease));

            Assert.Contains("same directory", exception.Message);
        }
        finally
        {
            Directory.Delete(ownedDirectory, recursive: true);
            Directory.Delete(otherDirectory, recursive: true);
        }
    }

    private static Process StartEvidenceWorker(
        string directory,
        string mode,
        bool enableEvidence = true)
    {
        ProcessStartInfo startInfo = new(
            TaskbarCompatibilityClient.ResolveWorkerPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--recovery-lease-evidence");
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--directory");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        if (enableEvidence)
        {
            startInfo.Environment["LONGGRID_TASKBAR_WORKER_EVIDENCE"] = "1";
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the formal taskbar worker.");
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"long-grid-taskbar-lease-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static TaskbarAppearanceRecoveryJournal Journal()
    {
        DateTimeOffset created = new(
            2026,
            8,
            27,
            5,
            0,
            0,
            TimeSpan.Zero);
        return new(
            TaskbarAppearanceRecoveryJournalPolicy.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            TaskbarAppearanceRecoveryPhase.Staged,
            TaskbarAppearancePreset.Clear,
            TaskbarAppearancePreset.SystemDefault,
            WindowsBuild: 22631,
            ExplorerProcessId: 1234,
            TaskbarWindowClasses: ["Shell_TrayWnd"],
            CreatedUtc: created,
            ConfirmationDeadlineUtc:
                created + TaskbarAppearanceTransactionPolicy.ConfirmationWindow);
    }
}
