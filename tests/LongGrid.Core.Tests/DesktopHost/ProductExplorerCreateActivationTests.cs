using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductExplorerCreateActivationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    [Fact]
    public void ParseAcceptsOneCurrentVersionedIntent()
    {
        var intent = new ProductExplorerCreateActivationIntent(
            ProductExplorerCreateActivation.CurrentVersion,
            -1440,
            720,
            Now.AddSeconds(-2),
            Guid.Parse("20d3bffe-ecbb-458b-af89-1c13a12e7931"));

        ProductExplorerCreateActivationDecision decision =
            ProductExplorerCreateActivation.Parse(
                ["LongGrid.App.exe", ProductExplorerCreateActivation.Format(intent)],
                Now);

        Assert.Equal(ProductExplorerCreateActivationStatus.Ready, decision.Status);
        Assert.Equal(intent, decision.Intent);
        Assert.True(decision.IsCommand);
        Assert.True(decision.CanActivate);
    }

    [Fact]
    public void ParseIgnoresOrdinaryLaunch()
    {
        ProductExplorerCreateActivationDecision decision =
            ProductExplorerCreateActivation.Parse(["--background"], Now);

        Assert.Equal(
            ProductExplorerCreateActivationStatus.NotPresent,
            decision.Status);
        Assert.False(decision.IsCommand);
        Assert.False(decision.CanActivate);
    }

    [Theory]
    [InlineData("--long-grid-create-box=v2,0,0,1800000000000,20d3bffeecbb458baf891c13a12e7931", ProductExplorerCreateActivationStatus.UnsupportedVersion)]
    [InlineData("--long-grid-create-box=v1,1000001,0,1800000000000,20d3bffeecbb458baf891c13a12e7931", ProductExplorerCreateActivationStatus.CoordinateOutOfRange)]
    [InlineData("--long-grid-create-box=v1,0,0,nope,20d3bffeecbb458baf891c13a12e7931", ProductExplorerCreateActivationStatus.InvalidIssuedAt)]
    [InlineData("--long-grid-create-box=v1,0,0,1800000000000,not-a-nonce", ProductExplorerCreateActivationStatus.InvalidNonce)]
    [InlineData("--long-grid-create-box=v1,0", ProductExplorerCreateActivationStatus.InvalidFormat)]
    public void ParseRejectsMalformedOrUnsupportedIntent(
        string argument,
        ProductExplorerCreateActivationStatus expected)
    {
        ProductExplorerCreateActivationDecision decision =
            ProductExplorerCreateActivation.Parse([argument], Now);

        Assert.Equal(expected, decision.Status);
        Assert.Null(decision.Intent);
        Assert.True(decision.IsCommand);
        Assert.False(decision.CanActivate);
    }

    [Theory]
    [InlineData(-31)]
    [InlineData(6)]
    public void ParseRejectsStaleOrExcessivelyFutureIntent(int issuedOffsetSeconds)
    {
        string argument = BuildArgument(Now.AddSeconds(issuedOffsetSeconds));

        ProductExplorerCreateActivationDecision decision =
            ProductExplorerCreateActivation.Parse([argument], Now);

        Assert.Equal(ProductExplorerCreateActivationStatus.Stale, decision.Status);
    }

    [Fact]
    public void ParseRejectsMultipleCreateCommands()
    {
        string argument = BuildArgument(Now);

        ProductExplorerCreateActivationDecision decision =
            ProductExplorerCreateActivation.Parse([argument, argument], Now);

        Assert.Equal(
            ProductExplorerCreateActivationStatus.MultipleCommands,
            decision.Status);
        Assert.Null(decision.Intent);
    }

    [Fact]
    public void FormatRejectsEmptyNonce()
    {
        var intent = new ProductExplorerCreateActivationIntent(
            ProductExplorerCreateActivation.CurrentVersion,
            0,
            0,
            Now,
            Guid.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductExplorerCreateActivation.Format(intent));
    }

    private static string BuildArgument(DateTimeOffset issuedAt) =>
        ProductExplorerCreateActivation.Format(new(
            ProductExplorerCreateActivation.CurrentVersion,
            0,
            0,
            issuedAt,
            Guid.Parse("20d3bffe-ecbb-458b-af89-1c13a12e7931")));
}
