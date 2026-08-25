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
    private readonly double? pointerOffsetXDip;
    private readonly double? pointerOffsetYDip;
    private ProductWorkspaceContainerLayoutGestureSessionStatus status;
    private ProductWorkspaceContainerLayoutPreviewStatus previewStatus;
    private ProductContainerPlacementState placement;
    private long updateCount;
    private double lastDeltaX;
    private double lastDeltaY;
    private bool lastSnapEnabled;
    private bool lastShiftPressed;
    private string? lastTargetDisplayId;
    private double? lastTargetXDip;
    private double? lastTargetYDip;
    private bool changed;
    private bool snappedX;
    private bool snappedY;

    private ProductWorkspaceContainerLayoutGestureSession(
        ProductWorkspaceContainerLayoutGestureKind kind,
        string containerId,
        long expectedEditRevision,
        long expectedTopologyGeneration,
        string displayId,
        ProductContainerPlacementState originalPlacement,
        double? pointerOffsetXDip,
        double? pointerOffsetYDip)
    {
        this.kind = kind;
        this.containerId = containerId;
        this.expectedEditRevision = expectedEditRevision;
        this.expectedTopologyGeneration = expectedTopologyGeneration;
        this.displayId = displayId;
        this.originalPlacement = originalPlacement;
        this.pointerOffsetXDip = pointerOffsetXDip;
        this.pointerOffsetYDip = pointerOffsetYDip;
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
        string displayId,
        int? pointerScreenX = null,
        int? pointerScreenY = null)
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

        if ((pointerScreenX is null) != (pointerScreenY is null))
        {
            return new(
                ProductWorkspaceContainerLayoutGestureBeginStatus.Rejected,
                ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest,
                null);
        }

        double? pointerOffsetX = null;
        double? pointerOffsetY = null;
        if (kind == ProductWorkspaceContainerLayoutGestureKind.Move
            && pointerScreenX is { } startX
            && pointerScreenY is { } startY)
        {
            DisplayTopologyNode[] sources = displays!
                .Where(display => string.Equals(
                    display.StableId,
                    displayId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (sources.Length != 1)
            {
                return new(
                    ProductWorkspaceContainerLayoutGestureBeginStatus.Rejected,
                    ProductWorkspaceContainerLayoutPreviewStatus
                        .DisplayUnavailable,
                    null);
            }
            double scale = sources[0].EffectiveDpi / 96d;
            pointerOffsetX =
                (startX - sources[0].WorkArea.Left) / scale
                - decision.Placement!.XDip;
            pointerOffsetY =
                (startY - sources[0].WorkArea.Top) / scale
                - decision.Placement.YDip;
            if (pointerOffsetX < 0
                || pointerOffsetY < 0
                || pointerOffsetX > decision.Placement.WidthDip
                || pointerOffsetY > decision.Placement.HeightDip)
            {
                return new(
                    ProductWorkspaceContainerLayoutGestureBeginStatus.Rejected,
                    ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest,
                    null);
            }
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
                Clone(decision.Placement!),
                pointerOffsetX,
                pointerOffsetY));
    }

    public ProductWorkspaceContainerLayoutGestureSnapshot Update(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays,
        double cumulativeDeltaXDip,
        double cumulativeDeltaYDip,
        bool snapEnabled,
        bool shiftPressed,
        int? pointerScreenX = null,
        int? pointerScreenY = null)
    {
        lock (gate)
        {
            if (status != ProductWorkspaceContainerLayoutGestureSessionStatus.Previewing)
            {
                return Snapshot();
            }

            if (!TryResolvePointerTarget(
                    displays,
                    pointerScreenX,
                    pointerScreenY,
                    out string? targetDisplayId,
                    out double? targetX,
                    out double? targetY))
            {
                status =
                    ProductWorkspaceContainerLayoutGestureSessionStatus.Cancelled;
                previewStatus =
                    ProductWorkspaceContainerLayoutPreviewStatus
                        .DisplayUnavailable;
                placement = Clone(originalPlacement);
                changed = false;
                snappedX = false;
                snappedY = false;
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
                shiftPressed,
                targetDisplayId,
                targetX,
                targetY);
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
            lastTargetDisplayId = targetDisplayId;
            lastTargetXDip = targetX;
            lastTargetYDip = targetY;
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
                lastShiftPressed,
                lastTargetDisplayId,
                lastTargetXDip,
                lastTargetYDip);
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
                    decision.Placement!.DisplayKey,
                    Clone(originalPlacement),
                    Clone(decision.Placement),
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
        bool shiftPressed,
        string? targetDisplayId = null,
        double? targetXDip = null,
        double? targetYDip = null) =>
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
                shiftPressed,
                targetDisplayId,
                targetXDip,
                targetYDip));

    private bool TryResolvePointerTarget(
        IReadOnlyList<DisplayTopologyNode>? displays,
        int? pointerScreenX,
        int? pointerScreenY,
        out string? targetDisplayId,
        out double? targetXDip,
        out double? targetYDip)
    {
        targetDisplayId = null;
        targetXDip = null;
        targetYDip = null;
        bool anchored = pointerOffsetXDip is not null
            || pointerOffsetYDip is not null;
        if (!anchored)
        {
            return (pointerScreenX is null) == (pointerScreenY is null);
        }
        if (kind != ProductWorkspaceContainerLayoutGestureKind.Move
            || pointerOffsetXDip is not { } offsetX
            || pointerOffsetYDip is not { } offsetY
            || pointerScreenX is not { } screenX
            || pointerScreenY is not { } screenY
            || displays is null)
        {
            return false;
        }

        DisplayTopologyNode[] targets = displays
            .Where(display =>
                screenX >= display.Bounds.Left
                && screenX < display.Bounds.Right
                && screenY >= display.Bounds.Top
                && screenY < display.Bounds.Bottom)
            .Take(2)
            .ToArray();
        if (targets.Length != 1)
        {
            return false;
        }

        DisplayTopologyNode target = targets[0];
        double scale = target.EffectiveDpi / 96d;
        targetDisplayId = target.StableId;
        targetXDip = (screenX - target.WorkArea.Left) / scale - offsetX;
        targetYDip = (screenY - target.WorkArea.Top) / scale - offsetY;
        return double.IsFinite(targetXDip.Value)
            && double.IsFinite(targetYDip.Value);
    }

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
