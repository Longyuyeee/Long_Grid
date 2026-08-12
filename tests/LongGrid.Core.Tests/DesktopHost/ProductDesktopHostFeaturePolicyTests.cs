using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostFeaturePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void AnythingExceptExactOptInRemainsDisabled(string? value)
    {
        ProductDesktopHostFeatureDecision decision =
            ProductDesktopHostFeaturePolicy.Evaluate(value);

        Assert.False(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopHostFeatureStatus.DisabledBySafetyPolicy,
            decision.Status);
    }

    [Fact]
    public void ExactDevelopmentOptInEnablesOnlyTheFeatureDecision()
    {
        ProductDesktopHostFeatureDecision decision =
            ProductDesktopHostFeaturePolicy.Evaluate("1");

        Assert.True(decision.IsEnabled);
        Assert.Equal(
            ProductDesktopHostFeatureStatus.EnabledForDevelopment,
            decision.Status);
    }
}
