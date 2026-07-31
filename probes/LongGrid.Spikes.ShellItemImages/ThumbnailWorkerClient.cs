using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class ThumbnailWorkerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly int _maximumRequestsPerProcess;
    private Process? _process;
    private int _requestsInCurrentProcess;
    private bool _disposed;

    internal ThumbnailWorkerClient(int maximumRequestsPerProcess)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRequestsPerProcess, 1);
        _maximumRequestsPerProcess = maximumRequestsPerProcess;
    }

    internal int ProcessesStarted { get; private set; }

    internal int BudgetRecycles { get; private set; }

    internal int TimeoutKills { get; private set; }

    internal int ProtocolKills { get; private set; }

    internal int UnexpectedExits { get; private set; }

    internal long PeakWorkingSetBytes { get; private set; }

    internal int PeakHandleCount { get; private set; }

    internal double TotalCpuMilliseconds { get; private set; }

    internal async Task<double> MeasureIdleCpuMillisecondsAsync(TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Process process = EnsureProcess();
        process.Refresh();
        TimeSpan before = process.TotalProcessorTime;
        await Task.Delay(duration);
        process.Refresh();
        SampleResources(process);
        return (process.TotalProcessorTime - before).TotalMilliseconds;
    }

    internal async Task<ThumbnailWorkerCallResult> ExecuteAsync(
        ThumbnailWorkerRequest request,
        TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Process process = EnsureProcess();
        var stopwatch = Stopwatch.StartNew();
        await process.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize(request, JsonOptions));
        await process.StandardInput.FlushAsync();

        Task<string?> responseTask = process.StandardOutput.ReadLineAsync();
        Task completed = await Task.WhenAny(responseTask, Task.Delay(timeout));
        if (!ReferenceEquals(completed, responseTask))
        {
            stopwatch.Stop();
            TimeoutKills++;
            StopProcess(force: true);
            return new ThumbnailWorkerCallResult(
                Completed: false,
                TimedOut: true,
                WorkerExited: true,
                ProtocolError: false,
                Response: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        string? line = await responseTask;
        stopwatch.Stop();
        if (line is null)
        {
            UnexpectedExits++;
            StopProcess(force: false);
            return new ThumbnailWorkerCallResult(
                Completed: false,
                TimedOut: false,
                WorkerExited: true,
                ProtocolError: false,
                Response: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        ThumbnailWorkerResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ThumbnailWorkerResponse>(
                line,
                JsonOptions);
        }
        catch (JsonException)
        {
            ProtocolKills++;
            StopProcess(force: true);
            return new ThumbnailWorkerCallResult(
                Completed: false,
                TimedOut: false,
                WorkerExited: true,
                ProtocolError: true,
                Response: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        if (response is null
            || response.ProtocolVersion != ThumbnailWorkerServer.CurrentProtocolVersion
            || !string.Equals(
                response.RequestId,
                request.RequestId,
                StringComparison.Ordinal))
        {
            ProtocolKills++;
            StopProcess(force: true);
            return new ThumbnailWorkerCallResult(
                Completed: false,
                TimedOut: false,
                WorkerExited: true,
                ProtocolError: true,
                Response: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        _requestsInCurrentProcess++;
        SampleResources(process);
        if (_requestsInCurrentProcess >= _maximumRequestsPerProcess)
        {
            BudgetRecycles++;
            StopProcess(force: false);
        }

        return new ThumbnailWorkerCallResult(
            Completed: response is not null,
            TimedOut: false,
            WorkerExited: false,
            ProtocolError: false,
            response,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopProcess(force: false);
        _disposed = true;
    }

    private Process EnsureProcess()
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        _process?.Dispose();
        _process = Process.Start(CreateStartInfo())
            ?? throw new InvalidOperationException("The thumbnail worker did not start.");
        _requestsInCurrentProcess = 0;
        ProcessesStarted++;
        return _process;
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The process path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add("--thumbnail-worker");
        return startInfo;
    }

    private void StopProcess(bool force)
    {
        Process? process = _process;
        _process = null;
        _requestsInCurrentProcess = 0;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                if (!force)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(milliseconds: 2_000))
                    {
                        force = true;
                    }
                }

                if (force && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }

            SampleResources(process);
            TotalCpuMilliseconds += process.TotalProcessorTime.TotalMilliseconds;
        }
        finally
        {
            process.Dispose();
        }
    }

    private void SampleResources(Process process)
    {
        try
        {
            process.Refresh();
            PeakWorkingSetBytes = Math.Max(
                PeakWorkingSetBytes,
                process.WorkingSet64);
            PeakHandleCount = Math.Max(PeakHandleCount, process.HandleCount);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed record ThumbnailWorkerCallResult(
    bool Completed,
    bool TimedOut,
    bool WorkerExited,
    bool ProtocolError,
    ThumbnailWorkerResponse? Response,
    double RoundTripMilliseconds);
