namespace LongGrid.Core.FileOperations;

public enum FileOrganizationMode
{
    SafeReference,
    ManagedMove,
}

public enum FileOrganizationAction
{
    AddReference,
    MoveFile,
}

public enum FileOrganizationIssueCode
{
    SourceUnavailable,
    NotFileSystemItem,
    DestinationNotConfigured,
    SourceEqualsDestination,
    DestinationConflict,
    ReparsePoint,
    NetworkPath,
    CloudPlaceholder,
}

public sealed record FileOrganizationItemFacts(
    string ItemId,
    bool SourceAvailable,
    bool IsFileSystemItem,
    bool DestinationConfigured = false,
    bool SourceEqualsDestination = false,
    bool DestinationExists = false,
    bool IsReparsePoint = false,
    bool IsNetworkPath = false,
    bool IsCloudPlaceholder = false);

public sealed record FileOrganizationPlanEntry(
    string ItemId,
    FileOrganizationAction Action,
    IReadOnlyList<FileOrganizationIssueCode> Issues)
{
    public bool IsBlocked => Issues.Count > 0;
}

public sealed record FileOrganizationPlan(
    FileOrganizationMode Mode,
    IReadOnlyList<FileOrganizationPlanEntry> Entries)
{
    public bool HasBlockingIssues => Entries.Any(entry => entry.IsBlocked);

    public bool HasFileSystemMutations => Entries.Any(
        entry => entry.Action == FileOrganizationAction.MoveFile);

    public bool RequiresExplicitApproval =>
        !HasBlockingIssues && HasFileSystemMutations;

    public bool CanApplyWithoutFileApproval =>
        !HasBlockingIssues && !HasFileSystemMutations;
}

public static class FileOrganizationPlanner
{
    public static FileOrganizationPlan Create(
        FileOrganizationMode mode,
        IEnumerable<FileOrganizationItemFacts> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        FileOrganizationItemFacts[] itemArray = items.ToArray();
        Validate(itemArray);

        FileOrganizationPlanEntry[] entries = itemArray
            .Select(item => CreateEntry(mode, item))
            .ToArray();
        return new FileOrganizationPlan(mode, entries);
    }

    private static FileOrganizationPlanEntry CreateEntry(
        FileOrganizationMode mode,
        FileOrganizationItemFacts item)
    {
        FileOrganizationAction action = mode switch
        {
            FileOrganizationMode.SafeReference =>
                FileOrganizationAction.AddReference,
            FileOrganizationMode.ManagedMove =>
                FileOrganizationAction.MoveFile,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var issues = new List<FileOrganizationIssueCode>();
        if (!item.SourceAvailable)
        {
            issues.Add(FileOrganizationIssueCode.SourceUnavailable);
        }

        if (mode == FileOrganizationMode.ManagedMove)
        {
            AddManagedMoveIssues(item, issues);
        }

        return new FileOrganizationPlanEntry(item.ItemId, action, issues.ToArray());
    }

    private static void AddManagedMoveIssues(
        FileOrganizationItemFacts item,
        List<FileOrganizationIssueCode> issues)
    {
        if (!item.IsFileSystemItem)
        {
            issues.Add(FileOrganizationIssueCode.NotFileSystemItem);
        }

        if (!item.DestinationConfigured)
        {
            issues.Add(FileOrganizationIssueCode.DestinationNotConfigured);
        }

        if (item.SourceEqualsDestination)
        {
            issues.Add(FileOrganizationIssueCode.SourceEqualsDestination);
        }

        if (item.DestinationExists)
        {
            issues.Add(FileOrganizationIssueCode.DestinationConflict);
        }

        if (item.IsReparsePoint)
        {
            issues.Add(FileOrganizationIssueCode.ReparsePoint);
        }

        if (item.IsNetworkPath)
        {
            issues.Add(FileOrganizationIssueCode.NetworkPath);
        }

        if (item.IsCloudPlaceholder)
        {
            issues.Add(FileOrganizationIssueCode.CloudPlaceholder);
        }
    }

    private static void Validate(IReadOnlyList<FileOrganizationItemFacts> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException(
                "At least one item is required.",
                nameof(items));
        }

        if (items.Any(item => string.IsNullOrWhiteSpace(item.ItemId)))
        {
            throw new ArgumentException(
                "Every item requires an anonymous stable ID.",
                nameof(items));
        }

        if (items
            .Select(item => item.ItemId)
            .Distinct(StringComparer.Ordinal)
            .Count() != items.Count)
        {
            throw new ArgumentException(
                "Item IDs must be unique within a plan.",
                nameof(items));
        }
    }
}
