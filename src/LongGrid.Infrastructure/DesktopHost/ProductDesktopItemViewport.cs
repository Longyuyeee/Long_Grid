namespace LongGrid.Infrastructure.DesktopHost;

using LongGrid.Core.Configuration;

public static class ProductDesktopItemViewportPolicy
{
    public static int ClampStart(
        int requestedStart,
        int totalItemCount,
        ProductContainerContentDensity density =
            ProductContainerContentDensity.Comfortable)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalItemCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(totalItemCount, 500);
        int maximumStart = Math.Max(
            0,
            totalItemCount
                - ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(density));
        return Math.Clamp(requestedStart, 0, maximumStart);
    }

    public static int Move(
        int currentStart,
        int totalItemCount,
        int wheelDelta,
        ProductContainerContentDensity density =
            ProductContainerContentDensity.Comfortable,
        bool pageNavigation = false)
    {
        if (wheelDelta == 0)
        {
            return ClampStart(currentStart, totalItemCount, density);
        }
        int direction = wheelDelta < 0 ? 1 : -1;
        int wheelSteps = Math.Max(1, Math.Abs(wheelDelta) / 120);
        int distance = pageNavigation
            ? ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(density)
            : wheelSteps;
        int requested = checked(currentStart + direction * distance);
        return ClampStart(requested, totalItemCount, density);
    }
}

internal sealed record ProductDesktopItemViewportSurfaceInput(
    string ContainerId,
    int WheelDelta,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat = false,
    bool PageNavigation = false);

public sealed record ProductDesktopItemViewportRequest(
    string ContainerId,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    int WheelDelta,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat = false,
    bool PageNavigation = false);
