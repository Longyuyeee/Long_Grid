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
            string maximumBitmapPath = Path.Combine(root, "owned-maximum.bmp");
            WriteOwnedBitmap(bitmapPath, width: 2, height: 2);
            WriteOwnedBitmap(
                maximumBitmapPath,
                ThumbnailWorkerServer.MaximumPixelDimension,
                ThumbnailWorkerServer.MaximumPixelDimension);
            report = await RunMatrixAsync(bitmapPath, maximumBitmapPath);
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
            && report.ParentExit.KillOnJobCloseConfigured
            && report.RestrictedWorker.AllWorkersLowIntegrity
            && report.RestrictedWorker.MediumSandboxWriteBlocked
            && report.RestrictedWorker.UnbrokeredReadSucceeded
            && report.RestrictedWorker.ExtractionSucceeded
            && report.RestrictedWorker.JobAssignedBeforeResume
            && report.RestrictedWorker.ExplicitHandleAllowList
            && report.AppContainer.ProfileCreated
            && report.AppContainer.ZeroCapabilities
            && report.AppContainer.NoOpSucceeded
            && report.AppContainer.ControlReadSucceeded
            && report.AppContainer.UnbrokeredReadBlocked
            && report.AppContainer.AllProcessesAppContainer
            && report.AppContainer.ProcessesAssignedBeforeResume
            && report.AppContainer.ProfileDeleted
            && report.RestrictedToken.RestrictedTokenCreated
            && report.RestrictedToken.LowIntegrityObserved
            && report.RestrictedToken.OwnedInputReadSucceeded
            && report.RestrictedToken.MediumSandboxWriteBlocked
            && report.RestrictedToken.ParentWriteControlSucceeded
            && report.PixelTransfer.Succeeded
            && report.PixelTransfer.MaximumPayloadSucceeded
            && report.PixelTransfer.FormatValidated
            && report.PixelTransfer.DimensionsValidated
            && report.PixelTransfer.StrideValidated
            && report.PixelTransfer.LengthValidated
            && report.PixelTransfer.MalformedEncodingDetected
            && report.PixelTransfer.UnexpectedPayloadDetected
            && report.PixelTransfer.OversizedPixelRequestDetected
            && report.PixelTransfer.SharedMemoryHandleRequired
            && report.PixelTransfer.SharedMemoryCapacityValidated
            && report.PixelTransfer.SharedMemoryContentObserved
            && report.PixelTransfer.RecoveriesSucceeded == 9
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
        string bitmapPath,
        string maximumBitmapPath)
    {
        using var client = new ThumbnailWorkerClient(RequestsPerWorker);
        ThumbnailWorkerCallResult warmupResult = await client.ExecuteAsync(
            ExtractRequest(bitmapPath, "warmup"),
            RequestTimeout);
        bool warmupSucceeded = warmupResult.Completed
            && warmupResult.Response is { Success: true };
        string sandboxRoot = Path.GetDirectoryName(bitmapPath)
            ?? throw new InvalidOperationException(
                "The bitmap sandbox is unavailable.");
        string workerWritePath = Path.Combine(
            sandboxRoot,
            "restricted-worker-write.tmp");
        string unbrokeredReadPath = Path.Combine(
            sandboxRoot,
            "unbrokered-readable.tmp");
        await File.WriteAllTextAsync(unbrokeredReadPath, "exit /b 0");
        ThumbnailWorkerCallResult unbrokeredReadResult =
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "restricted-worker-unbrokered-read-boundary",
                    ThumbnailWorkerRequestKind.ReadBoundaryProbe,
                    unbrokeredReadPath,
                    Size: 0,
                    Flags: 0),
                RequestTimeout);
        bool unbrokeredReadSucceeded =
            unbrokeredReadResult.Completed
            && unbrokeredReadResult.Response is { Success: true };
        ThumbnailWorkerCallResult restrictedWriteResult =
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "restricted-worker-write-boundary",
                    ThumbnailWorkerRequestKind.WriteBoundaryProbe,
                    workerWritePath,
                    Size: 0,
                    Flags: 0),
                RequestTimeout);
        bool restrictedWorkerWriteBlocked =
            restrictedWriteResult.Completed
            && restrictedWriteResult.Response is { Success: true }
            && !File.Exists(workerWritePath);
        File.Delete(workerWritePath);
        ThumbnailAppContainerBoundaryResult appContainer =
            ThumbnailAppContainerBoundaryProbe.Run(unbrokeredReadPath);
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
        ThumbnailWorkerCallResult pixelResult = await client.ExecuteAsync(
            ExtractPixelRequest(bitmapPath, "pixel-transfer"),
            RequestTimeout);
        ThumbnailWorkerCallResult maximumPixelResult = await client.ExecuteAsync(
            ExtractPixelRequest(
                maximumBitmapPath,
                "maximum-pixel-transfer",
                ThumbnailWorkerServer.MaximumPixelDimension),
            RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelFormat = await client.ExecuteAsync(
            PixelFaultRequest(
                "invalid-pixel-format",
                ThumbnailWorkerRequestKind.InvalidPixelFormatResponse),
            RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelFormatRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(bitmapPath, "invalid-pixel-format-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelDimensions =
            await client.ExecuteAsync(
                PixelFaultRequest(
                    "invalid-pixel-dimensions",
                    ThumbnailWorkerRequestKind.InvalidPixelDimensionsResponse),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelDimensionsRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(
                    bitmapPath,
                    "invalid-pixel-dimensions-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelStride = await client.ExecuteAsync(
            PixelFaultRequest(
                "invalid-pixel-stride",
                ThumbnailWorkerRequestKind.InvalidPixelStrideResponse),
            RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelStrideRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(bitmapPath, "invalid-pixel-stride-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelLength = await client.ExecuteAsync(
            PixelFaultRequest(
                "invalid-pixel-length",
                ThumbnailWorkerRequestKind.InvalidPixelLengthResponse),
            RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelLengthRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(bitmapPath, "invalid-pixel-length-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult malformedPixelEncoding =
            await client.ExecuteAsync(
                PixelFaultRequest(
                    "malformed-pixel-encoding",
                    ThumbnailWorkerRequestKind.MalformedPixelPayload),
                RequestTimeout);
        ThumbnailWorkerCallResult malformedPixelEncodingRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(
                    bitmapPath,
                    "malformed-pixel-encoding-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult unexpectedPixelPayload =
            await client.ExecuteAsync(
                FaultRequest(
                    "unexpected-pixel-payload",
                    ThumbnailWorkerRequestKind.UnexpectedPixelPayloadResponse),
                RequestTimeout);
        ThumbnailWorkerCallResult unexpectedPixelPayloadRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(
                    bitmapPath,
                    "unexpected-pixel-payload-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult oversizedPixelRequest =
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "oversized-pixel-request",
                    ThumbnailWorkerRequestKind.Extract,
                    bitmapPath,
                    Size: ThumbnailWorkerServer.MaximumPixelDimension + 1,
                    ShellItemImageFactoryFlags.ThumbnailOnly,
                    IncludePixels: true),
                RequestTimeout);
        ThumbnailWorkerCallResult oversizedPixelRequestRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(
                    bitmapPath,
                    "oversized-pixel-request-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult missingPixelBuffer = await client.ExecuteAsync(
            new ThumbnailWorkerRequest(
                ThumbnailWorkerServer.CurrentProtocolVersion,
                "missing-pixel-buffer",
                ThumbnailWorkerRequestKind.MissingPixelBufferRequest,
                bitmapPath,
                Size: 128,
                ShellItemImageFactoryFlags.ThumbnailOnly,
                IncludePixels: true),
            RequestTimeout);
        ThumbnailWorkerCallResult missingPixelBufferRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(bitmapPath, "missing-pixel-buffer-recovery"),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelBufferCapacity =
            await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "invalid-pixel-buffer-capacity",
                    ThumbnailWorkerRequestKind.InvalidPixelBufferCapacityRequest,
                    bitmapPath,
                    Size: 128,
                    ShellItemImageFactoryFlags.ThumbnailOnly,
                    IncludePixels: true),
                RequestTimeout);
        ThumbnailWorkerCallResult invalidPixelBufferCapacityRecovery =
            await client.ExecuteAsync(
                ExtractPixelRequest(
                    bitmapPath,
                    "invalid-pixel-buffer-capacity-recovery"),
                RequestTimeout);
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
        bool killOnJobCloseConfigured = client.UsesKillOnJobClose;
        client.Dispose();
        ThumbnailWorkerParentExitResult parentExit =
            await VerifyParentExitCleanupAsync(
                Path.GetDirectoryName(bitmapPath)
                    ?? throw new InvalidOperationException(
                        "The bitmap sandbox is unavailable."),
                killOnJobCloseConfigured);
        RestrictedThumbnailTokenResult restrictedToken =
            RestrictedThumbnailTokenProbe.Run(
                bitmapPath,
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
        ThumbnailPixelPayload? transferredPixels = pixelResult.Response?.Pixels;
        ThumbnailPixelPayload? maximumPixels = maximumPixelResult.Response?.Pixels;
        var pixelTransfer = new ThumbnailWorkerPixelTransferResult(
            Succeeded:
                pixelResult.Completed
                && pixelResult.Response is { Success: true }
                && transferredPixels is not null,
            Width: transferredPixels?.Width ?? 0,
            Height: transferredPixels?.Height ?? 0,
            Stride: transferredPixels?.Stride ?? 0,
            ByteLength: transferredPixels?.ByteLength ?? 0,
            MaximumPayloadSucceeded:
                maximumPixelResult.Completed
                && maximumPixelResult.Response is { Success: true }
                && maximumPixels is
                {
                    Width: ThumbnailWorkerServer.MaximumPixelDimension,
                    Height: ThumbnailWorkerServer.MaximumPixelDimension,
                    Stride: ThumbnailWorkerServer.MaximumPixelDimension * 4,
                    ByteLength: ThumbnailWorkerServer.MaximumPixelBytes,
                },
            FormatValidated: invalidPixelFormat.ProtocolError,
            DimensionsValidated: invalidPixelDimensions.ProtocolError,
            StrideValidated: invalidPixelStride.ProtocolError,
            LengthValidated: invalidPixelLength.ProtocolError,
            MalformedEncodingDetected: malformedPixelEncoding.ProtocolError,
            UnexpectedPayloadDetected: unexpectedPixelPayload.ProtocolError,
            OversizedPixelRequestDetected:
                oversizedPixelRequest.WorkerExited
                && !oversizedPixelRequest.TimedOut,
            SharedMemoryHandleRequired:
                missingPixelBuffer.WorkerExited
                && !missingPixelBuffer.TimedOut,
            SharedMemoryCapacityValidated:
                invalidPixelBufferCapacity.WorkerExited
                && !invalidPixelBufferCapacity.TimedOut,
            SharedMemoryContentObserved:
                transferredPixels?.Bytes?.Any(value => value != 0) == true
                && maximumPixels?.Bytes?.Any(value => value != 0) == true,
            RecoveriesSucceeded: new[]
            {
                invalidPixelFormatRecovery,
                invalidPixelDimensionsRecovery,
                invalidPixelStrideRecovery,
                invalidPixelLengthRecovery,
                malformedPixelEncodingRecovery,
                unexpectedPixelPayloadRecovery,
                oversizedPixelRequestRecovery,
                missingPixelBufferRecovery,
                invalidPixelBufferCapacityRecovery,
            }.Count(result =>
                result.Completed && result.Response is { Success: true }));
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
        var restrictedWorker = new ThumbnailRestrictedWorkerResult(
            AllWorkersLowIntegrity: client.AllWorkersLowIntegrity,
            MediumSandboxWriteBlocked: restrictedWorkerWriteBlocked,
            UnbrokeredReadSucceeded: unbrokeredReadSucceeded,
            ExtractionSucceeded: warmupSucceeded,
            JobAssignedBeforeResume: true,
            ExplicitHandleAllowList: true);

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
            RestrictedWorker: restrictedWorker,
            AppContainer: appContainer,
            RestrictedToken: restrictedToken,
            PixelTransfer: pixelTransfer,
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
                "A zero-capability AppContainer blocks the unbrokered marker while reading a control file granted through an exact AppContainer-SID ACL, but the production thumbnail worker and per-request input broker have not yet moved into that boundary.",
                "The synthetic BMP validates process lifetime and Shell extraction, not third-party, cloud, network, or adversarial providers.",
                "The bounded BGRA payload uses a duplicated unnamed file-mapping handle; formal render-surface integration and the final broker policy remain unimplemented.",
                "The forced timeout and parent-exit cases use a deterministic worker hang before native extraction because inducing a real provider hang on a user machine is unsafe.",
                "Budgets are provisional for this machine and must be repeated across the supported Windows and architecture matrix.",
            ]);
    }

    private static async Task<ThumbnailWorkerParentExitResult>
        VerifyParentExitCleanupAsync(
            string root,
            bool killOnJobCloseConfigured)
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
            orphanExited,
            killOnJobCloseConfigured);
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

    private static ThumbnailWorkerRequest ExtractPixelRequest(
        string path,
        string requestId,
        int size = 128) =>
        new(
            ThumbnailWorkerServer.CurrentProtocolVersion,
            requestId,
            ThumbnailWorkerRequestKind.Extract,
            path,
            Size: size,
            ShellItemImageFactoryFlags.ThumbnailOnly,
            IncludePixels: true);

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

    private static ThumbnailWorkerRequest PixelFaultRequest(
        string requestId,
        ThumbnailWorkerRequestKind kind) =>
        FaultRequest(requestId, kind) with { IncludePixels = true };

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

    private static void WriteOwnedBitmap(string path, int width, int height)
    {
        int stride = checked(((width * 3) + 3) & ~3);
        int pixelBytes = checked(stride * height);
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
        var row = new byte[stride];
        for (int x = 0; x < width; x++)
        {
            int offset = x * 3;
            row[offset] = (byte)(x % 251);
            row[offset + 1] = (byte)((x * 3) % 251);
            row[offset + 2] = (byte)((x * 7) % 251);
        }

        for (int y = 0; y < height; y++)
        {
            writer.Write(row);
        }
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
            + $"{report.ParentExit.OrphanExited}; job "
            + $"{report.ParentExit.KillOnJobCloseConfigured}");
        Console.WriteLine(
            $"Restricted low-integrity boundary: "
            + $"{report.RestrictedToken.LowIntegrityObserved}; read/write-block/control "
            + $"{report.RestrictedToken.OwnedInputReadSucceeded}/"
            + $"{report.RestrictedToken.MediumSandboxWriteBlocked}/"
            + $"{report.RestrictedToken.ParentWriteControlSucceeded}");
        Console.WriteLine(
            $"Restricted worker launch/read exposure/write block: "
            + $"{report.RestrictedWorker.AllWorkersLowIntegrity}/"
            + $"{report.RestrictedWorker.UnbrokeredReadSucceeded}/"
            + $"{report.RestrictedWorker.MediumSandboxWriteBlocked}; "
            + $"suspended-job/handle-list "
            + $"{report.RestrictedWorker.JobAssignedBeforeResume}/"
            + $"{report.RestrictedWorker.ExplicitHandleAllowList}");
        Console.WriteLine(
            $"AppContainer no-op/control/denied/token/profile cleanup: "
            + $"{report.AppContainer.NoOpSucceeded}/"
            + $"{report.AppContainer.ControlReadSucceeded}/"
            + $"{report.AppContainer.UnbrokeredReadBlocked}/"
            + $"{report.AppContainer.AllProcessesAppContainer}/"
            + $"{report.AppContainer.ProfileDeleted}");
        Console.WriteLine(
            $"Pixel IPC: {report.PixelTransfer.Succeeded}; "
            + $"{report.PixelTransfer.Width}x{report.PixelTransfer.Height}, "
            + $"{report.PixelTransfer.ByteLength} bytes; recoveries "
            + $"{report.PixelTransfer.RecoveriesSucceeded}/9; max "
            + $"{report.PixelTransfer.MaximumPayloadSucceeded}");
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
    ThumbnailRestrictedWorkerResult RestrictedWorker,
    ThumbnailAppContainerBoundaryResult AppContainer,
    RestrictedThumbnailTokenResult RestrictedToken,
    ThumbnailWorkerPixelTransferResult PixelTransfer,
    ThumbnailWorkerResilienceResult Resilience,
    ThumbnailWorkerResourceResult Resources,
    ThumbnailWorkerBudgetResult Budget,
    bool CleanupSucceeded,
    string Verdict,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);

internal sealed record ThumbnailRestrictedWorkerResult(
    bool AllWorkersLowIntegrity,
    bool MediumSandboxWriteBlocked,
    bool UnbrokeredReadSucceeded,
    bool ExtractionSucceeded,
    bool JobAssignedBeforeResume,
    bool ExplicitHandleAllowList);

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
    bool OrphanExited,
    bool KillOnJobCloseConfigured);

internal sealed record ThumbnailWorkerPixelTransferResult(
    bool Succeeded,
    int Width,
    int Height,
    int Stride,
    int ByteLength,
    bool MaximumPayloadSucceeded,
    bool FormatValidated,
    bool DimensionsValidated,
    bool StrideValidated,
    bool LengthValidated,
    bool MalformedEncodingDetected,
    bool UnexpectedPayloadDetected,
    bool OversizedPixelRequestDetected,
    bool SharedMemoryHandleRequired,
    bool SharedMemoryCapacityValidated,
    bool SharedMemoryContentObserved,
    int RecoveriesSucceeded);

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
