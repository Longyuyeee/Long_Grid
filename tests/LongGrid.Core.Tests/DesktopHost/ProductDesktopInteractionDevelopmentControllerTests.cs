using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionDevelopmentControllerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null)]
    [InlineData("1", null)]
    [InlineData(null, "1")]
    public void IndependentOptInsAreBothRequired(
        string? hostValue,
        string? interactionValue)
    {
        var controller = Controller(hostValue, interactionValue);

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.DisabledBySafetyPolicy,
            controller.Snapshot.Status);
        Assert.False(controller.Snapshot.IsDevelopmentInteractionAvailable);
        Assert.True(controller.Snapshot.HiddenRequired);
    }

    [Fact]
    public void ExactDoubleOptInStartsPassiveWithoutNativeAdapterOrFileAccess()
    {
        var controller = Controller("1", "1");

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.Passive,
            controller.Snapshot.Status);
        Assert.Equal(
            ProductDesktopInteractionMode.Passive,
            controller.Snapshot.Admission.Mode);
        Assert.True(controller.Snapshot.IsDevelopmentInteractionAvailable);
        Assert.False(controller.Snapshot.NativeSurfaceAdapterConnected);
        Assert.False(controller.Snapshot.RealFileOperationsAllowed);
        Assert.False(controller.Snapshot.HiddenRequired);
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("true")]
    public void ExactEmergencyDisableHasPriorityOnlyForExactOne(string value)
    {
        var controller = Controller("1", "1", value);

        ProductDesktopInteractionDevelopmentStatus expected = value == "1"
            ? ProductDesktopInteractionDevelopmentStatus.EmergencyDisabled
            : ProductDesktopInteractionDevelopmentStatus.Passive;
        Assert.Equal(expected, controller.Snapshot.Status);
        Assert.Equal(value == "1", controller.Snapshot.HiddenRequired);
    }

    [Theory]
    [InlineData(ProductDesktopInteractionCancellationSignal.EscapePressed)]
    [InlineData(ProductDesktopInteractionCancellationSignal.FocusLost)]
    [InlineData(ProductDesktopInteractionCancellationSignal.DesktopRevealRequested)]
    [InlineData(ProductDesktopInteractionCancellationSignal.FullScreenTransition)]
    [InlineData(ProductDesktopInteractionCancellationSignal.SessionLockedOrDisconnected)]
    [InlineData(ProductDesktopInteractionCancellationSignal.RemoteSessionTransition)]
    [InlineData(ProductDesktopInteractionCancellationSignal.ExplorerRestarted)]
    public void SystemTransitionSuspendsAndRequiresHiddenSurface(
        ProductDesktopInteractionCancellationSignal signal)
    {
        var controller = Controller("1", "1");

        ProductDesktopInteractionDevelopmentSnapshot snapshot =
            controller.SuspendFailClosed(signal, Now);

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
            snapshot.Status);
        Assert.True(snapshot.HiddenRequired);
        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Admission.Mode);
        Assert.False(snapshot.RealFileOperationsAllowed);
    }

    [Fact]
    public void ResumeRequiresCompleteCurrentPassiveAttestation()
    {
        var controller = Controller("1", "1");
        controller.SuspendFailClosed(
            ProductDesktopInteractionCancellationSignal
                .SessionLockedOrDisconnected,
            Now);

        ProductDesktopInteractionDevelopmentSnapshot rejected =
            controller.TryResumePassive(Evidence() with
            {
                PassiveWindowContractAttested = false,
            });
        ProductDesktopInteractionDevelopmentSnapshot resumed =
            controller.TryResumePassive(Evidence());

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.SuspendedFailClosed,
            rejected.Status);
        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.Passive,
            resumed.Status);
        Assert.False(resumed.HiddenRequired);
    }

    [Fact]
    public void RuntimeEmergencyDisableIsIrreversibleAndIdempotent()
    {
        var controller = Controller("1", "1");

        ProductDesktopInteractionDevelopmentSnapshot disabled =
            controller.EmergencyDisable(Now);
        ProductDesktopInteractionDevelopmentSnapshot repeated =
            controller.EmergencyDisable(Now.AddSeconds(1));
        ProductDesktopInteractionDevelopmentSnapshot resumeAttempt =
            controller.TryResumePassive(Evidence());

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.EmergencyDisabled,
            disabled.Status);
        Assert.True(disabled.HiddenRequired);
        Assert.Equal(disabled, repeated);
        Assert.Equal(disabled, resumeAttempt);
    }

    [Fact]
    public void ShutdownCompletesAndPermanentlyRequiresHiddenSurface()
    {
        var controller = Controller("1", "1");

        ProductDesktopInteractionDevelopmentSnapshot completed =
            controller.Complete(Now);
        ProductDesktopInteractionDevelopmentSnapshot repeated =
            controller.Complete(Now.AddSeconds(1));

        Assert.Equal(
            ProductDesktopInteractionDevelopmentStatus.Completed,
            completed.Status);
        Assert.True(completed.HiddenRequired);
        Assert.False(completed.RealFileOperationsAllowed);
        Assert.Equal(completed, repeated);
    }

    [Fact]
    public void EvidenceAndTimerSignalsCannotMasqueradeAsSystemTransitions()
    {
        var controller = Controller("1", "1");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.SuspendFailClosed(
                ProductDesktopInteractionCancellationSignal.EvidenceChanged,
                Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.SuspendFailClosed(
                ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed,
                Now));
    }

    private static ProductDesktopInteractionDevelopmentController Controller(
        string? hostValue,
        string? interactionValue,
        string? emergencyValue = null)
    {
        ProductDesktopHostFeatureDecision host =
            ProductDesktopHostFeaturePolicy.Evaluate(hostValue);
        return new(
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                host,
                interactionValue,
                emergencyValue));
    }

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: new HashSet<string>(StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(StringComparer.Ordinal));
}
