using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerLayoutGestureSessionStatus
{
    Previewing,
    Cancelled,
    Completed,
}

public enum ProductWorkspaceContainerLayoutGestureBeginStatus
{
    Ready,
    Rejected,
}

public sealed record ProductWorkspaceContainerLayoutGestureBeginResult(
    ProductWorkspaceContainerLayoutGestureBeginStatus Status,
    ProductWorkspaceContainerLayoutPreviewStatus PreviewStatus,
    ProductWorkspaceContainerLayoutGestureSession? Session)
{
    public bool IsReady =>
        Status == ProductWorkspaceContainerLayoutGestureBeginStatus.Ready
        && Session is not null;
}

public sealed record ProductWorkspaceContainerLayoutGestureSnapshot(
    ProductWorkspaceContainerLayoutGestureSessionStatus Status,
    ProductWorkspaceContainerLayoutPreviewStatus PreviewStatus,
    ProductContainerPlacementState Placement,
    long UpdateCount,
    bool Changed,
    bool SnappedX,
    bool SnappedY);

public enum ProductWorkspaceContainerLayoutGestureCompletionStatus
{
    Ready,
    NoChange,
    Unavailable,
    Rejected,
}

public sealed record ProductWorkspaceContainerLayoutGestureCompletionResult(
    ProductWorkspaceContainerLayoutGestureCompletionStatus Status,
    ProductWorkspaceContainerLayoutPreviewStatus PreviewStatus,
    ProductWorkspaceContainerLayoutGestureCompletion? Completion)
{
    public bool IsReady =>
        Status == ProductWorkspaceContainerLayoutGestureCompletionStatus.Ready
        && Completion is not null;
}

public sealed class ProductWorkspaceContainerLayoutGestureCompletion
{
    internal ProductWorkspaceContainerLayoutGestureCompletion(
        Guid operationId,
        string containerId,
        long expectedEditRevision,
        long expectedTopologyGeneration,
        string displayId,
        ProductContainerPlacementState originalPlacement,
        ProductContainerPlacementState placement,
        long updateCount)
    {
        OperationId = operationId;
        ContainerId = containerId;
        ExpectedEditRevision = expectedEditRevision;
        ExpectedTopologyGeneration = expectedTopologyGeneration;
        DisplayId = displayId;
        OriginalPlacement = originalPlacement;
        Placement = placement;
        UpdateCount = updateCount;
    }

    public Guid OperationId { get; }
    public string ContainerId { get; }
    public long ExpectedEditRevision { get; }
    public long ExpectedTopologyGeneration { get; }
    public string DisplayId { get; }
    public ProductContainerPlacementState OriginalPlacement { get; }
    public ProductContainerPlacementState Placement { get; }
    public long UpdateCount { get; }
}

public sealed class ProductWorkspaceContainerLayoutGestureSession
{
    private readonly object gate = new();
    private readonly Guid operationId = Guid.NewGuid();
    private readonly ProductWorkspaceContainerLayoutGestureKind kind;
    private readonly string containerId;
    private readonly long expectedEditRevision;
    private readonly long expectedTopologyGeneration;
    private readonly string displayId;
    private readonly ProductContainerPlacementState originalPlacement;
    private ProductWorkspaceContainerLayoutGestureSessionStatus status;
    private ProductWorkspaceContainerLayoutPreviewStatus previewStatus;
    private ProductContainerPlacementState placement;
    private long updateCount;
    private double lastDeltaX;
    private double lastDeltaY;
    private bool lastSnapEnabled;
    private bool lastShiftPressed;
    private bool changed;
    private bool snappedX;
    private bool snappedY;

    private ProductWorkspaceContainerLayoutGestureSession(
        ProductWorkspaceContainerLayoutGestureKind kind,
        string containerId,
        long expectedEditRevision,
        long expectedTopologyGeneration,
        string displayId,
        ProductContainerPlacementState originalPlacement)
    {
        this.kind = kind;
        this.containerId = containerId;
        this.expectedEditRevision = expectedEditRevision;
        this.expectedTopologyGeneration = expectedTopologyGeneration;
        this.displayId = displayId;
        this.originalPlacement = originalPlacement;
        placement = originalPlacement;
        status = ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing;
        previewStatus = ProductWorkspaceContainerLayoutPreviewStatus.Ready;
    }

