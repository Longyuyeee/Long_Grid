using System.Security.Cryptography;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceLayoutRecoveryReviewToken(
    long TopologyGeneration,
    long EditRevision,
    string SavedTopologyFingerprint,
    string CurrentTopologyFingerprint,
    string ConfigurationFingerprint,
    int ContainerCount,
    int DisplayMappingCount,
    int VisibilityCorrectionCount);

public sealed record ProductWorkspaceLayoutRecoveryReviewResult(
    ProductWorkspaceLayoutRecoveryPreviewResult Preview,
    ProductWorkspaceLayoutRecoveryReviewToken? Token)
{
    public bool CanConfirm =>
        Preview.Status == ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired
        && Token is not null;
}

public enum ProductWorkspaceLayoutRecoveryConfirmationStatus
{
    Accepted,
    ConfirmationRequired,
    TopologyGenerationChanged,
    EditRevisionChanged,
    TokenMismatch,
    PlanUnavailable,
    PlanBlocked,
    ContainerLocked,
    InvalidState,
}

public sealed record ProductWorkspaceLayoutRecoveryConfirmationResult(
    ProductWorkspaceLayoutRecoveryConfirmationStatus Status,
    ProductWorkspaceEditResult? Edit)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceLayoutRecoveryConfirmationStatus.Accepted
        && Edit is { IsSuccess: true, Changed: true };
}

public static class ProductWorkspaceLayoutRecoveryReview
{
    public static ProductWorkspaceLayoutRecoveryReviewResult Prepare(
        ProductWorkspaceState? state,
        IReadOnlyList<DisplayTopologyNode>? currentTopology,
        bool currentTopologyAuthoritative,
        long topologyGeneration,
        long editRevision)
    {
        IReadOnlyList<DisplayTopologyNode>? savedTopology =
            ProductSavedDisplayTopology.ToNodes(state?.SavedDisplayTopology);
        ProductWorkspaceLayoutRecoveryPreviewResult preview =
            ProductWorkspaceLayoutRecoveryPreview.Create(
                state,
                savedTopology,
                currentTopology,
                currentTopologyAuthoritative);
        if (preview.Status != ProductWorkspaceLayoutRecoveryPreviewStatus.ReviewRequired
            || state is null
            || currentTopology is null
            || topologyGeneration <= 0
            || editRevision < 0)
        {
            return new(preview, null);
        }

        try
        {
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(state);
            if (!projection.IsSuccess)
            {
                return Invalid(preview.ContainerCount);
            }

            return new(
                preview,
                new(
                    topologyGeneration,
                    editRevision,
                    DisplayTopologyFingerprint.Compute(savedTopology!),
                    DisplayTopologyFingerprint.Compute(currentTopology),
                    Fingerprint(projection.Document!),
                    preview.ContainerCount,
                    preview.DisplayMappingCount,
                    preview.VisibilityCorrectionCount));
        }
        catch (ArgumentException)
        {
            return Invalid(preview.ContainerCount);
        }
        catch (OverflowException)
        {
            return Invalid(preview.ContainerCount);
        }
    }

