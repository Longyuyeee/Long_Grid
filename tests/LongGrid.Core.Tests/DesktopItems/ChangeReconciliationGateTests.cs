using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.DesktopItems;

public sealed class ChangeReconciliationGateTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QuietPeriodTriggersReconciliation()
    {
        var gate = new ChangeReconciliationGate(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

        gate.RecordChange(Start);

        Assert.False(gate.ShouldReconcile(Start.AddMilliseconds(499)));
        Assert.True(gate.ShouldReconcile(Start.AddMilliseconds(500)));
    }

    [Fact]
    public void NewEventsExtendTheQuietWindow()
    {
        var gate = new ChangeReconciliationGate(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

        gate.RecordChange(Start);
        gate.RecordChange(Start.AddMilliseconds(400));

        Assert.False(gate.ShouldReconcile(Start.AddMilliseconds(899)));
        Assert.True(gate.ShouldReconcile(Start.AddMilliseconds(900)));
    }

    [Fact]
    public void MaximumDelayPreventsContinuousEventsFromStarvingReconciliation()
    {
        var gate = new ChangeReconciliationGate(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

        gate.RecordChange(Start);
        gate.RecordChange(Start.AddMilliseconds(1900));

        Assert.True(gate.ShouldReconcile(Start.AddSeconds(2)));
    }

    [Fact]
    public void CompletionClearsDirtyState()
    {
        var gate = new ChangeReconciliationGate(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2));

        gate.RecordChange(Start);
        gate.CompleteReconciliation();

        Assert.False(gate.IsDirty);
        Assert.False(gate.ShouldReconcile(Start.AddSeconds(3)));
    }

    [Fact]
    public void ConstructorRejectsInvalidTiming()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChangeReconciliationGate(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChangeReconciliationGate(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1)));
    }
}