    public static ProductWorkspaceContainerLayoutGestureBeginResult Begin(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays,
        ProductWorkspaceContainerLayoutGestureKind kind,
        string containerId,
        string displayId)
    {
        var request = new ProductWorkspaceContainerLayoutPreviewRequest(
            kind,
            containerId,
            currentEditRevision,
            currentTopologyGeneration,
            displayId,
            DeltaXDip: 0,
            DeltaYDip: 0,
            SnapEnabled: false,
            ShiftPressed: false);
        ProductWorkspaceContainerLayoutPreviewDecision decision =
            ProductWorkspaceContainerLayoutPreview.Evaluate(
                state,
                currentEditRevision,
                currentTopologyGeneration,
                displays,
                request);
        if (!decision.CanPreview)
        {
            return new(
                ProductWorkspaceContainerLayoutGestureBeginStatus.Rejected,
                decision.Status,
                null);
        }

        return new(
            ProductWorkspaceContainerLayoutGestureBeginStatus.Ready,
            ProductWorkspaceContainerLayoutPreviewStatus.Ready,
            new(
                kind,
                containerId,
                currentEditRevision,
                currentTopologyGeneration,
                displayId,
                Clone(decision.Placement!)));
    }

    public ProductWorkspaceContainerLayoutGestureSnapshot Update(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays,
        double cumulativeDeltaXDip,
        double cumulativeDeltaYDip,
        bool snapEnabled,
        bool shiftPressed)
    {
        lock (gate)
        {
            if (status != ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing)
            {
                return Snapshot();
            }

            ProductWorkspaceContainerLayoutPreviewDecision decision = Evaluate(
                state,
                currentEditRevision,
                currentTopologyGeneration,
                displays,
                cumulativeDeltaXDip,
                cumulativeDeltaYDip,
                snapEnabled,
                shiftPressed);
            if (!decision.CanPreview)
            {
                status = ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled;
                previewStatus = decision.Status;
                placement = Clone(originalPlacement);
                changed = false;
                snappedX = false;
                snappedY = false;
                return Snapshot();
            }

            updateCount = checked(updateCount + 1);
            lastDeltaX = cumulativeDeltaXDip;
            lastDeltaY = cumulativeDeltaYDip;
            lastSnapEnabled = snapEnabled;
            lastShiftPressed = shiftPressed;
            previewStatus = decision.Status;
            placement = Clone(decision.Placement!);
            changed = decision.Changed;
            snappedX = decision.SnappedX;
            snappedY = decision.SnappedY;
            return Snapshot();
        }
    }

    public ProductWorkspaceContainerLayoutGestureSnapshot Cancel()
    {
        lock (gate)
        {
            if (status == ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing)
            {
                status = ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled;
                placement = Clone(originalPlacement);
                changed = false;
                snappedX = false;
                snappedY = false;
            }
            return Snapshot();
        }
    }

    public ProductWorkspaceContainerLayoutGestureCompletionResult Complete(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays)
    {
        lock (gate)
        {
            if (status != ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing)
            {
                return new(
                    ProductWorkspaceContainerLayoutGestureCompletionStatus.Unavailable,
                    previewStatus,
                    null);
            }

            ProductWorkspaceContainerLayoutPreviewDecision decision = Evaluate(
                state,
                currentEditRevision,
                currentTopologyGeneration,
                displays,
                lastDeltaX,
                lastDeltaY,
                lastSnapEnabled,
                lastShiftPressed);
            if (!decision.CanPreview)
            {
                status = ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled;
                previewStatus = decision.Status;
                placement = Clone(originalPlacement);
                changed = false;
                return new(
                    ProductWorkspaceContainerLayoutGestureCompletionStatus.Rejected,
                    decision.Status,
                    null);
            }

            status = ProductWorkspaceContainerLayoutGestureSessionStatus.Completed;
            previewStatus = decision.Status;
            placement = Clone(decision.Placement!);
            changed = decision.Changed;
            if (!decision.Changed)
            {
                return new(
                    ProductWorkspaceContainerLayoutGestureCompletionStatus.NoChange,
                    decision.Status,
                    null);
            }

            return new(
                ProductWorkspaceContainerLayoutGestureCompletionStatus.Ready,
                decision.Status,
                new(
                    operationId,
                    containerId,
                    expectedEditRevision,
                    expectedTopologyGeneration,
                    displayId,
                    Clone(originalPlacement),
                    Clone(decision.Placement!),
                    updateCount));
        }
    }

    private ProductWorkspaceContainerLayoutPreviewDecision Evaluate(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays,
        double deltaX,
        double deltaY,
        bool snapEnabled,
        bool shiftPressed) =>
        ProductWorkspaceContainerLayoutPreview.Evaluate(
            state,
            currentEditRevision,
            currentTopologyGeneration,
            displays,
            new(
                kind,
                containerId,
                expectedEditRevision,
                expectedTopologyGeneration,
                displayId,
                deltaX,
                deltaY,
                snapEnabled,
                shiftPressed));

    private ProductWorkspaceContainerLayoutGestureSnapshot Snapshot() =>
        new(
            status,
            previewStatus,
            Clone(placement),
            updateCount,
            changed,
            snappedX,
            snappedY);

    private static ProductContainerPlacementState Clone(
        ProductContainerPlacementState source) =>
        source with
        {
            ExtensionData = source.ExtensionData is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(
                    source.ExtensionData,
                    StringComparer.Ordinal),
        };
}
