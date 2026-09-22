using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Runtime;

public sealed class PackageDesktopHostMetadataTests
{
    [Theory]
    [InlineData("Pack-LongGrid.ps1")]
    [InlineData("Pack-LongGridMsix.ps1")]
    [InlineData("Build-LongGridReleaseCandidate.ps1")]
    public void PackageDefaultMatchesProductWithoutClaimingUserAcceptance(string script)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LongGrid.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(root.FullName, "eng", script));
        Assert.True(ProductDesktopHostFeaturePolicy.Evaluate(null).IsEnabled);
        Assert.Matches(@"desktopHostExecutionEnabled\s*=\s*\$true", source);
        Assert.Matches(@"desktopHostExecutionScope\s*=\s*'ProductDefaultSubjectToSafetyPolicy'", source);
        Assert.Matches(@"desktopHostUserAcceptance\s*=\s*'Pending'", source);
        Assert.Matches(@"distributionApproved\s*=\s*\$false", source);
        Assert.Matches(@"signed\s*=\s*\$false", source);
    }
}
