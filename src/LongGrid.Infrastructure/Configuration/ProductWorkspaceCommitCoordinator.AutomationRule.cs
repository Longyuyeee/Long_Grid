using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductAutomationRuleCommitStatus
{
    Accepted,
    StaleEditRevision,
    StaleCatalogGeneration,
    StalePreview,
    InvalidRequest,
    ReducerRejected,
    SaveRejected,
}

public sealed record ProductAutomationRuleCommitResult(
    ProductAutomationRuleCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductAutomationRuleCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public enum ProductAutomationRuleLifecycleCommitStatus
{
    Accepted,
    StaleEditRevision,
    InvalidRequest,
    ReducerRejected,
    SaveRejected,
}

public sealed record ProductAutomationRuleLifecycleCommitRequest(
    long ExpectedEditRevision,
    ProductAutomationRuleLifecycleRequest Edit);

public sealed record ProductAutomationRuleLifecycleCommitResult(
    ProductAutomationRuleLifecycleCommitStatus Status,
    ProductWorkspaceEditError EditError,
    ProductWorkspaceSaveSubmissionStatus? SubmissionStatus,
    long EditRevision,
    ProductWorkspaceState? State,
    ProductConfigurationDocument? Document)
{
    public bool IsAccepted =>
        Status == ProductAutomationRuleLifecycleCommitStatus.Accepted
        && State is not null
        && Document is not null;
}

public sealed partial class ProductWorkspaceCommitCoordinator
{
    public ProductAutomationRuleLifecycleCommitResult CommitAutomationRuleLifecycle(
        ProductWorkspaceState state,
        ProductAutomationRuleLifecycleCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Edit);

        lock (gate)
        {
            if (request.ExpectedEditRevision != editRevision)
            {
                return LifecycleFailure(
                    ProductAutomationRuleLifecycleCommitStatus.StaleEditRevision);
            }
            ProductAutomationRuleState? current = state.Rules.FirstOrDefault(rule =>
                string.Equals(rule.Id, request.Edit.RuleId, StringComparison.Ordinal));
            if (current is null || !Enum.IsDefined(request.Edit.Action))
            {
                return LifecycleFailure(
                    ProductAutomationRuleLifecycleCommitStatus.InvalidRequest);
            }

            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.EditAutomationRule(state, request.Edit);
            if (!edit.IsSuccess || !edit.Changed)
            {
                return LifecycleFailure(
                    ProductAutomationRuleLifecycleCommitStatus.ReducerRejected,
                    edit.Error);
            }
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return LifecycleFailure(
                    ProductAutomationRuleLifecycleCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }
            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return LifecycleFailure(
                    ProductAutomationRuleLifecycleCommitStatus.SaveRejected,
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
            (ProductWorkspaceSessionHistoryActionKind kind, string actionText) =
                request.Edit.Action switch
                {
                    ProductAutomationRuleLifecycleAction.Update =>
                        (ProductWorkspaceSessionHistoryActionKind.RuleEdit, "编辑自动整理规则"),
                    ProductAutomationRuleLifecycleAction.Duplicate =>
                        (ProductWorkspaceSessionHistoryActionKind.RuleCopy, "复制自动整理规则"),
                    ProductAutomationRuleLifecycleAction.SetEnabled =>
                        (ProductWorkspaceSessionHistoryActionKind.RuleEnabled,
                            request.Edit.Enabled == true ? "启用自动整理规则" : "停用自动整理规则"),
                    ProductAutomationRuleLifecycleAction.Remove =>
                        (ProductWorkspaceSessionHistoryActionKind.RuleDelete, "删除自动整理规则"),
                    _ => (ProductWorkspaceSessionHistoryActionKind.RuleOrder, "调整自动整理规则顺序"),
                };
            RecordSessionHistoryAction(
                state,
                edit.State!,
                kind,
                actionText,
                "规则",
                request.Edit.Rule?.Name ?? current.Name,
                1);
            return new(
                ProductAutomationRuleLifecycleCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    public ProductAutomationRuleCommitResult CommitAutomationRule(
        ProductWorkspaceState state,
        long currentCatalogGeneration,
        IReadOnlyList<DesktopCatalogEntry> catalog,
        ProductAutomationRulePreviewSnapshot preview)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(preview);

        lock (gate)
        {
            if (preview.WorkspaceRevision != editRevision)
            {
                return Failure(ProductAutomationRuleCommitStatus.StaleEditRevision);
            }
            if (preview.CatalogGeneration != currentCatalogGeneration)
            {
                return Failure(ProductAutomationRuleCommitStatus.StaleCatalogGeneration);
            }
            if (!preview.CanApply
                || preview.Matches.Count > MaximumResolvedReferenceBatchSize
                || preview.Matches.Select(match => match.CatalogIndex).Distinct().Count()
                    != preview.Matches.Count
                || preview.Matches.Any(match => match.CatalogIndex < 0
                    || match.CatalogIndex >= catalog.Count))
            {
                return Failure(ProductAutomationRuleCommitStatus.InvalidRequest);
            }

            ProductWorkspaceProjectionResult beforeProjection =
                ProductWorkspaceConfigurationProjector.Project(state);
            if (!beforeProjection.IsSuccess
                || !string.Equals(
                    ProductWorkspaceConfigurationFingerprint.Compute(
                        beforeProjection.Document!),
                    preview.WorkspaceFingerprint,
                    StringComparison.Ordinal)
                || !string.Equals(
                    ProductAutomationRulePreviewPlanner.ComputeRuleFingerprint(
                        preview.Rule),
                    preview.RuleFingerprint,
                    StringComparison.Ordinal))
            {
                return Failure(ProductAutomationRuleCommitStatus.StalePreview);
            }

            DesktopCatalogEntry[] entries = preview.Matches
                .Select(match => catalog[match.CatalogIndex])
                .ToArray();
            if (!string.Equals(
                    ProductQuickStartSuggestionPlanner.ComputeCatalogFingerprint(entries),
                    preview.CatalogFingerprint,
                    StringComparison.Ordinal)
                || entries.Where((entry, index) =>
                    !string.Equals(
                        entry.Identity.CanonicalTarget,
                        preview.Matches[index].CanonicalTarget,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        entry.DisplayName,
                        preview.Matches[index].DisplayName,
                        StringComparison.Ordinal)
                    || entry.Kind != preview.Matches[index].Kind).Any())
            {
                return Failure(ProductAutomationRuleCommitStatus.StalePreview);
            }

            ProductItemReferenceState[] items = entries.Select(entry =>
                ProductItemReferenceState.CreateResolved(
                    $"item-{Guid.NewGuid():N}", entry)).ToArray();
            ProductWorkspaceEditResult edit =
                ProductWorkspaceReducer.ApplyAutomationRule(
                    state,
                    preview.Rule,
                    items);
            if (!edit.IsSuccess || !edit.Changed)
            {
                return Failure(
                    ProductAutomationRuleCommitStatus.ReducerRejected,
                    edit.Error);
            }
            ProductWorkspaceProjectionResult projection =
                ProductWorkspaceConfigurationProjector.Project(edit.State!);
            if (!projection.IsSuccess)
            {
                return Failure(
                    ProductAutomationRuleCommitStatus.ReducerRejected,
                    ProductWorkspaceEditError.InvalidState,
                    ProductWorkspaceSaveSubmissionStatus.InvalidState);
            }
            ProductWorkspaceSaveSubmissionResult submission = saves.Submit(edit);
            if (!submission.IsAccepted)
            {
                return Failure(
                    ProductAutomationRuleCommitStatus.SaveRejected,
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
            RecordSessionHistoryAction(
                state,
                edit.State!,
                ProductWorkspaceSessionHistoryActionKind.RuleApplication,
                "应用自动整理规则",
                "规则",
                preview.Rule.Name,
                items.Length);
            return new(
                ProductAutomationRuleCommitStatus.Accepted,
                ProductWorkspaceEditError.None,
                submission.Status,
                editRevision,
                edit.State,
                projection.Document);
        }
    }

    private ProductAutomationRuleCommitResult Failure(
        ProductAutomationRuleCommitStatus status,
        ProductWorkspaceEditError error = ProductWorkspaceEditError.None,
        ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) => new(
            status,
            error,
            submissionStatus,
            editRevision,
            null,
            null);

    private ProductAutomationRuleLifecycleCommitResult LifecycleFailure(
        ProductAutomationRuleLifecycleCommitStatus status,
        ProductWorkspaceEditError error = ProductWorkspaceEditError.None,
        ProductWorkspaceSaveSubmissionStatus? submissionStatus = null) => new(
            status,
            error,
            submissionStatus,
            editRevision,
            null,
            null);
}
