using System.Text.Json;
using LongGrid.Core.Taskbar;

namespace LongGrid.TaskbarWorker;

internal static class Program
{
    private const string EvidenceEnvironmentVariable =
        "LONGGRID_TASKBAR_WORKER_EVIDENCE";

    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 1
            && string.Equals(args[0], "--compatibility-probe", StringComparison.Ordinal))
        {
            return WriteLegacyProbe();
        }

        if (TryParseRecoveryLeaseEvidence(
                args,
                out RecoveryLeaseEvidenceInvocation? leaseInvocation)
            && leaseInvocation is not null)
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        EvidenceEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return 65;
            }

            return await RunRecoveryLeaseEvidenceAsync(leaseInvocation)
                .ConfigureAwait(false);
        }

        if (TryParseNativeAdapterCertificationInvocation(
                args,
                out NativeAdapterCertificationInvocation?
                    certificationInvocation)
            && certificationInvocation is not null)
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        EvidenceEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return 65;
            }

            return await RunNativeAdapterCertificationAsync(
                certificationInvocation).ConfigureAwait(false);
        }

        if (TryParseStartupRecoveryInvocation(
                args,
                out StartupRecoveryInvocation? recoveryInvocation)
            && recoveryInvocation is not null)
        {
            if ((recoveryInvocation.DirectoryPath is not null
                    || recoveryInvocation.EvidenceFault is not null)
                && !string.Equals(
                    Environment.GetEnvironmentVariable(
                        EvidenceEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return 65;
            }

            return await RunStartupRecoveryAsync(recoveryInvocation)
                .ConfigureAwait(false);
        }

        if (!TryParseInvocation(args, out WorkerInvocation? invocation)
            || invocation is null)
        {
            return 64;
        }

        using CancellationTokenSource parentExited = new();
        Task<int> parentMonitor = MonitorParentAsync(
            invocation.ParentProcessId,
            parentExited.Token);
        if (parentMonitor.IsCompleted)
        {
            return await parentMonitor.ConfigureAwait(false);
        }

        try
        {
            if (invocation.EvidenceFault is not null)
            {
                if (!string.Equals(
                        Environment.GetEnvironmentVariable(
                            EvidenceEnvironmentVariable),
                        "1",
                        StringComparison.Ordinal))
                {
                    return 65;
                }

                return await RunEvidenceFaultAsync(
                    invocation,
                    parentMonitor).ConfigureAwait(false);
            }

            TaskbarCompatibilityReport report = TaskbarCompatibilityProbe.Capture();
            WriteResponse(invocation.RequestId, report, TaskbarWorkerProtocol.CurrentVersion);
            return report.ProbeOutcome == TaskbarProbeOutcome.Pass ? 0 : 1;
        }
        finally
        {
            parentExited.Cancel();
            try
            {
                await parentMonitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal completion stops the parent watcher.
            }
        }
    }

    private static int WriteLegacyProbe()
    {
        TaskbarCompatibilityReport report = TaskbarCompatibilityProbe.Capture();
        Console.WriteLine(JsonSerializer.Serialize(
            report,
            TaskbarJsonContext.Default.TaskbarCompatibilityReport));
        return report.ProbeOutcome == TaskbarProbeOutcome.Pass ? 0 : 1;
    }

    private static async Task<int> RunRecoveryLeaseEvidenceAsync(
        RecoveryLeaseEvidenceInvocation invocation)
    {
        TaskbarAppearanceRecoveryLeaseResult result =
            TaskbarAppearanceRecoveryLease.TryAcquire(invocation.DirectoryPath);
        WriteRecoveryLeaseEvidence(result);
        if (!result.IsAcquired)
        {
            return result.Status == TaskbarAppearanceRecoveryLeaseStatus.Contended
                ? 73
                : 74;
        }

        using (result.Lease)
        {
            if (!invocation.HoldUntilParentExit)
            {
                return 0;
            }

            using CancellationTokenSource cancellation = new();
            return await MonitorParentAsync(
                invocation.ParentProcessId,
                cancellation.Token).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunStartupRecoveryAsync(
        StartupRecoveryInvocation invocation)
    {
        using CancellationTokenSource parentExited = new();
        Task<int> parentMonitor = MonitorParentAsync(
            invocation.ParentProcessId,
            parentExited.Token);
        if (parentMonitor.IsCompleted)
        {
            return await parentMonitor.ConfigureAwait(false);
        }

        try
        {
            if (invocation.EvidenceFault is not null)
            {
                return await RunStartupRecoveryEvidenceFaultAsync(
                    invocation,
                    parentMonitor).ConfigureAwait(false);
            }

            string directoryPath = invocation.DirectoryPath
                ?? TaskbarAppearanceRecoveryPath.ResolveDefaultDirectory();
            TaskbarAppearanceRecoveryLeaseResult leaseResult =
                TaskbarAppearanceRecoveryLease.TryAcquire(directoryPath);
            if (!leaseResult.IsAcquired)
            {
                TaskbarStartupRecoveryStatus status = leaseResult.Status
                    == TaskbarAppearanceRecoveryLeaseStatus.Contended
                        ? TaskbarStartupRecoveryStatus.LeaseContended
                        : TaskbarStartupRecoveryStatus.RecoveryJournalIoFailure;
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    status,
                    leaseResult.DiagnosticCode,
                    null,
                    journalPreserved: true,
                    report: null);
                return 1;
            }

            using TaskbarAppearanceRecoveryLease lease = leaseResult.Lease!;
            TaskbarAppearanceRecoveryJournalStore store = new(
                directoryPath,
                lease);
            TaskbarAppearanceRecoveryLoadResult load =
                await store.LoadAsync().ConfigureAwait(false);
            if (load.Status == TaskbarAppearanceRecoveryLoadStatus.Missing)
            {
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    TaskbarStartupRecoveryStatus.NoRecoveryRequired,
                    "Missing",
                    null,
                    journalPreserved: false,
                    report: null);
                return 0;
            }

            if (load.Status != TaskbarAppearanceRecoveryLoadStatus.RecoveryRequired)
            {
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    load.Status == TaskbarAppearanceRecoveryLoadStatus.Invalid
                        ? TaskbarStartupRecoveryStatus.RecoveryJournalInvalid
                        : TaskbarStartupRecoveryStatus.RecoveryJournalIoFailure,
                    load.DiagnosticCode,
                    null,
                    journalPreserved: true,
                    report: null);
                return 1;
            }

            TaskbarAppearanceRecoveryJournal journal = load.Journal!;
            TaskbarCompatibilityReport report = TaskbarCompatibilityProbe.Capture();
            ITaskbarAppearanceNativeAdapter? adapter =
                TaskbarNativeAdapterCatalog.Resolve(report.Actual.WindowsBuild);
            TaskbarNativeRestoreAdmission admission =
                TaskbarNativeRestoreAdmissionPolicy.Evaluate(
                    journal,
                    report,
                    adapter);
            if (admission.Status
                == TaskbarNativeRestoreAdmissionStatus.TargetChanged)
            {
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    TaskbarStartupRecoveryStatus.RecoveryDeferredTargetChanged,
                    admission.DiagnosticCode,
                    journal.Phase,
                    journalPreserved: true,
                    report);
                return 1;
            }

            if (admission.Status
                    == TaskbarNativeRestoreAdmissionStatus.CompatibilityDenied
                || admission.Status
                    == TaskbarNativeRestoreAdmissionStatus.RecoveryJournalInvalid)
            {
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    TaskbarStartupRecoveryStatus.RecoveryDeferredCompatibility,
                    admission.DiagnosticCode,
                    journal.Phase,
                    journalPreserved: true,
                    report);
                return 1;
            }

            WriteStartupRecoveryResponse(
                invocation.RequestId,
                TaskbarStartupRecoveryStatus.RecoveryDeferredAdapterUnavailable,
                admission.DiagnosticCode,
                journal.Phase,
                journalPreserved: true,
                report);
            return 1;
        }
        finally
        {
            parentExited.Cancel();
            try
            {
                await parentMonitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal completion stops the parent watcher.
            }
        }
    }

    private static async Task<int> RunNativeAdapterCertificationAsync(
        NativeAdapterCertificationInvocation invocation)
    {
        using CancellationTokenSource parentExited = new();
        Task<int> parentMonitor = MonitorParentAsync(
            invocation.ParentProcessId,
            parentExited.Token);
        if (parentMonitor.IsCompleted)
        {
            return await parentMonitor.ConfigureAwait(false);
        }

        try
        {
            TaskbarCompatibilityReport report = TaskbarCompatibilityProbe.Capture();
            ITaskbarAppearanceNativeAdapter? adapter =
                TaskbarNativeAdapterCatalog.Resolve(report.Actual.WindowsBuild);
            TaskbarNativeAdapterCertificationResponse response = new(
                TaskbarWorkerProtocol.CurrentVersion,
                TaskbarWorkerProtocol.NativeAdapterCertificationPurpose,
                invocation.RequestId,
                adapter?.Availability
                    ?? TaskbarNativeAdapterAvailability.Unavailable,
                adapter?.AdapterId ?? "None",
                ModifiedSystemState: false,
                report);
            Console.WriteLine(JsonSerializer.Serialize(
                response,
                TaskbarJsonContext.Default
                    .TaskbarNativeAdapterCertificationResponse));
            Console.Out.Flush();
            return 0;
        }
        finally
        {
            parentExited.Cancel();
            try
            {
                await parentMonitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal completion stops the parent watcher.
            }
        }
    }

    private static async Task<int> RunStartupRecoveryEvidenceFaultAsync(
        StartupRecoveryInvocation invocation,
        Task<int> parentMonitor)
    {
        switch (invocation.EvidenceFault)
        {
            case "hang":
                WriteParentMonitorReadyEvidence(invocation.DirectoryPath);
                return await parentMonitor.ConfigureAwait(false);
            case "exit":
                return 71;
            case "malformed":
                Console.WriteLine("{malformed");
                return 0;
            case "wrong-version":
                WriteStartupRecoveryResponse(
                    invocation.RequestId,
                    TaskbarStartupRecoveryStatus.NoRecoveryRequired,
                    "Missing",
                    null,
                    journalPreserved: false,
                    report: null,
                    protocolVersion: TaskbarWorkerProtocol.CurrentVersion + 1);
                return 0;
            case "oversized":
                Console.WriteLine(new string(
                    'x',
                    TaskbarWorkerProtocol.MaximumResponseCharacters + 1));
                return 0;
            default:
                return 66;
        }
    }

    private static void WriteParentMonitorReadyEvidence(string? directoryPath)
    {
        if (directoryPath is null)
        {
            return;
        }

        Directory.CreateDirectory(directoryPath);
        string evidencePath = Path.Combine(
            directoryPath,
            TaskbarWorkerProtocol.ParentMonitorReadyEvidenceFileName);
        string pendingPath = evidencePath + ".new";
        try
        {
            File.WriteAllText(
                pendingPath,
                "ParentMonitorReady",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(pendingPath, evidencePath, overwrite: true);
        }
        finally
        {
            File.Delete(pendingPath);
        }
    }

    private static void WriteStartupRecoveryResponse(
        string requestId,
        TaskbarStartupRecoveryStatus status,
        string diagnosticCode,
        TaskbarAppearanceRecoveryPhase? recoveryPhase,
        bool journalPreserved,
        TaskbarCompatibilityReport? report,
        int protocolVersion = TaskbarWorkerProtocol.CurrentVersion)
    {
        TaskbarStartupRecoveryWorkerResponse response = new(
            protocolVersion,
            TaskbarWorkerProtocol.StartupRecoveryPurpose,
            requestId,
            status,
            diagnosticCode,
            recoveryPhase,
            journalPreserved,
            ModifiedSystemState: false,
            report);
        Console.WriteLine(JsonSerializer.Serialize(
            response,
            TaskbarJsonContext.Default.TaskbarStartupRecoveryWorkerResponse));
        Console.Out.Flush();
    }

    private static void WriteRecoveryLeaseEvidence(
        TaskbarAppearanceRecoveryLeaseResult result)
    {
        TaskbarRecoveryLeaseEvidenceResponse response = new(
            result.Status,
            result.DiagnosticCode);
        Console.WriteLine(JsonSerializer.Serialize(
            response,
            TaskbarJsonContext.Default.TaskbarRecoveryLeaseEvidenceResponse));
        Console.Out.Flush();
    }

    private static async Task<int> RunEvidenceFaultAsync(
        WorkerInvocation invocation,
        Task<int> parentMonitor)
    {
        switch (invocation.EvidenceFault)
        {
            case "hang":
                return await parentMonitor.ConfigureAwait(false);
            case "exit":
                return 71;
            case "malformed":
                Console.WriteLine("{malformed");
                return 0;
            case "wrong-version":
                WriteResponse(
                    invocation.RequestId,
                    TaskbarCompatibilityProbe.Capture(),
                    TaskbarWorkerProtocol.CurrentVersion + 1);
                return 0;
            case "oversized":
                Console.WriteLine(new string(
                    'x',
                    TaskbarWorkerProtocol.MaximumResponseCharacters + 1));
                return 0;
            default:
                return 66;
        }
    }

    private static void WriteResponse(
        string requestId,
        TaskbarCompatibilityReport report,
        int protocolVersion)
    {
        TaskbarWorkerResponse response = new(
            protocolVersion,
            requestId,
            report);
        Console.WriteLine(JsonSerializer.Serialize(
            response,
            TaskbarJsonContext.Default.TaskbarWorkerResponse));
    }

    private static async Task<int> MonitorParentAsync(
        int parentProcessId,
        CancellationToken cancellationToken)
    {
        try
        {
            using System.Diagnostics.Process parent =
                System.Diagnostics.Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return 72;
        }
        catch (ArgumentException)
        {
            return 72;
        }
        catch (InvalidOperationException)
        {
            return 72;
        }
    }

    private static bool TryParseInvocation(
        string[] args,
        out WorkerInvocation? invocation)
    {
        invocation = null;
        if (args.Length is not (5 or 7)
            || !string.Equals(args[0], "--taskbar-worker", StringComparison.Ordinal)
            || !string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            || !int.TryParse(args[2], out int parentProcessId)
            || parentProcessId <= 0
            || !string.Equals(args[3], "--request-id", StringComparison.Ordinal)
            || !Guid.TryParseExact(args[4], "N", out _))
        {
            return false;
        }

        string? evidenceFault = null;
        if (args.Length == 7)
        {
            if (!string.Equals(args[5], "--evidence-fault", StringComparison.Ordinal))
            {
                return false;
            }

            evidenceFault = args[6];
        }

        invocation = new(parentProcessId, args[4], evidenceFault);
        return true;
    }

    private static bool TryParseRecoveryLeaseEvidence(
        string[] args,
        out RecoveryLeaseEvidenceInvocation? invocation)
    {
        invocation = null;
        if (args.Length != 7
            || !string.Equals(
                args[0],
                "--recovery-lease-evidence",
                StringComparison.Ordinal)
            || !string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            || !int.TryParse(args[2], out int parentProcessId)
            || parentProcessId <= 0
            || !string.Equals(args[3], "--directory", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[4])
            || !string.Equals(args[5], "--mode", StringComparison.Ordinal)
            || args[6] is not ("try" or "hold"))
        {
            return false;
        }

        invocation = new(
            parentProcessId,
            args[4],
            string.Equals(args[6], "hold", StringComparison.Ordinal));
        return true;
    }

    private static bool TryParseNativeAdapterCertificationInvocation(
        string[] args,
        out NativeAdapterCertificationInvocation? invocation)
    {
        invocation = null;
        if (args.Length != 5
            || !string.Equals(
                args[0],
                "--native-adapter-certification",
                StringComparison.Ordinal)
            || !string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            || !int.TryParse(args[2], out int parentProcessId)
            || parentProcessId <= 0
            || !string.Equals(args[3], "--request-id", StringComparison.Ordinal)
            || !Guid.TryParseExact(args[4], "N", out _))
        {
            return false;
        }

        invocation = new(parentProcessId, args[4]);
        return true;
    }

    private static bool TryParseStartupRecoveryInvocation(
        string[] args,
        out StartupRecoveryInvocation? invocation)
    {
        invocation = null;
        if (args.Length is not (5 or 7 or 9)
            || !string.Equals(
                args[0],
                "--startup-recovery",
                StringComparison.Ordinal)
            || !string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            || !int.TryParse(args[2], out int parentProcessId)
            || parentProcessId <= 0
            || !string.Equals(args[3], "--request-id", StringComparison.Ordinal)
            || !Guid.TryParseExact(args[4], "N", out _))
        {
            return false;
        }

        string? directoryPath = null;
        string? evidenceFault = null;
        for (int index = 5; index < args.Length; index += 2)
        {
            if (string.Equals(
                    args[index],
                    "--evidence-directory",
                    StringComparison.Ordinal)
                && directoryPath is null
                && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                directoryPath = args[index + 1];
            }
            else if (string.Equals(
                         args[index],
                         "--evidence-fault",
                         StringComparison.Ordinal)
                     && evidenceFault is null
                     && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                evidenceFault = args[index + 1];
            }
            else
            {
                return false;
            }
        }

        invocation = new(
            parentProcessId,
            args[4],
            directoryPath,
            evidenceFault);
        return true;
    }

    private sealed record WorkerInvocation(
        int ParentProcessId,
        string RequestId,
        string? EvidenceFault);

    private sealed record RecoveryLeaseEvidenceInvocation(
        int ParentProcessId,
        string DirectoryPath,
        bool HoldUntilParentExit);

    private sealed record NativeAdapterCertificationInvocation(
        int ParentProcessId,
        string RequestId);

    private sealed record StartupRecoveryInvocation(
        int ParentProcessId,
        string RequestId,
        string? DirectoryPath,
        string? EvidenceFault);
}
