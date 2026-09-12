using LongGrid.App;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductQuickStartPreviewContinuityTests
{
    private static ProductQuickStartSuggestionSnapshot Ready() => new(
        ProductQuickStartSuggestionStatus.Ready, Guid.NewGuid(), 2, 3,
        new string('A', 64), new string('B', 64), "桌面项目",
        [new(0, "文件甲.txt", DesktopItemKind.File), new(1, "文件乙.txt", DesktopItemKind.File)],
        2, false);

    [Fact]
    public void IdenticalRegeneratedSuggestionKeepsExplicitPreview()
    {
        var previous = Ready();
        var regenerated = previous with { PreviewId = Guid.NewGuid(), Items = previous.Items.ToArray() };
        Assert.True(ProductQuickStartPreviewContinuity.CanRetain(true, previous, regenerated));
        Assert.False(ProductQuickStartPreviewContinuity.CanRetain(false, previous, regenerated));
        Assert.False(ProductQuickStartPreviewContinuity.CanRetain(true, null, regenerated));
    }

    [Fact]
    public void RevisionOrVisibleContentChangesRequireNewConfirmation()
    {
        var original = Ready();
        ProductQuickStartSuggestionSnapshot[] changes =
        [
            original with { WorkspaceRevision = 4 },
            original with { CatalogGeneration = 3 },
            original with { WorkspaceFingerprint = new string('C', 64) },
            original with { CatalogFingerprint = new string('C', 64) },
            original with { ContainerName = "新名称" },
            original with { TotalCandidateCount = 3 },
            original with { IsTruncated = true },
            original with { Items = original.Items.Reverse().ToArray() },
            original with { Items = [original.Items[0] with { DisplayName = "已改名" }, original.Items[1]] },
            original with { Items = [original.Items[0] with { CatalogIndex = 2 }, original.Items[1]] },
            original with { Items = [original.Items[0] with { Kind = DesktopItemKind.Directory }, original.Items[1]] },
        ];
        Assert.All(changes, next => Assert.False(ProductQuickStartPreviewContinuity.CanRetain(true, original, next)));
    }

    [Fact]
    public void UnavailableOrInvalidPreviewCannotStayConfirmed()
    {
        var original = Ready();
        foreach (var status in Enum.GetValues<ProductQuickStartSuggestionStatus>().Where(value => value != ProductQuickStartSuggestionStatus.Ready))
        {
            Assert.False(ProductQuickStartPreviewContinuity.CanRetain(true, original, original with { Status = status }));
        }
        Assert.False(ProductQuickStartPreviewContinuity.CanRetain(true, original, original with { PreviewId = Guid.Empty }));
        Assert.False(ProductQuickStartPreviewContinuity.CanRetain(true, original with { Items = [] }, original));
    }
}
