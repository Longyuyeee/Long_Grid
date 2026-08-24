using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerLayoutGestureKind
{
    Move,
    ResizeLeft,
    ResizeTop,
    ResizeRight,
    ResizeBottom,
    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight,
}

public enum ProductWorkspaceContainerLayoutPreviewStatus
{
    Ready,
    InvalidRequest,
    ContainerNotFound,
    ContainerLocked,
    StaleEditRevision,
    StaleTopology,
    DisplayUnavailable,
}

public sealed record ProductWorkspaceContainerLayoutPreviewRequest(
    ProductWorkspaceContainerLayoutGestureKind Kind,
    string ContainerId,
    long ExpectedEditRevision,
    long ExpectedTopologyGeneration,
    string DisplayId,
    double DeltaXDip,
    double DeltaYDip,
    bool SnapEnabled,
    bool ShiftPressed,
    string? TargetDisplayId = null,
    double? TargetXDip = null,
    double? TargetYDip = null);

public sealed record ProductWorkspaceContainerLayoutPreviewDecision(
    ProductWorkspaceContainerLayoutPreviewStatus Status,
    ProductContainerPlacementState? Placement,
    bool Changed,
    bool SnappedX,
    bool SnappedY)
{
    public bool CanPreview =>
        Status == ProductWorkspaceContainerLayoutPreviewStatus.Ready
        && Placement is not null;
}

public static class ProductWorkspaceContainerLayoutPreview
{
    public const double GridSizeDip = 8;
    public const double SnapThresholdDip = 6;
    public const double MinimumWidthDip = 160;
    public const double MinimumHeightDip = 120;
    private const double MaximumAbsoluteDeltaDip = 1_000_000;

