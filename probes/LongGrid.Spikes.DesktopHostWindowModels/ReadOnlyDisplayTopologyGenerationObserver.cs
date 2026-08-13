using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

internal enum ReadOnlyDisplayTopologyObservationKind
{
    BaselineReady,
    GenerationChanged,
    ReadUnavailable,
    Stabilized,
}

internal sealed record ReadOnlyDisplayTopologyObservation(
    ReadOnlyDisplayTopologyObservationKind Kind,
    long Generation,
    ProductDisplayTopologyReadStatus ReadStatus,
    DisplayTopologyStabilizationState StabilizationState,
    int ConsecutiveIdenticalSamples);

internal sealed class ReadOnlyDisplayTopologyGenerationObserver : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly object sync = new();
    private readonly IProductDisplayTopologyReader reader;
    private readonly DisplayTopologyStabilizer stabilizer = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? observationLoop;
    private string? lastAuthoritativeFingerprint;
    private bool baselineReady;
    private bool unsafeGeneration;
    private bool disposed;

    internal ReadOnlyDisplayTopologyGenerationObserver(
        IProductDisplayTopologyReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
    }

    internal event EventHandler<ReadOnlyDisplayTopologyObservation>? Changed;

    internal void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (observationLoop is not null)
            {
                throw new InvalidOperationException(
                    "The display-topology observer has already started.");
            }

            _ = stabilizer.RecordChange(
                DisplayChangeReason.Startup,
                DateTimeOffset.UtcNow);
            observationLoop = Task.Run(() => ObserveAsync(lifetime.Token));
        }
    }

    public void Dispose()
    {
        Task? loop;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            loop = observationLoop;
        }

        try
        {
            loop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProductDisplayTopologyReadResult read =
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlyDisplayTopologyObservation? observation = Inspect(
                read,
                DateTimeOffset.UtcNow);
            if (observation is not null)
            {
                Changed?.Invoke(this, observation);
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private ReadOnlyDisplayTopologyObservation? Inspect(
        ProductDisplayTopologyReadResult read,
        DateTimeOffset observedAt)
    {
        lock (sync)
        {
            if (!read.IsAuthoritative)
            {
                if (!baselineReady || unsafeGeneration)
                {
                    return null;
                }

                DisplayTopologyStabilizationResult unavailable =
                    stabilizer.RecordChange(
                        DisplayChangeReason.DisplayConfiguration,
                        observedAt);
                unsafeGeneration = true;
                return CreateObservation(
                    ReadOnlyDisplayTopologyObservationKind.ReadUnavailable,
                    read.Status,
                    unavailable);
            }

            string fingerprint = DisplayTopologyFingerprint.Compute(read.Displays);
            if (baselineReady
                && !string.Equals(
                    fingerprint,
                    lastAuthoritativeFingerprint,
                    StringComparison.Ordinal))
            {
                DisplayTopologyStabilizationResult changed =
                    stabilizer.RecordChange(
                        DisplayChangeReason.DisplayConfiguration,
                        observedAt);
                lastAuthoritativeFingerprint = fingerprint;
                unsafeGeneration = true;
                return CreateObservation(
                    ReadOnlyDisplayTopologyObservationKind.GenerationChanged,
                    read.Status,
                    changed);
            }

            DisplayTopologyStabilizationResult result =
                stabilizer.ObserveTopology(fingerprint, observedAt);
            if (!baselineReady
                && result.State == DisplayTopologyStabilizationState.Ready)
            {
                baselineReady = true;
                lastAuthoritativeFingerprint = fingerprint;
                return CreateObservation(
                    ReadOnlyDisplayTopologyObservationKind.BaselineReady,
                    read.Status,
                    result);
            }

            if (unsafeGeneration
                && result.State == DisplayTopologyStabilizationState.Ready)
            {
                unsafeGeneration = false;
                return CreateObservation(
                    ReadOnlyDisplayTopologyObservationKind.Stabilized,
                    read.Status,
                    result);
            }

            return null;
        }
    }

    private static ReadOnlyDisplayTopologyObservation CreateObservation(
        ReadOnlyDisplayTopologyObservationKind kind,
        ProductDisplayTopologyReadStatus readStatus,
        DisplayTopologyStabilizationResult stabilization) =>
        new(
            kind,
            stabilization.Generation,
            readStatus,
            stabilization.State,
            stabilization.ConsecutiveIdenticalSamples);
}
