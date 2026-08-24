using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopContainerLayoutInteractionStatus
{
    Began,
    PreviewUpdated,
    Cancelled,
    NoChange,
    Committed,
    Rejected,
}

public sealed record ProductDesktopContainerLayoutInteractionResult(
    ProductDesktopContainerLayoutInteractionStatus Status,
    string DisplayId,
    string ContainerId,
    long ExpectedWorkspaceRevision,
    long ExpectedTopologyGeneration,
    ProductContainerPlacementState? PreviewPlacement = null,
    ProductWorkspaceState? State = null,
    ProductConfigurationDocument? Document = null,
    bool ClearPreview = false)
{
    public bool IsAccepted => Status is
        ProductDesktopContainerLayoutInteractionStatus.Began
        or ProductDesktopContainerLayoutInteractionStatus.PreviewUpdated
        or ProductDesktopContainerLayoutInteractionStatus.Cancelled
        or ProductDesktopContainerLayoutInteractionStatus.NoChange
        or ProductDesktopContainerLayoutInteractionStatus.Committed;
}

public enum ProductDesktopContainerLayoutPublicationStatus
{
    None,
    AwaitingSave,
    Published,
    Superseded,
    Compensated,
    Rejected,
}

public sealed record ProductDesktopContainerLayoutPublicationResult(
    ProductDesktopContainerLayoutPublicationStatus Status,
    ProductWorkspaceState? State = null,
    ProductConfigurationDocument? Document = null)
{
    public bool IsCompensated =>
        Status == ProductDesktopContainerLayoutPublicationStatus.Compensated
        && State is not null
        && Document is not null;
}

public sealed class ProductDesktopContainerLayoutInteractionController
{
    private readonly object gate = new();
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private ActiveGesture? active;
    private ProductWorkspaceContainerLayoutPublicationToken? publication;

    public ProductDesktopContainerLayoutInteractionController(
        ProductWorkspaceCommitCoordinator workspaceCommits)
    {
        ArgumentNullException.ThrowIfNull(workspaceCommits);
        this.workspaceCommits = workspaceCommits;
    }

    public ProductDesktopContainerLayoutInteractionResult Handle(
        ProductDesktopContainerLayoutRequest request,
        ProductWorkspaceState? state,
        bool isReadOnly,
        long currentEditRevision,
        ProductDisplayTopologySnapshot topology)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(topology);

