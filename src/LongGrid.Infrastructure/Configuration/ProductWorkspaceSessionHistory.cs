using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductWorkspaceSessionHistoryActionKind
{
    Create,
    Rename,
    Locked,
    Collapsed,
    Appearance,
    Delete,
    Placement,
    FolderBinding,
    ReferenceAddition,
    ReferenceRemoval,
    ReferenceReassignment,
    ReferenceOrder,
    LayoutRecovery,
    QuickStart,
}

public enum ProductWorkspaceSessionHistoryDirection
{
    Undo,
    Redo,
}

public enum ProductWorkspaceSessionHistoryNavigationStatus
{
    Accepted,
    Unavailable,
    CurrentConfigurationChanged,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceSessionHistoryItem(
    Guid OperationId,
    ProductWorkspaceSessionHistoryActionKind Kind,
    string ActionText,
    string TargetType,
    string TargetName,
    int TargetCount,
    DateTimeOffset OccurredAtUtc,
    bool IsApplied,
    bool CanUndo,
    bool CanRedo);

public sealed record ProductWorkspaceSessionHistorySnapshot(
    IReadOnlyList<ProductWorkspaceSessionHistoryItem> Items,
    int Cursor,
    int Capacity,
    string? UnavailableReason)
{
    public static ProductWorkspaceSessionHistorySnapshot Empty { get; } =
        new([], 0, ProductWorkspaceSessionHistory.MaximumCapacity, null);

    public bool CanUndo => UnavailableReason is null
        && Cursor > 0
        && Items.Count > 0;

    public bool CanRedo => UnavailableReason is null
        && Cursor < Items.Count;
}

public sealed record ProductWorkspaceSessionHistoryNavigationToken(
    Guid OperationId,
    ProductWorkspaceSessionHistoryDirection Direction,
    int FromCursor,
    int ToCursor,
    string FromConfigurationFingerprint,
    string ToConfigurationFingerprint);

public sealed record ProductWorkspaceSessionHistoryCommitResult(
    ProductWorkspaceSessionHistoryNavigationStatus Status,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    ProductWorkspaceSessionHistoryDirection Direction,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceSessionHistoryNavigationToken? NavigationToken,
    bool IsCompensation = false)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceSessionHistoryNavigationStatus.Accepted
        && State is not null
        && Document is not null;
}

internal sealed class ProductWorkspaceSessionHistory
{
    public const int MaximumCapacity = 50;

    private readonly List<Entry> entries = [];
    private int cursor;
    private string? unavailableReason;
    private RecordRollback? pendingRecordRollback;

    public ProductWorkspaceSessionHistorySnapshot Snapshot(
        ProductWorkspaceState? currentState)
    {
        if (currentState is not null
            && entries.Count > 0
            && !TryMatchCurrentState(currentState))
        {
            unavailableReason = "当前配置已在历史之外发生变化";
        }

        ProductWorkspaceSessionHistoryItem[] items = entries
            .Select((entry, index) => new ProductWorkspaceSessionHistoryItem(
                entry.OperationId,
                entry.Kind,
                entry.ActionText,
                entry.TargetType,
                entry.TargetName,
                entry.TargetCount,
                entry.OccurredAtUtc,
                IsApplied: index < cursor,
                CanUndo: unavailableReason is null && index == cursor - 1,
                CanRedo: unavailableReason is null && index == cursor))
            .Reverse()
            .ToArray();
        return new(items, cursor, MaximumCapacity, unavailableReason);
    }

