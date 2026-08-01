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
    private readonly TimeSpan _initialRestartBackoff;
    private readonly TimeSpan _maximumRestartBackoff;
    private Process? _process;
    private BoundedLineReader? _outputReader;
    private int _requestsInCurrentProcess;
    private int _consecutiveTimeouts;
    private bool _disposed;

    internal ThumbnailWorkerClient(
        int maximumRequestsPerProcess,
        TimeSpan? initialRestartBackoff = null,
        TimeSpan? maximumRestartBackoff = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRequestsPerProcess, 1);
        _maximumRequestsPerProcess = maximumRequestsPerProcess;
        _initialRestartBackoff = initialRestartBackoff
            ?? TimeSpan.FromMilliseconds(50);
        _maximumRestartBackoff = maximumRestartBackoff
            ?? TimeSpan.FromMilliseconds(800);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _initialRestartBackoff,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            _maximumRestartBackoff,
            _initialRestartBackoff);
    }

    internal int ProcessesStarted { get; private set; }

    internal int BudgetRecycles { get; private set; }

    internal int TimeoutKills { get; private set; }

    internal int ProtocolKills { get; private set; }

    internal int UnexpectedExits { get; private set; }

    internal int RestartBackoffsApplied { get; private set; }

    internal double TotalRestartBackoffMilliseconds { get; private set; }

    internal int MaximumConsecutiveTimeouts { get; private set; }

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

        await ApplyRestartBackoffAsync();
        Process process = EnsureProcess();
        var stopwatch = Stopwatch.StartNew();
        await process.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize(request, JsonOptions));
        await process.StandardInput.FlushAsync();

        Task<string?> responseTask = (_outputReader
            ?? throw new InvalidOperationException("The worker output is unavailable."))
            .ReadLineAsync();
        Task completed = await Task.WhenAny(responseTask, Task.Delay(timeout));
        if (!ReferenceEquals(completed, responseTask))
        {
            stopwatch.Stop();
            TimeoutKills++;
            _consecutiveTimeouts++;
            MaximumConsecutiveTimeouts = Math.Max(
                MaximumConsecutiveTimeouts,
                _consecutiveTimeouts);
            StopProcess(force: true);
            return new ThumbnailWorkerCallResult(
                Completed: false,
                TimedOut: true,
                WorkerExited: true,
                ProtocolError: false,
                Response: null,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        string? line;
        try
        {
            line = await responseTask;
        }
        catch (InvalidDataException)
        {
            stopwatch.Stop();
            ResetTimeoutStreak();
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

        stopwatch.Stop();
        if (line is null)
        {
            ResetTimeoutStreak();
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
            ResetTimeoutStreak();
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
            ResetTimeoutStreak();
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
        ResetTimeoutStreak();
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

    internal int EnsureWorkerStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnsureProcess().Id;
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
        _outputReader = new BoundedLineReader(
            _process.StandardOutput,
            ThumbnailWorkerServer.MaximumResponseCharacters);
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
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    private async Task ApplyRestartBackoffAsync()
    {
        if (_consecutiveTimeouts == 0)
        {
            return;
        }

        double multiplier = Math.Pow(2, _consecutiveTimeouts - 1);
        double delayMilliseconds = Math.Min(
            _maximumRestartBackoff.TotalMilliseconds,
            _initialRestartBackoff.TotalMilliseconds * multiplier);
        TimeSpan delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        RestartBackoffsApplied++;
        TotalRestartBackoffMilliseconds += delay.TotalMilliseconds;
        await Task.Delay(delay);
    }

    private void ResetTimeoutStreak() => _consecutiveTimeouts = 0;

    private void StopProcess(bool force)
    {
        Process? process = _process;
        _process = null;
        _outputReader = null;
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
