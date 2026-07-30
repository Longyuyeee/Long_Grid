using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.DesktopItems;

public sealed class DesktopInventoryComparisonTests
{
    [Fact]
    public void CompareNormalizesDeduplicatesAndIgnoresCase()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "long-grid-audit"));
        string alpha = Path.Combine(root, "Alpha.lnk");
        string beta = Path.Combine(root, "Beta");

        DesktopInventoryComparisonResult result = DesktopInventoryComparison.Compare(
            [alpha, alpha.ToUpperInvariant(), beta],
            [alpha.ToLowerInvariant(), Path.Combine(root, ".", "Gamma.url")]);

        Assert.Single(result.MatchedPaths);
        Assert.Single(result.PhysicalOnlyPaths);
        Assert.Single(result.ShellOnlyPaths);
        Assert.Equal(alpha, result.MatchedPaths[0], ignoreCase: true);
        Assert.Equal(beta, result.PhysicalOnlyPaths[0], ignoreCase: true);
    }

    [Fact]
    public void CompareRejectsInvalidPaths()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => DesktopInventoryComparison.Compare([""], []));
        Assert.ThrowsAny<ArgumentException>(
            () => DesktopInventoryComparison.Compare([], [" "]));
    }
}
