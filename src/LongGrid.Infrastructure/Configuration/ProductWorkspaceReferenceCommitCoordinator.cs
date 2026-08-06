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

public sealed class ProductWorkspaceReferenceCommitCoordinator
{
    private readonly object gate = new();
    private readonly ProductWorkspaceSaveController saves;
    private long editRevision;

    public ProductWorkspaceReferenceCommitCoordinator(
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
}
