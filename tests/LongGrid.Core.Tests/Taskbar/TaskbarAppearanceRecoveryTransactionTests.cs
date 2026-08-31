using System.Diagnostics;
using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarAppearanceRecoveryTransactionTests
{
    private const string AppliedReadyEvidenceFileName =
        "applied-ready.evidence";

    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UncertifiedBuildCannotStartAppearanceTransaction()
    {
        TaskbarCompatibilityReport compatibility =
            TaskbarCompatibilityPolicy.Evaluate(Actual(), isWindows: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.Begin(
                compatibility,
                TaskbarAppearancePreset.Clear,
                Now);

        Assert.Equal(
            TaskbarAppearanceTransactionStatus.AdmissionDenied,
            result.Status);
        Assert.Equal(TaskbarAppearanceTransactionAction.None, result.NextAction);
        Assert.Null(result.TransactionId);
    }

    [Fact]
    public void AllowedBuildStagesBeforeApplyAndUsesExactFifteenSecondDeadline()
    {
        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.Begin(
                AllowedCompatibility(),
                TaskbarAppearancePreset.Clear,
                Now);

        Assert.Equal(TaskbarAppearanceTransactionStatus.ReadyToStage, result.Status);
        Assert.Equal(
            TaskbarAppearanceTransactionAction.StageRecoveryThenApply,
            result.NextAction);
        Assert.True(Guid.TryParseExact(result.TransactionId, "N", out _));
        Assert.Equal(Now.AddSeconds(15), result.ConfirmationDeadlineUtc);
    }

    [Theory]
    [InlineData(false, true, TaskbarAppearanceRollbackReason.ApplyFailed)]
    [InlineData(true, false, TaskbarAppearanceRollbackReason.VerificationFailed)]
    public void ApplyOrVerificationFailureRequiresSystemDefaultRollback(
        bool applySucceeded,
        bool verificationSucceeded,
        TaskbarAppearanceRollbackReason expectedReason)
    {
        TaskbarAppearanceTransactionSnapshot staged = Staged();

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.Applied(
                staged,
                applySucceeded,
                verificationSucceeded);

        Assert.Equal(
            TaskbarAppearanceTransactionStatus.RollbackRequired,
            result.Status);
        Assert.Equal(
            TaskbarAppearanceTransactionAction.RestoreSystemDefault,
            result.NextAction);
        Assert.Equal(expectedReason, result.RollbackReason);
    }

    [Fact]
    public void ConfirmationAtDeadlineExpiresInsteadOfKeepingAppearance()
    {
        TaskbarAppearanceTransactionSnapshot awaiting =
            TaskbarAppearanceTransactionPolicy.Applied(
                Staged(),
                applySucceeded: true,
                verificationSucceeded: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.Confirm(
                awaiting,
                Now.AddSeconds(15));

        Assert.Equal(
            TaskbarAppearanceTransactionStatus.RollbackRequired,
            result.Status);
        Assert.Equal(
            TaskbarAppearanceRollbackReason.ConfirmationExpired,
            result.RollbackReason);
    }

    [Fact]
    public void ConfirmationPreservesRecoveryJournalForCrashOrUninstall()
    {
        TaskbarAppearanceTransactionSnapshot awaiting =
            TaskbarAppearanceTransactionPolicy.Applied(
                Staged(),
                applySucceeded: true,
                verificationSucceeded: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.Confirm(
                awaiting,
                Now.AddSeconds(14));

        Assert.Equal(TaskbarAppearanceTransactionStatus.Confirmed, result.Status);
        Assert.Equal(
            TaskbarAppearanceTransactionAction.PreserveRecoveryJournal,
            result.NextAction);
    }

    [Theory]
    [InlineData(true, false, false, TaskbarAppearanceRollbackReason.ParentExited)]
    [InlineData(false, true, false, TaskbarAppearanceRollbackReason.StartupRecovery)]
    [InlineData(false, false, true, TaskbarAppearanceRollbackReason.UserRejected)]
    public void ExternalFailureSignalsRequireRollback(
        bool parentExited,
        bool startupRecovery,
        bool userRejected,
        TaskbarAppearanceRollbackReason expectedReason)
    {
        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.EvaluateRollback(
                Staged(),
                Now,
                parentExited,
                startupRecovery,
                userRejected);

        Assert.Equal(
            TaskbarAppearanceTransactionStatus.RollbackRequired,
            result.Status);
        Assert.Equal(expectedReason, result.RollbackReason);
    }

    [Fact]
    public void FailedRollbackPreservesJournalForNextStartup()
    {
        TaskbarAppearanceTransactionSnapshot rollback =
            TaskbarAppearanceTransactionPolicy.EvaluateRollback(
                Staged(),
                Now,
                startupRecovery: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.CompleteRollback(
                rollback,
                restoreSucceeded: true,
                verificationSucceeded: false);

        Assert.Equal(
            TaskbarAppearanceTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            TaskbarAppearanceTransactionAction.PreserveRecoveryJournal,
            result.NextAction);
    }

    [Fact]
    public void VerifiedRollbackClearsRecoveryJournal()
    {
        TaskbarAppearanceTransactionSnapshot rollback =
            TaskbarAppearanceTransactionPolicy.EvaluateRollback(
                Staged(),
                Now,
                userRejected: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.CompleteRollback(
                rollback,
                restoreSucceeded: true,
                verificationSucceeded: true);

        Assert.Equal(TaskbarAppearanceTransactionStatus.RolledBack, result.Status);
        Assert.Equal(
            TaskbarAppearanceTransactionAction.ClearRecoveryJournal,
            result.NextAction);
    }

    [Fact]
    public void NoFailureSignalLeavesAwaitingTransactionUnchanged()
    {
        TaskbarAppearanceTransactionSnapshot awaiting =
            TaskbarAppearanceTransactionPolicy.Applied(
                Staged(),
                applySucceeded: true,
                verificationSucceeded: true);

        TaskbarAppearanceTransactionSnapshot result =
            TaskbarAppearanceTransactionPolicy.EvaluateRollback(
                awaiting,
                Now.AddSeconds(14));

        Assert.Same(awaiting, result);
    }

    [Fact]
    public void OutOfOrderTransitionRequestsAreDeterministicNoOps()
    {
        TaskbarAppearanceTransactionSnapshot denied =
            TaskbarAppearanceTransactionPolicy.Begin(
                TaskbarCompatibilityPolicy.Evaluate(Actual(), isWindows: true),
                TaskbarAppearancePreset.Clear,
                Now);

        Assert.Equal(TaskbarAppearancePreset.SystemDefault, denied.RequestedPreset);
        Assert.Null(denied.StartedUtc);
        Assert.Same(
            denied,
            TaskbarAppearanceTransactionPolicy.Applied(
                denied,
                applySucceeded: true,
                verificationSucceeded: true));
        Assert.Same(
            denied,
            TaskbarAppearanceTransactionPolicy.Confirm(denied, Now));
        Assert.Same(
            denied,
            TaskbarAppearanceTransactionPolicy.EvaluateRollback(
                denied,
                Now,
                userRejected: true));
        Assert.Same(
            denied,
            TaskbarAppearanceTransactionPolicy.CompleteRollback(
                denied,
                restoreSucceeded: true,
                verificationSucceeded: true));
    }

    [Fact]
    public async Task RealDiskJournalRoundTripsAndClearsOnlyMatchingTransaction()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryJournal journal = Journal();
            using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
            TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);

            bool staged = await store.StageAsync(journal);
            TaskbarAppearanceRecoveryLoadResult loaded = await store.LoadAsync();
            bool wrongClear = await store.ClearAsync(Guid.NewGuid().ToString("N"));
            bool cleared = await store.ClearAsync(journal.TransactionId);
            TaskbarAppearanceRecoveryLoadResult after = await store.LoadAsync();

            Assert.True(staged);
            Assert.Equal(
                TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired,
                loaded.Status);
            Assert.Equal(journal.TransactionId, loaded.Journal!.TransactionId);
            Assert.False(wrongClear);
            Assert.True(cleared);
            Assert.Equal(TaskbarAppearanceRecoveryLoadStatus.Missing, after.Status);
            Assert.Equal(
                [TaskbarAppearanceRecoveryLease.LeaseFileName],
                Directory.EnumerateFiles(directory)
                    .Select(path => Path.GetFileName(path)!)
                    .ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingJournalCannotBeOverwrittenBySecondTransaction()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryJournal first = Journal();
            TaskbarAppearanceRecoveryJournal second = Journal() with
            {
                TransactionId = Guid.NewGuid().ToString("N"),
            };
            using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
            TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);

            Assert.True(await store.StageAsync(first));
            Assert.False(await store.StageAsync(second));
            TaskbarAppearanceRecoveryLoadResult loaded = await store.LoadAsync();
            Assert.Equal(first.TransactionId, loaded.Journal!.TransactionId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidJournalCannotBeStagedOrCleared()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
            TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
            TaskbarAppearanceRecoveryJournal invalid = Journal() with
            {
                BaselinePreset = TaskbarAppearancePreset.Clear,
            };

            Assert.False(await store.StageAsync(invalid));
            Assert.False(await store.ClearAsync(invalid.TransactionId));
            Assert.Equal(
                [TaskbarAppearanceRecoveryLease.LeaseFileName],
                Directory.EnumerateFiles(directory)
                    .Select(path => Path.GetFileName(path)!)
                    .ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OversizedJournalIsRejectedWithoutParsing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(
                directory,
                "taskbar-appearance-recovery.json");
            await File.WriteAllTextAsync(
                path,
                new string(
                    'x',
                    TaskbarAppearanceRecoveryJournalPolicy.MaximumJournalBytes + 1));
            TaskbarAppearanceRecoveryJournalStore store = new(directory);

            TaskbarAppearanceRecoveryLoadResult result = await store.LoadAsync();

            Assert.Equal(TaskbarAppearanceRecoveryLoadStatus.Invalid, result.Status);
            Assert.Equal("InvalidSize", result.DiagnosticCode);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealDiskPhaseUpdatesAreOrderedAndDurable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            TaskbarAppearanceRecoveryJournal journal = Journal();
            using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
            TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
            Assert.True(await store.StageAsync(journal));

            Assert.False(await store.UpdatePhaseAsync(
                journal.TransactionId,
                TaskbarAppearanceRecoveryPhase.Applied,
                TaskbarAppearanceRecoveryPhase.Confirmed));
            Assert.True(await store.UpdatePhaseAsync(
                journal.TransactionId,
                TaskbarAppearanceRecoveryPhase.Staged,
                TaskbarAppearanceRecoveryPhase.Applied));
            Assert.True(await store.UpdatePhaseAsync(
                journal.TransactionId,
                TaskbarAppearanceRecoveryPhase.Applied,
                TaskbarAppearanceRecoveryPhase.Confirmed));

            TaskbarAppearanceRecoveryLoadResult loaded = await store.LoadAsync();
            Assert.Equal(
                TaskbarAppearanceRecoveryPhase.Confirmed,
                loaded.Journal!.Phase);
            Assert.False(File.Exists(Path.Combine(
                directory,
                "taskbar-appearance-recovery.json.new")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownJsonFieldIsRejectedAndEvidenceIsPreserved()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(
                directory,
                "taskbar-appearance-recovery.json");
            await File.WriteAllTextAsync(
                path,
                "{\"SchemaVersion\":1,\"Unexpected\":true}");
            TaskbarAppearanceRecoveryJournalStore store = new(directory);

            TaskbarAppearanceRecoveryLoadResult result = await store.LoadAsync();

            Assert.Equal(TaskbarAppearanceRecoveryLoadStatus.Invalid, result.Status);
            Assert.Equal("MalformedJson", result.DiagnosticCode);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealKilledChildLeavesDurableRecoveryJournal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string dotnetHost = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
        Assert.True(
            File.Exists(dotnetHost),
            $"The x64 dotnet host was not found: {dotnetHost}");
        string directory = CreateTemporaryDirectory();
        Process? child = null;
        try
        {
            ProcessStartInfo startInfo = new(dotnetHost)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(typeof(
                TaskbarAppearanceRecoveryTransactionTests).Assembly.Location);
            startInfo.ArgumentList.Add(
                "--Tests:LongGrid.Core.Tests.Taskbar.TaskbarAppearanceRecoveryTransactionTests.ChildStagesRecoveryJournalAndWaits");
            Dictionary<string, string?> cleanEnvironment = new(
                StringComparer.OrdinalIgnoreCase);
            foreach (string variable in new[]
            {
                "PATH",
                "SystemRoot",
                "TEMP",
                "TMP",
                "DOTNET_ROOT",
                "USERPROFILE",
            })
            {
                cleanEnvironment[variable] =
                    Environment.GetEnvironmentVariable(variable);
            }

            startInfo.Environment.Clear();
            foreach ((string variable, string? value) in cleanEnvironment)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    startInfo.Environment[variable] = value;
                }
            }

            startInfo.Environment["LONGGRID_TASKBAR_RECOVERY_CHILD"] = "1";
            startInfo.Environment["LONGGRID_TASKBAR_RECOVERY_DIRECTORY"] = directory;
            child = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Child test process did not start.");
            TaskbarAppearanceRecoveryJournalStore store = new(directory);

            await WaitForFileAsync(
                Path.Combine(directory, AppliedReadyEvidenceFileName),
                TimeSpan.FromSeconds(10));
            TaskbarAppearanceRecoveryLoadResult beforeKill =
                await store.LoadAsync();
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            TaskbarAppearanceRecoveryLoadResult afterKill = await store.LoadAsync();

            Assert.Equal(
                TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired,
                beforeKill.Status);
            Assert.Equal(
                TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired,
                afterKill.Status);
            Assert.Equal(
                beforeKill.Journal!.TransactionId,
                afterKill.Journal!.TransactionId);
            Assert.Equal(
                TaskbarAppearanceRecoveryPhase.Applied,
                afterKill.Journal.Phase);
            using TaskbarAppearanceRecoveryLease recoveryLease =
                await WaitForLeaseAsync(directory, TimeSpan.FromSeconds(3));
            TaskbarAppearanceRecoveryJournalStore recoveryStore =
                new(directory, recoveryLease);
            Assert.True(await recoveryStore.ClearAsync(
                afterKill.Journal.TransactionId));
            recoveryLease.Dispose();
            Assert.False(recoveryLease.IsHeld);
            using TaskbarAppearanceRecoveryLease finalLease =
                AcquireLease(directory);
            finalLease.Dispose();
            Assert.False(finalLease.IsHeld);
        }
        finally
        {
            if (child is not null && !child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }

            await DeleteTemporaryDirectoryWithRetryAsync(directory);
        }
    }

    [Fact]
    public async Task ChildStagesRecoveryJournalAndWaits()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "LONGGRID_TASKBAR_RECOVERY_CHILD"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string directory = Environment.GetEnvironmentVariable(
            "LONGGRID_TASKBAR_RECOVERY_DIRECTORY")
            ?? throw new InvalidOperationException("Missing child directory.");
        using TaskbarAppearanceRecoveryLease lease = AcquireLease(directory);
        TaskbarAppearanceRecoveryJournalStore store = new(directory, lease);
        TaskbarAppearanceRecoveryJournal journal = Journal();
        Assert.True(await store.StageAsync(journal));
        Assert.True(await store.UpdatePhaseAsync(
            journal.TransactionId,
            TaskbarAppearanceRecoveryPhase.Staged,
            TaskbarAppearanceRecoveryPhase.Applied));
        await File.WriteAllTextAsync(
            Path.Combine(directory, AppliedReadyEvidenceFileName),
            "AppliedReady");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            while (!File.Exists(path))
            {
                await Task.Delay(25, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Child readiness evidence was not created: {path}");
        }
    }

    private static async Task DeleteTemporaryDirectoryWithRetryAsync(
        string directory)
    {
        for (int attempt = 1; attempt <= 40; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 40
                && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(50);
            }
        }
    }

    private static async Task<TaskbarAppearanceRecoveryLease> WaitForLeaseAsync(
        string directory,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TaskbarAppearanceRecoveryLeaseResult result;
        do
        {
            result = TaskbarAppearanceRecoveryLease.TryAcquire(directory);
            if (result.IsAcquired)
            {
                return result.Lease!;
            }

            if (result.Status != TaskbarAppearanceRecoveryLeaseStatus.Contended)
            {
                Assert.Fail(result.DiagnosticCode);
            }

            await Task.Delay(50);
        }
        while (stopwatch.Elapsed < timeout);

        Assert.Fail(result.DiagnosticCode);
        throw new InvalidOperationException("Unreachable assertion fallback.");
    }

    private static TaskbarAppearanceTransactionSnapshot Staged() =>
        TaskbarAppearanceTransactionPolicy.Begin(
            AllowedCompatibility(),
            TaskbarAppearancePreset.Clear,
            Now);

    private static TaskbarAppearanceRecoveryLease AcquireLease(string directory)
    {
        TaskbarAppearanceRecoveryLeaseResult result =
            TaskbarAppearanceRecoveryLease.TryAcquire(directory);
        Assert.True(result.IsAcquired, result.DiagnosticCode);
        return result.Lease!;
    }

    private static TaskbarAppearanceRecoveryJournal Journal() => new(
        TaskbarAppearanceRecoveryJournalPolicy.CurrentSchemaVersion,
        Guid.NewGuid().ToString("N"),
        TaskbarAppearanceRecoveryPhase.Staged,
        TaskbarAppearancePreset.Clear,
        TaskbarAppearancePreset.SystemDefault,
        WindowsBuild: 26200,
        ExplorerProcessId: 6932,
        TaskbarWindowClasses: ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"],
        CreatedUtc: Now,
        ConfirmationDeadlineUtc:
            Now + TaskbarAppearanceTransactionPolicy.ConfirmationWindow);

    private static TaskbarCompatibilityReport AllowedCompatibility() => new(
        TaskbarWorkerProtocol.CurrentVersion,
        TaskbarWorkerProtocol.ProbePurpose,
        new(true, 1, "explorer", [], false),
        Actual(),
        [],
        TaskbarProbeOutcome.Pass,
        TaskbarRuntimeAdmission.Allowed);

    private static TaskbarCompatibilityActual Actual() => new(
        "10.0.26200.0",
        26200,
        2,
        [new(1, "Shell_TrayWnd", 6932, "explorer")],
        [],
        ModifiedSystemState: false,
        ProbeMilliseconds: 1);

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"LongGridTaskbarRecovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
