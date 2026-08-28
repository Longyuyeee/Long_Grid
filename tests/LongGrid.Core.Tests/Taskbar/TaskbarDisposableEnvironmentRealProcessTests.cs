using System.Diagnostics;
using System.Text.Json;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarDisposableEnvironmentRealProcessTests
{
    private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessCleanupTimeout =
        TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RealHostPreflightProducesFiniteFailClosedEvidence()
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "LongGrid-TaskbarSandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string configurationPath = Path.Combine(
                temporaryDirectory,
                "LongGrid-Taskbar-R2B1.wsb");
            string evidenceDirectory = Path.Combine(
                temporaryDirectory,
                "evidence");
            ProcessResult result = await RunPreflightAsync(
                configurationPath,
                evidenceDirectory,
                prepareConfiguration: true,
                requireReady: false);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "TaskbarR2B1DisposableEnvironmentAdmission",
                root.GetProperty("purpose").GetString());
            string? outcome = root.GetProperty("outcome").GetString();
            Assert.True(outcome is "ReadyToLaunch" or "Blocked");

            JsonElement actual = root.GetProperty("actual");
            Assert.True(actual.GetProperty("windows").GetBoolean());
            Assert.False(
                actual.GetProperty("modifiedSystemState").GetBoolean());
            Assert.False(actual.GetProperty("mutationAllowed").GetBoolean());
            JsonElement configuration = actual.GetProperty("configuration");
            Assert.True(configuration.GetProperty("present").GetBoolean());
            Assert.True(
                configuration.GetProperty("networkDisabled").GetBoolean());
            Assert.True(configuration.GetProperty("sourceReadOnly").GetBoolean());
            Assert.True(
                configuration.GetProperty("evidenceWriteOnly").GetBoolean());
            Assert.True(
                configuration.GetProperty("clipboardDisabled").GetBoolean());
            Assert.True(configuration
                .GetProperty("peripheralRedirectionDisabled")
                .GetBoolean());
            Assert.True(
                configuration.GetProperty("boundedGuestCommand").GetBoolean());
            Assert.True(File.Exists(configurationPath));

            bool ready = string.Equals(
                outcome,
                "ReadyToLaunch",
                StringComparison.Ordinal);
            ProcessResult required = await RunPreflightAsync(
                configurationPath,
                evidenceDirectory,
                prepareConfiguration: false,
                requireReady: true);
            Assert.Equal(ready ? 0 : 2, required.ExitCode);
            Assert.Empty(required.Error);
            using JsonDocument requiredDocument = JsonDocument.Parse(
                required.Output);
            Assert.Equal(
                root.GetProperty("outcome").GetString(),
                requiredDocument.RootElement
                    .GetProperty("outcome")
                    .GetString());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RealProcessTimeoutTerminatesHangingChild()
    {
        ProcessStartInfo startInfo = CreatePowerShellStartInfo();
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 60");

        RealProcessTimeoutException exception =
            await Assert.ThrowsAsync<RealProcessTimeoutException>(() =>
                RunProcessAsync(
                    startInfo,
                    TimeSpan.FromMilliseconds(500),
                    "controlled hanging child"));

        Assert.Equal("controlled hanging child", exception.Purpose);
        Assert.True(exception.ProcessId > 0);
        Assert.False(IsProcessRunning(exception.ProcessId));
    }

    private static async Task<ProcessResult> RunPreflightAsync(
        string configurationPath,
        string evidenceDirectory,
        bool prepareConfiguration,
        bool requireReady)
    {
        string scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "Test-LongGridTaskbarDisposableEnvironment.ps1");
        ProcessStartInfo startInfo = CreatePowerShellStartInfo();
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ConfigurationPath");
        startInfo.ArgumentList.Add(configurationPath);
        startInfo.ArgumentList.Add("-EvidenceDirectory");
        startInfo.ArgumentList.Add(evidenceDirectory);
        if (prepareConfiguration)
        {
            startInfo.ArgumentList.Add("-PrepareConfiguration");
        }

        if (requireReady)
        {
            startInfo.ArgumentList.Add("-RequireReady");
        }

        return await RunProcessAsync(
            startInfo,
            PreflightTimeout,
            "disposable environment preflight");
    }

    private static ProcessStartInfo CreatePowerShellStartInfo()
    {
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        return startInfo;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string purpose)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {purpose}.");
        int processId = process.Id;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            using CancellationTokenSource cleanupTimeoutSource =
                new(ProcessCleanupTimeout);
            try
            {
                await process.WaitForExitAsync(cleanupTimeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"{purpose} process {processId} did not terminate within "
                    + $"{ProcessCleanupTimeout.TotalMilliseconds:F0} ms.");
            }

            string timedOutOutput = await output;
            string timedOutError = await error;
            throw new RealProcessTimeoutException(
                purpose,
                processId,
                timeout,
                timedOutOutput.Length,
                timedOutError.Length);
        }

        return new(
            process.ExitCode,
            (await output).Trim(),
            (await error).Trim());
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            // The test-owned process may already have exited.
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongGrid.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "LongGrid repository root was not found.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class RealProcessTimeoutException : TimeoutException
    {
        public RealProcessTimeoutException(
            string purpose,
            int processId,
            TimeSpan timeout,
            int outputLength,
            int errorLength)
            : base(
                $"{purpose} process {processId} exceeded "
                + $"{timeout.TotalMilliseconds:F0} ms; "
                + $"stdoutLength={outputLength}; stderrLength={errorLength}.")
        {
            Purpose = purpose;
            ProcessId = processId;
        }

        public string Purpose { get; }

        public int ProcessId { get; }
    }
}
