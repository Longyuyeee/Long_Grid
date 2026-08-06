using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceLayoutRecoveryPreviewStatus
{
    UnavailableSession,
    AwaitingAuthoritativeTopology,
    SavedTopologyMissing,
    Automatic,
    ReviewRequired,
    Blocked,
    InvalidState,
}

public sealed record ProductWorkspaceLayoutRecoveryPreviewResult(
    ProductWorkspaceLayoutRecoveryPreviewStatus Status,
    int ContainerCount,
    int DisplayMappingCount,
    int UnresolvedDisplayCount,
    int VisibilityCorrectionCount,
    bool DesktopWindowsChanged);

public static class ProductWorkspaceLayoutRecoveryPreview
{
    public static ProductWorkspaceLayoutRecoveryPreviewResult Create(
        ProductWorkspaceState? state,
        IReadOnlyList<DisplayTopologyNode>? savedTopology,
        IReadOnlyList<DisplayTopologyNode>? currentTopology,
        bool currentTopologyAuthoritative)
    {
        if (state is null)
        {
            return Empty(ProductWorkspaceLayoutRecoveryPreviewStatus.UnavailableSession);
        }

        ProductWorkspaceProjectionResult validation =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!validation.IsSuccess)
        {
            return Empty(ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState);
        }

        if (!currentTopologyAuthoritative
            || currentTopology is null
            || currentTopology.Count == 0)
        {
            return new(
                ProductWorkspaceLayoutRecoveryPreviewStatus
                    .AwaitingAuthoritativeTopology,
                state.Containers.Count,
                0,
                0,
                0,
                DesktopWindowsChanged: false);
        }

        if (savedTopology is null || savedTopology.Count == 0)
        {
            return new(
                ProductWorkspaceLayoutRecoveryPreviewStatus.SavedTopologyMissing,
                state.Containers.Count,
                0,
                0,
                0,
                DesktopWindowsChanged: false);
        }

        try
        {
            SavedContainerLayout[] layouts = state.Containers
                .Select(container => new SavedContainerLayout(
                    container.Id,
                    container.Placement.DisplayKey,
                    new(
                        ToPlannerDip(container.Placement.XDip),
                        ToPlannerDip(container.Placement.YDip),
                        ToPlannerDip(container.Placement.WidthDip),
                        ToPlannerDip(container.Placement.HeightDip))))
                .ToArray();
            LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
                savedTopology,
                currentTopology,
                layouts);
            return new(
                MapStatus(plan.Status),
                state.Containers.Count,
                plan.DisplayMappings.Count,
                plan.UnresolvedSavedDisplayIds.Count,
                plan.ContainerPlacements.Count(placement =>
                    placement.WasVisibilityCorrected),
                DesktopWindowsChanged: false);
        }
        catch (ArgumentException)
        {
            return new(
                ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
                state.Containers.Count,
                0,
                0,
                0,
                DesktopWindowsChanged: false);
        }
        catch (OverflowException)
        {
            return new(
                ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
                state.Containers.Count,
                0,
                0,
                0,
                DesktopWindowsChanged: false);
        }
    }

    private static int ToPlannerDip(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static ProductWorkspaceLayoutRecoveryPreviewStatus MapStatus(
        LayoutRecoveryStatus status) => status switch
        {
            LayoutRecoveryStatus.Automatic =>
                ProductWorkspaceLayoutRecoveryPreviewStatus.Automatic,
            LayoutRecoveryStatus.ReviewRequired =>
                ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired,
            LayoutRecoveryStatus.Blocked =>
                ProductWorkspaceLayoutRecoveryPreviewStatus.Blocked,
            _ => ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
        };

    private static ProductWorkspaceLayoutRecoveryPreviewResult Empty(
        ProductWorkspaceLayoutRecoveryPreviewStatus status) =>
        new(status, 0, 0, 0, 0, DesktopWindowsChanged: false);
}
