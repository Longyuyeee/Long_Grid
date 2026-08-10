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
    bool Confirmed = false);

public sealed record ProductWorkspaceContainerCommitResult(
    ProductWorkspaceContainerCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document,
    ProductWorkspaceContainerRemovalUndoToken? RemovalUndoToken = null)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerCommitStatus.Accepted
        && State is not null
        && Document is not null;
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
    int ItemOrdinal,
    int TargetContainerOrdinal);

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

    private readonly object gate = new();
    private readonly ProductWorkspaceSaveController saves;
    private long editRevision;
    private PendingLayoutRecoveryUndo? pendingLayoutRecoveryUndo;
    private PendingReferenceRemovalUndo? pendingReferenceRemovalUndo;
    private PendingReferenceReassignmentUndo? pendingReferenceReassignmentUndo;
    private PendingContainerRemovalUndo? pendingContainerRemovalUndo;
    private PendingReferenceBatchAdditionUndo? pendingReferenceBatchAdditionUndo;

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

    public ProductWorkspaceReferenceBatchAdditionUndoToken?
        CurrentReferenceBatchAdditionUndoToken
    {
        get
        {
            lock (gate)
            {
                return pendingReferenceBatchAdditionUndo?.Token;
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
            pendingReferenceBatchAdditionUndo = null;
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
            if (request.Action == ProductWorkspaceContainerCommitAction.Remove
                && removalUndoToken is null)
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
            pendingReferenceBatchAdditionUndo = null;
            return new(
                ProductWorkspaceContainerCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document,
                removalUndoToken);
        }
    }

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
            pendingReferenceBatchAdditionUndo = new(undoToken, state);
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

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .StaleEditRevision);
            }

            if (request.SourceContainerOrdinal <= 0
                || request.SourceContainerOrdinal > state.Containers.Count
                || request.TargetContainerOrdinal <= 0
                || request.TargetContainerOrdinal > state.Containers.Count
                || request.SourceContainerOrdinal == request.TargetContainerOrdinal)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .InvalidRequest);
            }

            ProductContainerState source =
                state.Containers[request.SourceContainerOrdinal - 1];
            ProductContainerState target =
                state.Containers[request.TargetContainerOrdinal - 1];
            if (request.ItemOrdinal <= 0
                || request.ItemOrdinal > source.Items.Count
                || source.Items[request.ItemOrdinal - 1].Resolution !=
                    ProductItemReferenceResolution.Resolved)
            {
                return ResolvedReferenceReassignmentFailure(
                    ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                        .InvalidRequest);
            }

            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.ReassignResolvedReference(
                    state,
                    source.Id,
                    source.Items[request.ItemOrdinal - 1].Id,
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

    private sealed record PendingReferenceBatchAdditionUndo(
        ProductWorkspaceReferenceBatchAdditionUndoToken Token,
        ProductWorkspaceState RestoreState);

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
