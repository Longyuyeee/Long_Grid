using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.Taskbar;

namespace LongGrid.Infrastructure.Taskbar;

public enum TaskbarStartupRecoveryClientStatus
{
    Completed,
    TimedOut,
    WorkerExited,
    ProtocolError,
    StartFailed,
}

public sealed record TaskbarStartupRecoveryClientResult(
    TaskbarStartupRecoveryClientStatus Status,
    TaskbarStartupRecoveryWorkerResponse? Response,
    string DiagnosticCode)
{
    public bool IsCompleted =>
        Status == TaskbarStartupRecoveryClientStatus.Completed
        && Response is not null;
}

public static class TaskbarAppearanceRecoveryClient
{
    private const string EvidenceEnvironmentVariable =
        "LONGGRID_TASKBAR_WORKER_EVIDENCE";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<TaskbarStartupRecoveryClientResult> RecoverAtStartupAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RecoverAtStartupAsync(
            timeout,
            directoryPath: null,
            TaskbarWorkerEvidenceFault.None,
            Environment.ProcessId,
            workerPath: null,
            cancellationToken);

    internal static async Task<TaskbarStartupRecoveryClientResult>
        RecoverAtStartupAsync(
            TimeSpan timeout,
            string? directoryPath,
            TaskbarWorkerEvidenceFault evidenceFault,
            int parentProcessId,
            string? workerPath = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentProcessId);
        if (directoryPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        }

        string requestId = Guid.NewGuid().ToString("N");
        ProcessStartInfo startInfo = CreateStartInfo(
            requestId,
            parentProcessId,
            directoryPath,
            evidenceFault,
            workerPath);
        using Process worker = new() { StartInfo = startInfo };
        try
        {
            if (!worker.Start())
            {
                return Failure(
                    TaskbarStartupRecoveryClientStatus.StartFailed,
                    "WorkerStartReturnedFalse");
            }
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException)
        {
            return Failure(
                TaskbarStartupRecoveryClientStatus.StartFailed,
                "WorkerStartFailed");
        }

        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                timeoutSource.Token,
                cancellationToken);
        try
        {
            string output = await ReadBoundedOutputAsync(
                worker.StandardOutput,
                linkedSource.Token).ConfigureAwait(false);
            await worker.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            if (worker.ExitCode is not (0 or 1))
            {
                return Failure(
                    TaskbarStartupRecoveryClientStatus.WorkerExited,
                    $"WorkerExit{worker.ExitCode}");
            }

            return ValidateResponse(output, requestId);
        }
        catch (ResponseTooLargeException)
        {
            TryTerminate(worker);
            return Failure(
                TaskbarStartupRecoveryClientStatus.ProtocolError,
                "ResponseTooLarge");
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            TryTerminate(worker);
            return Failure(
                TaskbarStartupRecoveryClientStatus.TimedOut,
                "WorkerTimeout");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(worker);
            throw;
        }
        catch (IOException)
        {
            TryTerminate(worker);
            return Failure(
                TaskbarStartupRecoveryClientStatus.WorkerExited,
                "WorkerOutputClosed");
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string requestId,
        int parentProcessId,
        string? directoryPath,
        TaskbarWorkerEvidenceFault evidenceFault,
        string? workerPath)
    {
        ProcessStartInfo startInfo = new(
            workerPath ?? TaskbarCompatibilityClient.ResolveWorkerPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--startup-recovery");
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--request-id");
        startInfo.ArgumentList.Add(requestId);
        if (directoryPath is not null)
        {
            startInfo.ArgumentList.Add("--evidence-directory");
            startInfo.ArgumentList.Add(directoryPath);
            startInfo.Environment[EvidenceEnvironmentVariable] = "1";
        }

        if (evidenceFault != TaskbarWorkerEvidenceFault.None)
        {
            startInfo.ArgumentList.Add("--evidence-fault");
            startInfo.ArgumentList.Add(evidenceFault switch
            {
                TaskbarWorkerEvidenceFault.Hang => "hang",
                TaskbarWorkerEvidenceFault.Exit => "exit",
                TaskbarWorkerEvidenceFault.Malformed => "malformed",
                TaskbarWorkerEvidenceFault.WrongVersion => "wrong-version",
                TaskbarWorkerEvidenceFault.Oversized => "oversized",
                _ => throw new ArgumentOutOfRangeException(nameof(evidenceFault)),
            });
            startInfo.Environment[EvidenceEnvironmentVariable] = "1";
        }

        return startInfo;
    }

    private static async Task<string> ReadBoundedOutputAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        char[] buffer = new char[1024];
        while (true)
        {
            int count = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            if (output.Length + count
                > TaskbarWorkerProtocol.MaximumResponseCharacters)
            {
                throw new ResponseTooLargeException();
            }

            output.Append(buffer, 0, count);
        }

        return output.ToString().TrimEnd('\r', '\n');
    }

    private static TaskbarStartupRecoveryClientResult ValidateResponse(
        string output,
        string requestId)
    {
        TaskbarStartupRecoveryWorkerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<
                TaskbarStartupRecoveryWorkerResponse>(output, JsonOptions);
        }
        catch (JsonException)
        {
            return Failure(
                TaskbarStartupRecoveryClientStatus.ProtocolError,
                "MalformedResponse");
        }

        if (response is null
            || response.ProtocolVersion != TaskbarWorkerProtocol.CurrentVersion
            || !string.Equals(
                response.Purpose,
                TaskbarWorkerProtocol.StartupRecoveryPurpose,
                StringComparison.Ordinal)
            || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal)
            || response.ModifiedSystemState
            || response.Status == TaskbarStartupRecoveryStatus.NoRecoveryRequired
                && (response.JournalPreserved
                    || response.RecoveryPhase is not null
                    || response.Report is not null)
            || response.Status != TaskbarStartupRecoveryStatus.NoRecoveryRequired
                && !response.JournalPreserved
            || response.Report?.Actual.ModifiedSystemState == true)
        {
            return Failure(
                TaskbarStartupRecoveryClientStatus.ProtocolError,
                "InvalidRecoveryResponse");
        }

        return new(
            TaskbarStartupRecoveryClientStatus.Completed,
            response,
            "None");
    }

    private static TaskbarStartupRecoveryClientResult Failure(
        TaskbarStartupRecoveryClientStatus status,
        string diagnosticCode) => new(status, null, diagnosticCode);

    private static void TryTerminate(Process worker)
    {
        try
        {
            if (!worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
                worker.WaitForExit(2000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            // The worker may have already exited while the client was failing closed.
        }
    }

    private sealed class ResponseTooLargeException : Exception;
}
