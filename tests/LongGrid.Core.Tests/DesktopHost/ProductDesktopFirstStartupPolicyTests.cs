using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopFirstStartupPolicyTests
{
    [Theory]
    [InlineData(
        ProductDesktopFirstHostReadiness.Ready,
        ProductDesktopFirstStartupReason.DesktopReady)]
    [InlineData(
        ProductDesktopFirstHostReadiness.AwaitingWorkspace,
        ProductDesktopFirstStartupReason.EmptyWorkspaceReady)]
    [InlineData(
        ProductDesktopFirstHostReadiness.SuspendedSystemSurface,
        ProductDesktopFirstStartupReason.SystemSurfaceSuspended)]
    public void EvaluateKeepsControlCenterHiddenForUsableDesktopStates(
        ProductDesktopFirstHostReadiness readiness,
        ProductDesktopFirstStartupReason expectedReason)
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(readiness));

        Assert.Equal(
            ProductDesktopFirstStartupAction.KeepControlCenterHidden,
            decision.Action);
        Assert.Equal(expectedReason, decision.Reason);
        Assert.False(decision.ActivateControlCenter);
    }

    [Theory]
    [InlineData(ProductDesktopFirstHostReadiness.AwaitingHost)]
    [InlineData(ProductDesktopFirstHostReadiness.DisabledByUser)]
    [InlineData(ProductDesktopFirstHostReadiness.DisabledBySafetyPolicy)]
    [InlineData(ProductDesktopFirstHostReadiness.SuspendedUnsafeTopology)]
    [InlineData(ProductDesktopFirstHostReadiness.Faulted)]
    public void EvaluateActivatesWhenDesktopHostCannotProvideEntry(
        ProductDesktopFirstHostReadiness readiness)
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(readiness));

        Assert.True(decision.ActivateControlCenter);
        Assert.Equal(
            ProductDesktopFirstStartupReason.DesktopHostUnavailable,
            decision.Reason);
    }

    [Fact]
    public void EvaluateActivatesForEvidenceSession()
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(
                    ProductDesktopFirstHostReadiness.Ready) with
                {
                    EvidenceSession = true,
                });

        Assert.True(decision.ActivateControlCenter);
        Assert.Equal(
            ProductDesktopFirstStartupReason.EvidenceSession,
            decision.Reason);
    }

    [Fact]
    public void EvaluateActivatesForRedirectedUserLaunch()
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(
                    ProductDesktopFirstHostReadiness.Ready) with
                {
                    RedirectedActivationPending = true,
                });

        Assert.True(decision.ActivateControlCenter);
        Assert.Equal(
            ProductDesktopFirstStartupReason.RedirectedActivation,
            decision.Reason);
    }

    [Fact]
    public void EvaluateActivatesWhenBoxesAreDisabled()
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(
                    ProductDesktopFirstHostReadiness.Ready) with
                {
                    BoxesEnabled = false,
                });

        Assert.True(decision.ActivateControlCenter);
        Assert.Equal(ProductDesktopFirstStartupReason.BoxesDisabled, decision.Reason);
    }

    [Fact]
    public void EvaluateActivatesWhenConfigurationRequiresAttention()
    {
        ProductDesktopFirstStartupDecision decision =
            ProductDesktopFirstStartupPolicy.Evaluate(
                CreateRequest(
                    ProductDesktopFirstHostReadiness.Ready) with
                {
                    ConfigurationRequiresAttention = true,
                });

        Assert.True(decision.ActivateControlCenter);
        Assert.Equal(
            ProductDesktopFirstStartupReason.ConfigurationRequiresAttention,
            decision.Reason);
    }

    private static ProductDesktopFirstStartupRequest CreateRequest(
        ProductDesktopFirstHostReadiness readiness) =>
        new(
            EvidenceSession: false,
            RedirectedActivationPending: false,
            BoxesEnabled: true,
            ConfigurationRequiresAttention: false,
            HostReadiness: readiness);
}
