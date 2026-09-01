using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductWorkspaceReferenceCommitStatus
{
    Kept,
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceCommitResult(
    ProductWorkspaceReferenceCommitStatus Status,
    ProductWorkspaceReferenceGateError GateError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceContainerCommitAction
{
    Create,
    Rename,
    SetLocked,
    SetCollapsed,
    SetAppearancePreset,
    SetPlacementPreset,
    BindFolder,
    UnbindFolder,
    Remove,
}

public enum ProductWorkspaceContainerColorPreset
{
    Azure,
    Indigo,
    Slate,
    Emerald,
    Amber,
}

public enum ProductWorkspaceContainerOpacityPreset
{
    Solid,
    Strong,
    Soft,
    Subtle,
}

public enum ProductWorkspaceContainerPositionPreset
{
    Start,
    OffsetOne,
    OffsetTwo,
    OffsetThree,
}

public enum ProductWorkspaceContainerSizePreset
{
    Compact,
    Standard,
    Wide,
    Large,
}

public enum ProductWorkspaceContainerCommitStatus
{
    Accepted,
    NoChange,
    StaleEditRevision,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceContainerCommitRequest(
    ProductWorkspaceContainerCommitAction Action,
    long ExpectedEditRevision,
    int ContainerOrdinal,
    string Name,
    ProductContainerState? NewContainer = null,
    bool? StateValue = null,
    ProductWorkspaceContainerColorPreset? ColorPreset = null,
    ProductWorkspaceContainerOpacityPreset? OpacityPreset = null,
    ProductWorkspaceContainerPositionPreset? PositionPreset = null,
    ProductWorkspaceContainerSizePreset? SizePreset = null,
    bool Confirmed = false,
    ProductContainerTitleVisibilityPolicy? TitleVisibility = null,
    ProductContainerTitleDoubleClickAction? TitleDoubleClickAction = null,
    bool TrackUndo = true,
    ProductContainerFolderBindingState? FolderBinding = null,
    ProductContainerContentDensity? ContentDensity = null);

public sealed record ProductWorkspaceContainerCommitResult(
    ProductWorkspaceContainerCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceContainerRemovalUndoToken? RemovalUndoToken = null,
    ProductWorkspaceContainerEditUndoToken? EditUndoToken = null)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceContainerEditUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceContainerEditUndoCommitResult(
    ProductWorkspaceContainerEditUndoCommitStatus Status,
    ProductWorkspaceContainerEditUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerEditUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceContainerLayoutGestureCommitStatus
{
    Accepted,
    StaleEditRevision,
    StaleTopology,
    StateChanged,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceContainerLayoutGestureCommitResult(
    ProductWorkspaceContainerLayoutGestureCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceContainerLayoutPublicationToken? PublicationToken = null)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerLayoutGestureCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceContainerLayoutCompensationCommitStatus
{
    Accepted,
    AwaitingSave,
    Published,
    Superseded,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceContainerLayoutCompensationCommitResult(
    ProductWorkspaceContainerLayoutCompensationCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    ProductWorkspaceSaveFailure SourceFailure,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerLayoutCompensationCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceSelectedReferenceContainerCommitStatus
{
    Accepted,
    StaleEditRevision,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceSelectedReferenceContainerCommitRequest(
    long ExpectedEditRevision,
    int SourceContainerOrdinal,
    IReadOnlyList<string> ItemIds,
    ProductContainerState NewContainer);

public sealed record ProductWorkspaceSelectedReferenceContainerCommitResult(
    ProductWorkspaceSelectedReferenceContainerCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceBatchAdditionUndoToken? UndoToken)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceSelectedReferenceContainerCommitStatus.Accepted
        && State is not null
        && Document is not null
        && UndoToken is not null;
}

public enum ProductWorkspaceContainerRemovalUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceContainerRemovalUndoCommitResult(
    ProductWorkspaceContainerRemovalUndoCommitStatus Status,
    ProductWorkspaceContainerRemovalUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerRemovalUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceResolvedReferenceCommitStatus
{
    Accepted,
    StaleEditRevision,
    StaleCatalogGeneration,
    AlreadyReferenced,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceResolvedReferenceCommitRequest(
    long ExpectedEditRevision,
    long ExpectedCatalogGeneration,
    int ContainerOrdinal,
    int CatalogIndex);

public sealed record ProductWorkspaceResolvedReferenceCommitResult(
    ProductWorkspaceResolvedReferenceCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceResolvedReferenceCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceResolvedReferenceBatchCommitStatus
{
    Accepted,
    StaleEditRevision,
    StaleCatalogGeneration,
    AlreadyReferenced,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceResolvedReferenceBatchCommitRequest(
    long ExpectedEditRevision,
    long ExpectedCatalogGeneration,
    int ContainerOrdinal,
    IReadOnlyList<int> CatalogIndexes);

public sealed record ProductWorkspaceResolvedReferenceBatchCommitResult(
    ProductWorkspaceResolvedReferenceBatchCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceBatchAdditionUndoToken? UndoToken)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceResolvedReferenceBatchCommitStatus.Accepted
        && State is not null
        && Document is not null
        && UndoToken is not null;
}

public enum ProductWorkspaceReferenceBatchAdditionUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceBatchAdditionUndoCommitResult(
    ProductWorkspaceReferenceBatchAdditionUndoCommitStatus Status,
    ProductWorkspaceReferenceBatchAdditionUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceResolvedReferenceRemovalCommitStatus
{
    Accepted,
    StaleEditRevision,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceResolvedReferenceRemovalCommitRequest(
    long ExpectedEditRevision,
    int ContainerOrdinal,
    int ItemOrdinal);

public sealed record ProductWorkspaceResolvedReferenceRemovalCommitResult(
    ProductWorkspaceResolvedReferenceRemovalCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceRemovalUndoToken? UndoToken)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceResolvedReferenceRemovalCommitStatus.Accepted
        && State is not null
        && Document is not null
        && UndoToken is not null;
}

public enum ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
{
    Accepted,
    StaleEditRevision,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceResolvedReferenceBatchRemovalCommitRequest(
    long ExpectedEditRevision,
    int ContainerOrdinal,
    IReadOnlyList<int> ItemOrdinals);

public sealed record ProductWorkspaceResolvedReferenceBatchRemovalCommitResult(
    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceRemovalUndoToken? UndoToken)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.Accepted
        && State is not null
        && Document is not null
        && UndoToken is not null;
}

public enum ProductWorkspaceReferenceRemovalUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceRemovalUndoCommitResult(
    ProductWorkspaceReferenceRemovalUndoCommitStatus Status,
    ProductWorkspaceReferenceRemovalUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceRemovalUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceResolvedReferenceReassignmentCommitStatus
{
    Accepted,
    StaleEditRevision,
    ReducerRejected,
    SaveRejected,
    InvalidRequest,
}

public sealed record ProductWorkspaceResolvedReferenceReassignmentCommitRequest(
    long ExpectedEditRevision,
    int SourceContainerOrdinal,
    IReadOnlyList<int> ItemOrdinals,
    int TargetContainerOrdinal)
{
    public ProductWorkspaceResolvedReferenceReassignmentCommitRequest(
        long expectedEditRevision,
        int sourceContainerOrdinal,
        int itemOrdinal,
        int targetContainerOrdinal)
        : this(
            expectedEditRevision,
            sourceContainerOrdinal,
            [itemOrdinal],
            targetContainerOrdinal)
    {
    }
}

public sealed record ProductWorkspaceResolvedReferenceReassignmentCommitResult(
    ProductWorkspaceResolvedReferenceReassignmentCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceReferenceReassignmentUndoToken? UndoToken)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceResolvedReferenceReassignmentCommitStatus.Accepted
        && State is not null
        && Document is not null
        && UndoToken is not null;
}

public enum ProductWorkspaceReferenceReassignmentUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceReferenceReassignmentUndoCommitResult(
    ProductWorkspaceReferenceReassignmentUndoCommitStatus Status,
    ProductWorkspaceReferenceReassignmentUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceReferenceReassignmentUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceLayoutRecoveryCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceLayoutRecoveryCommitResult(
    ProductWorkspaceLayoutRecoveryCommitStatus Status,
    ProductWorkspaceLayoutRecoveryConfirmationStatus ConfirmationStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceLayoutRecoveryCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductWorkspaceLayoutRecoveryUndoCommitStatus
{
    Accepted,
    GateRejected,
    SaveRejected,
    InvalidState,
}

public sealed record ProductWorkspaceLayoutRecoveryUndoCommitResult(
    ProductWorkspaceLayoutRecoveryUndoCommitStatus Status,
    ProductWorkspaceLayoutRecoveryUndoStatus UndoStatus,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceLayoutRecoveryUndoCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public sealed class ProductWorkspaceCommitCoordinator
{
    public const int MaximumResolvedReferenceBatchSize = 256;
    public const int MaximumResolvedReferenceRemovalBatchSize = 256;
    public const int MaximumResolvedReferenceReassignmentBatchSize = 256;

    private readonly object gate = new();
    private readonly ProductWorkspaceSaveController saves;
    private long editRevision;
    private PendingLayoutRecoveryUndo? pendingLayoutRecoveryUndo;
    private PendingReferenceRemovalUndo? pendingReferenceRemovalUndo;
    private PendingReferenceReassignmentUndo? pendingReferenceReassignmentUndo;
    private PendingContainerRemovalUndo? pendingContainerRemovalUndo;
    private PendingContainerEditUndo? pendingContainerEditUndo;
    private PendingReferenceBatchAdditionUndo? pendingReferenceBatchAdditionUndo;
    private PendingContainerLayoutPublication?
        pendingContainerLayoutPublication;

    public ProductWorkspaceCommitCoordinator(
        ProductWorkspaceSaveController saves)
    {
        ArgumentNullException.ThrowIfNull(saves);
        this.saves = saves;
    }

    public long CurrentEditRevision
    {
        get
        {
            lock (gate)
            {
                return editRevision;
            }
        }
    }

    public ProductWorkspaceLayoutRecoveryUndoToken? CurrentLayoutRecoveryUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingLayoutRecoveryUndo?.Token;
            }
        }
    }

    public ProductWorkspaceReferenceRemovalUndoToken?
        CurrentReferenceRemovalUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingReferenceRemovalUndo?.Token;
            }
        }
    }

    public ProductWorkspaceReferenceReassignmentUndoToken?
        CurrentReferenceReassignmentUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingReferenceReassignmentUndo?.Token;
            }
        }
    }

    public ProductWorkspaceContainerRemovalUndoToken?
        CurrentContainerRemovalUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingContainerRemovalUndo?.Token;
            }
        }
    }

    public ProductWorkspaceContainerEditUndoToken?
        CurrentContainerEditUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingContainerEditUndo?.Token;
            }
        }
    }

    public ProductWorkspaceReferenceBatchAdditionUndoToken?
        CurrentReferenceBatchAdditionUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingReferenceBatchAdditionUndo is { CreatesContainer: false }
                    ? pendingReferenceBatchAdditionUndo.Token
                    : null;
            }
        }
    }

    public ProductWorkspaceReferenceBatchAdditionUndoToken?
        CurrentSelectedReferenceContainerUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingReferenceBatchAdditionUndo is { CreatesContainer: true }
                    ? pendingReferenceBatchAdditionUndo.Token
                    : null;
            }
        }
    }

    public long AdvanceExternalRevision()
    {
        lock (gate)
        {
            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            pendingContainerLayoutPublication = null;
            return editRevision;
        }
    }

    public ProductWorkspaceReferenceCommitResult Commit(
        ProductWorkspaceState state,
        long catalogGeneration,
        IReadOnlyList<DesktopCatalogEntry> catalog,
        ProductWorkspaceReferenceActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            ProductWorkspaceReferenceGateResult review =
                ProductWorkspaceReferenceGate.Evaluate(
                    state,
                    catalogGeneration,
                    editRevision,
                    catalog,
                    request);
            if (!review.IsSuccess)
            {
                return new(
                    ProductWorkspaceReferenceCommitStatus.GateRejected,
                    review.Error,
                    null,
                    editRevision,
                    null,
                    null);
            }

            if (!review.WouldChange || review.Preview is null)
            {
                return new(
                    ProductWorkspaceReferenceCommitStatus.Kept,
                    ProductWorkspaceReferenceGateError.None,
                    ProductWorkspaceSaveSubmissionStatus.NoChange,
                    editRevision,
                    state,
                    null);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(
                    review.Preview.State!);
            if (!projection.IsSuccess)
            {
                return new(
                    ProductWorkspaceReferenceCommitStatus.InvalidState,
                    ProductWorkspaceReferenceGateError.ReducerRejected,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState,
                    editRevision,
                    null,
                    null);
            }

            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceSaveSubmissionResult submission =
                saves.Submit(review.Preview);
            if (!submission.IsAccepted)
            {
                return new(
                    ProductWorkspaceReferenceCommitStatus.SaveRejected,
                    ProductWorkspaceReferenceGateError.None,
                    submission.Status,
                    editRevision,
                    null,
                    null);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceReferenceCommitStatus.Accepted,
                ProductWorkspaceReferenceGateError.None,
                submission.Status,
                editRevision,
                review.Preview.State,
                projection.Document);
        }
    }

    public ProductWorkspaceContainerLayoutGestureCommitResult
        CommitContainerLayoutGesture(
            ProductWorkspaceState state,
            long currentTopologyGeneration,
            ProductWorkspaceContainerLayoutGestureCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(completion);

        lock (gate)
        {
            if (completion.ExpectedEditRevision != editRevision)
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .StaleEditRevision);
            }

            if (completion.ExpectedTopologyGeneration
                != currentTopologyGeneration)
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .StaleTopology);
            }

            ProductContainerState[] targets = state.Containers
                .Where(candidate => candidate is not null && string.Equals(
                    candidate.Id,
                    completion.ContainerId,
                    StringComparison.Ordinal))
                .ToArray();
            if (completion.OperationId == Guid.Empty
                || completion.ExpectedTopologyGeneration <= 0
                || completion.UpdateCount <= 0
                || string.IsNullOrWhiteSpace(completion.DisplayId)
                || targets.Length != 1
                || !string.Equals(
                    completion.DisplayId,
                    completion.Placement.DisplayKey,
                    StringComparison.Ordinal))
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState target = targets[0];
            if (!PlacementMatches(
                    target.Placement,
                    completion.OriginalPlacement))
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .StateChanged);
            }

            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.UpdatePlacement(
                    state,
                    target.Id,
                    completion.Placement);
            if (!edit.IsSuccess || !edit.Changed)
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ContainerLayoutGestureFailure(
                    ProductWorkspaceContainerLayoutGestureCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            var publicationToken =
                new ProductWorkspaceContainerLayoutPublicationToken(
                    completion.OperationId,
                    completion.ContainerId,
                    editRevision,
                    submission.Snapshot.CurrentRevision,
                    completion.ExpectedTopologyGeneration,
                    ClonePlacement(completion.OriginalPlacement),
                    ClonePlacement(completion.Placement));
            pendingContainerLayoutPublication = new(
                publicationToken,
                ClonePlacement(completion.OriginalPlacement),
                ClonePlacement(completion.Placement));
            return new(
                ProductWorkspaceContainerLayoutGestureCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                publicationToken);
        }
    }

    public ProductWorkspaceContainerLayoutCompensationCommitResult
        CompensateContainerLayoutGesture(
            ProductWorkspaceState state,
            ProductWorkspaceContainerLayoutPublicationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            if (pendingContainerLayoutPublication is null)
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .Superseded);
            }
            if (!ReferenceEquals(
                    token,
                    pendingContainerLayoutPublication.PublicToken))
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState[] targets = state.Containers
                .Where(candidate => candidate is not null && string.Equals(
                    candidate.Id,
                    token.ContainerId,
                    StringComparison.Ordinal))
                .ToArray();
            if (token.OperationId == Guid.Empty
                || string.IsNullOrWhiteSpace(token.ContainerId)
                || token.WorkspaceRevision <= 0
                || token.SaveRevision <= 0
                || token.TopologyGeneration <= 0
                || token.OriginalPlacement is null
                || token.CommittedPlacement is null
                || targets.Length > 1)
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .InvalidRequest);
            }

            ProductWorkspaceContainerLayoutPublicationToken trustedToken =
                token with
                {
                    OriginalPlacement = ClonePlacement(
                        pendingContainerLayoutPublication.OriginalPlacement),
                    CommittedPlacement = ClonePlacement(
                        pendingContainerLayoutPublication.CommittedPlacement),
                };
            ProductWorkspaceSaveSnapshot save = saves.Snapshot;
            ProductWorkspaceContainerLayoutPublicationDecision decision =
                ProductWorkspaceContainerLayoutPublication.Evaluate(
                    trustedToken,
                    save,
                    editRevision,
                    targets.SingleOrDefault()?.Placement);
            if (decision !=
                ProductWorkspaceContainerLayoutPublicationDecision
                    .CompensationRequired)
            {
                if (decision is
                    ProductWorkspaceContainerLayoutPublicationDecision.Published
                    or ProductWorkspaceContainerLayoutPublicationDecision.Superseded)
                {
                    pendingContainerLayoutPublication = null;
                }
                return ContainerLayoutCompensationFailure(
                    decision switch
                    {
                        ProductWorkspaceContainerLayoutPublicationDecision
                            .AwaitingSave =>
                            ProductWorkspaceContainerLayoutCompensationCommitStatus
                                .AwaitingSave,
                        ProductWorkspaceContainerLayoutPublicationDecision
                            .Published =>
                            ProductWorkspaceContainerLayoutCompensationCommitStatus
                                .Published,
                        _ =>
                            ProductWorkspaceContainerLayoutCompensationCommitStatus
                                .Superseded,
                    },
                    sourceFailure: save.Failure);
            }

            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.UpdatePlacement(
                    state,
                    token.ContainerId,
                    trustedToken.OriginalPlacement);
            if (!edit.IsSuccess || !edit.Changed)
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .ReducerRejected,
                    edit.Error,
                    sourceFailure: save.Failure);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState,
                    save.Failure);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ContainerLayoutCompensationFailure(
                    ProductWorkspaceContainerLayoutCompensationCommitStatus
                        .SaveRejected,
                    submission.EditError,
                    submission.Status,
                    save.Failure);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            pendingContainerLayoutPublication = null;
            return new(
                ProductWorkspaceContainerLayoutCompensationCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                save.Failure,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceContainerCommitResult CommitContainer(
        ProductWorkspaceState state,
        ProductWorkspaceContainerCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.StaleEditRevision);
            }
            bool hasTitleVisibility = request.TitleVisibility is not null;
            bool hasTitleDoubleClick = request.TitleDoubleClickAction is not null;
            if (hasTitleVisibility != hasTitleDoubleClick
                || (request.ContentDensity is not null
                    && (request.Action !=
                            ProductWorkspaceContainerCommitAction.SetAppearancePreset
                        || !Enum.IsDefined(request.ContentDensity.Value)))
                || (request.Action != ProductWorkspaceContainerCommitAction.BindFolder
                    && request.FolderBinding is not null)
                || (hasTitleVisibility
                    && (request.Action !=
                            ProductWorkspaceContainerCommitAction.SetAppearancePreset
                        || !Enum.IsDefined(request.TitleVisibility!.Value)
                        || !Enum.IsDefined(request.TitleDoubleClickAction!.Value))))
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.InvalidRequest);
            }

            ProductContainerState? target = request.ContainerOrdinal > 0
                && request.ContainerOrdinal <= state.Containers.Count
                    ? state.Containers[request.ContainerOrdinal - 1]
                    : null;
            ProductWorkspaceEditResult edit = request.Action switch
            {
                ProductWorkspaceContainerCommitAction.Create
                    when request.NewContainer is not null
                        && request.ContainerOrdinal == 0
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && !request.Confirmed
                        && string.Equals(
                            request.Name,
                            request.NewContainer.Name,
                            StringComparison.Ordinal) =>
                    ProductWorkspaceReducer.CreateContainer(
                        state,
                        request.NewContainer),
                ProductWorkspaceContainerCommitAction.Rename
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.RenameContainer(
                        state,
                        target.Id,
                        request.Name),
                ProductWorkspaceContainerCommitAction.SetLocked
                    when request.NewContainer is null
                        && request.StateValue is not null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.SetContainerLocked(
                        state,
                        target.Id,
                        request.StateValue.Value),
                ProductWorkspaceContainerCommitAction.SetCollapsed
                    when request.NewContainer is null
                        && request.StateValue is not null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.UpdateAppearance(
                        state,
                        target.Id,
                        target.Appearance with
                        {
                            Collapsed = request.StateValue.Value,
                        }),
                ProductWorkspaceContainerCommitAction.SetAppearancePreset
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is not null
                        && request.OpacityPreset is not null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && Enum.IsDefined(request.ColorPreset.Value)
                        && Enum.IsDefined(request.OpacityPreset.Value)
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.UpdateAppearance(
                        state,
                        target.Id,
                        target.Appearance with
                        {
                            Color = ResolveColor(request.ColorPreset.Value),
                            Opacity = ResolveOpacity(request.OpacityPreset.Value),
                            TitleVisibility = request.TitleVisibility
                                ?? target.Appearance.TitleVisibility,
                            TitleDoubleClickAction = request.TitleDoubleClickAction
                                ?? target.Appearance.TitleDoubleClickAction,
                            ContentDensity = request.ContentDensity
                                ?? target.Appearance.ContentDensity,
                        }),
                ProductWorkspaceContainerCommitAction.SetPlacementPreset
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is not null
                        && request.SizePreset is not null
                        && Enum.IsDefined(request.PositionPreset.Value)
                        && Enum.IsDefined(request.SizePreset.Value)
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.UpdatePlacement(
                        state,
                        target.Id,
                        target.Placement with
                        {
                            XDip = ResolvePosition(request.PositionPreset.Value).XDip,
                            YDip = ResolvePosition(request.PositionPreset.Value).YDip,
                            WidthDip = ResolveSize(request.SizePreset.Value).WidthDip,
                            HeightDip = ResolveSize(request.SizePreset.Value).HeightDip,
                        }),
                ProductWorkspaceContainerCommitAction.BindFolder
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && request.FolderBinding is
                        {
                            Resolution:
                                ProductContainerFolderBindingResolution.Resolved,
                        }
                        && string.IsNullOrEmpty(request.Name)
                        && !request.Confirmed
                        && target is not null =>
                    ProductWorkspaceReducer.SetFolderBinding(
                        state,
                        target.Id,
                        request.FolderBinding),
                ProductWorkspaceContainerCommitAction.UnbindFolder
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && request.FolderBinding is null
                        && string.IsNullOrEmpty(request.Name)
                        && request.Confirmed
                        && target is not null
                        && target.FolderBinding is not null =>
                    ProductWorkspaceReducer.SetFolderBinding(
                        state,
                        target.Id,
                        null),
                ProductWorkspaceContainerCommitAction.Remove
                    when request.NewContainer is null
                        && request.StateValue is null
                        && request.ColorPreset is null
                        && request.OpacityPreset is null
                        && request.PositionPreset is null
                        && request.SizePreset is null
                        && request.Confirmed
                        && string.IsNullOrEmpty(request.Name)
                        && target is not null =>
                    ProductWorkspaceReducer.RemoveContainer(
                        state,
                        target.Id,
                        confirmUnresolvedReferences: true),
                _ => null!,
            };
            if (edit is null)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.InvalidRequest);
            }

            if (!edit.IsSuccess)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.ReducerRejected,
                    edit.Error);
            }

            if (!edit.Changed)
            {
                return new(
                    ProductWorkspaceContainerCommitStatus.NoChange,
                    ProductWorkspaceEditError.None,
                    ProductWorkspaceSaveSubmissionStatus.NoChange,
                    editRevision,
                    edit.State,
                    null);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceContainerRemovalUndoToken? removalUndoToken =
                request.Action == ProductWorkspaceContainerCommitAction.Remove
                    ? ProductWorkspaceContainerRemovalUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        Guid.NewGuid())
                    : null;
            ProductWorkspaceContainerEditUndoKind? editUndoKind =
                request.TrackUndo ? MapContainerEditUndoKind(request.Action) : null;
            ProductWorkspaceContainerEditUndoToken? editUndoToken =
                editUndoKind is { } kind
                    ? ProductWorkspaceContainerEditUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        kind,
                        Guid.NewGuid())
                    : null;
            if (request.Action == ProductWorkspaceContainerCommitAction.Remove
                && removalUndoToken is null)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState);
            }
            if (editUndoKind is not null && editUndoToken is null)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = removalUndoToken is null
                ? null
                : new(removalUndoToken, state);
            pendingContainerEditUndo = editUndoToken is null
                ? null
                : new(editUndoToken, state);
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceContainerCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                removalUndoToken,
                editUndoToken);
        }
    }

    private static ProductWorkspaceContainerEditUndoKind?
        MapContainerEditUndoKind(ProductWorkspaceContainerCommitAction action) =>
        action switch
        {
            ProductWorkspaceContainerCommitAction.Rename =>
                ProductWorkspaceContainerEditUndoKind.Rename,
            ProductWorkspaceContainerCommitAction.SetLocked =>
                ProductWorkspaceContainerEditUndoKind.Locked,
            ProductWorkspaceContainerCommitAction.SetCollapsed =>
                ProductWorkspaceContainerEditUndoKind.Collapsed,
            ProductWorkspaceContainerCommitAction.SetAppearancePreset =>
                ProductWorkspaceContainerEditUndoKind.Appearance,
            ProductWorkspaceContainerCommitAction.SetPlacementPreset =>
                ProductWorkspaceContainerEditUndoKind.Placement,
            ProductWorkspaceContainerCommitAction.BindFolder
                or ProductWorkspaceContainerCommitAction.UnbindFolder =>
                ProductWorkspaceContainerEditUndoKind.FolderBinding,
            _ => null,
        };

    public ProductWorkspaceContainerRemovalUndoCommitResult
        CommitContainerRemovalUndo(
            ProductWorkspaceState state,
            ProductWorkspaceContainerRemovalUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            PendingContainerRemovalUndo? pending = pendingContainerRemovalUndo;
            if (pending is null)
            {
                return ContainerRemovalUndoFailure(
                    ProductWorkspaceContainerRemovalUndoCommitStatus.GateRejected,
                    ProductWorkspaceContainerRemovalUndoStatus.Unavailable);
            }

            ProductWorkspaceContainerRemovalUndoResult undo =
                ProductWorkspaceContainerRemovalUndo.Confirm(
                    state,
                    pending.RestoreState,
                    editRevision,
                    token,
                    pending.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return ContainerRemovalUndoFailure(
                    ProductWorkspaceContainerRemovalUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(undo.Edit!.State!);
            if (!projection.IsSuccess)
            {
                return ContainerRemovalUndoFailure(
                    ProductWorkspaceContainerRemovalUndoCommitStatus.InvalidState,
                    ProductWorkspaceContainerRemovalUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(undo.Edit);
            if (!submission.IsAccepted)
            {
                return ContainerRemovalUndoFailure(
                    ProductWorkspaceContainerRemovalUndoCommitStatus.SaveRejected,
                    ProductWorkspaceContainerRemovalUndoStatus.Accepted,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceContainerRemovalUndoCommitStatus.Accepted,
                ProductWorkspaceContainerRemovalUndoStatus.Accepted,
                submission.Status,
                editRevision,
                undo.Edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceContainerEditUndoCommitResult
        CommitContainerEditUndo(
            ProductWorkspaceState state,
            ProductWorkspaceContainerEditUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            PendingContainerEditUndo? pending = pendingContainerEditUndo;
            if (pending is null)
            {
                return ContainerEditUndoFailure(
                    ProductWorkspaceContainerEditUndoCommitStatus.GateRejected,
                    ProductWorkspaceContainerEditUndoStatus.Unavailable);
            }

            ProductWorkspaceContainerEditUndoResult undo =
                ProductWorkspaceContainerEditUndo.Confirm(
                    state,
                    pending.RestoreState,
                    editRevision,
                    token,
                    pending.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return ContainerEditUndoFailure(
                    ProductWorkspaceContainerEditUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(undo.Edit!.State!);
            if (!projection.IsSuccess)
            {
                return ContainerEditUndoFailure(
                    ProductWorkspaceContainerEditUndoCommitStatus.InvalidState,
                    ProductWorkspaceContainerEditUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(undo.Edit);
            if (!submission.IsAccepted)
            {
                return ContainerEditUndoFailure(
                    ProductWorkspaceContainerEditUndoCommitStatus.SaveRejected,
                    ProductWorkspaceContainerEditUndoStatus.Accepted,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceContainerEditUndoCommitStatus.Accepted,
                ProductWorkspaceContainerEditUndoStatus.Accepted,
                submission.Status,
                editRevision,
                undo.Edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceSelectedReferenceContainerCommitResult
        CommitSelectedReferenceContainer(
            ProductWorkspaceState state,
            ProductWorkspaceSelectedReferenceContainerCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ItemIds);
        ArgumentNullException.ThrowIfNull(request.NewContainer);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return SelectedReferenceContainerFailure(
                    ProductWorkspaceSelectedReferenceContainerCommitStatus
                        .StaleEditRevision);
            }

            string[] itemIds = request.ItemIds.ToArray();
            if (request.SourceContainerOrdinal <= 0
                || request.SourceContainerOrdinal > state.Containers.Count
                || itemIds.Length == 0
                || itemIds.Length > MaximumResolvedReferenceBatchSize
                || itemIds.Any(string.IsNullOrWhiteSpace)
                || itemIds.Distinct(StringComparer.Ordinal).Count() != itemIds.Length
                || request.NewContainer.Items.Count != 0)
            {
                return SelectedReferenceContainerFailure(
                    ProductWorkspaceSelectedReferenceContainerCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState source =
                state.Containers[request.SourceContainerOrdinal - 1];
            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.CreateContainerFromResolvedReferences(
                    state,
                    source.Id,
                    itemIds,
                    request.NewContainer);
            if (!edit.IsSuccess)
            {
                return SelectedReferenceContainerFailure(
                    ProductWorkspaceSelectedReferenceContainerCommitStatus
                        .ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceBatchAdditionUndoToken? undoToken =
                projection.IsSuccess
                    ? ProductWorkspaceReferenceBatchAdditionUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        Guid.NewGuid())
                    : null;
            if (!projection.IsSuccess || undoToken is null)
            {
                return SelectedReferenceContainerFailure(
                    ProductWorkspaceSelectedReferenceContainerCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return SelectedReferenceContainerFailure(
                    ProductWorkspaceSelectedReferenceContainerCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = new(
                undoToken,
                state,
                CreatesContainer: true);
            return new(
                ProductWorkspaceSelectedReferenceContainerCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                undoToken);
        }
    }

    public ProductWorkspaceResolvedReferenceCommitResult CommitResolvedReference(
        ProductWorkspaceState state,
        long currentCatalogGeneration,
        IReadOnlyList<DesktopCatalogEntry> catalog,
        ProductWorkspaceResolvedReferenceCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.StaleEditRevision);
            }

            if (currentCatalogGeneration <= 0
                || request.ExpectedCatalogGeneration != currentCatalogGeneration)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus
                        .StaleCatalogGeneration);
            }

            if (request.ContainerOrdinal <= 0
                || request.ContainerOrdinal > state.Containers.Count
                || request.CatalogIndex < 0
                || request.CatalogIndex >= catalog.Count)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.InvalidRequest);
            }

            ProductContainerState container =
                state.Containers[request.ContainerOrdinal - 1];
            DesktopCatalogEntry catalogEntry = catalog[request.CatalogIndex];
            if (state.Containers
                .SelectMany(candidate => candidate.Items)
                .Any(item => string.Equals(
                    item.PersistedTarget,
                    catalogEntry.Identity.CanonicalTarget,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.AlreadyReferenced);
            }

            ProductItemReferenceState item =
                ProductItemReferenceState.CreateResolved(
                    $"item-{Guid.NewGuid():N}",
                    catalogEntry);
            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.AddResolvedReference(
                    state,
                    container.Id,
                    item);
            if (!edit.IsSuccess)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ResolvedReferenceFailure(
                    ProductWorkspaceResolvedReferenceCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceResolvedReferenceCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceResolvedReferenceBatchCommitResult
        CommitResolvedReferenceBatch(
            ProductWorkspaceState state,
            long currentCatalogGeneration,
            IReadOnlyList<DesktopCatalogEntry> catalog,
            ProductWorkspaceResolvedReferenceBatchCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CatalogIndexes);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.StaleEditRevision);
            }

            if (currentCatalogGeneration <= 0
                || request.ExpectedCatalogGeneration != currentCatalogGeneration)
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus
                        .StaleCatalogGeneration);
            }

            int[] indexes = request.CatalogIndexes.ToArray();
            if (request.ContainerOrdinal <= 0
                || request.ContainerOrdinal > state.Containers.Count
                || indexes.Length == 0
                || indexes.Length > MaximumResolvedReferenceBatchSize
                || indexes.Distinct().Count() != indexes.Length
                || indexes.Any(index => index < 0 || index >= catalog.Count))
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.InvalidRequest);
            }

            ProductContainerState container = state.Containers[request.ContainerOrdinal - 1];
            var assignedTargets = state.Containers
                .SelectMany(candidate => candidate.Items)
                .Select(item => item.PersistedTarget)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            DesktopCatalogEntry[] catalogEntries = indexes
                .Select(index => catalog[index])
                .ToArray();
            string[] targets = catalogEntries
                .Select(entry => entry.Identity.CanonicalTarget)
                .ToArray();
            if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != targets.Length
                || targets.Any(assignedTargets.Contains))
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.AlreadyReferenced);
            }

            ProductItemReferenceState[] items = catalogEntries
                .Select(entry => ProductItemReferenceState.CreateResolved(
                    $"item-{Guid.NewGuid():N}",
                    entry))
                .ToArray();
            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.AddResolvedReferences(state, container.Id, items);
            if (!edit.IsSuccess)
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceBatchAdditionUndoToken? undoToken =
                projection.IsSuccess
                    ? ProductWorkspaceReferenceBatchAdditionUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        Guid.NewGuid())
                    : null;
            if (!projection.IsSuccess || undoToken is null)
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ResolvedReferenceBatchFailure(
                    ProductWorkspaceResolvedReferenceBatchCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = new(
                undoToken,
                state,
                CreatesContainer: false);
            return new(
                ProductWorkspaceResolvedReferenceBatchCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                undoToken);
        }
    }

    public ProductWorkspaceReferenceBatchAdditionUndoCommitResult
        CommitReferenceBatchAdditionUndo(
            ProductWorkspaceState state,
            ProductWorkspaceReferenceBatchAdditionUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            if (pendingReferenceBatchAdditionUndo is null)
            {
                return ReferenceBatchAdditionUndoFailure(
                    ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.GateRejected,
                    ProductWorkspaceReferenceBatchAdditionUndoStatus.Unavailable);
            }

            ProductWorkspaceReferenceBatchAdditionUndoResult undo =
                ProductWorkspaceReferenceBatchAdditionUndo.Confirm(
                    state,
                    pendingReferenceBatchAdditionUndo.RestoreState,
                    editRevision,
                    token,
                    pendingReferenceBatchAdditionUndo.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return ReferenceBatchAdditionUndoFailure(
                    ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceEditResult edit = undo.Edit!;
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ReferenceBatchAdditionUndoFailure(
                    ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.InvalidState,
                    ProductWorkspaceReferenceBatchAdditionUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ReferenceBatchAdditionUndoFailure(
                    ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.SaveRejected,
                    undo.Status,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.Accepted,
                undo.Status,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceResolvedReferenceBatchRemovalCommitResult
        CommitResolvedReferenceBatchRemoval(
            ProductWorkspaceState state,
            ProductWorkspaceResolvedReferenceBatchRemovalCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ItemOrdinals);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .StaleEditRevision);
            }

            int[] ordinals = request.ItemOrdinals.ToArray();
            if (request.ContainerOrdinal <= 0
                || request.ContainerOrdinal > state.Containers.Count
                || ordinals.Length == 0
                || ordinals.Length > MaximumResolvedReferenceRemovalBatchSize
                || ordinals.Distinct().Count() != ordinals.Length)
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState container =
                state.Containers[request.ContainerOrdinal - 1];
            if (ordinals.Any(ordinal =>
                    ordinal <= 0
                    || ordinal > container.Items.Count
                    || container.Items[ordinal - 1].Resolution !=
                        ProductItemReferenceResolution.Resolved))
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .InvalidRequest);
            }

            string[] itemIds = ordinals
                .Select(ordinal => container.Items[ordinal - 1].Id)
                .ToArray();
            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.RemoveResolvedReferences(
                    state,
                    container.Id,
                    itemIds);
            if (!edit.IsSuccess)
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceRemovalUndoToken? undoToken = projection.IsSuccess
                ? ProductWorkspaceReferenceRemovalUndo.Prepare(
                    state,
                    edit.State!,
                    nextEditRevision,
                    Guid.NewGuid())
                : null;
            if (!projection.IsSuccess || undoToken is null)
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ResolvedReferenceBatchRemovalFailure(
                    ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus
                        .SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = new(undoToken, state);
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                undoToken);
        }
    }

    public ProductWorkspaceResolvedReferenceRemovalCommitResult
        CommitResolvedReferenceRemoval(
            ProductWorkspaceState state,
            ProductWorkspaceResolvedReferenceRemovalCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus
                        .StaleEditRevision);
            }

            if (request.ContainerOrdinal <= 0
                || request.ContainerOrdinal > state.Containers.Count)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState container =
                state.Containers[request.ContainerOrdinal - 1];
            if (request.ItemOrdinal <= 0
                || request.ItemOrdinal > container.Items.Count
                || container.Items[request.ItemOrdinal - 1].Resolution !=
                    ProductItemReferenceResolution.Resolved)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus
                        .InvalidRequest);
            }

            ProductWorkspaceEditResult edit = ProductWorkspaceReducer.RemoveReference(
                state,
                container.Id,
                container.Items[request.ItemOrdinal - 1].Id);
            if (!edit.IsSuccess)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus
                        .ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceRemovalUndoToken? undoToken = projection.IsSuccess
                ? ProductWorkspaceReferenceRemovalUndo.Prepare(
                    state,
                    edit.State!,
                    nextEditRevision,
                    Guid.NewGuid())
                : null;
            if (!projection.IsSuccess || undoToken is null)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ResolvedReferenceRemovalFailure(
                    ProductWorkspaceResolvedReferenceRemovalCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = new(undoToken, state);
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceResolvedReferenceRemovalCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                undoToken);
        }
    }

    public ProductWorkspaceReferenceRemovalUndoCommitResult
        CommitReferenceRemovalUndo(
            ProductWorkspaceState state,
            ProductWorkspaceReferenceRemovalUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            if (pendingReferenceRemovalUndo is null)
            {
                return ReferenceRemovalUndoFailure(
                    ProductWorkspaceReferenceRemovalUndoCommitStatus.GateRejected,
                    ProductWorkspaceReferenceRemovalUndoStatus.Unavailable);
            }

            ProductWorkspaceReferenceRemovalUndoResult undo =
                ProductWorkspaceReferenceRemovalUndo.Confirm(
                    state,
                    pendingReferenceRemovalUndo.RestoreState,
                    editRevision,
                    token,
                    pendingReferenceRemovalUndo.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return ReferenceRemovalUndoFailure(
                    ProductWorkspaceReferenceRemovalUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceEditResult edit = undo.Edit!;
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ReferenceRemovalUndoFailure(
                    ProductWorkspaceReferenceRemovalUndoCommitStatus.InvalidState,
                    ProductWorkspaceReferenceRemovalUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ReferenceRemovalUndoFailure(
                    ProductWorkspaceReferenceRemovalUndoCommitStatus.SaveRejected,
                    undo.Status,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingReferenceRemovalUndo = null;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceReferenceRemovalUndoCommitStatus.Accepted,
                undo.Status,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceResolvedReferenceReassignmentCommitResult
        CommitResolvedReferenceReassignment(
            ProductWorkspaceState state,
            ProductWorkspaceResolvedReferenceReassignmentCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ItemOrdinals);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .StaleEditRevision);
            }

            int[] ordinals = request.ItemOrdinals.ToArray();
            if (request.SourceContainerOrdinal <= 0
                || request.SourceContainerOrdinal > state.Containers.Count
                || request.TargetContainerOrdinal <= 0
                || request.TargetContainerOrdinal > state.Containers.Count
                || request.SourceContainerOrdinal == request.TargetContainerOrdinal
                || ordinals.Length == 0
                || ordinals.Length >
                    MaximumResolvedReferenceReassignmentBatchSize
                || ordinals.Distinct().Count() != ordinals.Length)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState source =
                state.Containers[request.SourceContainerOrdinal - 1];
            ProductContainerState target =
                state.Containers[request.TargetContainerOrdinal - 1];
            if (ordinals.Any(ordinal =>
                    ordinal <= 0
                    || ordinal > source.Items.Count
                    || source.Items[ordinal - 1].Resolution !=
                        ProductItemReferenceResolution.Resolved))
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .InvalidRequest);
            }

            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.ReassignResolvedReferences(
                    state,
                    source.Id,
                    ordinals
                        .Select(ordinal => source.Items[ordinal - 1].Id)
                        .ToArray(),
                    target.Id);
            if (!edit.IsSuccess || !edit.Changed)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .ReducerRejected,
                    edit.Error);
            }

            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceReferenceReassignmentUndoToken? undoToken =
                projection.IsSuccess
                    ? ProductWorkspaceReferenceReassignmentUndo.Prepare(
                        state,
                        edit.State!,
                        nextEditRevision,
                        Guid.NewGuid())
                    : null;
            if (!projection.IsSuccess || undoToken is null)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = new(undoToken, state);
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceResolvedReferenceReassignmentCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                undoToken);
        }
    }

    public ProductWorkspaceReferenceReassignmentUndoCommitResult
        CommitReferenceReassignmentUndo(
            ProductWorkspaceState state,
            ProductWorkspaceReferenceReassignmentUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            if (pendingReferenceReassignmentUndo is null)
            {
                return ReferenceReassignmentUndoFailure(
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.GateRejected,
                    ProductWorkspaceReferenceReassignmentUndoStatus.Unavailable);
            }

            ProductWorkspaceReferenceReassignmentUndoResult undo =
                ProductWorkspaceReferenceReassignmentUndo.Confirm(
                    state,
                    pendingReferenceReassignmentUndo.RestoreState,
                    editRevision,
                    token,
                    pendingReferenceReassignmentUndo.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return ReferenceReassignmentUndoFailure(
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceEditResult edit = undo.Edit!;
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return ReferenceReassignmentUndoFailure(
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.InvalidState,
                    ProductWorkspaceReferenceReassignmentUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ReferenceReassignmentUndoFailure(
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.SaveRejected,
                    undo.Status,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingReferenceReassignmentUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingLayoutRecoveryUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceReferenceReassignmentUndoCommitStatus.Accepted,
                undo.Status,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceLayoutRecoveryCommitResult CommitLayoutRecovery(
        ProductWorkspaceState state,
        IReadOnlyList<DisplayTopologyNode>? currentTopology,
        bool currentTopologyAuthoritative,
        long topologyGeneration,
        ProductWorkspaceLayoutRecoveryReviewToken token,
        bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            ProductWorkspaceLayoutRecoveryConfirmationResult confirmation =
                ProductWorkspaceLayoutRecoveryReview.Confirm(
                    state,
                    currentTopology,
                    currentTopologyAuthoritative,
                    topologyGeneration,
                    editRevision,
                    token,
                    confirmed);
            if (!confirmation.IsAccepted)
            {
                return new(
                    ProductWorkspaceLayoutRecoveryCommitStatus.GateRejected,
                    confirmation.Status,
                    null,
                    editRevision,
                    null,
                    null);
            }

            ProductWorkspaceEditResult edit = confirmation.Edit!;
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return new(
                    ProductWorkspaceLayoutRecoveryCommitStatus.InvalidState,
                    ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState,
                    editRevision,
                    null,
                    null);
            }

            long nextEditRevision = checked(editRevision + 1);
            ProductWorkspaceLayoutRecoveryUndoToken? undoToken =
                ProductWorkspaceLayoutRecoveryUndo.Prepare(
                    state,
                    edit.State!,
                    nextEditRevision,
                    Guid.NewGuid());
            if (undoToken is null)
            {
                return new(
                    ProductWorkspaceLayoutRecoveryCommitStatus.InvalidState,
                    ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState,
                    editRevision,
                    null,
                    null);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return new(
                    ProductWorkspaceLayoutRecoveryCommitStatus.SaveRejected,
                    confirmation.Status,
                    submission.Status,
                    editRevision,
                    null,
                    null);
            }

            editRevision = nextEditRevision;
            pendingLayoutRecoveryUndo = new(undoToken, state);
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceLayoutRecoveryCommitStatus.Accepted,
                confirmation.Status,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductWorkspaceLayoutRecoveryUndoCommitResult
        CommitLayoutRecoveryUndo(
            ProductWorkspaceState state,
            ProductWorkspaceLayoutRecoveryUndoToken token,
            bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        lock (gate)
        {
            if (pendingLayoutRecoveryUndo is null)
            {
                return UndoFailure(
                    ProductWorkspaceLayoutRecoveryUndoCommitStatus.GateRejected,
                    ProductWorkspaceLayoutRecoveryUndoStatus.Unavailable);
            }

            ProductWorkspaceLayoutRecoveryUndoResult undo =
                ProductWorkspaceLayoutRecoveryUndo.Confirm(
                    state,
                    pendingLayoutRecoveryUndo.RestoreState,
                    editRevision,
                    token,
                    pendingLayoutRecoveryUndo.Token,
                    confirmed);
            if (!undo.IsAccepted)
            {
                return UndoFailure(
                    ProductWorkspaceLayoutRecoveryUndoCommitStatus.GateRejected,
                    undo.Status);
            }

            ProductWorkspaceEditResult edit = undo.Edit!;
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return UndoFailure(
                    ProductWorkspaceLayoutRecoveryUndoCommitStatus.InvalidState,
                    ProductWorkspaceLayoutRecoveryUndoStatus.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }

            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return UndoFailure(
                    ProductWorkspaceLayoutRecoveryUndoCommitStatus.SaveRejected,
                    undo.Status,
                    submission.Status);
            }

            editRevision = checked(editRevision + 1);
            pendingLayoutRecoveryUndo = null;
            pendingReferenceRemovalUndo = null;
            pendingReferenceReassignmentUndo = null;
            pendingContainerRemovalUndo = null;
            pendingContainerEditUndo = null;
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceLayoutRecoveryUndoCommitStatus.Accepted,
                undo.Status,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    private ProductWorkspaceLayoutRecoveryUndoCommitResult UndoFailure(
        ProductWorkspaceLayoutRecoveryUndoCommitStatus status,
        ProductWorkspaceLayoutRecoveryUndoStatus undoStatus,
        ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

    private sealed record PendingLayoutRecoveryUndo(
        ProductWorkspaceLayoutRecoveryUndoToken Token,
        ProductWorkspaceState RestoreState);

    private sealed record PendingReferenceRemovalUndo(
        ProductWorkspaceReferenceRemovalUndoToken Token,
        ProductWorkspaceState RestoreState);

    private sealed record PendingReferenceReassignmentUndo(
        ProductWorkspaceReferenceReassignmentUndoToken Token,
        ProductWorkspaceState RestoreState);

    private sealed record PendingContainerRemovalUndo(
        ProductWorkspaceContainerRemovalUndoToken Token,
        ProductWorkspaceState RestoreState);

    private sealed record PendingContainerEditUndo(
        ProductWorkspaceContainerEditUndoToken Token,
        ProductWorkspaceState RestoreState);

    private sealed record PendingReferenceBatchAdditionUndo(
        ProductWorkspaceReferenceBatchAdditionUndoToken Token,
        ProductWorkspaceState RestoreState,
        bool CreatesContainer);

    private sealed record PendingContainerLayoutPublication(
        ProductWorkspaceContainerLayoutPublicationToken PublicToken,
        ProductContainerPlacementState OriginalPlacement,
        ProductContainerPlacementState CommittedPlacement);

    public static string ResolveColor(ProductWorkspaceContainerColorPreset preset) =>
        preset switch
        {
            ProductWorkspaceContainerColorPreset.Azure => "#2563EB",
            ProductWorkspaceContainerColorPreset.Indigo => "#5B5FF5",
            ProductWorkspaceContainerColorPreset.Slate => "#334155",
            ProductWorkspaceContainerColorPreset.Emerald => "#059669",
            ProductWorkspaceContainerColorPreset.Amber => "#D97706",
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

    public static double ResolveOpacity(
        ProductWorkspaceContainerOpacityPreset preset) => preset switch
        {
            ProductWorkspaceContainerOpacityPreset.Solid => 1.0,
            ProductWorkspaceContainerOpacityPreset.Strong => 0.88,
            ProductWorkspaceContainerOpacityPreset.Soft => 0.72,
            ProductWorkspaceContainerOpacityPreset.Subtle => 0.56,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

    public static (double XDip, double YDip) ResolvePosition(
        ProductWorkspaceContainerPositionPreset preset) => preset switch
        {
            ProductWorkspaceContainerPositionPreset.Start => (32, 48),
            ProductWorkspaceContainerPositionPreset.OffsetOne => (56, 72),
            ProductWorkspaceContainerPositionPreset.OffsetTwo => (80, 96),
            ProductWorkspaceContainerPositionPreset.OffsetThree => (104, 120),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

    public static (double WidthDip, double HeightDip) ResolveSize(
        ProductWorkspaceContainerSizePreset preset) => preset switch
        {
            ProductWorkspaceContainerSizePreset.Compact => (280, 192),
            ProductWorkspaceContainerSizePreset.Standard => (360, 240),
            ProductWorkspaceContainerSizePreset.Wide => (480, 280),
            ProductWorkspaceContainerSizePreset.Large => (560, 360),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

    private ProductWorkspaceContainerLayoutGestureCommitResult
        ContainerLayoutGestureFailure(
            ProductWorkspaceContainerLayoutGestureCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceContainerLayoutCompensationCommitResult
        ContainerLayoutCompensationFailure(
            ProductWorkspaceContainerLayoutCompensationCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null,
            ProductWorkspaceSaveFailure sourceFailure =
                ProductWorkspaceSaveFailure.None) =>
        new(
            status,
            editError,
            submissionStatus,
            sourceFailure,
            editRevision,
            null,
            null);

    private static bool PlacementMatches(
        ProductContainerPlacementState left,
        ProductContainerPlacementState right) =>
        left is not null
        && right is not null
        && string.Equals(
            left.DisplayKey,
            right.DisplayKey,
            StringComparison.Ordinal)
        && Math.Abs(left.XDip - right.XDip) < 0.001
        && Math.Abs(left.YDip - right.YDip) < 0.001
        && Math.Abs(left.WidthDip - right.WidthDip) < 0.001
        && Math.Abs(left.HeightDip - right.HeightDip) < 0.001
        && ExtensionDataMatches(left.ExtensionData, right.ExtensionData);

    private static ProductContainerPlacementState ClonePlacement(
        ProductContainerPlacementState source) =>
        source with
        {
            ExtensionData = source.ExtensionData is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(
                    source.ExtensionData,
                    StringComparer.Ordinal),
        };

    private static bool ExtensionDataMatches(
        IDictionary<string, System.Text.Json.JsonElement>? left,
        IDictionary<string, System.Text.Json.JsonElement>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }
        foreach ((string key, System.Text.Json.JsonElement value) in left)
        {
            if (!right.TryGetValue(key, out System.Text.Json.JsonElement candidate)
                || !string.Equals(
                    value.GetRawText(),
                    candidate.GetRawText(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private ProductWorkspaceContainerCommitResult ContainerFailure(
        ProductWorkspaceContainerCommitStatus status,
        ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
        ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceContainerRemovalUndoCommitResult
        ContainerRemovalUndoFailure(
            ProductWorkspaceContainerRemovalUndoCommitStatus status,
            ProductWorkspaceContainerRemovalUndoStatus undoStatus,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceContainerEditUndoCommitResult
        ContainerEditUndoFailure(
            ProductWorkspaceContainerEditUndoCommitStatus status,
            ProductWorkspaceContainerEditUndoStatus undoStatus,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceSelectedReferenceContainerCommitResult
        SelectedReferenceContainerFailure(
            ProductWorkspaceSelectedReferenceContainerCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);

    private ProductWorkspaceResolvedReferenceCommitResult
        ResolvedReferenceFailure(
            ProductWorkspaceResolvedReferenceCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceResolvedReferenceRemovalCommitResult
        ResolvedReferenceRemovalFailure(
            ProductWorkspaceResolvedReferenceRemovalCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);

    private ProductWorkspaceResolvedReferenceBatchRemovalCommitResult
        ResolvedReferenceBatchRemovalFailure(
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);

    private ProductWorkspaceResolvedReferenceBatchCommitResult
        ResolvedReferenceBatchFailure(
            ProductWorkspaceResolvedReferenceBatchCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);

    private ProductWorkspaceReferenceBatchAdditionUndoCommitResult
        ReferenceBatchAdditionUndoFailure(
            ProductWorkspaceReferenceBatchAdditionUndoCommitStatus status,
            ProductWorkspaceReferenceBatchAdditionUndoStatus undoStatus,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceReferenceRemovalUndoCommitResult
        ReferenceRemovalUndoFailure(
            ProductWorkspaceReferenceRemovalUndoCommitStatus status,
            ProductWorkspaceReferenceRemovalUndoStatus undoStatus,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductWorkspaceResolvedReferenceReassignmentCommitResult
        ResolvedReferenceReassignmentFailure(
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus status,
            ProductWorkspaceEditError editError = ProductWorkspaceEditError.None,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            editError,
            submissionStatus,
            editRevision,
            null,
            null,
            null);

    private ProductWorkspaceReferenceReassignmentUndoCommitResult
        ReferenceReassignmentUndoFailure(
            ProductWorkspaceReferenceReassignmentUndoCommitStatus status,
            ProductWorkspaceReferenceReassignmentUndoStatus undoStatus,
            ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) =>
        new(
            status,
            undoStatus,
            submissionStatus,
            editRevision,
            null,
            null);

}
