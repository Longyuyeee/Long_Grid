namespace LongGrid.Core.DesktopItems;

public sealed class ChangeReconciliationGate
{
    private readonly TimeSpan quietPeriod;
    private readonly TimeSpan maximumDelay;

    public ChangeReconciliationGate(
        TimeSpan quietPeriod,
        TimeSpan maximumDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            quietPeriod,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumDelay,
            quietPeriod);

        this.quietPeriod = quietPeriod;
        this.maximumDelay = maximumDelay;
    }

    public bool IsDirty { get; private set; }

    public DateTimeOffset? FirstChangeAt { get; private set; }

    public DateTimeOffset? LastChangeAt { get; private set; }

    public void RecordChange(DateTimeOffset observedAt)
    {
        if (!IsDirty)
        {
            IsDirty = true;
            FirstChangeAt = observedAt;
            LastChangeAt = observedAt;
            return;
        }

        if (observedAt > LastChangeAt)
        {
            LastChangeAt = observedAt;
        }
    }

    public bool ShouldReconcile(DateTimeOffset now)
    {
        if (!IsDirty || FirstChangeAt is null || LastChangeAt is null)
        {
            return false;
        }

        return now - LastChangeAt >= quietPeriod
            || now - FirstChangeAt >= maximumDelay;
    }

    public void CompleteReconciliation()
    {
        IsDirty = false;
        FirstChangeAt = null;
        LastChangeAt = null;
    }
}
