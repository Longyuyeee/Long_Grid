using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.DesktopItems;

public sealed class DesktopCatalogTests
{
    [Fact]
    public void BuildClassifiesSupportedFileSystemEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "LongGrid", "DesktopCatalog");
        var candidates = new[]
        {
            new DesktopCatalogCandidate("user", Path.Combine(root, "Folder"), true),
            new DesktopCatalogCandidate("user", Path.Combine(root, "App.lnk"), false),
            new DesktopCatalogCandidate("user", Path.Combine(root, "Website.url"), false),
            new DesktopCatalogCandidate("user", Path.Combine(root, "Notes.txt"), false),
        };

        IReadOnlyList<DesktopCatalogEntry> result = DesktopCatalog.Build(candidates);

        Assert.Collection(
            result,
            entry => Assert.Equal(DesktopItemKind.Shortcut, entry.Kind),
            entry => Assert.Equal(DesktopItemKind.Directory, entry.Kind),
            entry => Assert.Equal(DesktopItemKind.File, entry.Kind),
            entry => Assert.Equal(DesktopItemKind.InternetShortcut, entry.Kind));
    }

    [Fact]
    public void BuildDeduplicatesCanonicalPathsCaseInsensitively()
    {
        string root = Path.Combine(Path.GetTempPath(), "LongGrid", "DesktopCatalog");
        string path = Path.Combine(root, "Project.lnk");
        var candidates = new[]
        {
            new DesktopCatalogCandidate("user", path, false),
            new DesktopCatalogCandidate("public", path.ToUpperInvariant(), false),
        };

        DesktopCatalogEntry entry = Assert.Single(DesktopCatalog.Build(candidates));

        Assert.Equal("user", entry.SourceId);
    }

    [Theory]
    [InlineData(null, "C:\\Desktop\\Item.txt")]
    [InlineData("", "C:\\Desktop\\Item.txt")]
    [InlineData("user", null)]
    [InlineData("user", "")]
    public void BuildRejectsInvalidCandidates(string? sourceId, string? path)
    {
        var candidate = new DesktopCatalogCandidate(sourceId!, path!, false);

        Assert.ThrowsAny<ArgumentException>(() => DesktopCatalog.Build([candidate]));
    }
}
