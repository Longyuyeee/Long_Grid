using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class DisplayTopologyStabilizerTests
{
    private static readonly DateTimeOffset Origin =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ChangeWaitsForQuietPeriod()
    {
        var stabilizer = new DisplayTopologyStabilizer();

        DisplayTopologyStabilizationResult changed =
            stabilizer.RecordChange(
                DisplayChangeReason.DisplayConfiguration,
                Origin);
        DisplayTopologyStabilizationResult early =
            stabilizer.ObserveTopology(
                "topology-a",
                Origin.AddMilliseconds(749));

        Assert.Equal(
            DisplayTopologyStabilizationState.WaitingQuietPeriod,
            changed.State);
        Assert.Equal(
            Origin.AddMilliseconds(750),
            changed.NextActionAt);
        Assert.Equal(
            DisplayTopologyStabilizationState.WaitingQuietPeriod,
            early.State);
        Assert.Equal(0, early.ConsecutiveIdenticalSamples);
    }

    [Fact]
    public void RepeatedChangeStartsANewGenerationAndExtendsQuietPeriod()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(
            DisplayChangeReason.DisplayConfiguration,
            Origin);

        DisplayTopologyStabilizationResult result =
            stabilizer.RecordChange(
                DisplayChangeReason.Dpi,
                Origin.AddMilliseconds(500));

        Assert.Equal(2, result.Generation);
        Assert.Equal(
            DisplayChangeReason.DisplayConfiguration
            | DisplayChangeReason.Dpi,
            result.Reasons);
        Assert.Equal(
            Origin.AddMilliseconds(1250),
            result.NextActionAt);
    }

    [Fact]
    public void TwoSeparatedIdenticalSamplesBecomeReady()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);

        DisplayTopologyStabilizationResult first =
            stabilizer.ObserveTopology(
                "topology-a",
                Origin.AddMilliseconds(750));
        DisplayTopologyStabilizationResult second =
            stabilizer.ObserveTopology(
                "topology-a",
                Origin.AddMilliseconds(1000));

        Assert.Equal(
            DisplayTopologyStabilizationState.Sampling,
            first.State);
        Assert.Equal(
            DisplayTopologyStabilizationState.Ready,
            second.State);
        Assert.True(second.CanCreateRecoveryPlan);
    }

    [Fact]
    public void SamplesThatAreTooCloseDoNotCountTwice()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);
        stabilizer.ObserveTopology(
            "topology-a",
            Origin.AddMilliseconds(750));

        DisplayTopologyStabilizationResult result =
            stabilizer.ObserveTopology(
                "topology-a",
                Origin.AddMilliseconds(900));

        Assert.Equal(
            DisplayTopologyStabilizationState.Sampling,
            result.State);
        Assert.Equal(1, result.ConsecutiveIdenticalSamples);
        Assert.Equal(
            Origin.AddMilliseconds(1000),
            result.NextActionAt);
    }

    [Fact]
    public void DifferentSampleRestartsConsecutiveCount()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);
        stabilizer.ObserveTopology(
            "topology-a",
            Origin.AddMilliseconds(750));

        DisplayTopologyStabilizationResult changed =
            stabilizer.ObserveTopology(
                "topology-b",
                Origin.AddMilliseconds(1000));

        Assert.Equal(
            DisplayTopologyStabilizationState.Sampling,
            changed.State);
        Assert.Equal(1, changed.ConsecutiveIdenticalSamples);
    }

    [Fact]
    public void NewSignalInvalidatesReadyGeneration()
    {
        var stabilizer = ReadyStabilizer();
        long readyGeneration = stabilizer.Current.Generation;

        DisplayTopologyStabilizationResult result =
            stabilizer.RecordChange(
                DisplayChangeReason.Device,
                Origin.AddMilliseconds(1100));

        Assert.Equal(readyGeneration + 1, result.Generation);
        Assert.False(result.CanCreateRecoveryPlan);
        Assert.Equal(
            DisplayTopologyStabilizationState.WaitingQuietPeriod,
            result.State);
    }

    [Fact]
    public void UnsignaledFingerprintChangeInvalidatesReadyGeneration()
    {
        var stabilizer = ReadyStabilizer();
        long readyGeneration = stabilizer.Current.Generation;

        DisplayTopologyStabilizationResult result =
            stabilizer.ObserveTopology(
                "topology-b",
                Origin.AddMilliseconds(1100));

        Assert.Equal(readyGeneration + 1, result.Generation);
        Assert.Equal(
            DisplayChangeReason.TopologySampleChanged,
            result.Reasons);
        Assert.Equal(
            DisplayTopologyStabilizationState.WaitingQuietPeriod,
            result.State);
    }

    [Fact]
    public void MaximumWaitTimesOutUnstableTopology()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);
        stabilizer.ObserveTopology(
            "topology-a",
            Origin.AddMilliseconds(750));
        stabilizer.ObserveTopology(
            "topology-b",
            Origin.AddMilliseconds(1000));

        DisplayTopologyStabilizationResult result =
            stabilizer.ObserveTopology(
                "topology-c",
                Origin.AddSeconds(10));

        Assert.Equal(
            DisplayTopologyStabilizationState.TimedOut,
            result.State);
        Assert.False(result.CanCreateRecoveryPlan);
    }

    [Fact]
    public void ContinuousSignalsCannotExtendMaximumWaitForever()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(
            DisplayChangeReason.DisplayConfiguration,
            Origin);

        for (int second = 1; second < 10; second++)
        {
            stabilizer.RecordChange(
                DisplayChangeReason.Device,
                Origin.AddSeconds(second));
        }

        DisplayTopologyStabilizationResult result =
            stabilizer.RecordChange(
                DisplayChangeReason.Dpi,
                Origin.AddSeconds(10));

        Assert.Equal(
            DisplayTopologyStabilizationState.TimedOut,
            result.State);
        Assert.Equal(11, result.Generation);
        Assert.Equal(
            DisplayChangeReason.DisplayConfiguration
            | DisplayChangeReason.Device
            | DisplayChangeReason.Dpi,
            result.Reasons);
    }

    [Fact]
    public void PauseRejectsSamplesUntilResume()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);
        DisplayTopologyStabilizationResult paused =
            stabilizer.Pause(
                DisplayChangeReason.PowerSuspend,
                Origin.AddMilliseconds(100));
        DisplayTopologyStabilizationResult ignored =
            stabilizer.ObserveTopology(
                "topology-a",
                Origin.AddSeconds(2));
        DisplayTopologyStabilizationResult resumed =
            stabilizer.Resume(
                DisplayChangeReason.PowerResume,
                Origin.AddSeconds(3));

        Assert.Equal(
            DisplayTopologyStabilizationState.Paused,
            paused.State);
        Assert.Equal(
            DisplayTopologyStabilizationState.Paused,
            ignored.State);
        Assert.Equal(
            DisplayTopologyStabilizationState.WaitingQuietPeriod,
            resumed.State);
        Assert.Equal(3, resumed.Generation);
    }

    [Fact]
    public void BackwardTimestampIsRejected()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(
            DisplayChangeReason.DisplayConfiguration,
            Origin.AddSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stabilizer.ObserveTopology("topology", Origin));
    }

    [Fact]
    public void UnsafeOptionsAreRejected()
    {
        var options = new DisplayTopologyStabilizerOptions(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            1);

        Assert.Throws<ArgumentException>(
            () => new DisplayTopologyStabilizer(options));
    }

    private static DisplayTopologyStabilizer ReadyStabilizer()
    {
        var stabilizer = new DisplayTopologyStabilizer();
        stabilizer.RecordChange(DisplayChangeReason.Startup, Origin);
        stabilizer.ObserveTopology(
            "topology-a",
            Origin.AddMilliseconds(750));
        stabilizer.ObserveTopology(
            "topology-a",
            Origin.AddMilliseconds(1000));
        return stabilizer;
    }
}
