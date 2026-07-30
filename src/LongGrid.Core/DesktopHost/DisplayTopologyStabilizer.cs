namespace LongGrid.Core.DesktopHost;

[Flags]
public enum DisplayChangeReason
{
    None = 0,
    Startup = 1 << 0,
    DisplayConfiguration = 1 << 1,
    Dpi = 1 << 2,
    Device = 1 << 3,
    PowerResume = 1 << 4,
    SessionAvailable = 1 << 5,
    TopologySampleChanged = 1 << 6,
    PowerSuspend = 1 << 7,
    SessionUnavailable = 1 << 8,
}

public enum DisplayTopologyStabilizationState
{
    Idle,
    WaitingQuietPeriod,
    Sampling,
    Ready,
    Paused,
    TimedOut,
}

public sealed record DisplayTopologyStabilizerOptions(
    TimeSpan QuietPeriod,
    TimeSpan SampleInterval,
    TimeSpan MaximumWait,
    int RequiredIdenticalSamples)
{
    public static DisplayTopologyStabilizerOptions Default { get; } =
        new(
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(10),
            2);
}

public sealed record DisplayTopologyStabilizationResult(
    DisplayTopologyStabilizationState State,
    long Generation,
    DisplayChangeReason Reasons,
    int ConsecutiveIdenticalSamples,
    DateTimeOffset? NextActionAt)
{
    public bool CanCreateRecoveryPlan =>
        State == DisplayTopologyStabilizationState.Ready;
}

public sealed class DisplayTopologyStabilizer
{
    private readonly DisplayTopologyStabilizerOptions _options;
    private DisplayTopologyStabilizationState _state;
    private long _generation;
    private DisplayChangeReason _reasons;
    private DateTimeOffset? _firstChangeAt;
    private DateTimeOffset? _lastChangeAt;
    private DateTimeOffset? _lastAcceptedSampleAt;
    private DateTimeOffset? _lastObservedAt;
    private string? _candidateFingerprint;
    private int _consecutiveSamples;

    public DisplayTopologyStabilizer(
        DisplayTopologyStabilizerOptions? options = null)
    {
        _options = options ?? DisplayTopologyStabilizerOptions.Default;
        ValidateOptions(_options);
    }

    public DisplayTopologyStabilizationResult Current => CreateResult();

    public DisplayTopologyStabilizationResult RecordChange(
        DisplayChangeReason reason,
        DateTimeOffset observedAt)
    {
        ValidateReason(
            reason,
            nameof(reason),
            DisplayChangeReason.Startup
            | DisplayChangeReason.DisplayConfiguration
            | DisplayChangeReason.Dpi
            | DisplayChangeReason.Device
            | DisplayChangeReason.PowerResume
            | DisplayChangeReason.SessionAvailable);
        EnsureMonotonic(observedAt);
        if (_state is DisplayTopologyStabilizationState.WaitingQuietPeriod
            or DisplayTopologyStabilizationState.Sampling)
        {
            ContinueBurst(reason, observedAt);
        }
        else
        {
            StartGeneration(reason, observedAt);
        }

        return CreateResult();
    }

    public DisplayTopologyStabilizationResult Pause(
        DisplayChangeReason reason,
        DateTimeOffset observedAt)
    {
        ValidateReason(
            reason,
            nameof(reason),
            DisplayChangeReason.PowerSuspend
            | DisplayChangeReason.SessionUnavailable);
        EnsureMonotonic(observedAt);
        _generation = checked(_generation + 1);
        _reasons = reason;
        _firstChangeAt = observedAt;
        _lastChangeAt = observedAt;
        _lastAcceptedSampleAt = null;
        _candidateFingerprint = null;
        _consecutiveSamples = 0;
        _state = DisplayTopologyStabilizationState.Paused;
        _lastObservedAt = observedAt;
        return CreateResult();
    }

    public DisplayTopologyStabilizationResult Resume(
        DisplayChangeReason reason,
        DateTimeOffset observedAt)
    {
        ValidateReason(
            reason,
            nameof(reason),
            DisplayChangeReason.PowerResume
            | DisplayChangeReason.SessionAvailable);
        EnsureMonotonic(observedAt);
        StartGeneration(reason, observedAt);
        return CreateResult();
    }

