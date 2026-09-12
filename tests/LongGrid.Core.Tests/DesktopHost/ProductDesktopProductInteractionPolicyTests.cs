using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopProductInteractionPolicyTests
{
    [Fact]
    public void DefaultProductCompositionEnablesEntryWithoutManualAttestation()
    {
        var interaction = ProductDesktopInteractionFeaturePolicy.EvaluateForProduct(
            ProductDesktopHostFeaturePolicy.Evaluate(null), null);
        var bridge = ProductDesktopInteractionIntentBridgePolicy.EvaluateForProduct(
            interaction, null, null);
        var forwarding = ProductDesktopInteractionInputForwardingPolicy.EvaluateForProduct(
            bridge, null, null);
        Assert.Equal(ProductDesktopInteractionFeatureStatus.EnabledForProduct, interaction.Status);
        Assert.Equal(ProductDesktopInteractionIntentBridgeFeatureStatus.EnabledForProduct, bridge.Status);
        Assert.Equal(ProductDesktopInteractionInputForwardingFeatureStatus.EnabledForProduct, forwarding.Status);
        Assert.True(interaction.IsEnabled && bridge.IsEnabled && forwarding.IsEnabled);
        var controller = new ProductDesktopInteractionAdmissionController(interaction);
        Assert.Equal(ProductDesktopInteractionMode.Passive, controller.Snapshot.Mode);
        Assert.False(controller.Snapshot.HasActiveLease);
    }

    [Theory]
    [InlineData("0", null, null)]
    [InlineData(null, "0", null)]
    [InlineData(null, null, "1")]
    public void DisabledHostOrInteractionCannotEnableDownstream(string? hostValue, string? value, string? emergency)
    {
        var interaction = ProductDesktopInteractionFeaturePolicy.EvaluateForProduct(
            ProductDesktopHostFeaturePolicy.Evaluate(hostValue), value, emergency);
        var bridge = ProductDesktopInteractionIntentBridgePolicy.EvaluateForProduct(interaction, null, null);
        var forwarding = ProductDesktopInteractionInputForwardingPolicy.EvaluateForProduct(bridge, null, null);
        Assert.False(interaction.IsEnabled || bridge.IsEnabled || forwarding.IsEnabled);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("1", null)]
    [InlineData(null, "1")]
    [InlineData("1", "0")]
    public void ExplicitLegacyOverridesRetainManualSafetyRules(string? enabled, string? acknowledged)
    {
        var interaction = ProductDesktopInteractionFeaturePolicy.EvaluateForProduct(
            ProductDesktopHostFeaturePolicy.Evaluate(null), null);
        var bridge = ProductDesktopInteractionIntentBridgePolicy.EvaluateForProduct(interaction, enabled, acknowledged);
        Assert.False(bridge.IsEnabled);
        var productBridge = ProductDesktopInteractionIntentBridgePolicy.EvaluateForProduct(interaction, null, null);
        Assert.False(ProductDesktopInteractionInputForwardingPolicy.EvaluateForProduct(
            productBridge, enabled, acknowledged).IsEnabled);
    }

    [Fact]
    public void LegacyDevelopmentPolicyStillDefaultsOff()
    {
        var host = ProductDesktopHostFeaturePolicy.Evaluate(null);
        Assert.False(ProductDesktopInteractionFeaturePolicy.Evaluate(host, null).IsEnabled);
        var interaction = ProductDesktopInteractionFeaturePolicy.EvaluateForProduct(host, "1");
        Assert.Equal(ProductDesktopInteractionFeatureStatus.EnabledForDevelopment, interaction.Status);
        Assert.False(ProductDesktopInteractionIntentBridgePolicy.EvaluateForProduct(interaction, null, null).IsEnabled);
    }
}
