using LongGrid.Core.Configuration;
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
    bool? StateValue = null);

public sealed record ProductWorkspaceContainerCommitResult(
    ProductWorkspaceContainerCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductWorkspaceContainerCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public sealed class ProductWorkspaceCommitCoordinator
{
    private readonly object gate = new();
    private readonly ProductWorkspaceSaveController saves;
    private long editRevision;

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

    public long AdvanceExternalRevision()
    {
        lock (gate)
        {
            editRevision = checked(editRevision + 1);
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
                        && target is not null =>
                    ProductWorkspaceReducer.RenameContainer(
                        state,
                        target.Id,
                        request.Name),
                ProductWorkspaceContainerCommitAction.SetLocked
                    when request.NewContainer is null
                        && request.StateValue is not null
                        && target is not null =>
                    ProductWorkspaceReducer.SetContainerLocked(
                        state,
                        target.Id,
                        request.StateValue.Value),
                ProductWorkspaceContainerCommitAction.SetCollapsed
                    when request.NewContainer is null
                        && request.StateValue is not null
                        && target is not null =>
                    ProductWorkspaceReducer.UpdateAppearance(
                        state,
                        target.Id,
                        target.Appearance with
                        {
                            Collapsed = request.StateValue.Value,
                        }),
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
            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return ContainerFailure(
                    ProductWorkspaceContainerCommitStatus.SaveRejected,
                    submission.EditError,
                    submission.Status);
            }

            editRevision = nextEditRevision;
            return new(
                ProductWorkspaceContainerCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
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
}
