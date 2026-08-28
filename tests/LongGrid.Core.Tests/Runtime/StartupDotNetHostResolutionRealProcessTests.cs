using System.Diagnostics;

namespace LongGrid.Core.Tests.Runtime;

public sealed class StartupDotNetHostResolutionRealProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ValidateOnlyIgnoresEarlierPathHostWithoutCompatibleSdk()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        string compatibleHost = Path.Combine(
            programFiles,
            "dotnet",
            "dotnet.exe");
        if (!File.Exists(compatibleHost))
        {
            return;
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "LongGrid-StartupDotNet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "dotnet.cmd"),
                "@echo off\r\nexit /b 37\r\n");

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
                WorkingDirectory = FindRepositoryRoot(),
            };
            startInfo.Environment["PATH"] = temporaryDirectory;
            startInfo.Environment["ProgramW6432"] = programFiles;
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(
                FindRepositoryRoot(),
                "eng",
                "Start-LongGrid.ps1"));
            startInfo.ArgumentList.Add("-Configuration");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("-NoRestore");
            startInfo.ArgumentList.Add("-NoBuild");
            startInfo.ArgumentList.Add("-ValidateOnly");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Failed to start the startup-chain validation process.");
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
                throw new TimeoutException(
                    "Startup-chain validation did not finish within "
                    + $"{ProcessTimeout.TotalSeconds:F0} seconds.");
            }

            string output = await outputTask;
            string error = await errorTask;
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(error.Trim());
            Assert.Contains(
                "Long Grid startup chain validation passed: Release / x64",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                $"dotnet={compatibleHost}",
                output,
                StringComparison.OrdinalIgnoreCase);
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
}