    public void Invalidate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (entries.Count > 0)
        {
            unavailableReason = reason;
        }
    }

    public bool Record(
        ProductWorkspaceState before,
        ProductWorkspaceState after,
        ProductWorkspaceSessionHistoryActionKind kind,
        string actionText,
        string targetType,
        string targetName,
        int targetCount,
        DateTimeOffset occurredAtUtc,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!Enum.IsDefined(kind)
            || string.IsNullOrWhiteSpace(actionText)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(targetName)
            || targetCount <= 0
            || operationId == Guid.Empty
            || !TryFingerprint(before, out string beforeFingerprint)
            || !TryFingerprint(after, out string afterFingerprint)
            || string.Equals(
                beforeFingerprint,
                afterFingerprint,
                StringComparison.Ordinal))
        {
            return false;
        }

        Entry[] previousEntries = entries.ToArray();
        int previousCursor = cursor;
        string? previousUnavailableReason = unavailableReason;
        if (entries.Count > 0
            && (!TryExpectedFingerprint(out string expectedFingerprint)
                || !string.Equals(
                    expectedFingerprint,
                    beforeFingerprint,
                    StringComparison.Ordinal)))
        {
            entries.Clear();
            cursor = 0;
        }
        else if (cursor < entries.Count)
        {
            entries.RemoveRange(cursor, entries.Count - cursor);
        }

        unavailableReason = null;
        entries.Add(new(
            operationId,
            kind,
            actionText.Trim(),
            targetType.Trim(),
            targetName.Trim(),
            targetCount,
            occurredAtUtc,
            before,
            after,
            beforeFingerprint,
            afterFingerprint));
        cursor = entries.Count;
        if (entries.Count > MaximumCapacity)
        {
            int overflow = entries.Count - MaximumCapacity;
            entries.RemoveRange(0, overflow);
            cursor -= overflow;
        }
        pendingRecordRollback = new(
            beforeFingerprint,
            afterFingerprint,
            previousEntries,
            previousCursor,
            previousUnavailableReason);

        return true;
    }

    public bool RollbackLatestRecord(
        ProductWorkspaceState failedState,
        ProductWorkspaceState restoredState)
    {
        ArgumentNullException.ThrowIfNull(failedState);
        ArgumentNullException.ThrowIfNull(restoredState);
        RecordRollback? rollback = pendingRecordRollback;
        if (rollback is null
            || !TryFingerprint(failedState, out string failedFingerprint)
            || !TryFingerprint(restoredState, out string restoredFingerprint)
            || !string.Equals(
                failedFingerprint,
                rollback.AfterFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                restoredFingerprint,
                rollback.BeforeFingerprint,
                StringComparison.Ordinal))
        {
            return false;
        }

        entries.Clear();
        entries.AddRange(rollback.PreviousEntries);
        cursor = rollback.PreviousCursor;
        unavailableReason = rollback.PreviousUnavailableReason;
        pendingRecordRollback = null;
        return true;
    }

    public NavigationPlan? Prepare(
        ProductWorkspaceState currentState,
        ProductWorkspaceSessionHistoryDirection direction,
        out ProductWorkspaceSessionHistoryNavigationStatus failure)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        if (!Enum.IsDefined(direction))
        {
            failure = ProductWorkspaceSessionHistoryNavigationStatus.InvalidState;
            return null;
        }
        if (entries.Count == 0)
        {
            failure = ProductWorkspaceSessionHistoryNavigationStatus.Unavailable;
            return null;
        }
        if (unavailableReason is not null || !TryMatchCurrentState(currentState))
        {
            unavailableReason ??= "当前配置已在历史之外发生变化";
            failure = ProductWorkspaceSessionHistoryNavigationStatus
                .CurrentConfigurationChanged;
            return null;
        }

        int entryIndex = direction == ProductWorkspaceSessionHistoryDirection.Undo
            ? cursor - 1
            : cursor;
        if (entryIndex < 0 || entryIndex >= entries.Count)
        {
            failure = ProductWorkspaceSessionHistoryNavigationStatus.Unavailable;
            return null;
        }

        Entry entry = entries[entryIndex];
        ProductWorkspaceState target = direction ==
            ProductWorkspaceSessionHistoryDirection.Undo
                ? entry.Before
                : entry.After;
        int toCursor = direction == ProductWorkspaceSessionHistoryDirection.Undo
            ? cursor - 1
            : cursor + 1;
        string targetFingerprint = direction ==
            ProductWorkspaceSessionHistoryDirection.Undo
                ? entry.BeforeFingerprint
                : entry.AfterFingerprint;
        if (!TryFingerprint(currentState, out string currentFingerprint))
        {
            failure = ProductWorkspaceSessionHistoryNavigationStatus.InvalidState;
            return null;
        }

        failure = ProductWorkspaceSessionHistoryNavigationStatus.Accepted;
        return new(
            entry.OperationId,
            direction,
            cursor,
            toCursor,
            currentState,
            target,
            currentFingerprint,
            targetFingerprint);
    }

    public ProductWorkspaceSessionHistoryNavigationToken Accept(
        NavigationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cursor = plan.ToCursor;
        unavailableReason = null;
        return new(
            plan.OperationId,
            plan.Direction,
            plan.FromCursor,
            plan.ToCursor,
            plan.FromFingerprint,
            plan.ToFingerprint);
    }

    public bool TryPrepareCompensation(
        ProductWorkspaceState currentState,
        ProductWorkspaceSessionHistoryNavigationToken token,
        NavigationPlan acceptedPlan,
        out ProductWorkspaceState? restoreState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(acceptedPlan);
        restoreState = null;
        if (token.OperationId != acceptedPlan.OperationId
            || token.Direction != acceptedPlan.Direction
            || token.FromCursor != acceptedPlan.FromCursor
            || token.ToCursor != acceptedPlan.ToCursor
            || token.FromConfigurationFingerprint != acceptedPlan.FromFingerprint
            || token.ToConfigurationFingerprint != acceptedPlan.ToFingerprint
            || cursor != token.ToCursor
            || !TryFingerprint(currentState, out string currentFingerprint)
            || !string.Equals(
                currentFingerprint,
                token.ToConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            return false;
        }

        restoreState = acceptedPlan.FromState;
        return true;
    }

    public void AcceptCompensation(
        ProductWorkspaceSessionHistoryNavigationToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        cursor = token.FromCursor;
        unavailableReason = null;
    }

    private bool TryMatchCurrentState(ProductWorkspaceState currentState) =>
        TryFingerprint(currentState, out string fingerprint)
        && TryExpectedFingerprint(out string expected)
        && string.Equals(fingerprint, expected, StringComparison.Ordinal);

    private bool TryExpectedFingerprint(out string fingerprint)
    {
        fingerprint = string.Empty;
        if (entries.Count == 0)
        {
            return true;
        }
        if (cursor == 0)
        {
            fingerprint = entries[0].BeforeFingerprint;
            return true;
        }
        if (cursor <= entries.Count)
        {
            fingerprint = entries[cursor - 1].AfterFingerprint;
            return true;
        }

        return false;
    }

    private static bool TryFingerprint(
        ProductWorkspaceState state,
        out string fingerprint)
    {
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            fingerprint = string.Empty;
            return false;
        }

        fingerprint = ProductWorkspaceConfigurationFingerprint.Compute(
            projection.Document!);
        return true;
    }

    internal sealed record NavigationPlan(
        Guid OperationId,
        ProductWorkspaceSessionHistoryDirection Direction,
        int FromCursor,
        int ToCursor,
        ProductWorkspaceState FromState,
        ProductWorkspaceState ToState,
        string FromFingerprint,
        string ToFingerprint);

    private sealed record Entry(
        Guid OperationId,
        ProductWorkspaceSessionHistoryActionKind Kind,
        string ActionText,
        string TargetType,
        string TargetName,
        int TargetCount,
        DateTimeOffset OccurredAtUtc,
        ProductWorkspaceState Before,
        ProductWorkspaceState After,
        string BeforeFingerprint,
        string AfterFingerprint);

    private sealed record RecordRollback(
        string BeforeFingerprint,
        string AfterFingerprint,
        IReadOnlyList<Entry> PreviousEntries,
        int PreviousCursor,
        string? PreviousUnavailableReason);
}