    public static ProductWorkspaceContainerLayoutPreviewDecision Evaluate(
        ProductWorkspaceState? state,
        long currentEditRevision,
        long currentTopologyGeneration,
        IReadOnlyList<DisplayTopologyNode>? displays,
        ProductWorkspaceContainerLayoutPreviewRequest? request)
    {
        if (state?.Containers is null
            || displays is null
            || request is null
            || !Enum.IsDefined(request.Kind)
            || string.IsNullOrWhiteSpace(request.ContainerId)
            || string.IsNullOrWhiteSpace(request.DisplayId)
            || request.ExpectedEditRevision <= 0
            || request.ExpectedTopologyGeneration <= 0
            || !double.IsFinite(request.DeltaXDip)
            || !double.IsFinite(request.DeltaYDip)
            || Math.Abs(request.DeltaXDip) > MaximumAbsoluteDeltaDip
            || Math.Abs(request.DeltaYDip) > MaximumAbsoluteDeltaDip)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest);
        }

        if (request.ExpectedEditRevision != currentEditRevision)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.StaleEditRevision);
        }

        if (request.ExpectedTopologyGeneration != currentTopologyGeneration)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.StaleTopology);
        }

        ProductContainerState[] matchingContainers = state.Containers
            .Where(candidate => candidate is not null && string.Equals(
                candidate.Id,
                request.ContainerId,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingContainers.Length != 1)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.ContainerNotFound);
        }
        ProductContainerState container = matchingContainers[0];

        if (container.IsLocked)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.ContainerLocked);
        }

        DisplayTopologyNode[] matchingSourceDisplays = displays
            .Where(candidate => candidate is not null && string.Equals(
                candidate.StableId,
                request.DisplayId,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingSourceDisplays.Length != 1)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable);
        }
        DisplayTopologyNode sourceDisplay = matchingSourceDisplays[0];
        ProductContainerPlacementState source = container.Placement;
        if (!sourceDisplay.WorkArea.HasArea
            || sourceDisplay.EffectiveDpi is < 48 or > 768
            || source is null
            || !string.Equals(
                source.DisplayKey,
                request.DisplayId,
                StringComparison.Ordinal))
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable);
        }

        string targetDisplayId = string.IsNullOrWhiteSpace(
            request.TargetDisplayId)
                ? request.DisplayId
                : request.TargetDisplayId;
        DisplayTopologyNode[] matchingTargetDisplays = displays
            .Where(candidate => candidate is not null && string.Equals(
                candidate.StableId,
                targetDisplayId,
                StringComparison.Ordinal))
            .ToArray();
        bool hasAbsoluteTarget = request.TargetXDip is not null
            || request.TargetYDip is not null;
        bool crossesDisplay = !string.Equals(
            request.DisplayId,
            targetDisplayId,
            StringComparison.Ordinal);
        if (matchingTargetDisplays.Length != 1
            || (hasAbsoluteTarget
                && (request.TargetXDip is not { } targetX
                    || request.TargetYDip is not { } targetY
                    || !double.IsFinite(targetX)
                    || !double.IsFinite(targetY)
                    || Math.Abs(targetX) > MaximumAbsoluteDeltaDip
                    || Math.Abs(targetY) > MaximumAbsoluteDeltaDip
                    || request.Kind !=
                        ProductWorkspaceContainerLayoutGestureKind.Move))
            || (crossesDisplay
                && (request.Kind !=
                        ProductWorkspaceContainerLayoutGestureKind.Move
                    || !hasAbsoluteTarget)))
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable);
        }

        DisplayTopologyNode targetDisplay = matchingTargetDisplays[0];
        if (!targetDisplay.WorkArea.HasArea
            || targetDisplay.EffectiveDpi is < 48 or > 768)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.DisplayUnavailable);
        }

        double sourceScale = sourceDisplay.EffectiveDpi / 96d;
        double sourceWorkWidth = sourceDisplay.WorkArea.Width / sourceScale;
        double sourceWorkHeight = sourceDisplay.WorkArea.Height / sourceScale;
        double targetScale = targetDisplay.EffectiveDpi / 96d;
        double workWidth = targetDisplay.WorkArea.Width / targetScale;
        double workHeight = targetDisplay.WorkArea.Height / targetScale;
        if (!ValidPlacement(source)
            || sourceWorkWidth < MinimumWidthDip
            || sourceWorkHeight < MinimumHeightDip
            || workWidth < MinimumWidthDip
            || workHeight < MinimumHeightDip
            || source.WidthDip > workWidth
            || source.HeightDip > workHeight
            || source.XDip < 0
            || source.YDip < 0
            || source.XDip + source.WidthDip > sourceWorkWidth
            || source.YDip + source.HeightDip > sourceWorkHeight)
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest);
        }

        double left = hasAbsoluteTarget
            ? request.TargetXDip!.Value
            : source.XDip;
        double top = hasAbsoluteTarget
            ? request.TargetYDip!.Value
            : source.YDip;
        double right = source.XDip + source.WidthDip;
        double bottom = source.YDip + source.HeightDip;
        if (hasAbsoluteTarget)
        {
            right = left + source.WidthDip;
            bottom = top + source.HeightDip;
        }
        else
        {
            ApplyDelta(request.Kind, request.DeltaXDip, request.DeltaYDip,
                ref left, ref top, ref right, ref bottom);
        }
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(right)
            || !double.IsFinite(bottom))
        {
            return Failure(
                ProductWorkspaceContainerLayoutPreviewStatus.InvalidRequest);
        }

        bool snap = request.SnapEnabled != request.ShiftPressed;
        bool snappedX = false;
        bool snappedY = false;
        if (snap)
        {
            double[] xEdges = Edges(
                state,
                container.Id,
                targetDisplayId,
                horizontal: true,
                workWidth);
            double[] yEdges = Edges(
                state,
                container.Id,
                targetDisplayId,
                horizontal: false,
                workHeight);
            SnapHorizontal(
                request.Kind,
                source.WidthDip,
                xEdges,
                ref left,
                ref right,
                ref snappedX);
            SnapVertical(
                request.Kind,
                source.HeightDip,
                yEdges,
                ref top,
                ref bottom,
                ref snappedY);
        }

        double snappedLeft = left;
        double snappedTop = top;
        double snappedRight = right;
        double snappedBottom = bottom;
        Constrain(
            request.Kind,
            workWidth,
            workHeight,
            ref left,
            ref top,
            ref right,
            ref bottom);
        snappedX = snappedX
            && NearlyEqual(left, snappedLeft)
            && NearlyEqual(right, snappedRight);
        snappedY = snappedY
            && NearlyEqual(top, snappedTop)
            && NearlyEqual(bottom, snappedBottom);
        var placement = source with
        {
            DisplayKey = targetDisplayId,
            XDip = left,
            YDip = top,
            WidthDip = right - left,
            HeightDip = bottom - top,
        };
        bool changed = !string.Equals(
                source.DisplayKey,
                placement.DisplayKey,
                StringComparison.Ordinal)
            || !NearlyEqual(source.XDip, placement.XDip)
            || !NearlyEqual(source.YDip, placement.YDip)
            || !NearlyEqual(source.WidthDip, placement.WidthDip)
            || !NearlyEqual(source.HeightDip, placement.HeightDip);
        return new(
            ProductWorkspaceContainerLayoutPreviewStatus.Ready,
            placement,
            changed,
            snappedX,
            snappedY);
    }

    private static void ApplyDelta(
        ProductWorkspaceContainerLayoutGestureKind kind,
        double deltaX,
        double deltaY,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (kind == ProductWorkspaceContainerLayoutGestureKind.Move)
        {
            left += deltaX;
            right += deltaX;
            top += deltaY;
            bottom += deltaY;
            return;
        }

        if (ChangesLeft(kind))
        {
            left += deltaX;
        }
        if (ChangesRight(kind))
        {
            right += deltaX;
        }
        if (ChangesTop(kind))
        {
            top += deltaY;
        }
        if (ChangesBottom(kind))
        {
            bottom += deltaY;
        }
    }

    private static double[] Edges(
        ProductWorkspaceState state,
        string containerId,
        string displayId,
        bool horizontal,
        double workExtent)
    {
        var edges = new List<double>(2 + (state.Containers.Count * 2))
        {
            0,
            workExtent,
        };
        foreach (ProductContainerState candidate in state.Containers)
        {
            if (string.Equals(candidate.Id, containerId, StringComparison.Ordinal)
                || !string.Equals(
                    candidate.Placement.DisplayKey,
                    displayId,
                    StringComparison.Ordinal)
                || !ValidPlacement(candidate.Placement))
            {
                continue;
            }

            edges.Add(horizontal
                ? candidate.Placement.XDip
                : candidate.Placement.YDip);
            edges.Add(horizontal
                ? candidate.Placement.XDip + candidate.Placement.WidthDip
                : candidate.Placement.YDip + candidate.Placement.HeightDip);
        }
        return edges.ToArray();
    }

    private static void SnapHorizontal(
        ProductWorkspaceContainerLayoutGestureKind kind,
        double originalWidth,
        IReadOnlyList<double> edges,
        ref double left,
        ref double right,
        ref bool snapped)
    {
        if (kind == ProductWorkspaceContainerLayoutGestureKind.Move)
        {
            double adjustment = ClosestAdjustment(left, right, edges);
            if (Math.Abs(adjustment) <= SnapThresholdDip)
            {
                left += adjustment;
                right = left + originalWidth;
                snapped = true;
            }
        }
        else if (ChangesLeft(kind))
        {
            SnapEdge(ref left, edges, ref snapped);
        }
        else if (ChangesRight(kind))
        {
            SnapEdge(ref right, edges, ref snapped);
        }
    }

    private static void SnapVertical(
        ProductWorkspaceContainerLayoutGestureKind kind,
        double originalHeight,
        IReadOnlyList<double> edges,
        ref double top,
        ref double bottom,
        ref bool snapped)
    {
        if (kind == ProductWorkspaceContainerLayoutGestureKind.Move)
        {
            double adjustment = ClosestAdjustment(top, bottom, edges);
            if (Math.Abs(adjustment) <= SnapThresholdDip)
            {
                top += adjustment;
                bottom = top + originalHeight;
                snapped = true;
            }
        }
        else if (ChangesTop(kind))
        {
            SnapEdge(ref top, edges, ref snapped);
        }
        else if (ChangesBottom(kind))
        {
            SnapEdge(ref bottom, edges, ref snapped);
        }
    }

    private static double ClosestAdjustment(
        double start,
        double end,
        IReadOnlyList<double> edges)
    {
        double best = GridAdjustment(start);
        Consider(GridAdjustment(end), ref best);
        foreach (double edge in edges)
        {
            Consider(edge - start, ref best);
            Consider(edge - end, ref best);
        }
        return best;
    }

    private static void SnapEdge(
        ref double value,
        IReadOnlyList<double> edges,
        ref bool snapped)
    {
        double adjustment = GridAdjustment(value);
        foreach (double edge in edges)
        {
            Consider(edge - value, ref adjustment);
        }
        if (Math.Abs(adjustment) <= SnapThresholdDip)
        {
            value += adjustment;
            snapped = true;
        }
    }

    private static double GridAdjustment(double value) =>
        (Math.Round(value / GridSizeDip, MidpointRounding.AwayFromZero)
            * GridSizeDip) - value;

    private static void Consider(double candidate, ref double best)
    {
        if (Math.Abs(candidate) < Math.Abs(best))
        {
            best = candidate;
        }
    }

    private static void Constrain(
        ProductWorkspaceContainerLayoutGestureKind kind,
        double workWidth,
        double workHeight,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (kind == ProductWorkspaceContainerLayoutGestureKind.Move)
        {
            double width = Math.Min(right - left, workWidth);
            double height = Math.Min(bottom - top, workHeight);
            left = Math.Clamp(left, 0, workWidth - width);
            top = Math.Clamp(top, 0, workHeight - height);
            right = left + width;
            bottom = top + height;
            return;
        }

        if (ChangesLeft(kind))
        {
            left = Math.Clamp(left, 0, right - MinimumWidthDip);
        }
        if (ChangesRight(kind))
        {
            right = Math.Clamp(right, left + MinimumWidthDip, workWidth);
        }
        if (ChangesTop(kind))
        {
            top = Math.Clamp(top, 0, bottom - MinimumHeightDip);
        }
        if (ChangesBottom(kind))
        {
            bottom = Math.Clamp(bottom, top + MinimumHeightDip, workHeight);
        }
    }

    private static bool ChangesLeft(ProductWorkspaceContainerLayoutGestureKind kind) =>
        kind is ProductWorkspaceContainerLayoutGestureKind.ResizeLeft
            or ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft
            or ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft;

    private static bool ChangesRight(ProductWorkspaceContainerLayoutGestureKind kind) =>
        kind is ProductWorkspaceContainerLayoutGestureKind.ResizeRight
            or ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight
            or ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight;

    private static bool ChangesTop(ProductWorkspaceContainerLayoutGestureKind kind) =>
        kind is ProductWorkspaceContainerLayoutGestureKind.ResizeTop
            or ProductWorkspaceContainerLayoutGestureKind.ResizeTopLeft
            or ProductWorkspaceContainerLayoutGestureKind.ResizeTopRight;

    private static bool ChangesBottom(ProductWorkspaceContainerLayoutGestureKind kind) =>
        kind is ProductWorkspaceContainerLayoutGestureKind.ResizeBottom
            or ProductWorkspaceContainerLayoutGestureKind.ResizeBottomLeft
            or ProductWorkspaceContainerLayoutGestureKind.ResizeBottomRight;

    private static bool ValidPlacement(ProductContainerPlacementState placement) =>
        placement is not null
        && !string.IsNullOrWhiteSpace(placement.DisplayKey)
        && double.IsFinite(placement.XDip)
        && double.IsFinite(placement.YDip)
        && double.IsFinite(placement.WidthDip)
        && double.IsFinite(placement.HeightDip)
        && placement.WidthDip >= MinimumWidthDip
        && placement.HeightDip >= MinimumHeightDip;

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) < 0.001;

    private static ProductWorkspaceContainerLayoutPreviewDecision Failure(
        ProductWorkspaceContainerLayoutPreviewStatus status) =>
        new(status, Placement: null, Changed: false, SnappedX: false, SnappedY: false);
}
