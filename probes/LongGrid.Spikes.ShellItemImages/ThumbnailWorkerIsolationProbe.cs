using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class ThumbnailWorkerIsolationProbe
{
    private const int StressRequests = 500;
    private const int RequestsPerWorker = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForcedTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IdleSample = TimeSpan.FromMilliseconds(750);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(bool json)
    {
        string root = CreateOwnedSandbox();
        ThumbnailWorkerIsolationReport? report = null;

        try
        {
            string bitmapPath = Path.Combine(root, "owned-sample.bmp");
            WriteOwnedBitmap(bitmapPath);
            report = await RunMatrixAsync(bitmapPath);
        }
        finally
        {
            bool cleanupSucceeded = TryDeleteOwnedSandbox(root);
            if (report is not null)
            {
                report = report with { CleanupSucceeded = cleanupSucceeded };
            }
        }

        if (report is null)
        {
            return 2;
        }

        bool passed = report.Stress.Succeeded == StressRequests
            && report.WarmupSucceeded
            && report.HardTimeout.TimedOut
            && report.HardTimeout.WorkerKilled
            && report.HardTimeout.RecoverySucceeded
            && report.TimeoutBackoff.TimeoutsObserved == 3
            && report.TimeoutBackoff.MaximumConsecutiveTimeouts >= 3
            && report.TimeoutBackoff.BackoffsApplied == 3
            && report.TimeoutBackoff.RecoverySucceeded
            && report.ParentExit.WorkerStarted
            && report.ParentExit.ParentExited
            && report.ParentExit.OrphanExited
            && report.Resilience.MalformedResponseDetected
            && report.Resilience.MalformedResponseRecoverySucceeded
            && report.Resilience.WrongVersionDetected
            && report.Resilience.WrongVersionRecoverySucceeded
            && report.Resilience.OversizedResponseDetected
            && report.Resilience.OversizedResponseRecoverySucceeded
            && report.Resilience.OversizedRequestDetected
            && report.Resilience.OversizedRequestRecoverySucceeded
            && report.Resilience.UnexpectedExitDetected
            && report.Resilience.UnexpectedExitRecoverySucceeded
            && report.Budget.WithinProvisionalBudget
            && report.CleanupSucceeded;
        report = report with
        {
            Verdict = passed ? "ConditionalPass" : "Fail",
        };

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintText(report);
        }

        return passed ? 0 : 2;
    }

    private static async Task<ThumbnailWorkerIsolationReport> RunMatrixAsync(
        string bitmapPath)
    {
        using var client = new ThumbnailWorkerClient(RequestsPerWorker);
        ThumbnailWorkerCallResult warmupResult = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "warmup"),
            RequestTimeout);
        bool warmupSucceeded = warmupResult.Completed
            && warmupResult.Response is { Success: true };
        double idleCpuMilliseconds =
            await client.MeasureIdleCpuMillisecondsAsync(IdleSample);
        var durations = new List<double>(StressRequests);
        int succeeded = 0;
        int failed = 0;
        var stressStopwatch = Stopwatch.StartNew();

        for (int index = 0; index < StressRequests; index++)
        {
            ThumbnailWorkerCallResult result = await client.ExecuteAsync(
                ExtractRequest(bitmapPath, $"stress-{index}"),
                RequestTimeout);
            if (result.Completed && result.Response is { Success: true })
            {
                succeeded++;
            }
            else
            {
                failed++;
            }

            durations.Add(result.RoundTripMilliseconds);
        }

        stressStopwatch.Stop();
        ThumbnailWorkerCallResult timeoutResult = await client.ExecuteAsync(
            new ThumbnailWorkerRequest(
                ThumbnailWorkerServer.CurrentProtocolVersion,
                "forced-timeout",
                ThumbnailWorkerRequestKind.Hang,
                Path: null,
                Size: 0,
                Flags: 0),
            ForcedTimeout);
        ThumbnailWorkerCallResult recoveryResult = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "recovery"),
            RequestTimeout);
        ThumbnailWorkerCallResult malformedResult = await client.ExecuteAsync(
            new ThumbnailWorkerRequest(
                ThumbnailWorkerServer.CurrentProtocolVersion,
                "malformed-response",
                ThumbnailWorkerRequestKind.MalformedResponse,
                Path: null,
                Size: 0,
                Flags: 0),
            RequestTimeout);
        ThumbnailWorkerCallResult malformedRecovery = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "malformed-recovery"),
            RequestTimeout);
        ThumbnailWorkerCallResult wrongVersionResult = await client.ExecuteAsync(
            FaultRequest(
                "wrong-version",
                ThumbnailWorkerRequestKind.WrongVersionResponse),
            RequestTimeout);
        ThumbnailWorkerCallResult wrongVersionRecovery = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "wrong-version-recovery"),
            RequestTimeout);
        ThumbnailWorkerCallResult oversizedResponseResult =
            await client.ExecuteAsync(
                FaultRequest(
                    "oversized-response",
                    ThumbnailWorkerRequestKind.OversizedResponse),
                RequestTimeout);
        ThumbnailWorkerCallResult oversizedResponseRecovery =
            await client.ExecuteAsync(
                ExtractRequest(bitmapPath, "oversized-response-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult oversizedRequestResult =
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "oversized-request",
                    ThumbnailWorkerRequestKind.Extract,
                    new string(
                        'x',
                        ThumbnailWorkerServer.MaximumRequestCharacters + 1),
                    Size: 128,
                    ShellItemImageFactoryFlags.ThumbnailOnly),
                RequestTimeout);
        ThumbnailWorkerCallResult oversizedRequestRecovery =
            await client.ExecuteAsync(
                ExtractRequest(bitmapPath, "oversized-request-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult exitResult = await client.ExecuteAsync(
            new ThumbnailWorkerRequest(
                ThumbnailWorkerServer.CurrentProtocolVersion,
                "unexpected-exit",
                ThumbnailWorkerRequestKind.Exit,
                Path: null,
                Size: 0,
                Flags: 0),
            RequestTimeout);
        ThumbnailWorkerCallResult exitRecovery = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "exit-recovery"),
            RequestTimeout);
        int backoffsBefore = client.RestartBackoffsApplied;
        double backoffMillisecondsBefore =
            client.TotalRestartBackoffMilliseconds;
        int timeoutKillsBefore = client.TimeoutKills;
        for (int index = 0; index < 3; index++)
        {
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    $"consecutive-timeout-{index}",
                    ThumbnailWorkerRequestKind.Hang,
                    Path: null,
                    Size: 0,
                    Flags: 0),
                ForcedTimeout);
        }

        ThumbnailWorkerCallResult backoffRecovery = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "backoff-recovery"),
            RequestTimeout);
        client.Dispose();
        ThumbnailWorkerParentExitResult parentExit =
            await VerifyParentExitCleanupAsync(
                Path.GetDirectoryName(bitmapPath)
                    ?? throw new InvalidOperationException(
                        "The bitmap sandbox is unavailable."));

        double p50 = Percentile(durations, 0.50);
        double p95 = Percentile(durations, 0.95);
        var stress = new ThumbnailWorkerStressResult(
            Requested: StressRequests,
            Succeeded: succeeded,
            Failed: failed,
            P50RoundTripMilliseconds: p50,
            P95RoundTripMilliseconds: p95,
            TotalWallMilliseconds: stressStopwatch.Elapsed.TotalMilliseconds,
            ProcessesStarted: client.ProcessesStarted,
            BudgetRecycles: client.BudgetRecycles);
        var hardTimeout = new ThumbnailWorkerTimeoutResult(
            timeoutResult.TimedOut,
            WorkerKilled: timeoutResult.WorkerExited,
            RecoverySucceeded:
                recoveryResult.Completed
                && recoveryResult.Response is { Success: true });
        var timeoutBackoff = new ThumbnailWorkerBackoffResult(
            TimeoutsObserved: client.TimeoutKills - timeoutKillsBefore,
            MaximumConsecutiveTimeouts: client.MaximumConsecutiveTimeouts,
            BackoffsApplied: client.RestartBackoffsApplied - backoffsBefore,
            BackoffMilliseconds:
                client.TotalRestartBackoffMilliseconds
                - backoffMillisecondsBefore,
            RecoverySucceeded:
                backoffRecovery.Completed
                && backoffRecovery.Response is { Success: true });
        var resources = new ThumbnailWorkerResourceResult(
            IdleSampleMilliseconds: IdleSample.TotalMilliseconds,
            IdleCpuMilliseconds: idleCpuMilliseconds,
            TotalWorkerCpuMilliseconds: client.TotalCpuMilliseconds,
            PeakWorkingSetBytes: client.PeakWorkingSetBytes,
            PeakHandleCount: client.PeakHandleCount,
            TimeoutKills: client.TimeoutKills);
        var resilience = new ThumbnailWorkerResilienceResult(
            MalformedResponseDetected: malformedResult.ProtocolError,
            MalformedResponseRecoverySucceeded:
                malformedRecovery.Completed
                && malformedRecovery.Response is { Success: true },
            WrongVersionDetected: wrongVersionResult.ProtocolError,
            WrongVersionRecoverySucceeded:
                wrongVersionRecovery.Completed
                && wrongVersionRecovery.Response is { Success: true },
            OversizedResponseDetected: oversizedResponseResult.ProtocolError,
            OversizedResponseRecoverySucceeded:
                oversizedResponseRecovery.Completed
                && oversizedResponseRecovery.Response is { Success: true },
            OversizedRequestDetected:
                oversizedRequestResult.WorkerExited
                && !oversizedRequestResult.TimedOut,
            OversizedRequestRecoverySucceeded:
                oversizedRequestRecovery.Completed
                && oversizedRequestRecovery.Response is { Success: true },
            UnexpectedExitDetected:
                exitResult.WorkerExited
                && !exitResult.TimedOut
                && !exitResult.ProtocolError,
            UnexpectedExitRecoverySucceeded:
                exitRecovery.Completed
                && exitRecovery.Response is { Success: true },
            client.ProtocolKills,
            client.UnexpectedExits);
        var budget = CreateBudget(stress, resources);

        return new ThumbnailWorkerIsolationReport(
            Probe: "P0-03b-thumbnail-worker-isolation",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            WarmupSucceeded: warmupSucceeded,
            Stress: stress,
            HardTimeout: hardTimeout,
            TimeoutBackoff: timeoutBackoff,
            ParentExit: parentExit,
            Resilience: resilience,
            Resources: resources,
            Budget: budget,
            CleanupSucceeded: false,
            Verdict: "PendingCleanup",
            Privacy:
            [
                "Only an owned synthetic BMP inside a random temporary sandbox was opened.",
                "The path traveled through redirected stdin and never appeared in command-line arguments or report output.",
                "No image bytes, names, paths, HRESULT values, or Shell identities are emitted.",
            ],
            Limitations:
            [
                "The worker currently runs with the caller's token; AppContainer or another low-privilege token remains required for production.",
                "The synthetic BMP validates process lifetime and Shell extraction, not third-party, cloud, network, or adversarial providers.",
                "The probe returns dimensions and status only; a production IPC pixel-transfer contract remains unimplemented.",
                "The forced timeout and parent-exit cases use a deterministic worker hang before native extraction because inducing a real provider hang on a user machine is unsafe.",
                "Budgets are provisional for this machine and must be repeated across the supported Windows and architecture matrix.",
            ]);
    }

    private static async Task<ThumbnailWorkerParentExitResult>
        VerifyParentExitCleanupAsync(string root)
    {
        string readyPath = Path.Combine(root, "parent-exit-ready.txt");
        using Process parentHarness = Process.Start(
            CreateParentExitHarnessStartInfo(readyPath))
            ?? throw new InvalidOperationException(
                "The parent-exit harness did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!File.Exists(readyPath))
        {
            if (parentHarness.HasExited)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        int workerProcessId = 0;
        bool workerStarted = File.Exists(readyPath)
            && int.TryParse(
                await File.ReadAllTextAsync(readyPath, timeout.Token),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out workerProcessId);
        await parentHarness.WaitForExitAsync(timeout.Token);
        bool parentExited = parentHarness.ExitCode == 0;
        bool orphanExited = workerStarted
            && await WaitForProcessExitAsync(workerProcessId, timeout.Token);
        return new ThumbnailWorkerParentExitResult(
            workerStarted,
            parentExited,
            orphanExited);
    }

    private static ProcessStartInfo CreateParentExitHarnessStartInfo(
        string readyPath)
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The process path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add("--thumbnail-parent-exit-probe");
        startInfo.ArgumentList.Add(readyPath);
        return startInfo;
    }

    private static async Task<bool> WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static ThumbnailWorkerBudgetResult CreateBudget(
        ThumbnailWorkerStressResult stress,
        ThumbnailWorkerResourceResult resources)
    {
        const double maximumP95Milliseconds = 250;
        const double maximumTotalWallMilliseconds = 30_000;
        const double maximumIdleCpuMilliseconds = 50;
        const long maximumWorkingSetBytes = 256L * 1024 * 1024;
        const int maximumHandleCount = 512;

        bool withinBudget = stress.P95RoundTripMilliseconds <= maximumP95Milliseconds
            && stress.TotalWallMilliseconds <= maximumTotalWallMilliseconds
            && resources.IdleCpuMilliseconds <= maximumIdleCpuMilliseconds
            && resources.PeakWorkingSetBytes <= maximumWorkingSetBytes
            && resources.PeakHandleCount <= maximumHandleCount;

        return new ThumbnailWorkerBudgetResult(
            maximumP95Milliseconds,
            maximumTotalWallMilliseconds,
            maximumIdleCpuMilliseconds,
            maximumWorkingSetBytes,
            maximumHandleCount,
            withinBudget);
    }

    private static ThumbnailWorkerRequest ExtractRequest(
        string path,
        string requestId) =>
        new(
            ThumbnailWorkerServer.CurrentProtocolVersion,
            requestId,
            ThumbnailWorkerRequestKind.Extract,
            path,
            Size: 128,
            ShellItemImageFactoryFlags.ThumbnailOnly
                | ShellItemImageFactoryFlags.BiggerSizeOk);

    private static ThumbnailWorkerRequest FaultRequest(
        string requestId,
        ThumbnailWorkerRequestKind kind) =>
        new(
            ThumbnailWorkerServer.CurrentProtocolVersion,
            requestId,
            kind,
            Path: null,
            Size: 0,
            Flags: 0);

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.Order().ToArray();
        int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Max(index, 0)];
    }

    private static string CreateOwnedSandbox()
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetFullPath(Path.Combine(
            temporaryRoot,
            $"LongGrid-P0-03b-{Guid.NewGuid():N}"));
        if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The sandbox escaped the temporary root.");
        }

        return Directory.CreateDirectory(root).FullName;
    }

    private static void WriteOwnedBitmap(string path)
    {
        const int width = 2;
        const int height = 2;
        const int stride = 8;
        const int pixelBytes = stride * height;
        const int pixelOffset = 54;

        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(pixelOffset + pixelBytes);
        writer.Write(0);
        writer.Write(pixelOffset);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2_835);
        writer.Write(2_835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[]
        {
            0x00, 0x00, 0xFF,
            0x00, 0xFF, 0x00,
            0x00, 0x00,
            0xFF, 0x00, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00,
        });
    }

    private static bool TryDeleteOwnedSandbox(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
            return !Directory.Exists(root);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void PrintText(ThumbnailWorkerIsolationReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"Verdict: {report.Verdict}");
        Console.WriteLine(
            $"Stress: {report.Stress.Succeeded}/{report.Stress.Requested}; "
            + $"p95 {report.Stress.P95RoundTripMilliseconds:F2} ms");
        Console.WriteLine(
            $"Workers: {report.Stress.ProcessesStarted}; "
            + $"budget recycles {report.Stress.BudgetRecycles}; "
            + $"timeout kills {report.Resources.TimeoutKills}");
        Console.WriteLine(
            $"Hard timeout/recovery: {report.HardTimeout.TimedOut}/"
            + $"{report.HardTimeout.RecoverySucceeded}");
        Console.WriteLine(
            $"Consecutive timeout/backoff/recovery: "
            + $"{report.TimeoutBackoff.TimeoutsObserved}/"
            + $"{report.TimeoutBackoff.BackoffsApplied}/"
            + $"{report.TimeoutBackoff.RecoverySucceeded}");
        Console.WriteLine(
            $"Parent exit/orphan cleanup: {report.ParentExit.ParentExited}/"
            + $"{report.ParentExit.OrphanExited}");
        Console.WriteLine(
            $"Working set/handles: {report.Resources.PeakWorkingSetBytes}/"
            + $"{report.Resources.PeakHandleCount}");
        Console.WriteLine($"Within budget: {report.Budget.WithinProvisionalBudget}");
    }
}

internal sealed record ThumbnailWorkerIsolationReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool WarmupSucceeded,
    ThumbnailWorkerStressResult Stress,
    ThumbnailWorkerTimeoutResult HardTimeout,
    ThumbnailWorkerBackoffResult TimeoutBackoff,
    ThumbnailWorkerParentExitResult ParentExit,
    ThumbnailWorkerResilienceResult Resilience,
    ThumbnailWorkerResourceResult Resources,
    ThumbnailWorkerBudgetResult Budget,
    bool CleanupSucceeded,
    string Verdict,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);

internal sealed record ThumbnailWorkerStressResult(
    int Requested,
    int Succeeded,
    int Failed,
    double P50RoundTripMilliseconds,
    double P95RoundTripMilliseconds,
    double TotalWallMilliseconds,
    int ProcessesStarted,
    int BudgetRecycles);

internal sealed record ThumbnailWorkerTimeoutResult(
    bool TimedOut,
    bool WorkerKilled,
    bool RecoverySucceeded);

internal sealed record ThumbnailWorkerBackoffResult(
    int TimeoutsObserved,
    int MaximumConsecutiveTimeouts,
    int BackoffsApplied,
    double BackoffMilliseconds,
    bool RecoverySucceeded);

internal sealed record ThumbnailWorkerParentExitResult(
    bool WorkerStarted,
    bool ParentExited,
    bool OrphanExited);

internal sealed record ThumbnailWorkerResilienceResult(
    bool MalformedResponseDetected,
    bool MalformedResponseRecoverySucceeded,
    bool WrongVersionDetected,
    bool WrongVersionRecoverySucceeded,
    bool OversizedResponseDetected,
    bool OversizedResponseRecoverySucceeded,
    bool OversizedRequestDetected,
    bool OversizedRequestRecoverySucceeded,
    bool UnexpectedExitDetected,
    bool UnexpectedExitRecoverySucceeded,
    int ProtocolKills,
    int UnexpectedExits);

internal sealed record ThumbnailWorkerResourceResult(
    double IdleSampleMilliseconds,
    double IdleCpuMilliseconds,
    double TotalWorkerCpuMilliseconds,
    long PeakWorkingSetBytes,
    int PeakHandleCount,
    int TimeoutKills);

internal sealed record ThumbnailWorkerBudgetResult(
    double MaximumP95Milliseconds,
    double MaximumTotalWallMilliseconds,
    double MaximumIdleCpuMilliseconds,
    long MaximumWorkingSetBytes,
    int MaximumHandleCount,
    bool WithinProvisionalBudget);
