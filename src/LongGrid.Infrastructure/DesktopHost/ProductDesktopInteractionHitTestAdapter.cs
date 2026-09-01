using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopInteractionHitRegion
{
    None,
    Header,
    Content,
    VisibleItem,
}

public enum ProductDesktopInteractionHitStatus
{
    Hit,
    OutsideSurface,
    NoTarget,
    AmbiguousTarget,
}

public sealed record ProductDesktopInteractionHitTestResult
{
    private ProductDesktopInteractionHitTestResult(
        ProductDesktopInteractionHitStatus status,
        string? containerId,
        ProductDesktopInteractionHitRegion region,
        int visibleItemIndex)
    {
        Status = status;
        ContainerId = containerId;
        Region = region;
        VisibleItemIndex = visibleItemIndex;
    }

    public ProductDesktopInteractionHitStatus Status { get; }

    public string? ContainerId { get; }

    public ProductDesktopInteractionHitRegion Region { get; }

    public int VisibleItemIndex { get; }

    public bool IsHit =>
        Status == ProductDesktopInteractionHitStatus.Hit
        && !string.IsNullOrWhiteSpace(ContainerId)
        && Region != ProductDesktopInteractionHitRegion.None;

    internal static ProductDesktopInteractionHitTestResult CreateHit(
        string containerId,
        ProductDesktopInteractionHitRegion region,
        int visibleItemIndex) =>
        new(
            ProductDesktopInteractionHitStatus.Hit,
            containerId,
            region,
            visibleItemIndex);

    internal static ProductDesktopInteractionHitTestResult CreateMiss(
        ProductDesktopInteractionHitStatus status) =>
        new(status, null, ProductDesktopInteractionHitRegion.None, -1);
}

public static class ProductDesktopInteractionHitTestAdapter
{
    public static ProductDesktopInteractionHitTestResult HitTest(
        ProductDesktopHostDisplayProjection display,
        int clientX,
        int clientY)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (clientX < 0
            || clientY < 0
            || clientX >= display.WorkArea.Width
            || clientY >= display.WorkArea.Height)
        {
            return Miss(ProductDesktopInteractionHitStatus.OutsideSurface);
        }

        var matches = new List<(ProductDesktopHostReadOnlyProjection Container,
            PixelRect Bounds)>();
        foreach (ProductDesktopHostReadOnlyProjection candidate
            in display.Containers)
        {
            PixelRect candidateBounds = ProductDesktopHostSurfaceLayout
                .GetContainerBounds(display, candidate);
            if (Contains(candidateBounds, clientX, clientY))
            {
                matches.Add((candidate, candidateBounds));
            }
        }

        if (matches.Count == 0)
        {
            return Miss(ProductDesktopInteractionHitStatus.NoTarget);
        }

        if (matches.Count != 1)
        {
            return Miss(ProductDesktopInteractionHitStatus.AmbiguousTarget);
        }

        (ProductDesktopHostReadOnlyProjection container, PixelRect bounds) =
            matches[0];
        int relativeY = clientY - bounds.Top;
        double scale = display.EffectiveDpi / 96d;
        int headerHeight = ProductDesktopHostSurfaceLayout.ToPixels(
            ProductDesktopHostSurfaceLayout.HeaderHeightDip,
            scale);
        if (relativeY < Math.Min(headerHeight, bounds.Height))
        {
            return Hit(
                container.ContainerId,
                ProductDesktopInteractionHitRegion.Header);
        }

        if (!container.IsCollapsed && container.ItemNames.Count > 0)
        {
            int itemHeight = ProductDesktopHostSurfaceLayout.ToPixels(
                ProductDesktopHostSurfaceLayout.GetItemHeightDip(container),
                scale);
            int visibleItemCount = Math.Min(
                container.ItemNames.Count,
                Math.Max(0, (bounds.Height - headerHeight) / itemHeight));
            int itemIndex = (relativeY - headerHeight) / itemHeight;
            if (itemIndex >= 0 && itemIndex < visibleItemCount)
            {
                return Hit(
                    container.ContainerId,
                    ProductDesktopInteractionHitRegion.VisibleItem,
                    itemIndex);
            }
        }

        return Hit(
            container.ContainerId,
            ProductDesktopInteractionHitRegion.Content);
    }

    private static bool Contains(PixelRect bounds, int x, int y) =>
        x >= bounds.Left
        && y >= bounds.Top
        && x < checked(bounds.Left + bounds.Width)
        && y < checked(bounds.Top + bounds.Height);

    private static ProductDesktopInteractionHitTestResult Hit(
        string containerId,
        ProductDesktopInteractionHitRegion region,
        int visibleItemIndex = -1) =>
        ProductDesktopInteractionHitTestResult.CreateHit(
            containerId,
            region,
            visibleItemIndex);

    private static ProductDesktopInteractionHitTestResult Miss(
        ProductDesktopInteractionHitStatus status) =>
        ProductDesktopInteractionHitTestResult.CreateMiss(status);
}

public enum ProductDesktopInteractionActivationKind
{
    PrimaryPointerPress,
    KeyboardActivation,
    AssistiveTechnologyActivation,
}

public enum ProductDesktopInteractionIntentCreationStatus
{
    Created,
    InvalidActivation,
    HitRequired,
    InvalidEvidence,
}

public sealed record ProductDesktopInteractionIntentCreationResult(
    ProductDesktopInteractionIntentCreationStatus Status,
    ProductDesktopInteractionIntent? Intent)
{
    public bool IsCreated =>
        Status == ProductDesktopInteractionIntentCreationStatus.Created
        && Intent is not null;
}

public static class ProductDesktopInteractionIntentFactory
{
    public static ProductDesktopInteractionIntentCreationResult Create(
        ProductDesktopInteractionActivationKind activation,
        ProductDesktopInteractionHitTestResult? hit,
        ProductDesktopInteractionEvidence evidence,
        Guid intentId,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!Enum.IsDefined(activation) || intentId == Guid.Empty)
        {
            return Failure(
                ProductDesktopInteractionIntentCreationStatus
                    .InvalidActivation);
        }

        if (hit?.IsHit != true)
        {
            return Failure(
                ProductDesktopInteractionIntentCreationStatus.HitRequired);
        }

        if (evidence.WorkspaceRevision <= 0
            || evidence.TopologyGeneration <= 0
            || evidence.WindowRegistryGeneration <= 0)
        {
            return Failure(
                ProductDesktopInteractionIntentCreationStatus.InvalidEvidence);
        }

        if (nowUtc > DateTimeOffset.MaxValue.Subtract(
                ProductDesktopInteractionAdmissionController
                    .MaximumIntentLifetime))
        {
            return Failure(
                ProductDesktopInteractionIntentCreationStatus.InvalidEvidence);
        }

        return new(
            ProductDesktopInteractionIntentCreationStatus.Created,
            new(
                intentId,
                hit.ContainerId!,
                evidence.WorkspaceRevision,
                evidence.TopologyGeneration,
                evidence.WindowRegistryGeneration,
                nowUtc,
                nowUtc.Add(
                    ProductDesktopInteractionAdmissionController
                        .MaximumIntentLifetime)));
    }

    private static ProductDesktopInteractionIntentCreationResult Failure(
        ProductDesktopInteractionIntentCreationStatus status) =>
        new(status, Intent: null);
}