    public DisplayTopologyStabilizationResult ObserveTopology(
        string fingerprint,
        DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        EnsureMonotonic(observedAt);

        if (_state is DisplayTopologyStabilizationState.Idle
            or DisplayTopologyStabilizationState.Paused
            or DisplayTopologyStabilizationState.TimedOut)
        {
            _lastObservedAt = observedAt;
            return CreateResult();
        }

        if (_state == DisplayTopologyStabilizationState.Ready)
        {
            if (string.Equals(
                fingerprint,
                _candidateFingerprint,
                StringComparison.Ordinal))
            {
                _lastObservedAt = observedAt;
                return CreateResult();
            }

            StartGeneration(
                DisplayChangeReason.TopologySampleChanged,
                observedAt);
            return CreateResult();
        }

        DateTimeOffset timeoutAt = checked(
            _firstChangeAt!.Value + _options.MaximumWait);
        if (observedAt >= timeoutAt)
        {
            _state = DisplayTopologyStabilizationState.TimedOut;
            _lastObservedAt = observedAt;
            return CreateResult();
        }

        DateTimeOffset quietUntil = checked(
            _lastChangeAt!.Value + _options.QuietPeriod);
        if (observedAt < quietUntil)
        {
            _state = DisplayTopologyStabilizationState.WaitingQuietPeriod;
            _lastObservedAt = observedAt;
            return CreateResult();
        }

        if (_lastAcceptedSampleAt is not null
            && observedAt
                < _lastAcceptedSampleAt.Value + _options.SampleInterval)
        {
            _lastObservedAt = observedAt;
            return CreateResult();
        }

        if (string.Equals(
            fingerprint,
            _candidateFingerprint,
            StringComparison.Ordinal))
        {
            _consecutiveSamples++;
        }
        else
        {
            _candidateFingerprint = fingerprint;
            _consecutiveSamples = 1;
        }

        _lastAcceptedSampleAt = observedAt;
        _state = _consecutiveSamples >= _options.RequiredIdenticalSamples
            ? DisplayTopologyStabilizationState.Ready
            : DisplayTopologyStabilizationState.Sampling;
        _lastObservedAt = observedAt;
        return CreateResult();
    }

    private void StartGeneration(
        DisplayChangeReason reason,
        DateTimeOffset observedAt)
    {
        _generation = checked(_generation + 1);
        _reasons = reason;
        _firstChangeAt = observedAt;
        _lastChangeAt = observedAt;
        _lastAcceptedSampleAt = null;
        _candidateFingerprint = null;
        _consecutiveSamples = 0;
        _state = DisplayTopologyStabilizationState.WaitingQuietPeriod;
        _lastObservedAt = observedAt;
    }

    private void ContinueBurst(
        DisplayChangeReason reason,
        DateTimeOffset observedAt)
    {
        _generation = checked(_generation + 1);
        _reasons |= reason;
        _lastChangeAt = observedAt;
        _lastAcceptedSampleAt = null;
        _candidateFingerprint = null;
        _consecutiveSamples = 0;
        _state = observedAt
            >= _firstChangeAt!.Value + _options.MaximumWait
            ? DisplayTopologyStabilizationState.TimedOut
            : DisplayTopologyStabilizationState.WaitingQuietPeriod;
        _lastObservedAt = observedAt;
    }

    private DisplayTopologyStabilizationResult CreateResult()
    {
        DateTimeOffset? nextActionAt = _state switch
        {
            DisplayTopologyStabilizationState.WaitingQuietPeriod =>
                Earlier(
                    _lastChangeAt + _options.QuietPeriod,
                    _firstChangeAt + _options.MaximumWait),
            DisplayTopologyStabilizationState.Sampling =>
                Earlier(
                    _lastAcceptedSampleAt + _options.SampleInterval,
                    _firstChangeAt + _options.MaximumWait),
            _ => null,
        };
        return new DisplayTopologyStabilizationResult(
            _state,
            _generation,
            _reasons,
            _consecutiveSamples,
            nextActionAt);
    }

    private static DateTimeOffset? Earlier(
        DateTimeOffset? first,
        DateTimeOffset? second) =>
        first <= second ? first : second;

    private void EnsureMonotonic(DateTimeOffset observedAt)
    {
        if (_lastObservedAt is not null
            && observedAt < _lastObservedAt.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                "Observation timestamps must be monotonic.");
        }
    }

    private static void ValidateReason(
        DisplayChangeReason reason,
        string parameterName,
        DisplayChangeReason allowed)
    {
        if (reason == DisplayChangeReason.None
            || (reason & ~allowed) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateOptions(
        DisplayTopologyStabilizerOptions options)
    {
        if (options.QuietPeriod <= TimeSpan.Zero
            || options.SampleInterval <= TimeSpan.Zero
            || options.MaximumWait <= options.QuietPeriod
            || options.RequiredIdenticalSamples < 2)
        {
            throw new ArgumentException(
                "Stabilization requires positive intervals, a longer timeout, and at least two samples.",
                nameof(options));
        }
    }
}
