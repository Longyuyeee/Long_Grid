namespace LongGrid.Core.DesktopHost;

public enum LayoutRecoveryTransactionStatus
{
    Applied,
    NoChanges,
    Rejected,
    Superseded,
    CaptureFailed,
    RolledBack,
    RollbackFailed,
}

public enum LayoutRecoveryTransactionFailure
{
    None,
    PlanBlocked,
    ReviewApprovalRequired,
    GenerationChanged,
    CaptureFailed,
    CaptureInvalid,
    ApplyFailed,
    VerificationCaptureFailed,
    VerificationMismatch,
}

public enum LayoutRecoveryRollbackStatus
{
    NotRequired,
    Succeeded,
    ApplyFailed,
    VerificationCaptureFailed,
    VerificationMismatch,
}

public sealed record LayoutRecoveryTransactionRequest(
    long Generation,
    LayoutRecoveryPlan Plan,
    bool ReviewApproved);

public sealed record LayoutRecoveryWindowPlacement(
    string ContainerId,
    PixelRect Bounds);

public sealed record LayoutRecoveryBoundsCapture(
    bool Succeeded,
    IReadOnlyDictionary<string, PixelRect> Bounds)
{
    public static LayoutRecoveryBoundsCapture Failed { get; } =
        new(
            false,
            new Dictionary<string, PixelRect>(
                StringComparer.Ordinal));
}

public interface ILayoutRecoveryWindowBatchAdapter
{
    LayoutRecoveryBoundsCapture Capture(
        IReadOnlyList<string> containerIds);

    bool Apply(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements);
}

public sealed record LayoutRecoveryTransactionResult(
    LayoutRecoveryTransactionStatus Status,
    LayoutRecoveryTransactionFailure Failure,
    LayoutRecoveryRollbackStatus Rollback,
    long Generation,
    int PlacementCount)
{
    public bool KeepsProposedLayout =>
        Status is LayoutRecoveryTransactionStatus.Applied
            or LayoutRecoveryTransactionStatus.NoChanges;
}

public sealed class LayoutRecoveryTransactionCoordinator
{
    private readonly Func<long> _currentGeneration;
    private readonly ILayoutRecoveryWindowBatchAdapter _adapter;

    public LayoutRecoveryTransactionCoordinator(
        Func<long> currentGeneration,
        ILayoutRecoveryWindowBatchAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(currentGeneration);
        ArgumentNullException.ThrowIfNull(adapter);
        _currentGeneration = currentGeneration;
        _adapter = adapter;
    }

    public LayoutRecoveryTransactionResult Execute(
        LayoutRecoveryTransactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            request.Generation);

        ContainerRecoveryPlacement[] placements =
            request.Plan.ContainerPlacements
                .OrderBy(
                    placement => placement.ContainerId,
                    StringComparer.Ordinal)
                .ToArray();
        ValidatePlacements(placements);

        if (request.Plan.Status == LayoutRecoveryStatus.Blocked)
        {
            return Result(
                LayoutRecoveryTransactionStatus.Rejected,
                LayoutRecoveryTransactionFailure.PlanBlocked,
                request,
                placements);
        }

        if (request.Plan.Status == LayoutRecoveryStatus.ReviewRequired
            && !request.ReviewApproved)
        {
            return Result(
                LayoutRecoveryTransactionStatus.Rejected,
                LayoutRecoveryTransactionFailure.ReviewApprovalRequired,
                request,
                placements);
        }

        if (!IsCurrent(request.Generation))
        {
            return Result(
                LayoutRecoveryTransactionStatus.Superseded,
                LayoutRecoveryTransactionFailure.GenerationChanged,
                request,
                placements);
        }

        if (placements.Length == 0)
        {
            return Result(
                LayoutRecoveryTransactionStatus.NoChanges,
                LayoutRecoveryTransactionFailure.None,
                request,
                placements);
        }

        string[] containerIds = placements
            .Select(placement => placement.ContainerId)
            .ToArray();
        LayoutRecoveryBoundsCapture original =
            _adapter.Capture(containerIds);
        LayoutRecoveryTransactionFailure captureFailure =
            ValidateCapture(original, containerIds);
        if (captureFailure != LayoutRecoveryTransactionFailure.None)
        {
            return Result(
                LayoutRecoveryTransactionStatus.CaptureFailed,
                captureFailure,
                request,
                placements);
        }

        Dictionary<string, PixelRect> originalBounds =
            original.Bounds.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        if (!IsCurrent(request.Generation))
        {
            return Result(
                LayoutRecoveryTransactionStatus.Superseded,
                LayoutRecoveryTransactionFailure.GenerationChanged,
                request,
                placements);
        }

        LayoutRecoveryWindowPlacement[] proposed = placements
            .Select(placement =>
                new LayoutRecoveryWindowPlacement(
                    placement.ContainerId,
                    placement.ProposedBounds))
            .ToArray();
        if (Matches(originalBounds, proposed))
        {
            return Result(
                LayoutRecoveryTransactionStatus.NoChanges,
                LayoutRecoveryTransactionFailure.None,
                request,
                placements);
        }

        if (!_adapter.Apply(proposed))
        {
            return RollBack(
                request,
                placements,
                originalBounds,
                LayoutRecoveryTransactionFailure.ApplyFailed);
        }

        if (!IsCurrent(request.Generation))
        {
            return RollBack(
                request,
                placements,
                originalBounds,
                LayoutRecoveryTransactionFailure.GenerationChanged);
        }

