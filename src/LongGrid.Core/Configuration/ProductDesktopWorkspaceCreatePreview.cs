using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public enum ProductDesktopWorkspaceCreatePreviewStatus
{
    Editing,
    Submitting,
    Rejected,
    Cancelled,
}

public enum ProductDesktopWorkspaceCreatePreviewFailure
{
    None,
    InvalidRequest,
    InvalidName,
    DuplicateName,
    LimitReached,
    PlacementUnavailable,
    StaleWorkspace,
    StaleTopology,
    StaleSelection,
    DisplayUnavailable,
    HostUnavailable,
    Replaced,
    UserCancelled,
    WindowClosing,
}

public sealed record ProductDesktopWorkspaceCreatePreviewSnapshot(
    Guid SessionId,
    ProductDesktopWorkspaceCreateRequest Request,
    ProductDesktopWorkspaceCreatePreviewStatus Status,
    ProductDesktopWorkspaceCreatePreviewFailure Failure,
    string Name,
    ProductContainerPlacementState? CandidatePlacement)
{
    public bool CanSubmit =>
        Status == ProductDesktopWorkspaceCreatePreviewStatus.Editing
        && Failure == ProductDesktopWorkspaceCreatePreviewFailure.None
        && CandidatePlacement is not null;
}

public sealed class ProductDesktopWorkspaceCreatePreviewSession
{
    private ProductDesktopWorkspaceCreatePreviewSnapshot snapshot;

    private ProductDesktopWorkspaceCreatePreviewSession(
        ProductDesktopWorkspaceCreatePreviewSnapshot snapshot) =>
        this.snapshot = snapshot;

    public ProductDesktopWorkspaceCreatePreviewSnapshot Snapshot => snapshot;

    public static ProductDesktopWorkspaceCreatePreviewSession Start(
        ProductDesktopWorkspaceCreateRequest? request,
        ProductWorkspaceContainerCreationDefaultsDecision? defaults)
    {
        ProductDesktopWorkspaceCreatePreviewFailure failure =
            request is null || defaults is null
                ? ProductDesktopWorkspaceCreatePreviewFailure.InvalidRequest
                : Map(defaults.Status);
        bool ready = request is not null
            && defaults?.CanCreate == true
            && failure == ProductDesktopWorkspaceCreatePreviewFailure.None;
        ProductDesktopWorkspaceCreateRequest safeRequest = request ?? new(
            ProductDesktopWorkspaceCreateInputKind.PrimaryPointer,
            string.Empty,
            WorkspaceRevision: -1,
            TopologyGeneration: -1,
            SourceAttested: false,
            IsInjected: true,
            IsAutoRepeat: false);
        return new(new(
            Guid.NewGuid(),
            safeRequest,
            ready
                ? ProductDesktopWorkspaceCreatePreviewStatus.Editing
                : ProductDesktopWorkspaceCreatePreviewStatus.Rejected,
            ready ? ProductDesktopWorkspaceCreatePreviewFailure.None : failure,
            defaults?.Name ?? string.Empty,
            defaults?.Placement));
    }

    public ProductDesktopWorkspaceCreatePreviewSnapshot UpdateName(
        string? enteredName,
        ProductWorkspaceContainerCreationDefaultsDecision? decision)
    {
        if (snapshot.Status !=
            ProductDesktopWorkspaceCreatePreviewStatus.Editing)
        {
            return snapshot;
        }

        ProductDesktopWorkspaceCreatePreviewFailure failure = decision is null
            ? ProductDesktopWorkspaceCreatePreviewFailure.InvalidRequest
            : Map(decision.Status);
        snapshot = snapshot with
        {
            Failure = failure,
            Name = decision?.CanCreate == true
                ? decision.Name!
                : enteredName ?? string.Empty,
            CandidatePlacement = decision?.Placement
                ?? snapshot.CandidatePlacement,
        };
        return snapshot;
    }

    public ProductDesktopWorkspaceCreatePreviewSnapshot PrepareSubmit(
        long currentWorkspaceRevision,
        long currentTopologyGeneration)
    {
        if (snapshot.Status !=
            ProductDesktopWorkspaceCreatePreviewStatus.Editing)
        {
            return snapshot;
        }

        ProductDesktopWorkspaceCreatePreviewFailure failure =
            snapshot.Request.WorkspaceRevision != currentWorkspaceRevision
                ? ProductDesktopWorkspaceCreatePreviewFailure.StaleWorkspace
                : snapshot.Request.TopologyGeneration != currentTopologyGeneration
                    ? ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology
                    : snapshot.CanSubmit
                        ? ProductDesktopWorkspaceCreatePreviewFailure.None
                        : snapshot.Failure ==
                            ProductDesktopWorkspaceCreatePreviewFailure.None
                                ? ProductDesktopWorkspaceCreatePreviewFailure.InvalidName
                                : snapshot.Failure;
        snapshot = snapshot with
        {
            Status = failure == ProductDesktopWorkspaceCreatePreviewFailure.None
                ? ProductDesktopWorkspaceCreatePreviewStatus.Submitting
                : ProductDesktopWorkspaceCreatePreviewStatus.Rejected,
            Failure = failure,
        };
        return snapshot;
    }

    public ProductDesktopWorkspaceCreatePreviewSnapshot Cancel(
        ProductDesktopWorkspaceCreatePreviewFailure failure)
    {
        if (snapshot.Status is
            ProductDesktopWorkspaceCreatePreviewStatus.Submitting
            or ProductDesktopWorkspaceCreatePreviewStatus.Rejected
            or ProductDesktopWorkspaceCreatePreviewStatus.Cancelled)
        {
            return snapshot;
        }
        if (failure is ProductDesktopWorkspaceCreatePreviewFailure.None
            or ProductDesktopWorkspaceCreatePreviewFailure.InvalidName
            or ProductDesktopWorkspaceCreatePreviewFailure.DuplicateName
            or ProductDesktopWorkspaceCreatePreviewFailure.LimitReached
            or ProductDesktopWorkspaceCreatePreviewFailure.PlacementUnavailable)
        {
            failure = ProductDesktopWorkspaceCreatePreviewFailure.InvalidRequest;
        }

        snapshot = snapshot with
        {
            Status = ProductDesktopWorkspaceCreatePreviewStatus.Cancelled,
            Failure = failure,
        };
        return snapshot;
    }

    private static ProductDesktopWorkspaceCreatePreviewFailure Map(
        ProductWorkspaceContainerCreationDefaultsStatus status) => status switch
        {
            ProductWorkspaceContainerCreationDefaultsStatus.Ready =>
                ProductDesktopWorkspaceCreatePreviewFailure.None,
            ProductWorkspaceContainerCreationDefaultsStatus.DuplicateName =>
                ProductDesktopWorkspaceCreatePreviewFailure.DuplicateName,
            ProductWorkspaceContainerCreationDefaultsStatus.LimitReached =>
                ProductDesktopWorkspaceCreatePreviewFailure.LimitReached,
            ProductWorkspaceContainerCreationDefaultsStatus.PlacementUnavailable =>
                ProductDesktopWorkspaceCreatePreviewFailure.PlacementUnavailable,
            _ => ProductDesktopWorkspaceCreatePreviewFailure.InvalidName,
        };
}
