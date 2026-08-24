namespace LongGrid.Infrastructure.DesktopHost;

public static class ProductDesktopItemViewportPolicy
{
    public static int ClampStart(int requestedStart, int totalItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalItemCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(totalItemCount, 500);
        int maximumStart = Math.Max(
            0,
            totalItemCount
                - ProductDesktopHostReadOnlyProjection.MaximumVisibleItems);
        return Math.Clamp(requestedStart, 0, maximumStart);
    }

    public static int Move(
        int currentStart,
        int totalItemCount,
        int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return ClampStart(currentStart, totalItemCount);
        }
        int direction = wheelDelta < 0 ? 1 : -1;
        int requested = checked(currentStart + direction
            * ProductDesktopHostReadOnlyProjection.MaximumVisibleItems);
        return ClampStart(requested, totalItemCount);
    }
}

internal sealed record ProductDesktopItemViewportSurfaceInput(
    string ContainerId,
    int WheelDelta,
    bool SourceAttested,
    bool IsInjected);

public sealed record ProductDesktopItemViewportRequest(
    string ContainerId,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    int WheelDelta,
    bool SourceAttested,
    bool IsInjected);
