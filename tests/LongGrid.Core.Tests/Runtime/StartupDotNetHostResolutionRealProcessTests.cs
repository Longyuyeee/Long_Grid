using System.Diagnostics;
using System.Text.Json;

namespace LongGrid.Core.Tests.Runtime;

public sealed class DotNetHostResolutionRealProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(20);

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
    [InlineData("Pack-LongGrid.ps1", "portable-unpacked-zip")]
    [InlineData(
        "Build-LongGridReleaseCandidate.ps1",
        "internal-unsigned-developer-preview")]
    public async Task PackagingValidateOnlyUsesCompatibleSdkDespitePoisonedPath(
        string scriptName,
        string expectedType)
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
        string? actualType = root.TryGetProperty(
            "packageType",
            out JsonElement packageType)
                ? packageType.GetString()
                : root.GetProperty("candidateType").GetString();
        Assert.Equal(expectedType, actualType);
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
            startInfo.Environment["PATH"] = temporaryDirectory;
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
