using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongGrid.Core.Tests.Runtime;

public sealed class DotNetHostResolutionRealProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void EngineeringScriptsDoNotInvokePathSelectedDotNetDirectly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string engineeringDirectory = Path.Combine(repositoryRoot, "eng");
        Regex directDotNetInvocation = new(
            @"^\s*(?:&\s+)?dotnet(?:\.exe)?(?:\s|$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Regex pathOnlyLookup = new(
            @"Get-Command\s+dotnet(?:\.exe)?(?:\s|$)",
            RegexOptions.IgnoreCase);

        string[] violations = Directory
            .EnumerateFiles(engineeringDirectory, "*.ps1")
            .Where(path => !path.EndsWith(
                "LongGrid.DotNetHost.ps1",
                StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return directDotNetInvocation.IsMatch(source)
                    || pathOnlyLookup.IsMatch(source);
            })
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void EngineeringScriptsDoNotRequirePowerShell7ProcessTreeKill()
    {
        string repositoryRoot = FindRepositoryRoot();
        string engineeringDirectory = Path.Combine(repositoryRoot, "eng");
        Regex processTreeKill = new(
            @"\.Kill\(\$true\)",
            RegexOptions.IgnoreCase);

        string[] violations = Directory
            .EnumerateFiles(engineeringDirectory, "*.ps1")
            .Where(path => processTreeKill.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductEvidenceLaunchersRequireManagedReadinessOrDetectHostFailure()
    {
        string repositoryRoot = FindRepositoryRoot();
        string m1Launcher = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "eng",
            "Start-LongGridM1ManualEvidenceSession.ps1"));
        string boxLauncher = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "eng",
            "Test-LongGridBoxR1Activation.ps1"));

        Assert.Contains("'AppConstructed'", m1Launcher, StringComparison.Ordinal);
        Assert.Contains(
            "managed launch did not become ready",
            m1Launcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "OwnedProcess.MainWindowTitle",
            boxLauncher,
            StringComparison.Ordinal);
        Assert.Contains(
            "LongGrid.App host startup failed",
            boxLauncher,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupValidateOnlyIgnoresEarlierPathHostWithoutCompatibleSdk()
    {
        string? compatibleHost = GetCompatibleHost();
        if (compatibleHost is null)
        {
            return;
        }

        ProcessResult result = await RunWithPoisonedPathAsync(
            compatibleHost,
            "Start-LongGrid.ps1",
            "-Configuration",
            "Release",
            "-NoRestore",
            "-NoBuild",
            "-ValidateOnly");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "Long Grid startup chain validation passed: Release / x64",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"dotnet={compatibleHost}",
            result.Output,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "Pack-LongGrid.ps1",
        "packageType",
        "portable-unpacked-zip")]
    [InlineData(
        "Build-LongGridReleaseCandidate.ps1",
        "candidateType",
        "internal-unsigned-developer-preview")]
    [InlineData("New-LongGridSbom.ps1", "manifestFormat", "SPDX:2.2")]
    [InlineData(
        "Test-LongGridDependencyLicenses.ps1",
        "scope",
        "all-solution-projects-restored-assets")]
    public async Task PackagingValidateOnlyUsesCompatibleSdkDespitePoisonedPath(
        string scriptName,
        string expectedProperty,
        string expectedValue)
    {
        string? compatibleHost = GetCompatibleHost();
        if (compatibleHost is null)
        {
            return;
        }

        ProcessResult result = await RunWithPoisonedPathAsync(
            compatibleHost,
            scriptName,
            "-ValidateOnly");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal("Pass", root.GetProperty("outcome").GetString());
        Assert.Equal(
            compatibleHost,
            root.GetProperty("dotnetHost").GetString(),
            ignoreCase: true);
        Assert.Equal(
            expectedValue,
            root.GetProperty(expectedProperty).GetString());
    }

    [Fact]
    public async Task VulnerabilityGateUsesCompatibleSdkDespitePoisonedPath()
    {
        string? compatibleHost = GetCompatibleHost();
        if (compatibleHost is null)
        {
            return;
        }

        ProcessResult result = await RunWithPoisonedPathAsync(
            compatibleHost,
            "Verify-VulnerablePackages.ps1");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "Package vulnerability gate passed: no known vulnerable packages.",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"dotnet={compatibleHost}",
            result.Output,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetCompatibleHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static async Task<ProcessResult> RunWithPoisonedPathAsync(
        string compatibleHost,
        string scriptName,
        params string[] arguments)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "LongGrid-DotNetHost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "dotnet.cmd"),
                "@echo off\r\nexit /b 37\r\n");

            string repositoryRoot = FindRepositoryRoot();
            ProcessStartInfo startInfo = new(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = repositoryRoot,
            };
            startInfo.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                temporaryDirectory,
                Environment.GetEnvironmentVariable("PATH"));
            startInfo.Environment["ProgramW6432"] = Path.GetDirectoryName(
                Path.GetDirectoryName(compatibleHost))!;
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(
                repositoryRoot,
                "eng",
                scriptName));
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"Failed to start {scriptName} validation.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeoutSource = new(ProcessTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                throw new TimeoutException(
                    $"{scriptName} validation did not finish within "
                    + $"{ProcessTimeout.TotalSeconds:F0} seconds.");
            }

            return new(
                process.ExitCode,
                (await outputTask).Trim(),
                (await errorTask).Trim());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
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
}
