using System.Diagnostics;
using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarCompatibilityClientRealProcessTests
{
    [Fact]
    public void FormalWorkerRuntimeIsDeployedBesideClient()
    {
        string[] required =
        [
            "LongGrid.TaskbarWorker.exe",
            "LongGrid.TaskbarWorker.dll",
            "LongGrid.TaskbarWorker.deps.json",
            "LongGrid.TaskbarWorker.runtimeconfig.json",
        ];

        Assert.All(required, fileName => Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, fileName)),
            $"Required formal taskbar worker file was missing: {fileName}"));
    }

    [Fact]
    public async Task RealWorkerReturnsBoundedReadOnlyReport()
    {
        TaskbarCompatibilityClientResult result =
            await TaskbarCompatibilityClient.ProbeAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(TaskbarCompatibilityClientStatus.Completed, result.Status);
        Assert.NotNull(result.Report);
        Assert.False(result.Report.Actual.ModifiedSystemState);
        Assert.NotEqual(TaskbarRuntimeAdmission.Allowed, result.Report.RuntimeAdmission);
        Assert.Equal("None", result.DiagnosticCode);
    }

    [Theory]
    [InlineData(
        "Hang",
        TaskbarCompatibilityClientStatus.TimedOut,
        "WorkerTimeout")]
    [InlineData(
        "Exit",
        TaskbarCompatibilityClientStatus.WorkerExited,
        "WorkerExit71")]
    [InlineData(
        "Malformed",
        TaskbarCompatibilityClientStatus.ProtocolError,
        "MalformedResponse")]
    [InlineData(
        "WrongVersion",
        TaskbarCompatibilityClientStatus.ProtocolError,
        "ProtocolVersionMismatch")]
    [InlineData(
        "Oversized",
        TaskbarCompatibilityClientStatus.ProtocolError,
        "ResponseTooLarge")]
    public async Task RealWorkerFailsClosedForBoundedFaults(
        string faultName,
        TaskbarCompatibilityClientStatus expectedStatus,
        string expectedDiagnostic)
    {
        TaskbarWorkerEvidenceFault fault = Enum.Parse<TaskbarWorkerEvidenceFault>(
            faultName);
        TimeSpan timeout = fault == TaskbarWorkerEvidenceFault.Hang
            ? TimeSpan.FromMilliseconds(350)
            : TimeSpan.FromSeconds(3);

        TaskbarCompatibilityClientResult result =
            await TaskbarCompatibilityClient.ProbeAsync(
                timeout,
                fault,
                Environment.ProcessId);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Report);
        Assert.Equal(expectedDiagnostic, result.DiagnosticCode);
    }

    [Fact]
    public async Task RealWorkerExitsWhenBoundParentExits()
    {
        using Process parent = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command Start-Sleep -Milliseconds 500")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start parent process.");

        TaskbarCompatibilityClientResult result =
            await TaskbarCompatibilityClient.ProbeAsync(
                TimeSpan.FromSeconds(4),
                TaskbarWorkerEvidenceFault.Hang,
                parent.Id);

        Assert.Equal(TaskbarCompatibilityClientStatus.WorkerExited, result.Status);
        Assert.Null(result.Report);
        Assert.Equal("WorkerExit72", result.DiagnosticCode);
    }
}
