using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceRealWindowRecoveryPlanToken(
    long TopologyGeneration,
    long EditRevision,
    string ConfigurationFingerprint,
    string PlanFingerprint,
    bool ReviewApproved);

[Flags]
public enum ProductWorkspaceRealWindowRecoveryBlocker : ulong
{
    None = 0,
    SessionUnavailable = 1UL << 0,
    SessionReadOnly = 1UL << 1,
    TopologyNotAuthoritative = 1UL << 2,
    BoundPlanMissing = 1UL << 3,
    TopologyGenerationChanged = 1UL << 4,
    EditRevisionChanged = 1UL << 5,
    ConfigurationChanged = 1UL << 6,
    ConfigurationUndoUnavailable = 1UL << 7,
    ConfigurationUndoMismatch = 1UL << 8,
    PlanUnavailable = 1UL << 9,
    PlanBlocked = 1UL << 10,
    ReviewApprovalMissing = 1UL << 11,
    PlanFingerprintChanged = 1UL << 12,
    WindowRegistryUnavailable = 1UL << 13,
    WindowOwnershipUnverified = 1UL << 14,
    ContainerWindowSetMismatch = 1UL << 15,
    CompositeTransactionUnavailable = 1UL << 16,
    WindowBatchAdapterUnavailable = 1UL << 17,
    RollbackFaultMatrixPending = 1UL << 18,
    InputSurfaceMatrixPending = 1UL << 19,
    DynamicDisplayMatrixPending = 1UL << 20,
    CleanUiAutomationPending = 1UL << 21,
}

public sealed record ProductWorkspaceRealWindowRecoveryEvidence(
    ProductWorkspaceState? CurrentState,
    bool SessionWritable,
    bool CurrentTopologyAuthoritative,
    long CurrentTopologyGeneration,
    long CurrentEditRevision,
    ProductWorkspaceLayoutRecoveryUndoToken? ConfigurationUndoToken,
    ProductWorkspaceRealWindowRecoveryPlanToken? BoundPlanToken,
    LayoutRecoveryPlan? Plan,
    IReadOnlyList<string>? RegisteredContainerIds,
    bool WindowOwnershipAttested,
    bool CompositeTransactionAvailable,
    bool WindowBatchAdapterAvailable,
    bool RollbackFaultMatrixPassed,
    bool InputSurfaceMatrixPassed,
    bool DynamicDisplayMatrixPassed,
    bool CleanUiAutomationPassed);

public sealed record ProductWorkspaceRealWindowRecoveryAdmissionResult(
    ProductWorkspaceRealWindowRecoveryBlocker Blockers)
{
    public bool CanConnect =>
        Blockers == ProductWorkspaceRealWindowRecoveryBlocker.None;

    public int BlockerCount => BitOperations.PopCount((ulong)Blockers);
}