        lock (gate)
        {
            if (request.Phase == ProductDesktopContainerLayoutInputPhase.Cancel)
            {
                return CancelUnsafe(request);
            }

            if (state is null
                || isReadOnly
                || !topology.IsAuthoritative
                || request.ExpectedWorkspaceRevision != currentEditRevision
                || request.ExpectedTopologyGeneration != topology.Generation
                || request.CancellationReason !=
                    ProductDesktopContainerLayoutCancellationReason.None)
            {
                return Reject(request, clearPreview: MatchesActive(request));
            }

            return request.Phase switch
            {
                ProductDesktopContainerLayoutInputPhase.Begin => BeginUnsafe(
                    request,
                    state,
                    currentEditRevision,
                    topology),
                ProductDesktopContainerLayoutInputPhase.Update => UpdateUnsafe(
                    request,
                    state,
                    currentEditRevision,
                    topology),
                ProductDesktopContainerLayoutInputPhase.Complete => CompleteUnsafe(
                    request,
                    state,
                    currentEditRevision,
                    topology),
                _ => Reject(request, clearPreview: false),
            };
        }
    }

    public ProductDesktopContainerLayoutInteractionResult CancelActive(
        ProductDesktopContainerLayoutCancellationReason reason)
    {
        lock (gate)
        {
            if (active is not { } current)
            {
                return Reject(
                    new(
                        ProductDesktopContainerLayoutInputPhase.Cancel,
                        ProductWorkspaceContainerLayoutGestureKind.Move,
                        string.Empty,
                        string.Empty,
                        0,
                        0,
                        0,
                        0,
                        SnapEnabled: false,
                        ShiftPressed: false,
                        reason),
                    clearPreview: false);
            }

            return CancelUnsafe(new(
                ProductDesktopContainerLayoutInputPhase.Cancel,
                current.Kind,
                current.ContainerId,
                current.DisplayId,
                current.ExpectedWorkspaceRevision,
                current.ExpectedTopologyGeneration,
                0,
                0,
                SnapEnabled: false,
                ShiftPressed: false,
                reason));
        }
    }

    public ProductDesktopContainerLayoutPublicationResult ObserveSave(
        ProductWorkspaceState? state,
        long currentEditRevision,
        ProductWorkspaceSaveSnapshot save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (gate)
        {
            if (publication is not { } token || state is null)
            {
                return new(ProductDesktopContainerLayoutPublicationStatus.None);
            }

            ProductContainerPlacementState[] placements = state.Containers
                .Where(candidate => candidate is not null && string.Equals(
                    candidate.Id,
                    token.ContainerId,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.Placement)
                .Take(2)
                .ToArray();
            ProductContainerPlacementState? placement = placements.Length == 1
                ? placements[0]
                : null;
            ProductWorkspaceContainerLayoutPublicationDecision decision =
                ProductWorkspaceContainerLayoutPublication.Evaluate(
                    token,
                    save,
                    currentEditRevision,
                    placement);
            switch (decision)
            {
                case ProductWorkspaceContainerLayoutPublicationDecision.AwaitingSave:
                    return new(
                        ProductDesktopContainerLayoutPublicationStatus.AwaitingSave);
                case ProductWorkspaceContainerLayoutPublicationDecision.Published:
                    publication = null;
                    return new(
                        ProductDesktopContainerLayoutPublicationStatus.Published);
                case ProductWorkspaceContainerLayoutPublicationDecision.Superseded:
                    publication = null;
                    return new(
                        ProductDesktopContainerLayoutPublicationStatus.Superseded);
                case ProductWorkspaceContainerLayoutPublicationDecision
                    .CompensationRequired:
                    ProductWorkspaceContainerLayoutCompensationCommitResult result =
                        workspaceCommits.CompensateContainerLayoutGesture(
                            state,
                            token);
                    if (!result.IsAccepted)
                    {
                        return new(
                            ProductDesktopContainerLayoutPublicationStatus.Rejected);
                    }
                    publication = null;
                    return new(
                        ProductDesktopContainerLayoutPublicationStatus.Compensated,
                        result.State,
                        result.Document);
                default:
                    return new(
                        ProductDesktopContainerLayoutPublicationStatus.Rejected);
            }
        }
    }

    private ProductDesktopContainerLayoutInteractionResult BeginUnsafe(
        ProductDesktopContainerLayoutRequest request,
        ProductWorkspaceState state,
        long currentEditRevision,
        ProductDisplayTopologySnapshot topology)
    {
        if (active is not null
            || request.CumulativeDeltaXDip != 0
            || request.CumulativeDeltaYDip != 0)
        {
            return Reject(request, clearPreview: false);
        }

        ProductWorkspaceContainerLayoutGestureBeginResult begin =
            ProductWorkspaceContainerLayoutGestureSession.Begin(
                state,
                currentEditRevision,
                topology.Generation,
                topology.Displays,
                request.Kind,
                request.ContainerId,
                request.DisplayId,
                request.PointerScreenX,
                request.PointerScreenY);
        if (!begin.IsReady)
        {
            return Reject(request, clearPreview: false);
        }

        active = new(
            request.Kind,
            request.ContainerId,
            request.DisplayId,
            request.ExpectedWorkspaceRevision,
            request.ExpectedTopologyGeneration,
            begin.Session!);
        return Result(
            ProductDesktopContainerLayoutInteractionStatus.Began,
            request);
    }

    private ProductDesktopContainerLayoutInteractionResult UpdateUnsafe(
        ProductDesktopContainerLayoutRequest request,
        ProductWorkspaceState state,
        long currentEditRevision,
        ProductDisplayTopologySnapshot topology)
    {
        if (!MatchesActive(request) || active is not { } current)
        {
            return Reject(request, clearPreview: false);
        }

        ProductWorkspaceContainerLayoutGestureSnapshot snapshot =
            current.Session.Update(
                state,
                currentEditRevision,
                topology.Generation,
                topology.Displays,
                request.CumulativeDeltaXDip,
                request.CumulativeDeltaYDip,
                request.SnapEnabled,
                request.ShiftPressed,
                request.PointerScreenX,
                request.PointerScreenY);
        if (snapshot.Status !=
            ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing)
        {
            active = null;
            return Reject(request, clearPreview: true);
        }

        return Result(
            ProductDesktopContainerLayoutInteractionStatus.PreviewUpdated,
            request,
            snapshot.Placement);
    }

    private ProductDesktopContainerLayoutInteractionResult CompleteUnsafe(
        ProductDesktopContainerLayoutRequest request,
        ProductWorkspaceState state,
        long currentEditRevision,
        ProductDisplayTopologySnapshot topology)
    {
        if (!MatchesActive(request) || active is not { } current)
        {
            return Reject(request, clearPreview: false);
        }

        ProductWorkspaceContainerLayoutGestureCompletionResult completion =
            current.Session.Complete(
                state,
                currentEditRevision,
                topology.Generation,
                topology.Displays);
        active = null;
        if (completion.Status ==
            ProductWorkspaceContainerLayoutGestureCompletionStatus.NoChange)
        {
            return Result(
                ProductDesktopContainerLayoutInteractionStatus.NoChange,
                request,
                clearPreview: true);
        }
        if (!completion.IsReady)
        {
            return Reject(request, clearPreview: true);
        }

        ProductWorkspaceContainerLayoutGestureCommitResult committed =
            workspaceCommits.CommitContainerLayoutGesture(
                state,
                topology.Generation,
                completion.Completion!);
        if (!committed.IsAccepted)
        {
            return Reject(request, clearPreview: true);
        }

        publication = committed.PublicationToken;
        return Result(
            ProductDesktopContainerLayoutInteractionStatus.Committed,
            request,
            state: committed.State,
            document: committed.Document,
            clearPreview: true);
    }

    private ProductDesktopContainerLayoutInteractionResult CancelUnsafe(
        ProductDesktopContainerLayoutRequest request)
    {
        if (!MatchesActive(request) || active is not { } current)
        {
            return Reject(request, clearPreview: false);
        }

        _ = current.Session.Cancel();
        active = null;
        return Result(
            ProductDesktopContainerLayoutInteractionStatus.Cancelled,
            request,
            clearPreview: true);
    }

    private bool MatchesActive(ProductDesktopContainerLayoutRequest request) =>
        active is { } current
        && current.Kind == request.Kind
        && string.Equals(
            current.ContainerId,
            request.ContainerId,
            StringComparison.Ordinal)
        && string.Equals(
            current.DisplayId,
            request.DisplayId,
            StringComparison.Ordinal)
        && current.ExpectedWorkspaceRevision == request.ExpectedWorkspaceRevision
        && current.ExpectedTopologyGeneration == request.ExpectedTopologyGeneration;

    private static ProductDesktopContainerLayoutInteractionResult Reject(
        ProductDesktopContainerLayoutRequest request,
        bool clearPreview) =>
        Result(
            ProductDesktopContainerLayoutInteractionStatus.Rejected,
            request,
            clearPreview: clearPreview);

    private static ProductDesktopContainerLayoutInteractionResult Result(
        ProductDesktopContainerLayoutInteractionStatus status,
        ProductDesktopContainerLayoutRequest request,
        ProductContainerPlacementState? previewPlacement = null,
        ProductWorkspaceState? state = null,
        ProductConfigurationDocument? document = null,
        bool clearPreview = false) =>
        new(
            status,
            request.DisplayId,
            request.ContainerId,
            request.ExpectedWorkspaceRevision,
            request.ExpectedTopologyGeneration,
            previewPlacement,
            state,
            document,
            clearPreview);

    private sealed record ActiveGesture(
        ProductWorkspaceContainerLayoutGestureKind Kind,
        string ContainerId,
        string DisplayId,
        long ExpectedWorkspaceRevision,
        long ExpectedTopologyGeneration,
        ProductWorkspaceContainerLayoutGestureSession Session);
}
