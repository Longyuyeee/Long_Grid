using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionAdmissionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void InteractionRequiresItsOwnExactOptIn(string? value)
    {
        ProductDesktopInteractionFeatureDecision decision =
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("1"),
                value);

        Assert.False(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopInteractionFeatureStatus
                .DisabledByInteractionSafetyPolicy,
            decision.Status);
    }

    [Fact]
    public void DisabledDesktopHostCannotBeOverriddenByInteractionOptIn()
    {
        ProductDesktopInteractionFeatureDecision decision =
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("0"),
                "1");

        Assert.False(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopInteractionFeatureStatus
                .DisabledByDesktopHostSafetyPolicy,
            decision.Status);
    }

    [Fact]
    public void ExactDoubleOptInStartsPassiveWithoutLease()
    {
        var controller = Controller(enabled: true);

        Assert.Equal(ProductDesktopInteractionMode.Passive, controller.Snapshot.Mode);
        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.NotAttempted,
            controller.Snapshot.LastAdmissionStatus);
        Assert.False(controller.Snapshot.HasActiveLease);
    }

    [Fact]
    public void ValidExplicitIntentBindsEveryGenerationAndTarget()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionIntent intent = Intent();

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(intent, Evidence(), Now);

        Assert.Equal(
            ProductDesktopInteractionMode.ExplicitInteraction,
            snapshot.Mode);
        Assert.Equal(ProductDesktopInteractionAdmissionStatus.Admitted,
            snapshot.LastAdmissionStatus);
        Assert.Equal(intent.IntentId, snapshot.Lease!.IntentId);
        Assert.Equal(intent.TargetContainerId, snapshot.Lease.TargetContainerId);
        Assert.Equal(intent.WorkspaceRevision, snapshot.Lease.WorkspaceRevision);
        Assert.Equal(intent.TopologyGeneration, snapshot.Lease.TopologyGeneration);
        Assert.Equal(
            intent.WindowRegistryGeneration,
            snapshot.Lease.WindowRegistryGeneration);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("accessibility")]
    [InlineData("passive")]
    [InlineData("workspace")]
    [InlineData("topology")]
    [InlineData("registry")]
    [InlineData("missing")]
    [InlineData("locked")]
    public void MissingOrStaleEvidenceFailsClosed(string fault)
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionEvidence evidence = Evidence();
        evidence = fault switch
        {
            "host" => evidence with { NativeHostConnected = false },
            "accessibility" => evidence with
            {
                ReadOnlyAccessibilityAttested = false,
            },
            "passive" => evidence with
            {
                PassiveWindowContractAttested = false,
            },
            "workspace" => evidence with { WorkspaceRevision = 8 },
            "topology" => evidence with { TopologyGeneration = 10 },
            "registry" => evidence with { WindowRegistryGeneration = 12 },
            "missing" => evidence with
            {
                AvailableContainerIds = Set("other"),
            },
            "locked" => evidence with
            {
                LockedContainerIds = Set("container-1"),
            },
            _ => throw new InvalidOperationException(),
        };

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(Intent(), evidence, Now);

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.False(snapshot.HasActiveLease);
        Assert.NotEqual(
            ProductDesktopInteractionAdmissionStatus.Admitted,
            snapshot.LastAdmissionStatus);
    }

    [Fact]
    public void InvalidFutureOrOverlongIntentFailsClosed()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionIntent intent = Intent() with
        {
            IssuedAtUtc = Now.AddSeconds(1),
            ExpiresAtUtc = Now.AddSeconds(7),
        };

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(intent, Evidence(), Now);

        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.InvalidIntent,
            snapshot.LastAdmissionStatus);
        Assert.False(snapshot.HasActiveLease);
    }

    [Fact]
    public void MissingContainerEvidenceFailsClosedWithoutThrowing()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionEvidence evidence = Evidence() with
        {
            AvailableContainerIds = null!,
            LockedContainerIds = null!,
        };

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(Intent(), evidence, Now);

        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.TargetUnavailable,
            snapshot.LastAdmissionStatus);
        Assert.False(snapshot.HasActiveLease);
    }

    [Fact]
    public void ContainerIdentityAlwaysUsesOrdinalMatching()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionEvidence evidence = Evidence() with
        {
            AvailableContainerIds = new HashSet<string>(
                ["CONTAINER-1"],
                StringComparer.OrdinalIgnoreCase),
        };

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(Intent(), evidence, Now);

        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.TargetUnavailable,
            snapshot.LastAdmissionStatus);
        Assert.False(snapshot.HasActiveLease);
    }

    [Fact]
    public void ExpiredIntentIsDistinctFromMalformedIntent()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionIntent intent = Intent() with
        {
            IssuedAtUtc = Now.AddSeconds(-4),
            ExpiresAtUtc = Now,
        };

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(intent, Evidence(), Now);

        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.IntentExpired,
            snapshot.LastAdmissionStatus);
    }

    [Fact]
    public void ActiveLeaseCannotBeSilentlyReplaced()
    {
        var controller = Controller(enabled: true);
        ProductDesktopInteractionIntent first = Intent();
        controller.TryEnterExplicitInteraction(first, Evidence(), Now);

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(
                Intent() with { IntentId = Guid.NewGuid() },
                Evidence(),
                Now);

        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.AlreadyActive,
            snapshot.LastAdmissionStatus);
        Assert.Equal(first.IntentId, snapshot.Lease!.IntentId);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("host")]
    [InlineData("attestation")]
    [InlineData("workspace")]
    [InlineData("topology")]
    [InlineData("registry")]
    [InlineData("missing")]
    [InlineData("locked")]
    public void ActiveLeaseCancelsWhenBoundEvidenceChanges(string fault)
    {
        var controller = Controller(enabled: true);
        controller.TryEnterExplicitInteraction(Intent(), Evidence(), Now);
        ProductDesktopInteractionEvidence evidence = Evidence();
        DateTimeOffset revalidationTime = Now;
        evidence = fault switch
        {
            "expiry" => evidence,
            "host" => evidence with { HostReadyReadOnly = false },
            "attestation" => evidence with
            {
                PassiveWindowContractAttested = false,
            },
            "workspace" => evidence with { WorkspaceRevision = 8 },
            "topology" => evidence with { TopologyGeneration = 10 },
            "registry" => evidence with { WindowRegistryGeneration = 12 },
            "missing" => evidence with
            {
                AvailableContainerIds = Set("other"),
            },
            "locked" => evidence with
            {
                LockedContainerIds = Set("container-1"),
            },
            _ => throw new InvalidOperationException(),
        };
        if (fault == "expiry")
        {
            revalidationTime = Now.AddSeconds(5);
        }

        ProductDesktopInteractionSnapshot snapshot =
            controller.Revalidate(evidence, revalidationTime);

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.False(snapshot.HasActiveLease);
        Assert.NotEqual(
            ProductDesktopInteractionCancellationReason.None,
            snapshot.LastCancellationReason);
    }

    [Fact]
    public void ExplicitCancelReturnsToPassiveWithoutChangingGenerations()
    {
        var controller = Controller(enabled: true);
        controller.TryEnterExplicitInteraction(Intent(), Evidence(), Now);

        ProductDesktopInteractionSnapshot snapshot = controller.Cancel();

        Assert.Equal(ProductDesktopInteractionMode.Passive, snapshot.Mode);
        Assert.Equal(
            ProductDesktopInteractionCancellationReason.ExplicitCancel,
            snapshot.LastCancellationReason);
        Assert.Null(snapshot.Lease);
    }

    [Fact]
    public void DisabledControllerNeverCreatesLease()
    {
        var controller = Controller(enabled: false);

        ProductDesktopInteractionSnapshot snapshot =
            controller.TryEnterExplicitInteraction(Intent(), Evidence(), Now);

        Assert.Equal(
            ProductDesktopInteractionMode.DisabledBySafetyPolicy,
            snapshot.Mode);
        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.DisabledBySafetyPolicy,
            snapshot.LastAdmissionStatus);
        Assert.Null(snapshot.Lease);
    }

    private static ProductDesktopInteractionAdmissionController Controller(
        bool enabled) =>
        new(ProductDesktopInteractionFeaturePolicy.Evaluate(
            ProductDesktopHostFeaturePolicy.Evaluate(enabled ? "1" : null),
            enabled ? "1" : null));

    private static ProductDesktopInteractionIntent Intent() =>
        new(
            Guid.Parse("57a5aaef-5c0e-43f9-9dc3-cc224a7b6f42"),
            "container-1",
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            IssuedAtUtc: Now,
            ExpiresAtUtc: Now.AddSeconds(5));

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: Set("container-1"),
            LockedContainerIds: Set());

    private static HashSet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
