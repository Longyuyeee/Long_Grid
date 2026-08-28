using System.Diagnostics;

namespace LongGrid.Core.Tests.Taskbar;

internal sealed class TaskbarReadyParentProcess : IAsyncDisposable
{
    private const string ReadyMarker = "LONGGRID_PARENT_READY";
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);
    private readonly Process process;
    private bool released;

    private TaskbarReadyParentProcess(Process process)
    {
        this.process = process;
    }

    public int Id => process.Id;

    public static async Task<TaskbarReadyParentProcess> StartAsync()
    {
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "[Console]::Out.WriteLine('LONGGRID_PARENT_READY'); "
            + "[Console]::Out.Flush(); "
            + "[Console]::In.ReadLine() | Out-Null");

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start controlled evidence parent process.");
        try
        {
            using CancellationTokenSource timeout = new(ReadinessTimeout);
            string? marker = await process.StandardOutput
                .ReadLineAsync(timeout.Token);
            if (!string.Equals(marker, ReadyMarker, StringComparison.Ordinal))
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"Evidence parent readiness failed: marker={marker ?? "<null>"}; "
                    + $"stderrLength={error.Length}.");
            }

            return new TaskbarReadyParentProcess(process);
        }
        catch
        {
            Terminate(process);
            process.Dispose();
            throw;
        }
    }

    public async Task ReleaseAsync()
    {
        if (released)
        {
            return;
        }

        released = true;
        await process.StandardInput.WriteLineAsync(string.Empty);
        await process.StandardInput.FlushAsync();
        using CancellationTokenSource timeout = new(ExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            throw new TimeoutException(
                $"Controlled evidence parent {process.Id} did not exit.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            Terminate(process);
            using CancellationTokenSource timeout = new(ExitTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Controlled evidence parent {process.Id} cleanup timed out.");
            }
        }

        process.Dispose();
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
}