    public static ProductWorkspaceLayoutRecoveryConfirmationResult Confirm(
        ProductWorkspaceState state,
        IReadOnlyList<DisplayTopologyNode>? currentTopology,
        bool currentTopologyAuthoritative,
        long topologyGeneration,
        long editRevision,
        ProductWorkspaceLayoutRecoveryReviewToken token,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);
        if (token.TopologyGeneration != topologyGeneration)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryConfirmationStatus
                    .TopologyGenerationChanged);
        }

        if (token.EditRevision != editRevision)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryConfirmationStatus.EditRevisionChanged);
        }

        Evaluation evaluation = Evaluate(
            state,
            currentTopology,
            currentTopologyAuthoritative,
            topologyGeneration,
            editRevision);
        if (evaluation.Review.Token is null || evaluation.Plan is null)
        {
            return Failure(MapUnavailable(evaluation.Review.Preview.Status));
        }

        if (evaluation.Review.Token != token)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryConfirmationStatus.TokenMismatch);
        }

        if (!confirmed)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryConfirmationStatus
                    .ConfirmationRequired);
        }

        return Apply(state, currentTopology!, evaluation.Plan);
    }

    private static Evaluation Evaluate(
        ProductWorkspaceState state,
        IReadOnlyList<DisplayTopologyNode>? currentTopology,
        bool currentTopologyAuthoritative,
        long topologyGeneration,
        long editRevision)
    {
        ProductWorkspaceLayoutRecoveryReviewResult review = Prepare(
            state,
            currentTopology,
            currentTopologyAuthoritative,
            topologyGeneration,
            editRevision);
        if (!review.CanConfirm || currentTopology is null)
        {
            return new(review, null);
        }

        try
        {
            IReadOnlyList<DisplayTopologyNode>? saved =
                ProductSavedDisplayTopology.ToNodes(state.SavedDisplayTopology);
            SavedContainerLayout[] layouts = state.Containers
                .Select(container => new SavedContainerLayout(
                    container.Id,
                    container.Placement.DisplayKey,
                    new(
                        ToInt(container.Placement.XDip),
                        ToInt(container.Placement.YDip),
                        ToInt(container.Placement.WidthDip),
                        ToInt(container.Placement.HeightDip))))
                .ToArray();
            LayoutRecoveryPlan plan = LayoutRecoveryPlanner.Create(
                saved!,
                currentTopology,
                layouts);
            return plan.Status == LayoutRecoveryStatus.ReviewRequired
                ? new(review, plan)
                : new(review with { Token = null }, null);
        }
        catch (ArgumentException)
        {
            return new(Invalid(state.Containers.Count), null);
        }
        catch (OverflowException)
        {
            return new(Invalid(state.Containers.Count), null);
        }
    }

    private static ProductWorkspaceLayoutRecoveryConfirmationResult Apply(
        ProductWorkspaceState state,
        IReadOnlyList<DisplayTopologyNode> currentTopology,
        LayoutRecoveryPlan plan)
    {
        Dictionary<string, DisplayTopologyNode> displays = currentTopology
            .ToDictionary(display => display.StableId, StringComparer.Ordinal);
        Dictionary<string, ContainerRecoveryPlacement> placements =
            plan.ContainerPlacements.ToDictionary(
                placement => placement.ContainerId,
                StringComparer.Ordinal);
        ProductContainerState[] containers = new ProductContainerState[
            state.Containers.Count];
        for (int index = 0; index < state.Containers.Count; index++)
        {
            ProductContainerState container = state.Containers[index];
            if (!placements.TryGetValue(
                container.Id,
                out ContainerRecoveryPlacement? placement)
                || !displays.TryGetValue(
                    placement.CurrentDisplayId,
                    out DisplayTopologyNode? display))
            {
                return Failure(
                    ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState);
            }

            ProductContainerPlacementState recovered = container.Placement with
            {
                DisplayKey = display.StableId,
                XDip = ToDip(
                    placement.ProposedBounds.Left - display.WorkArea.Left,
                    display.EffectiveDpi),
                YDip = ToDip(
                    placement.ProposedBounds.Top - display.WorkArea.Top,
                    display.EffectiveDpi),
                WidthDip = ToDip(
                    placement.ProposedBounds.Width,
                    display.EffectiveDpi),
                HeightDip = ToDip(
                    placement.ProposedBounds.Height,
                    display.EffectiveDpi),
            };
            if (container.IsLocked && recovered != container.Placement)
            {
                return Failure(
                    ProductWorkspaceLayoutRecoveryConfirmationStatus.ContainerLocked);
            }

            containers[index] = container with { Placement = recovered };
        }

        ProductWorkspaceState recoveredState = state with
        {
            Containers = containers,
            SavedDisplayTopology = ProductSavedDisplayTopology.Capture(currentTopology),
        };
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(recoveredState);
        if (!projection.IsSuccess)
        {
            return Failure(
                ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState);
        }

        return new(
            ProductWorkspaceLayoutRecoveryConfirmationStatus.Accepted,
            new(
                ProductWorkspaceEditError.None,
                ProductWorkspaceProjectionError.None,
                ProductConfigurationError.None,
                recoveredState,
                Changed: true));
    }

    private static string Fingerprint(ProductConfigurationDocument document) =>
        Convert.ToHexString(SHA256.HashData(
            ProductConfigurationJson.SerializeToUtf8Bytes(document)));

    private static int ToInt(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static double ToDip(int pixels, uint dpi) =>
        Math.Round(
            pixels * 96d / dpi,
            MidpointRounding.AwayFromZero);

    private static ProductWorkspaceLayoutRecoveryReviewResult Invalid(
        int containerCount) =>
        new(
            new(
                ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState,
                containerCount,
                0,
                0,
                0,
                DesktopWindowsChanged: false),
            null);

    private static ProductWorkspaceLayoutRecoveryConfirmationStatus MapUnavailable(
        ProductWorkspaceLayoutRecoveryPreviewStatus status) => status switch
        {
            ProductWorkspaceLayoutRecoveryPreviewStatus.Blocked =>
                ProductWorkspaceLayoutRecoveryConfirmationStatus.PlanBlocked,
            ProductWorkspaceLayoutRecoveryPreviewStatus.InvalidState =>
                ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState,
            _ => ProductWorkspaceLayoutRecoveryConfirmationStatus.PlanUnavailable,
        };

    private static ProductWorkspaceLayoutRecoveryConfirmationResult Failure(
        ProductWorkspaceLayoutRecoveryConfirmationStatus status) =>
        new(status, null);

    private sealed record Evaluation(
        ProductWorkspaceLayoutRecoveryReviewResult Review,
        LayoutRecoveryPlan? Plan);
}
