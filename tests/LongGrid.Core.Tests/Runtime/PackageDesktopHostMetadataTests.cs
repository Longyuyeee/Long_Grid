using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Runtime;

public sealed class PackageDesktopHostMetadataTests
{
    [Fact]
    public async Task WindowsPowerShellPreservesChineseProductNameWithoutBom()
    {
        string scriptPath = Path.Combine(FindRepositoryRoot(), "eng", "Pack-LongGrid.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(
            "$ErrorActionPreference='Stop'; " +
            $"$source=Get-Content -LiteralPath '{scriptPath}' -Raw; " +
            @"$expression=[regex]::Match($source,'(?m)^\s*displayName\s*=\s*(.+)$').Groups[1].Value; " +
            "$actual=& ([scriptblock]::Create($expression)); " +
            "if ($actual -cne ('Long'+[char]0x65B9+[char]0x683C)) { exit 1 }; exit 0");
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        Assert.Equal(0, process.ExitCode);
    }

    [Theory]
    [InlineData("Pack-LongGrid.ps1")]
    [InlineData("Pack-LongGridMsix.ps1")]
    [InlineData("Build-LongGridReleaseCandidate.ps1")]
    public void PackageDefaultMatchesProductWithoutClaimingUserAcceptance(string script)
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng", script));
        Assert.True(ProductDesktopHostFeaturePolicy.Evaluate(null).IsEnabled);
        Assert.Matches(@"desktopHostExecutionEnabled\s*=\s*\$true", source);
        Assert.Matches(@"desktopHostExecutionScope\s*=\s*'ProductDefaultSubjectToSafetyPolicy'", source);
        Assert.Matches(@"desktopHostUserAcceptance\s*=\s*'Pending'", source);
        Assert.Matches(@"distributionApproved\s*=\s*\$false", source);
        Assert.Matches(@"signed\s*=\s*\$false", source);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LongGrid.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
