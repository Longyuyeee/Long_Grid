using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.Taskbar;

namespace LongGrid.Infrastructure.Taskbar;

public enum TaskbarCompatibilityClientStatus
{
    Completed,
    TimedOut,
    WorkerExited,
    ProtocolError,
    StartFailed,
}

public sealed record TaskbarCompatibilityClientResult(
    TaskbarCompatibilityClientStatus Status,
    TaskbarCompatibilityReport? Report,
    string DiagnosticCode)
{
    public bool IsCompleted =>
        Status == TaskbarCompatibilityClientStatus.Completed && Report is not null;
}

internal enum TaskbarWorkerEvidenceFault
{
    None,
    Hang,
    Exit,
    Malformed,
    WrongVersion,
    Oversized,
}

public static class TaskbarCompatibilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<TaskbarCompatibilityClientResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ProbeAsync(
            timeout,
            TaskbarWorkerEvidenceFault.None,
            Environment.ProcessId,
            cancellationToken);

    internal static async Task<TaskbarCompatibilityClientResult> ProbeAsync(
        TimeSpan timeout,
        TaskbarWorkerEvidenceFault evidenceFault,
        int parentProcessId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);

        string requestId = Guid.NewGuid().ToString("N");
        ProcessStartInfo startInfo = CreateStartInfo(
            requestId,
            parentProcessId,
            evidenceFault);
        using Process worker = new() { StartInfo = startInfo };
        try
        {
            if (!worker.Start())
            {
                return new(
                    TaskbarCompatibilityClientStatus.StartFailed,
                    null,
                    "WorkerStartReturnedFalse");
            }
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException)
        {
            return new(
                TaskbarCompatibilityClientStatus.StartFailed,
                null,
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
                TaskbarWorkerProtocol.MaximumResponseCharacters,
                linkedSource.Token).ConfigureAwait(false);
            await worker.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);

            if (worker.ExitCode is not (0 or 1))
            {
                return new(
                    TaskbarCompatibilityClientStatus.WorkerExited,
                    null,
                    $"WorkerExit{worker.ExitCode}");
            }

            return ValidateResponse(output, requestId);
        }
        catch (ResponseTooLargeException)
        {
            TryTerminate(worker);
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "ResponseTooLarge");
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            TryTerminate(worker);
            return new(
                TaskbarCompatibilityClientStatus.TimedOut,
                null,
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
            return new(
                TaskbarCompatibilityClientStatus.WorkerExited,
                null,
                "WorkerOutputClosed");
        }
    }

    internal static string ResolveWorkerPath()
    {
        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return Path.Combine(
            AppContext.BaseDirectory,
            $"LongGrid.TaskbarWorker{extension}");
    }

    private static ProcessStartInfo CreateStartInfo(
        string requestId,
        int parentProcessId,
        TaskbarWorkerEvidenceFault evidenceFault)
    {
        ProcessStartInfo startInfo = new(ResolveWorkerPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--taskbar-worker");
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--request-id");
        startInfo.ArgumentList.Add(requestId);
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
            startInfo.Environment["LONGGRID_TASKBAR_WORKER_EVIDENCE"] = "1";
        }

        return startInfo;
    }

    private static async Task<string> ReadBoundedOutputAsync(
        TextReader reader,
        int maximumCharacters,
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

            if (output.Length + count > maximumCharacters)
            {
                throw new ResponseTooLargeException();
            }

            output.Append(buffer, 0, count);
        }

        return output.ToString().TrimEnd('\r', '\n');
    }

    private static TaskbarCompatibilityClientResult ValidateResponse(
        string output,
        string requestId)
    {
        TaskbarWorkerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<TaskbarWorkerResponse>(
                output,
                JsonOptions);
        }
        catch (JsonException)
        {
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "MalformedResponse");
        }

        if (response is null)
        {
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "EmptyResponse");
        }

        if (response.ProtocolVersion != TaskbarWorkerProtocol.CurrentVersion)
        {
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "ProtocolVersionMismatch");
        }

        if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
        {
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "RequestIdMismatch");
        }

        TaskbarCompatibilityReport? report = response.Report;
        if (report is null
            || report.Expected is null
            || report.Actual is null
            || report.Expected.ConflictingProcesses is null
            || report.Actual.TaskbarWindows is null
            || report.Actual.ConflictingProcesses is null
            || report.Difference is null
            || report.SchemaVersion != TaskbarWorkerProtocol.CurrentVersion
            || !string.Equals(
                report.Purpose,
                TaskbarWorkerProtocol.ProbePurpose,
                StringComparison.Ordinal)
            || report.Actual.ModifiedSystemState)
        {
            return new(
                TaskbarCompatibilityClientStatus.ProtocolError,
                null,
                "InvalidProbeReport");
        }

        return new(
            TaskbarCompatibilityClientStatus.Completed,
            report,
            "None");
    }

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
