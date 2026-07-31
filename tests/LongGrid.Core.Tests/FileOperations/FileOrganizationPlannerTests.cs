using LongGrid.Core.FileOperations;

namespace LongGrid.Core.Tests.FileOperations;

public sealed class FileOrganizationPlannerTests
{
    [Fact]
    public void SafeReferenceNeverCreatesFileSystemMutation()
    {
        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.SafeReference,
            [new FileOrganizationItemFacts(
                "item-1",
                SourceAvailable: true,
                IsFileSystemItem: true,
                DestinationConfigured: true,
                SourceEqualsDestination: true,
                DestinationExists: true,
                IsReparsePoint: true,
                IsNetworkPath: true,
                IsCloudPlaceholder: true)]);

        FileOrganizationPlanEntry entry = Assert.Single(plan.Entries);
        Assert.Equal(FileOrganizationAction.AddReference, entry.Action);
        Assert.Empty(entry.Issues);
        Assert.False(plan.HasFileSystemMutations);
        Assert.False(plan.RequiresExplicitApproval);
        Assert.True(plan.CanApplyWithoutFileApproval);
    }

    [Fact]
    public void SafeReferenceBlocksUnavailableSource()
    {
        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.SafeReference,
            [new FileOrganizationItemFacts(
                "item-1",
                SourceAvailable: false,
                IsFileSystemItem: false)]);

        FileOrganizationPlanEntry entry = Assert.Single(plan.Entries);
        Assert.Equal(
            [FileOrganizationIssueCode.SourceUnavailable],
            entry.Issues);
        Assert.True(plan.HasBlockingIssues);
        Assert.False(plan.CanApplyWithoutFileApproval);
    }

    [Fact]
    public void ValidManagedMoveRequiresExplicitApproval()
    {
        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.ManagedMove,
            [Movable("item-1"), Movable("item-2")]);

        Assert.All(
            plan.Entries,
            entry =>
            {
                Assert.Equal(FileOrganizationAction.MoveFile, entry.Action);
                Assert.Empty(entry.Issues);
            });
        Assert.False(plan.HasBlockingIssues);
        Assert.True(plan.HasFileSystemMutations);
        Assert.True(plan.RequiresExplicitApproval);
        Assert.False(plan.CanApplyWithoutFileApproval);
    }

    [Fact]
    public void ManagedMoveReportsEveryUnsafeBoundary()
    {
        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.ManagedMove,
            [new FileOrganizationItemFacts(
                "unsafe",
                SourceAvailable: false,
                IsFileSystemItem: false,
                DestinationConfigured: false,
                SourceEqualsDestination: true,
                DestinationExists: true,
                IsReparsePoint: true,
                IsNetworkPath: true,
                IsCloudPlaceholder: true)]);

        FileOrganizationPlanEntry entry = Assert.Single(plan.Entries);
        Assert.Equal(
            [
                FileOrganizationIssueCode.SourceUnavailable,
                FileOrganizationIssueCode.NotFileSystemItem,
                FileOrganizationIssueCode.DestinationNotConfigured,
                FileOrganizationIssueCode.SourceEqualsDestination,
                FileOrganizationIssueCode.DestinationConflict,
                FileOrganizationIssueCode.ReparsePoint,
                FileOrganizationIssueCode.NetworkPath,
                FileOrganizationIssueCode.CloudPlaceholder,
            ],
            entry.Issues);
        Assert.True(plan.HasBlockingIssues);
        Assert.False(plan.RequiresExplicitApproval);
        Assert.False(plan.CanApplyWithoutFileApproval);
    }

    [Fact]
    public void OneUnsafeItemBlocksBatchApproval()
    {
        FileOrganizationItemFacts conflict = Movable("conflict") with
        {
            DestinationExists = true,
        };

        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.ManagedMove,
            [Movable("safe"), conflict]);

        Assert.True(plan.HasBlockingIssues);
        Assert.False(plan.RequiresExplicitApproval);
        Assert.False(plan.CanApplyWithoutFileApproval);
        Assert.False(plan.Entries[0].IsBlocked);
        Assert.True(plan.Entries[1].IsBlocked);
    }

    [Fact]
    public void InvalidPlanShapeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            FileOrganizationPlanner.Create(
                FileOrganizationMode.SafeReference,
                []));
        Assert.Throws<ArgumentException>(() =>
            FileOrganizationPlanner.Create(
                FileOrganizationMode.SafeReference,
                [Movable("duplicate"), Movable("duplicate")]));
        Assert.Throws<ArgumentException>(() =>
            FileOrganizationPlanner.Create(
                FileOrganizationMode.SafeReference,
                [Movable(" ")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileOrganizationPlanner.Create(
                (FileOrganizationMode)99,
                [Movable("item-1")]));
    }

    private static FileOrganizationItemFacts Movable(string itemId) =>
        new(
            itemId,
            SourceAvailable: true,
            IsFileSystemItem: true,
            DestinationConfigured: true);
}
