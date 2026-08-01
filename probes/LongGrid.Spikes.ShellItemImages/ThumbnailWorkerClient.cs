using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal enum ThumbnailInputStrategy
{
    ControlledCopy = 1,
    MinimumPathAcl = 2,
}

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
    private readonly ThumbnailInputStrategy _inputStrategy;
    private readonly ThumbnailAppContainerProfile _appContainerProfile;
    private readonly ThumbnailWorkerJob _workerJob;
    private Process? _process;
    private AppContainerThumbnailWorkerProcess? _workerProcess;
    private StreamWriter? _inputWriter;
    private BoundedLineReader? _outputReader;
    private int _requestsInCurrentProcess;
    private int _consecutiveTimeouts;
    private bool _disposed;

    internal ThumbnailWorkerClient(
        int maximumRequestsPerProcess,
        TimeSpan? initialRestartBackoff = null,
        TimeSpan? maximumRestartBackoff = null,
        ThumbnailInputStrategy inputStrategy =
            ThumbnailInputStrategy.ControlledCopy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRequestsPerProcess, 1);
        _maximumRequestsPerProcess = maximumRequestsPerProcess;
        _initialRestartBackoff = initialRestartBackoff
            ?? TimeSpan.FromMilliseconds(50);
        _maximumRestartBackoff = maximumRestartBackoff
            ?? TimeSpan.FromMilliseconds(800);
        if (!Enum.IsDefined(inputStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(inputStrategy));
        }

        _inputStrategy = inputStrategy;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _initialRestartBackoff,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            _maximumRestartBackoff,
            _initialRestartBackoff);
        _appContainerProfile = ThumbnailAppContainerProfile.Create();
        try
        {
            _workerJob = ThumbnailWorkerJob.Create();
        }
        catch
        {
            _appContainerProfile.Dispose();
            throw;
        }
    }

    internal int ProcessesStarted { get; private set; }

    internal int BudgetRecycles { get; private set; }

    internal int TimeoutKills { get; private set; }

    internal int ProtocolKills { get; private set; }

    internal int UnexpectedExits { get; private set; }

    internal int RestartBackoffsApplied { get; private set; }

    internal double TotalRestartBackoffMilliseconds { get; private set; }

    internal int MaximumConsecutiveTimeouts { get; private set; }

    internal bool UsesKillOnJobClose => _workerJob.IsConfigured;

    internal bool AllWorkersAppContainer { get; private set; } = true;

    internal bool UsesControlledInputCopies =>
        _appContainerProfile.BrokeredInputCopiesCreated > 0;

    internal bool UsesMinimumPathAcl =>
        _appContainerProfile.PathAclLeasesGranted > 0;

    internal bool AllPathAclLeasesRestored =>
        _appContainerProfile.AllPathAclLeasesRestored
        && _appContainerProfile.ActivePathAclLeases == 0;

    internal string AppContainerProfileName => _appContainerProfile.ProfileName;

    internal bool AppContainerProfileDeleted => _appContainerProfile.WasDeleted;

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
        bool authorizeInput = RequiresInputAuthorization(request);
        using ThumbnailPathAclLease? pathAclLease =
            authorizeInput && _inputStrategy == ThumbnailInputStrategy.MinimumPathAcl
                ? _appContainerProfile.GrantMinimumPathAccess(
                    request.Path
                        ?? throw new InvalidDataException(
                            "An extraction request requires an input path."))
                : null;
        ThumbnailWorkerRequest brokeredRequest = authorizeInput
            ? _inputStrategy switch
            {
                ThumbnailInputStrategy.ControlledCopy => request with
                {
                    Path = _appContainerProfile.BrokerReadOnlyCopy(
                        request.Path
                            ?? throw new InvalidDataException(
                                "An extraction request requires an input path.")),
                    InputTransport = ThumbnailInputTransport.ControlledCopy,
                },
                ThumbnailInputStrategy.MinimumPathAcl => request with
                {
                    InputTransport = ThumbnailInputTransport.MinimumPathAcl,
                },
                _ => throw new InvalidOperationException(
                    "The thumbnail input strategy is invalid."),
            }
            : request;
        using ThumbnailSharedMemoryTransfer? sharedMemory =
            brokeredRequest.IncludePixels
                && brokeredRequest.Kind
                    != ThumbnailWorkerRequestKind.MissingPixelBufferRequest
                ? ThumbnailSharedMemoryTransfer.Create(process)
                : null;
        ThumbnailWorkerRequest transmittedRequest = sharedMemory is null
            ? brokeredRequest
            : brokeredRequest with
            {
                PixelBufferHandle = sharedMemory.WorkerHandle,
                PixelBufferCapacity = ThumbnailWorkerServer.MaximumPixelBytes,
            };
        if (request.Kind
            == ThumbnailWorkerRequestKind.InvalidPixelBufferCapacityRequest)
        {
            transmittedRequest = transmittedRequest with
            {
                PixelBufferCapacity = ThumbnailWorkerServer.MaximumPixelBytes - 1,
            };
        }
        var stopwatch = Stopwatch.StartNew();
        string serializedRequest = JsonSerializer.Serialize(
            transmittedRequest,
            JsonOptions);
        if (serializedRequest.Length > ThumbnailWorkerServer.MaximumRequestCharacters)
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

        await (_inputWriter
            ?? throw new InvalidOperationException("The worker input is unavailable."))
            .WriteLineAsync(
            serializedRequest);
        await _inputWriter.FlushAsync();

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
                transmittedRequest.RequestId,
                StringComparison.Ordinal)
            || !IsValidResponse(transmittedRequest, response))
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

        if (sharedMemory is not null && response.Pixels is not null)
        {
            response = response with
            {
                Pixels = response.Pixels with
                {
                    Bytes = sharedMemory.Read(response.Pixels.ByteLength),
                },
            };
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
        _workerJob.Dispose();
        _appContainerProfile.Dispose();
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

        _workerProcess?.Dispose();
        _workerProcess = AppContainerThumbnailWorkerProcess.Start(
            _workerJob,
            _appContainerProfile);
        _process = _workerProcess.Process;
        _inputWriter = _workerProcess.StandardInput;
        AllWorkersAppContainer &= _workerProcess.IsAppContainer;

        _outputReader = new BoundedLineReader(
            _workerProcess.StandardOutput,
            ThumbnailWorkerServer.MaximumResponseCharacters);
        _requestsInCurrentProcess = 0;
        ProcessesStarted++;
        return _process;
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

    private bool RequiresInputAuthorization(ThumbnailWorkerRequest request) =>
        !string.IsNullOrWhiteSpace(request.Path)
        && request.Path.Length <= 32_767
        && (request.Kind is ThumbnailWorkerRequestKind.Extract
                or ThumbnailWorkerRequestKind.MissingPixelBufferRequest
                or ThumbnailWorkerRequestKind.InvalidPixelBufferCapacityRequest
            || (request.Kind == ThumbnailWorkerRequestKind.ReadBoundaryProbe
                && request.InputTransport == GetInputTransport(_inputStrategy)));

    private static ThumbnailInputTransport GetInputTransport(
        ThumbnailInputStrategy strategy) => strategy switch
        {
            ThumbnailInputStrategy.ControlledCopy =>
                ThumbnailInputTransport.ControlledCopy,
            ThumbnailInputStrategy.MinimumPathAcl =>
                ThumbnailInputTransport.MinimumPathAcl,
            _ => ThumbnailInputTransport.DirectPath,
        };

    private static bool IsValidResponse(
        ThumbnailWorkerRequest request,
        ThumbnailWorkerResponse response)
    {
        if (!double.IsFinite(response.NativeMilliseconds)
            || response.NativeMilliseconds < 0)
        {
            return false;
        }

        if (!response.Success)
        {
            return response.Width == 0
                && response.Height == 0
                && response.Pixels is null;
        }

        if (response.Width is < 1 or > 1_024
            || response.Height is < 1 or > 1_024)
        {
            return false;
        }

        if (!request.IncludePixels)
        {
            return response.Pixels is null;
        }

        ThumbnailPixelPayload? pixels = response.Pixels;
        if (pixels is null
            || pixels.Transport != ThumbnailPixelTransport.SharedMemory
            || pixels.Format != ThumbnailPixelFormat.Bgra32
            || pixels.Width != response.Width
            || pixels.Height != response.Height
            || pixels.Width > ThumbnailWorkerServer.MaximumPixelDimension
            || pixels.Height > ThumbnailWorkerServer.MaximumPixelDimension
            || pixels.Bytes is not null
            || request.PixelBufferHandle is null
            || request.PixelBufferCapacity != ThumbnailWorkerServer.MaximumPixelBytes)
        {
            return false;
        }

        try
        {
            int expectedStride = checked(pixels.Width * 4);
            int expectedLength = checked(expectedStride * pixels.Height);
            return pixels.Stride == expectedStride
                && pixels.ByteLength == expectedLength
                && expectedLength <= ThumbnailWorkerServer.MaximumPixelBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void StopProcess(bool force)
    {
        Process? process = _process;
        AppContainerThumbnailWorkerProcess? workerProcess = _workerProcess;
        _process = null;
        _workerProcess = null;
        _inputWriter = null;
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
                    workerProcess?.StandardInput.Close();
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
            try
            {
                TotalCpuMilliseconds += process.TotalProcessorTime.TotalMilliseconds;
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            workerProcess?.Dispose();
            if (workerProcess is null)
            {
                process.Dispose();
            }
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
