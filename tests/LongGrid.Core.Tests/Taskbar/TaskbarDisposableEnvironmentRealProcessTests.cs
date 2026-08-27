using System.Diagnostics;
using System.Text.Json;

namespace LongGrid.Core.Tests.Taskbar;

public sealed class TaskbarDisposableEnvironmentRealProcessTests
{
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
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
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

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start disposable environment preflight.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return new(process.ExitCode, output.Trim());
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

    private sealed record ProcessResult(int ExitCode, string Output);
}
