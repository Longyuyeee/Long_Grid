using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class LayoutRecoveryTransactionCoordinatorTests
{
    private static readonly PixelRect OriginalOne =
        new(-900, 100, 300, 200);
    private static readonly PixelRect OriginalTwo =
        new(100, 200, 400, 300);
    private static readonly PixelRect ProposedOne =
        new(-850, 150, 320, 220);
    private static readonly PixelRect ProposedTwo =
        new(150, 250, 420, 320);

    [Fact]
    public void AppliesAndVerifiesAWholeBatch()
    {
        long generation = 7;
        var adapter = new FakeBatchAdapter(OriginalBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => generation,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(generation));

        Assert.Equal(LayoutRecoveryTransactionStatus.Applied, result.Status);
        Assert.Equal(LayoutRecoveryTransactionFailure.None, result.Failure);
        Assert.Equal(LayoutRecoveryRollbackStatus.NotRequired, result.Rollback);
        Assert.True(result.KeepsProposedLayout);
        Assert.Equal(2, result.PlacementCount);
        Assert.Equal(ProposedOne, adapter.Bounds["one"]);
        Assert.Equal(ProposedTwo, adapter.Bounds["two"]);
        Assert.Single(adapter.AppliedBatches);
    }

    [Fact]
    public void TreatsAnAlreadyAppliedPlanAsNoChanges()
    {
        var adapter = new FakeBatchAdapter(ProposedBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(LayoutRecoveryTransactionStatus.NoChanges, result.Status);
        Assert.True(result.KeepsProposedLayout);
        Assert.Empty(adapter.AppliedBatches);
    }

    [Fact]
    public void TreatsAnEmptyCurrentPlanAsNoChanges()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);
        var request = new LayoutRecoveryTransactionRequest(
            7,
            new LayoutRecoveryPlan(
                LayoutRecoveryStatus.Automatic,
                [],
                [],
                []),
            ReviewApproved: false);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(request);

        Assert.Equal(LayoutRecoveryTransactionStatus.NoChanges, result.Status);
        Assert.Equal(0, result.PlacementCount);
        Assert.Equal(0, adapter.CaptureCalls);
        Assert.Empty(adapter.AppliedBatches);
    }

    [Fact]
    public void RejectsBlockedPlanBeforeTouchingTheAdapter()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result = coordinator.Execute(
            Request(
                7,
                LayoutRecoveryStatus.Blocked));

        Assert.Equal(LayoutRecoveryTransactionStatus.Rejected, result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.PlanBlocked,
            result.Failure);
        Assert.Equal(0, adapter.CaptureCalls);
    }

    [Fact]
    public void RequiresExplicitApprovalForReviewPlan()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result = coordinator.Execute(
            Request(
                7,
                LayoutRecoveryStatus.ReviewRequired,
                reviewApproved: false));

        Assert.Equal(LayoutRecoveryTransactionStatus.Rejected, result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.ReviewApprovalRequired,
            result.Failure);
        Assert.Equal(0, adapter.CaptureCalls);
    }

    [Fact]
    public void RejectsStaleGenerationBeforeCapture()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds());
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 8,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.Superseded,
            result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.GenerationChanged,
            result.Failure);
        Assert.Equal(0, adapter.CaptureCalls);
    }

    [Fact]
    public void RejectsGenerationThatChangesDuringCapture()
    {
        long generation = 7;
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            AfterCapture = () => generation = 8,
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => generation,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.Superseded,
            result.Status);
        Assert.Empty(adapter.AppliedBatches);
    }

    [Fact]
    public void RollsBackWhenBatchApplyReportsFailure()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            ApplyResults = new Queue<bool>([false, true]),
            MutateOnFailedApply = true,
            ReturnLiveBounds = true,
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.ApplyFailed,
            result.Failure);
        Assert.Equal(
            LayoutRecoveryRollbackStatus.Succeeded,
            result.Rollback);
        Assert.Equal(OriginalBounds(), adapter.Bounds);
        Assert.Equal(2, adapter.AppliedBatches.Count);
    }

    [Fact]
    public void RollsBackWhenPostApplyBoundsDoNotMatch()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            DistortNextSuccessfulApply = true,
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.VerificationMismatch,
            result.Failure);
        Assert.Equal(OriginalBounds(), adapter.Bounds);
    }

    [Fact]
    public void RollsBackWhenGenerationChangesAfterApply()
    {
        long generation = 7;
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            AfterApply = call =>
            {
                if (call == 1)
                {
                    generation = 8;
                }
            },
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => generation,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.GenerationChanged,
            result.Failure);
        Assert.Equal(OriginalBounds(), adapter.Bounds);
    }

    [Fact]
    public void ReportsRollbackFailureWithoutClaimingSuccess()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            ApplyResults = new Queue<bool>([false, false]),
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            LayoutRecoveryRollbackStatus.ApplyFailed,
            result.Rollback);
        Assert.False(result.KeepsProposedLayout);
    }

    [Fact]
    public void RejectsIncompleteCapture()
    {
        var adapter = new FakeBatchAdapter(OriginalBounds())
        {
            CaptureOverride = new LayoutRecoveryBoundsCapture(
                true,
                new Dictionary<string, PixelRect>
                {
                    ["one"] = OriginalOne,
                }),
        };
        var coordinator = new LayoutRecoveryTransactionCoordinator(
            () => 7,
            adapter);

        LayoutRecoveryTransactionResult result =
            coordinator.Execute(Request(7));

        Assert.Equal(
            LayoutRecoveryTransactionStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            LayoutRecoveryTransactionFailure.CaptureInvalid,
            result.Failure);
        Assert.Empty(adapter.AppliedBatches);
    }

    private static LayoutRecoveryTransactionRequest Request(
        long generation,
        LayoutRecoveryStatus status = LayoutRecoveryStatus.Automatic,
        bool reviewApproved = true) =>
        new(
            generation,
            new LayoutRecoveryPlan(
                status,
                [],
                status == LayoutRecoveryStatus.Blocked
                    ? ["missing"]
                    : [],
                [
                    Placement("one", OriginalOne, ProposedOne),
                    Placement("two", OriginalTwo, ProposedTwo),
                ]),
            reviewApproved);

    private static ContainerRecoveryPlacement Placement(
        string id,
        PixelRect requested,
        PixelRect proposed) =>
        new(
            id,
            "saved",
            "current",
            requested,
            proposed,
            requested != proposed);

    private static Dictionary<string, PixelRect> OriginalBounds() =>
        new(StringComparer.Ordinal)
        {
            ["one"] = OriginalOne,
            ["two"] = OriginalTwo,
        };

    private static Dictionary<string, PixelRect> ProposedBounds() =>
        new(StringComparer.Ordinal)
        {
            ["one"] = ProposedOne,
            ["two"] = ProposedTwo,
        };

    private sealed class FakeBatchAdapter(
        IReadOnlyDictionary<string, PixelRect> initial)
        : ILayoutRecoveryWindowBatchAdapter
    {
        internal Dictionary<string, PixelRect> Bounds { get; } =
            new(initial, StringComparer.Ordinal);

        internal List<IReadOnlyList<LayoutRecoveryWindowPlacement>>
            AppliedBatches
        { get; } = [];

        internal Queue<bool> ApplyResults { get; init; } = [];

        internal bool MutateOnFailedApply { get; init; }

        internal bool ReturnLiveBounds { get; init; }

        internal bool DistortNextSuccessfulApply { get; set; }

        internal Action? AfterCapture { get; init; }

        internal Action<int>? AfterApply { get; init; }

        internal LayoutRecoveryBoundsCapture? CaptureOverride { get; init; }

        internal int CaptureCalls { get; private set; }

        public LayoutRecoveryBoundsCapture Capture(
            IReadOnlyList<string> containerIds)
        {
            CaptureCalls++;
            LayoutRecoveryBoundsCapture result = CaptureOverride
                ?? new LayoutRecoveryBoundsCapture(
                    true,
                    ReturnLiveBounds
                        ? Bounds
                        : containerIds.ToDictionary(
                            id => id,
                            id => Bounds[id],
                            StringComparer.Ordinal));
            AfterCapture?.Invoke();
            return result;
        }

        public bool Apply(
            IReadOnlyList<LayoutRecoveryWindowPlacement> placements)
        {
            AppliedBatches.Add(placements.ToArray());
            bool succeeds = ApplyResults.Count == 0
                || ApplyResults.Dequeue();
            if (succeeds || MutateOnFailedApply)
            {
                foreach (LayoutRecoveryWindowPlacement placement in placements)
                {
                    Bounds[placement.ContainerId] = placement.Bounds;
                }
            }

            if (succeeds && DistortNextSuccessfulApply)
            {
                LayoutRecoveryWindowPlacement first = placements[0];
                Bounds[first.ContainerId] =
                    first.Bounds.OffsetBy(1, 0);
                DistortNextSuccessfulApply = false;
            }

            AfterApply?.Invoke(AppliedBatches.Count);
            return succeeds;
        }
    }
}