public static class ProductWorkspaceRealWindowRecoveryAdmission
{
    public static ProductWorkspaceRealWindowRecoveryPlanToken? PreparePlanToken(
        ProductWorkspaceState state,
        long topologyGeneration,
        long editRevision,
        LayoutRecoveryPlan plan,
        bool reviewApproved)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        if (topologyGeneration <= 0
            || editRevision <= 0
            || !reviewApproved
            || plan.Status == LayoutRecoveryStatus.Blocked)
        {
            return null;
        }

        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess
            || !HasExactStateContainerSet(state, plan)
            || !TryFingerprintPlan(plan, out string fingerprint))
        {
            return null;
        }

        return new(
            topologyGeneration,
            editRevision,
            ProductWorkspaceConfigurationFingerprint.Compute(
                projection.Document!),
            fingerprint,
            ReviewApproved: true);
    }

    public static ProductWorkspaceRealWindowRecoveryAdmissionResult Evaluate(
        ProductWorkspaceRealWindowRecoveryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ProductWorkspaceRealWindowRecoveryBlocker blockers =
            ProductWorkspaceRealWindowRecoveryBlocker.None;
        ProductWorkspaceProjectionResult? projection = null;
        if (evidence.CurrentState is null)
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .SessionUnavailable;
        }
        else
        {
            projection = ProductWorkspaceConfigurationProjector.Project(
                evidence.CurrentState);
            if (!projection.IsSuccess)
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .ConfigurationChanged;
            }
        }

        AddUnless(
            evidence.SessionWritable,
            ProductWorkspaceRealWindowRecoveryBlocker.SessionReadOnly,
            ref blockers);
        AddUnless(
            evidence.CurrentTopologyAuthoritative,
            ProductWorkspaceRealWindowRecoveryBlocker
                .TopologyNotAuthoritative,
            ref blockers);

        ProductWorkspaceRealWindowRecoveryPlanToken? planToken =
            evidence.BoundPlanToken;
        if (planToken is null)
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .BoundPlanMissing;
        }
        else
        {
            if (evidence.CurrentTopologyGeneration <= 0
                || planToken.TopologyGeneration !=
                    evidence.CurrentTopologyGeneration)
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .TopologyGenerationChanged;
            }

            if (evidence.CurrentEditRevision <= 0
                || planToken.EditRevision != evidence.CurrentEditRevision)
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .EditRevisionChanged;
            }

            if (projection is { IsSuccess: true }
                && !string.Equals(
                    planToken.ConfigurationFingerprint,
                    ProductWorkspaceConfigurationFingerprint.Compute(
                        projection.Document!),
                    StringComparison.Ordinal))
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .ConfigurationChanged;
            }

            AddUnless(
                planToken.ReviewApproved,
                ProductWorkspaceRealWindowRecoveryBlocker
                    .ReviewApprovalMissing,
                ref blockers);
        }

        ProductWorkspaceLayoutRecoveryUndoToken? undo =
            evidence.ConfigurationUndoToken;
        if (undo is null)
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .ConfigurationUndoUnavailable;
        }
        else if (planToken is null
            || undo.RecoveryEditRevision != planToken.EditRevision
            || !string.Equals(
                undo.RecoveredConfigurationFingerprint,
                planToken.ConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .ConfigurationUndoMismatch;
        }

        LayoutRecoveryPlan? plan = evidence.Plan;
        if (plan is null)
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .PlanUnavailable;
        }
        else
        {
            if (plan.Status == LayoutRecoveryStatus.Blocked)
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .PlanBlocked;
            }

            if (planToken is not null
                && (!TryFingerprintPlan(plan, out string fingerprint)
                    || !string.Equals(
                        planToken.PlanFingerprint,
                        fingerprint,
                        StringComparison.Ordinal)))
            {
                blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                    .PlanFingerprintChanged;
            }
        }

        IReadOnlyList<string>? registered = evidence.RegisteredContainerIds;
        if (registered is null)
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .WindowRegistryUnavailable;
        }
        else if (plan is not null && !HasExactContainerSet(plan, registered))
        {
            blockers |= ProductWorkspaceRealWindowRecoveryBlocker
                .ContainerWindowSetMismatch;
        }

        AddUnless(
            evidence.WindowOwnershipAttested,
            ProductWorkspaceRealWindowRecoveryBlocker
                .WindowOwnershipUnverified,
            ref blockers);
        AddUnless(
            evidence.CompositeTransactionAvailable,
            ProductWorkspaceRealWindowRecoveryBlocker
                .CompositeTransactionUnavailable,
            ref blockers);
        AddUnless(
            evidence.WindowBatchAdapterAvailable,
            ProductWorkspaceRealWindowRecoveryBlocker
                .WindowBatchAdapterUnavailable,
            ref blockers);
        AddUnless(
            evidence.RollbackFaultMatrixPassed,
            ProductWorkspaceRealWindowRecoveryBlocker
                .RollbackFaultMatrixPending,
            ref blockers);
        AddUnless(
            evidence.InputSurfaceMatrixPassed,
            ProductWorkspaceRealWindowRecoveryBlocker
                .InputSurfaceMatrixPending,
            ref blockers);
        AddUnless(
            evidence.DynamicDisplayMatrixPassed,
            ProductWorkspaceRealWindowRecoveryBlocker
                .DynamicDisplayMatrixPending,
            ref blockers);
        AddUnless(
            evidence.CleanUiAutomationPassed,
            ProductWorkspaceRealWindowRecoveryBlocker
                .CleanUiAutomationPending,
            ref blockers);

        return new(blockers);
    }

    private static bool HasExactContainerSet(
        LayoutRecoveryPlan plan,
        IReadOnlyList<string> registered)
    {
        string[] planned = plan.ContainerPlacements
            .Select(placement => placement.ContainerId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = registered
            .Order(StringComparer.Ordinal)
            .ToArray();
        return actual.All(id => !string.IsNullOrWhiteSpace(id))
            && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length
            && planned.SequenceEqual(actual, StringComparer.Ordinal);
    }

    private static bool HasExactStateContainerSet(
        ProductWorkspaceState state,
        LayoutRecoveryPlan plan) =>
        state.Containers
            .Select(container => container.Id)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                plan.ContainerPlacements
                    .Select(placement => placement.ContainerId)
                    .Order(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static bool TryFingerprintPlan(
        LayoutRecoveryPlan plan,
        out string fingerprint)
    {
        fingerprint = string.Empty;
        if (!Enum.IsDefined(plan.Status)
            || plan.DisplayMappings is null
            || plan.UnresolvedSavedDisplayIds is null
            || plan.ContainerPlacements is null
            || plan.DisplayMappings.Any(mapping =>
                mapping is null
                || string.IsNullOrWhiteSpace(mapping.SavedDisplayId)
                || string.IsNullOrWhiteSpace(mapping.CurrentDisplayId)
                || !Enum.IsDefined(mapping.MatchKind))
            || plan.UnresolvedSavedDisplayIds.Any(
                string.IsNullOrWhiteSpace)
            || plan.ContainerPlacements.Any(placement =>
                placement is null
                || string.IsNullOrWhiteSpace(placement.ContainerId)
                || string.IsNullOrWhiteSpace(placement.SavedDisplayId)
                || string.IsNullOrWhiteSpace(placement.CurrentDisplayId)
                || !placement.RequestedBounds.HasArea
                || !placement.ProposedBounds.HasArea)
            || plan.ContainerPlacements
                .Select(placement => placement.ContainerId)
                .Distinct(StringComparer.Ordinal)
                .Count() != plan.ContainerPlacements.Count
            || plan.DisplayMappings
                .Select(mapping => mapping.SavedDisplayId)
                .Distinct(StringComparer.Ordinal)
                .Count() != plan.DisplayMappings.Count
            || plan.DisplayMappings
                .Select(mapping => mapping.CurrentDisplayId)
                .Distinct(StringComparer.Ordinal)
                .Count() != plan.DisplayMappings.Count
            || plan.UnresolvedSavedDisplayIds
                .Distinct(StringComparer.Ordinal)
                .Count() != plan.UnresolvedSavedDisplayIds.Count
            || (plan.Status != LayoutRecoveryStatus.Blocked
                && plan.UnresolvedSavedDisplayIds.Count != 0))
        {
            return false;
        }

        var builder = new StringBuilder();
        builder.Append((int)plan.Status).Append('|');
        foreach (DisplayRecoveryMapping mapping in plan.DisplayMappings
            .OrderBy(item => item.SavedDisplayId, StringComparer.Ordinal)
            .ThenBy(item => item.CurrentDisplayId, StringComparer.Ordinal))
        {
            Append(builder, mapping.SavedDisplayId);
            Append(builder, mapping.CurrentDisplayId);
            builder.Append((int)mapping.MatchKind).Append('|');
        }

        foreach (string unresolved in plan.UnresolvedSavedDisplayIds
            .Order(StringComparer.Ordinal))
        {
            Append(builder, unresolved);
        }

        foreach (ContainerRecoveryPlacement placement in plan.ContainerPlacements
            .OrderBy(item => item.ContainerId, StringComparer.Ordinal))
        {
            Append(builder, placement.ContainerId);
            Append(builder, placement.SavedDisplayId);
            Append(builder, placement.CurrentDisplayId);
            Append(builder, placement.RequestedBounds);
            Append(builder, placement.ProposedBounds);
            builder.Append(placement.WasVisibilityCorrected ? '1' : '0')
                .Append('|');
        }

        fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString())));
        return true;
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }

    private static void Append(StringBuilder builder, PixelRect rect) =>
        builder.Append(rect.Left.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(rect.Top.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(rect.Width.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(rect.Height.ToString(CultureInfo.InvariantCulture)).Append('|');

    private static void AddUnless(
        bool condition,
        ProductWorkspaceRealWindowRecoveryBlocker blocker,
        ref ProductWorkspaceRealWindowRecoveryBlocker blockers)
    {
        if (!condition)
        {
            blockers |= blocker;
        }
    }
}
