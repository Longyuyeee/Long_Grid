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

        TaskbarPresetAvailability availability =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                result.Report,
                TaskbarNativeAdapterAvailability.Unavailable,
                recoveryPending: false);
        Assert.Equal(
            TaskbarPresetAvailabilityStatus.BuildNotCertified,
            availability.Status);
        Assert.False(availability.ClearEnabled);
        Assert.False(availability.RestoreSystemDefaultEnabled);
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
        await using TaskbarReadyParentProcess parent =
            await TaskbarReadyParentProcess.StartAsync();
        Task<TaskbarCompatibilityClientResult> recovery =
            TaskbarCompatibilityClient.ProbeAsync(
                TimeSpan.FromSeconds(4),
                TaskbarWorkerEvidenceFault.Hang,
                parent.Id);
        await parent.ReleaseAsync();
        TaskbarCompatibilityClientResult result = await recovery;

        Assert.Equal(TaskbarCompatibilityClientStatus.WorkerExited, result.Status);
        Assert.Null(result.Report);
        Assert.Equal("WorkerExit72", result.DiagnosticCode);
    }
}
