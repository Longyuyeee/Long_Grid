using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceRealWindowRecoveryAdmissionTests
{
    [Fact]
    public void CompleteBoundEvidenceIsReadyWithoutConnectingAnAdapter()
    {
        ProductWorkspaceState state = State();
        LayoutRecoveryPlan plan = Plan();
        ProductWorkspaceRealWindowRecoveryPlanToken token =
            ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
                state, 5, 7, plan, reviewApproved: true)!;
        ProductWorkspaceLayoutRecoveryUndoToken undo = new(
            Guid.NewGuid(),
            7,
            token.ConfigurationFingerprint,
            new string('A', 64),
            1);

        ProductWorkspaceRealWindowRecoveryAdmissionResult result =
            ProductWorkspaceRealWindowRecoveryAdmission.Evaluate(
                Evidence(state, plan, token, undo));

        Assert.True(result.CanConnect);
        Assert.Equal(0, result.BlockerCount);
        Assert.Equal(
            ProductWorkspaceRealWindowRecoveryBlocker.None,
            result.Blockers);
    }

    [Fact]
    public void CurrentProductStateRemainsBlockedByEveryUnimplementedBoundary()
    {
        ProductWorkspaceRealWindowRecoveryAdmissionResult result =
            ProductWorkspaceRealWindowRecoveryAdmission.Evaluate(new(
                CurrentState: null,
                SessionWritable: false,
                CurrentTopologyAuthoritative: false,
                CurrentTopologyGeneration: 0,
                CurrentEditRevision: 0,
                ConfigurationUndoToken: null,
                BoundPlanToken: null,
                Plan: null,
                RegisteredContainerIds: null,
                WindowOwnershipAttested: false,
                CompositeTransactionAvailable: false,
                WindowBatchAdapterAvailable: false,
                RollbackFaultMatrixPassed: false,
                InputSurfaceMatrixPassed: false,
                DynamicDisplayMatrixPassed: false,
                CleanUiAutomationPassed: false));

        Assert.False(result.CanConnect);
        Assert.True(result.BlockerCount >= 12);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.SessionUnavailable);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.BoundPlanMissing);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.ConfigurationUndoUnavailable);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.CompositeTransactionUnavailable);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.WindowBatchAdapterUnavailable);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.InputSurfaceMatrixPending);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.DynamicDisplayMatrixPending);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.CleanUiAutomationPending);
    }

    [Fact]
    public void StalePlanConfigurationUndoAndWindowSetAreAllReported()
    {
        ProductWorkspaceState state = State();
        LayoutRecoveryPlan plan = Plan();
        ProductWorkspaceRealWindowRecoveryPlanToken token =
            ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
                state, 5, 7, plan, reviewApproved: true)!;
        ProductWorkspaceLayoutRecoveryUndoToken undo = new(
            Guid.NewGuid(),
            8,
            "different",
            new string('A', 64),
            1);
        ProductWorkspaceRealWindowRecoveryEvidence evidence =
            Evidence(state with { ProfileId = "changed" }, plan, token, undo) with
            {
                CurrentTopologyGeneration = 6,
                CurrentEditRevision = 8,
                RegisteredContainerIds = ["other-container"],
            };

        ProductWorkspaceRealWindowRecoveryAdmissionResult result =
            ProductWorkspaceRealWindowRecoveryAdmission.Evaluate(evidence);

        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.TopologyGenerationChanged);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.EditRevisionChanged);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.ConfigurationChanged);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.ConfigurationUndoMismatch);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.ContainerWindowSetMismatch);
    }

    [Fact]
    public void PlanMutationAndMissingManualEvidenceRemainFiniteBlockers()
    {
        ProductWorkspaceState state = State();
        LayoutRecoveryPlan plan = Plan();
        ProductWorkspaceRealWindowRecoveryPlanToken token =
            ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
                state, 5, 7, plan, reviewApproved: true)!;
        ProductWorkspaceLayoutRecoveryUndoToken undo = new(
            Guid.NewGuid(), 7, token.ConfigurationFingerprint, "restore", 1);
        LayoutRecoveryPlan changed = plan with
        {
            ContainerPlacements =
            [
                plan.ContainerPlacements[0] with
                {
                    ProposedBounds = new(50, 60, 400, 260),
                },
            ],
        };
        ProductWorkspaceRealWindowRecoveryEvidence evidence =
            Evidence(state, changed, token, undo) with
            {
                RollbackFaultMatrixPassed = false,
                InputSurfaceMatrixPassed = false,
                DynamicDisplayMatrixPassed = false,
                CleanUiAutomationPassed = false,
            };

        ProductWorkspaceRealWindowRecoveryAdmissionResult result =
            ProductWorkspaceRealWindowRecoveryAdmission.Evaluate(evidence);

        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.PlanFingerprintChanged);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.RollbackFaultMatrixPending);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.InputSurfaceMatrixPending);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.DynamicDisplayMatrixPending);
        AssertBlocker(result, ProductWorkspaceRealWindowRecoveryBlocker.CleanUiAutomationPending);
    }

    [Fact]
    public void TokenPreparationRejectsUnsafeOrMalformedPlans()
    {
        ProductWorkspaceState state = State();
        LayoutRecoveryPlan plan = Plan();
        Assert.Null(ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
            state, 5, 7, plan, reviewApproved: false));
        Assert.Null(ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
            state, 0, 7, plan, reviewApproved: true));
        Assert.Null(ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
            state,
            5,
            7,
            plan with { Status = LayoutRecoveryStatus.Blocked },
            reviewApproved: true));
        Assert.Null(ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
            state,
            5,
            7,
            plan with
            {
                ContainerPlacements =
                [
                    plan.ContainerPlacements[0] with { ContainerId = " " },
                ],
            },
            reviewApproved: true));
        Assert.Null(ProductWorkspaceRealWindowRecoveryAdmission.PreparePlanToken(
            state with
            {
                Containers =
                [
                    .. state.Containers,
                    state.Containers[0] with { Id = "container-2" },
                ],
            },
            5,
            7,
            plan,
            reviewApproved: true));
        Assert.Throws<ArgumentNullException>(
            () => ProductWorkspaceRealWindowRecoveryAdmission.Evaluate(null!));
    }

    private static ProductWorkspaceRealWindowRecoveryEvidence Evidence(
        ProductWorkspaceState state,
        LayoutRecoveryPlan plan,
        ProductWorkspaceRealWindowRecoveryPlanToken token,
        ProductWorkspaceLayoutRecoveryUndoToken undo) => new(
            state,
            SessionWritable: true,
            CurrentTopologyAuthoritative: true,
            CurrentTopologyGeneration: 5,
            CurrentEditRevision: 7,
            undo,
            token,
            plan,
            RegisteredContainerIds: ["container-1"],
            WindowOwnershipAttested: true,
            CompositeTransactionAvailable: true,
            WindowBatchAdapterAvailable: true,
            RollbackFaultMatrixPassed: true,
            InputSurfaceMatrixPassed: true,
            DynamicDisplayMatrixPassed: true,
            CleanUiAutomationPassed: true);

    private static ProductWorkspaceState State() => new()
    {
        ProfileId = "default",
        Containers =
        [
            new ProductContainerState
            {
                Id = "container-1",
                Name = "Work",
                Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                Placement = new()
                {
                    DisplayKey = "display-current",
                    XDip = 32,
                    YDip = 48,
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = Array.Empty<ProductItemReferenceState>(),
            },
        ],
    };

    private static LayoutRecoveryPlan Plan() => new(
        LayoutRecoveryStatus.ReviewRequired,
        [
            new DisplayRecoveryMapping(
                "display-saved",
                "display-current",
                DisplayMatchKind.SimilarGeometry),
        ],
        Array.Empty<string>(),
        [
            new ContainerRecoveryPlacement(
                "container-1",
                "display-saved",
                "display-current",
                new(32, 48, 360, 240),
                new(40, 48, 360, 240),
                WasVisibilityCorrected: false),
        ]);

    private static void AssertBlocker(
        ProductWorkspaceRealWindowRecoveryAdmissionResult result,
        ProductWorkspaceRealWindowRecoveryBlocker blocker) =>
        Assert.True(result.Blockers.HasFlag(blocker), result.Blockers.ToString());
}
