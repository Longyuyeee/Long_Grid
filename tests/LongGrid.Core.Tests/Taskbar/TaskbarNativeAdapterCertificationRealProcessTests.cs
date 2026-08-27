using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarNativeAdapterCertificationRealProcessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task RealCertificationEntryRequiresEvidenceSwitch()
    {
        ProcessResult result = await RunWorkerAsync(evidenceEnabled: false);

        Assert.Equal(65, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public async Task RealCertificationEntryReportsDefaultUnavailableWithoutMutation()
    {
        TaskbarCompatibilityClientResult before =
            await TaskbarCompatibilityClient.ProbeAsync(TimeSpan.FromSeconds(3));
        ProcessResult result = await RunWorkerAsync(evidenceEnabled: true);
        TaskbarCompatibilityClientResult after =
            await TaskbarCompatibilityClient.ProbeAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, result.ExitCode);
        TaskbarNativeAdapterCertificationResponse? response =
            JsonSerializer.Deserialize<TaskbarNativeAdapterCertificationResponse>(
                result.Output,
                JsonOptions);
        Assert.NotNull(response);
        Assert.Equal(
            TaskbarWorkerProtocol.CurrentVersion,
            response.ProtocolVersion);
        Assert.Equal(
            TaskbarWorkerProtocol.NativeAdapterCertificationPurpose,
            response.Purpose);
        Assert.Equal(result.RequestId, response.RequestId);
        Assert.Equal(
            TaskbarNativeAdapterAvailability.Unavailable,
            response.AdapterAvailability);
        Assert.Equal("None", response.AdapterId);
        Assert.False(response.ModifiedSystemState);
        Assert.False(response.Report.Actual.ModifiedSystemState);
        Assert.Equal(
            TaskbarRuntimeAdmission.DeniedNoCertifiedBuild,
            response.Report.RuntimeAdmission);

        Assert.True(before.IsCompleted);
        Assert.True(after.IsCompleted);
        Assert.False(before.Report!.Actual.ModifiedSystemState);
        Assert.False(after.Report!.Actual.ModifiedSystemState);
        Assert.Equal(
            CaptureWindowIdentity(before.Report),
            CaptureWindowIdentity(after.Report));
    }

    private static async Task<ProcessResult> RunWorkerAsync(bool evidenceEnabled)
    {
        string requestId = Guid.NewGuid().ToString("N");
        ProcessStartInfo startInfo = new(Path.Combine(
            AppContext.BaseDirectory,
            "LongGrid.TaskbarWorker.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--native-adapter-certification");
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--request-id");
        startInfo.ArgumentList.Add(requestId);
        if (evidenceEnabled)
        {
            startInfo.Environment["LONGGRID_TASKBAR_WORKER_EVIDENCE"] = "1";
        }

        using Process worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start formal taskbar worker.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        string output = await worker.StandardOutput.ReadToEndAsync(
            timeout.Token);
        await worker.WaitForExitAsync(timeout.Token);
        return new(worker.ExitCode, output.Trim(), requestId);
    }

    private static string CaptureWindowIdentity(
        TaskbarCompatibilityReport report) => string.Join(
            "|",
            report.Actual.TaskbarWindows
                .OrderBy(window => window.WindowClass, StringComparer.Ordinal)
                .Select(window =>
                    $"{window.Handle}:{window.WindowClass}:{window.ProcessId}"));

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string RequestId);
}
