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

    private sealed record WorkerInvocation(
        int ParentProcessId,
        string RequestId,
        string? EvidenceFault);
}