        LayoutRecoveryBoundsCapture verification =
            _adapter.Capture(containerIds);
        LayoutRecoveryTransactionFailure verificationFailure =
            ValidateCapture(verification, containerIds);
        if (verificationFailure != LayoutRecoveryTransactionFailure.None)
        {
            return RollBack(
                request,
                placements,
                originalBounds,
                verificationFailure
                    == LayoutRecoveryTransactionFailure.CaptureFailed
                    ? LayoutRecoveryTransactionFailure
                        .VerificationCaptureFailed
                    : verificationFailure);
        }

        if (!Matches(verification.Bounds, proposed))
        {
            return RollBack(
                request,
                placements,
                originalBounds,
                LayoutRecoveryTransactionFailure.VerificationMismatch);
        }

        if (!IsCurrent(request.Generation))
        {
            return RollBack(
                request,
                placements,
                originalBounds,
                LayoutRecoveryTransactionFailure.GenerationChanged);
        }

        return Result(
            LayoutRecoveryTransactionStatus.Applied,
            LayoutRecoveryTransactionFailure.None,
            request,
            placements);
    }

    private LayoutRecoveryTransactionResult RollBack(
        LayoutRecoveryTransactionRequest request,
        IReadOnlyCollection<ContainerRecoveryPlacement> placements,
        IReadOnlyDictionary<string, PixelRect> original,
        LayoutRecoveryTransactionFailure failure)
    {
        LayoutRecoveryWindowPlacement[] rollbackPlacements = original
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
                new LayoutRecoveryWindowPlacement(
                    pair.Key,
                    pair.Value))
            .ToArray();
        if (!_adapter.Apply(rollbackPlacements))
        {
            return Result(
                LayoutRecoveryTransactionStatus.RollbackFailed,
                failure,
                LayoutRecoveryRollbackStatus.ApplyFailed,
                request,
                placements);
        }

        string[] containerIds = rollbackPlacements
            .Select(placement => placement.ContainerId)
            .ToArray();
        LayoutRecoveryBoundsCapture verification =
            _adapter.Capture(containerIds);
        if (!verification.Succeeded)
        {
            return Result(
                LayoutRecoveryTransactionStatus.RollbackFailed,
                failure,
                LayoutRecoveryRollbackStatus.VerificationCaptureFailed,
                request,
                placements);
        }

        if (ValidateCapture(verification, containerIds)
                != LayoutRecoveryTransactionFailure.None
            || !Matches(verification.Bounds, rollbackPlacements))
        {
            return Result(
                LayoutRecoveryTransactionStatus.RollbackFailed,
                failure,
                LayoutRecoveryRollbackStatus.VerificationMismatch,
                request,
                placements);
        }

        return Result(
            LayoutRecoveryTransactionStatus.RolledBack,
            failure,
            LayoutRecoveryRollbackStatus.Succeeded,
            request,
            placements);
    }

    private bool IsCurrent(long generation) =>
        _currentGeneration() == generation;

    private static LayoutRecoveryTransactionFailure ValidateCapture(
        LayoutRecoveryBoundsCapture capture,
        IReadOnlyCollection<string> expectedIds)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(capture.Bounds);

        if (!capture.Succeeded)
        {
            return LayoutRecoveryTransactionFailure.CaptureFailed;
        }

        if (capture.Bounds.Count != expectedIds.Count
            || capture.Bounds.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key)
                || !pair.Value.HasArea)
            || !capture.Bounds.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedIds))
        {
            return LayoutRecoveryTransactionFailure.CaptureInvalid;
        }

        return LayoutRecoveryTransactionFailure.None;
    }

    private static bool Matches(
        IReadOnlyDictionary<string, PixelRect> actual,
        IReadOnlyCollection<LayoutRecoveryWindowPlacement> expected) =>
        actual.Count == expected.Count
        && expected.All(placement =>
            actual.TryGetValue(
                placement.ContainerId,
                out PixelRect bounds)
            && bounds == placement.Bounds);

    private static void ValidatePlacements(
        IReadOnlyCollection<ContainerRecoveryPlacement> placements)
    {
        if (placements.Any(placement =>
                string.IsNullOrWhiteSpace(placement.ContainerId)
                || !placement.ProposedBounds.HasArea)
            || placements
                .Select(placement => placement.ContainerId)
                .Distinct(StringComparer.Ordinal)
                .Count() != placements.Count)
        {
            throw new ArgumentException(
                "A transaction requires unique containers and valid bounds.",
                nameof(placements));
        }
    }

    private static LayoutRecoveryTransactionResult Result(
        LayoutRecoveryTransactionStatus status,
        LayoutRecoveryTransactionFailure failure,
        LayoutRecoveryTransactionRequest request,
        IReadOnlyCollection<ContainerRecoveryPlacement> placements) =>
        Result(
            status,
            failure,
            LayoutRecoveryRollbackStatus.NotRequired,
            request,
            placements);

    private static LayoutRecoveryTransactionResult Result(
        LayoutRecoveryTransactionStatus status,
        LayoutRecoveryTransactionFailure failure,
        LayoutRecoveryRollbackStatus rollback,
        LayoutRecoveryTransactionRequest request,
        IReadOnlyCollection<ContainerRecoveryPlacement> placements) =>
        new(
            status,
            failure,
            rollback,
            request.Generation,
            placements.Count);
}
