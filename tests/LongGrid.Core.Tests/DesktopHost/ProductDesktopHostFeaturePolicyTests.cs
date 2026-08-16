using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostFeaturePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("true")]
    public void ProductHostIsEnabledWithoutDevelopmentOptIn(string? value)
    {
        ProductDesktopHostFeatureDecision decision =
            ProductDesktopHostFeaturePolicy.Evaluate(value);

        Assert.True(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopHostFeatureStatus.EnabledForProduct,
            decision.Status);
    }

    [Fact]
    public void ExplicitLegacyDisableStillFailsClosed()
    {
        ProductDesktopHostFeatureDecision decision =
            ProductDesktopHostFeaturePolicy.Evaluate("0");

        Assert.False(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopHostFeatureStatus.DisabledBySafetyPolicy,
            decision.Status);
    }

    [Fact]
    public void EmergencyDisableOverridesProductAndLegacyEnablement()
    {
        ProductDesktopHostFeatureDecision decision =
            ProductDesktopHostFeaturePolicy.Evaluate("1", "1");

        Assert.False(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopHostFeatureStatus.DisabledByEmergencyPolicy,
            decision.Status);
    }
}
