using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public sealed record ProductWorkspaceWindowCompositeBinding(
    long TopologyGeneration,
    long EditRevision,
    long WindowRegistryGeneration,
    Guid DesktopHostInstanceId,
    long DesktopHostGeneration,
    string ConfigurationFingerprint);

public sealed record ProductWorkspaceWindowCompositeToken(
    Guid OperationId,
    ProductWorkspaceWindowCompositeBinding Before,
    ProductWorkspaceWindowCompositeBinding After,
    ProductWorkspaceWindowCompositeBinding Undo,
    string PlanFingerprint,
    bool ReviewApproved);

public sealed record ProductWorkspaceWindowCompositeUndoToken(
    Guid OperationId,
    ProductWorkspaceWindowCompositeBinding Applied,
    ProductWorkspaceWindowCompositeBinding Undo,
    string PlanFingerprint);

public sealed record ProductWorkspaceWindowCompositeRequest(
    ProductWorkspaceState BeforeState,
    ProductWorkspaceState RecoveredState,
    LayoutRecoveryPlan Plan,
    IReadOnlyList<string> RegisteredContainerIds,
    bool WindowOwnershipAttested,
    ProductWorkspaceWindowCompositeToken Token,
    bool UserConfirmed);

public interface IProductWorkspaceWindowCompositeSnapshot : IDisposable
{
}

public sealed record ProductWorkspaceWindowCompositeCapture(
    bool Succeeded,
    IProductWorkspaceWindowCompositeSnapshot? Snapshot)
{
    public static ProductWorkspaceWindowCompositeCapture Failed { get; } =
        new(false, null);
}

public interface IProductWorkspaceCompositeConfigurationLayer
{
    ProductWorkspaceWindowCompositeCapture Capture();

    bool Apply(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding expectedBinding);

    bool Verify(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding expectedBinding);

    bool Restore(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        ProductWorkspaceWindowCompositeBinding expectedBinding);

    bool VerifyRestored(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        ProductWorkspaceWindowCompositeBinding expectedBinding);
}

public interface IProductWorkspaceCompositeWindowLayer
{
    ProductWorkspaceWindowCompositeCapture Capture(
        IReadOnlyList<string> containerIds,
        long registryGeneration);

    bool Apply(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
        long registryGeneration);

    bool Verify(
        IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
        long registryGeneration);

    bool Restore(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        long registryGeneration);

    bool VerifyRestored(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        long registryGeneration);
}

public interface IProductWorkspaceCompositeInputGate
{
    bool Close();

    bool Reopen();

    bool HideAffectedHosts();
}

public enum ProductWorkspaceWindowCompositeStatus
{
    Applied,
    Rejected,
    Superseded,
    InputGateFailed,
    CaptureFailed,
    RolledBack,
    RollbackFailed,
}

public enum ProductWorkspaceWindowCompositeFailure
{
    None,
    InvalidRequest,
    ConfirmationRequired,
    BindingChanged,
    InputCloseFailed,
    ConfigurationCaptureFailed,
    WindowCaptureFailed,
    WindowApplyFailed,
    WindowVerificationFailed,
    ConfigurationApplyFailed,
    ConfigurationVerificationFailed,
    FinalVerificationFailed,
    InputReopenFailed,
    ConfigurationRestoreFailed,
    WindowRestoreFailed,
    ConfigurationRestoreVerificationFailed,
    WindowRestoreVerificationFailed,
    EmergencyHideFailed,
}

public sealed record ProductWorkspaceWindowCompositeResult(
    ProductWorkspaceWindowCompositeStatus Status,
    ProductWorkspaceWindowCompositeFailure Failure,
    Guid OperationId,
    int PlacementCount,
    bool InputClosed,
    bool HostsHidden,
    ProductWorkspaceWindowCompositeUndoToken? UndoToken)
{
    public bool IsApplied =>
        Status == ProductWorkspaceWindowCompositeStatus.Applied
        && UndoToken is not null;
}

public enum ProductWorkspaceWindowCompositeUndoStatus
{
    Undone,
    Unavailable,
    TokenMismatch,
    ConfirmationRequired,
    Superseded,
    InputGateFailed,
    CaptureFailed,
    RolledForward,
    RecoveryFailed,
}

public sealed record ProductWorkspaceWindowCompositeUndoResult(
    ProductWorkspaceWindowCompositeUndoStatus Status,
    ProductWorkspaceWindowCompositeFailure Failure,
    bool InputClosed,
    bool HostsHidden)
{
    public bool IsUndone =>
        Status == ProductWorkspaceWindowCompositeUndoStatus.Undone;
}

public sealed class ProductWorkspaceWindowCompositeTransactionCoordinator
    : IDisposable
{
    private sealed record PendingUndo(
        ProductWorkspaceWindowCompositeUndoToken Token,
        IReadOnlyList<string> ContainerIds,
        IProductWorkspaceWindowCompositeSnapshot Configuration,
        IProductWorkspaceWindowCompositeSnapshot Windows);

    private readonly object sync = new();
    private readonly Func<ProductWorkspaceWindowCompositeBinding> currentBinding;
    private readonly IProductWorkspaceCompositeConfigurationLayer configuration;
    private readonly IProductWorkspaceCompositeWindowLayer windows;
    private readonly IProductWorkspaceCompositeInputGate input;
    private PendingUndo? pendingUndo;
    private bool disposed;

    public ProductWorkspaceWindowCompositeTransactionCoordinator(
        Func<ProductWorkspaceWindowCompositeBinding> currentBinding,
        IProductWorkspaceCompositeConfigurationLayer configuration,
        IProductWorkspaceCompositeWindowLayer windows,
        IProductWorkspaceCompositeInputGate input)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(input);
        this.currentBinding = currentBinding;
        this.configuration = configuration;
        this.windows = windows;
        this.input = input;
    }

    public ProductWorkspaceWindowCompositeUndoToken? CurrentUndoToken
    {
        get
        {
            lock (sync)
            {
                return pendingUndo?.Token;
            }
        }
    }

    public static ProductWorkspaceWindowCompositeToken? PrepareToken(
        ProductWorkspaceState beforeState,
        ProductWorkspaceState recoveredState,
        LayoutRecoveryPlan plan,
        IReadOnlyList<string> registeredContainerIds,
        bool windowOwnershipAttested,
        long topologyGeneration,
        long beforeEditRevision,
        long windowRegistryGeneration,
        Guid desktopHostInstanceId,
        long desktopHostGeneration,
        bool reviewApproved,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(beforeState);
        ArgumentNullException.ThrowIfNull(recoveredState);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(registeredContainerIds);
        if (!windowOwnershipAttested
            || topologyGeneration <= 0
            || beforeEditRevision <= 0
            || beforeEditRevision > long.MaxValue - 2
            || windowRegistryGeneration <= 0
            || desktopHostInstanceId == Guid.Empty
            || desktopHostGeneration <= 0
            || operationId == Guid.Empty
            || !reviewApproved
            || plan.Status == LayoutRecoveryStatus.Blocked
            || !HasExactContainerSet(plan, registeredContainerIds)
            || !HasExactStateContainerSet(beforeState, plan)
            || !HasExactStateContainerSet(recoveredState, plan)
            || !ProductWorkspaceRealWindowRecoveryAdmission.TryFingerprintPlan(
                plan,
                out string planFingerprint))
        {
            return null;
        }

        ProductWorkspaceProjectionResult before =
            ProductWorkspaceConfigurationProjector.Project(beforeState);
        ProductWorkspaceProjectionResult after =
            ProductWorkspaceConfigurationProjector.Project(recoveredState);
        if (!before.IsSuccess || !after.IsSuccess)
        {
            return null;
        }

        string beforeFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(before.Document!);
        string afterFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(after.Document!);
        if (string.Equals(
            beforeFingerprint,
            afterFingerprint,
            StringComparison.Ordinal))
        {
            return null;
        }

        long afterRevision = checked(beforeEditRevision + 1);
        long undoRevision = checked(afterRevision + 1);
        ProductWorkspaceWindowCompositeBinding Binding(
            long revision,
            string fingerprint) => new(
                topologyGeneration,
                revision,
                windowRegistryGeneration,
                desktopHostInstanceId,
                desktopHostGeneration,
                fingerprint);
        return new(
            operationId,
            Binding(beforeEditRevision, beforeFingerprint),
            Binding(afterRevision, afterFingerprint),
            Binding(undoRevision, beforeFingerprint),
            planFingerprint,
            ReviewApproved: true);
    }

    public ProductWorkspaceWindowCompositeResult Execute(
        ProductWorkspaceWindowCompositeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ProductWorkspaceWindowCompositeFailure validation =
                Validate(request);
            if (validation != ProductWorkspaceWindowCompositeFailure.None)
            {
                return Result(
                    validation == ProductWorkspaceWindowCompositeFailure
                        .BindingChanged
                        ? ProductWorkspaceWindowCompositeStatus.Superseded
                        : ProductWorkspaceWindowCompositeStatus.Rejected,
                    validation,
                    request,
                    inputClosed: false,
                    hostsHidden: false,
                    undoToken: null);
            }

            if (!TryCall(input.Close))
            {
                return Result(
                    ProductWorkspaceWindowCompositeStatus.InputGateFailed,
                    ProductWorkspaceWindowCompositeFailure.InputCloseFailed,
                    request,
                    inputClosed: false,
                    hostsHidden: false,
                    undoToken: null);
            }

            ProductWorkspaceWindowCompositeCapture configurationCapture =
                TryCapture(configuration.Capture);
            if (!IsValid(configurationCapture))
            {
                DisposeCapture(configurationCapture);
                return FinishCaptureFailure(
                    request,
                    ProductWorkspaceWindowCompositeFailure
                        .ConfigurationCaptureFailed);
            }

            string[] containerIds = request.Plan.ContainerPlacements
                .Select(placement => placement.ContainerId)
                .Order(StringComparer.Ordinal)
                .ToArray();
            ProductWorkspaceWindowCompositeCapture windowCapture = TryCapture(
                () => windows.Capture(
                    containerIds,
                    request.Token.Before.WindowRegistryGeneration));
            if (!IsValid(windowCapture))
            {
                DisposeCapture(windowCapture);
                configurationCapture.Snapshot!.Dispose();
                return FinishCaptureFailure(
                    request,
                    ProductWorkspaceWindowCompositeFailure.WindowCaptureFailed);
            }

            IProductWorkspaceWindowCompositeSnapshot configurationSnapshot =
                configurationCapture.Snapshot!;
            IProductWorkspaceWindowCompositeSnapshot windowSnapshot =
                windowCapture.Snapshot!;
            if (!MatchesCurrent(request.Token.Before))
            {
                Dispose(configurationSnapshot, windowSnapshot);
                return FinishWithoutMutation(
                    request,
                    ProductWorkspaceWindowCompositeStatus.Superseded,
                    ProductWorkspaceWindowCompositeFailure.BindingChanged);
            }

            LayoutRecoveryWindowPlacement[] placements =
                request.Plan.ContainerPlacements
                    .OrderBy(
                        placement => placement.ContainerId,
                        StringComparer.Ordinal)
                    .Select(placement => new LayoutRecoveryWindowPlacement(
                        placement.ContainerId,
                        placement.ProposedBounds))
                    .ToArray();
            long registryGeneration =
                request.Token.Before.WindowRegistryGeneration;
            if (!TryCall(() => windows.Apply(placements, registryGeneration)))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: false,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure.WindowApplyFailed);
            }

            if (!TryCall(() => windows.Verify(placements, registryGeneration)))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: false,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure
                        .WindowVerificationFailed);
            }

            if (!MatchesCurrent(request.Token.Before))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: false,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure.BindingChanged);
            }

            if (!TryCall(() => configuration.Apply(
                request.RecoveredState,
                request.Token.After)))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: true,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure
                        .ConfigurationApplyFailed);
            }

            if (!TryCall(() => configuration.Verify(
                    request.RecoveredState,
                    request.Token.After))
                || !MatchesCurrent(request.Token.After))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: true,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure
                        .ConfigurationVerificationFailed);
            }

            if (!TryCall(() => windows.Verify(placements, registryGeneration))
                || !TryCall(() => configuration.Verify(
                    request.RecoveredState,
                    request.Token.After))
                || !MatchesCurrent(request.Token.After))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: true,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure
                        .FinalVerificationFailed);
            }

            if (!TryCall(input.Reopen))
            {
                return RollBack(
                    request,
                    configurationSnapshot,
                    windowSnapshot,
                    configurationTouched: true,
                    windowTouched: true,
                    ProductWorkspaceWindowCompositeFailure.InputReopenFailed);
            }

            pendingUndo?.Configuration.Dispose();
            pendingUndo?.Windows.Dispose();
            ProductWorkspaceWindowCompositeUndoToken undoToken = new(
                request.Token.OperationId,
                request.Token.After,
                request.Token.Undo,
                request.Token.PlanFingerprint);
            pendingUndo = new(
                undoToken,
                Array.AsReadOnly(containerIds),
                configurationSnapshot,
                windowSnapshot);
            return Result(
                ProductWorkspaceWindowCompositeStatus.Applied,
                ProductWorkspaceWindowCompositeFailure.None,
                request,
                inputClosed: false,
                hostsHidden: false,
                undoToken);
        }
    }

    public ProductWorkspaceWindowCompositeUndoResult Undo(
        ProductWorkspaceWindowCompositeUndoToken token,
        bool userConfirmed)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pendingUndo is null)
            {
                return UndoResult(
                    ProductWorkspaceWindowCompositeUndoStatus.Unavailable);
            }

            if (token != pendingUndo.Token)
            {
                return UndoResult(
                    ProductWorkspaceWindowCompositeUndoStatus.TokenMismatch);
            }

            if (!userConfirmed)
            {
                return UndoResult(
                    ProductWorkspaceWindowCompositeUndoStatus
                        .ConfirmationRequired);
            }

            if (!MatchesCurrent(token.Applied))
            {
                return UndoResult(
                    ProductWorkspaceWindowCompositeUndoStatus.Superseded,
                    ProductWorkspaceWindowCompositeFailure.BindingChanged);
            }

            if (!TryCall(input.Close))
            {
                return UndoResult(
                    ProductWorkspaceWindowCompositeUndoStatus.InputGateFailed,
                    ProductWorkspaceWindowCompositeFailure.InputCloseFailed);
            }

            ProductWorkspaceWindowCompositeCapture appliedConfiguration =
                TryCapture(configuration.Capture);
            ProductWorkspaceWindowCompositeCapture appliedWindows = TryCapture(
                () => windows.Capture(
                    pendingUndo.ContainerIds,
                    token.Applied.WindowRegistryGeneration));
            if (!IsValid(appliedConfiguration) || !IsValid(appliedWindows))
            {
                DisposeCapture(appliedConfiguration);
                DisposeCapture(appliedWindows);
                bool reopened = TryCall(input.Reopen);
                ProductWorkspaceWindowCompositeFailure captureFailure =
                    !IsValid(appliedConfiguration)
                        ? ProductWorkspaceWindowCompositeFailure
                            .ConfigurationCaptureFailed
                        : ProductWorkspaceWindowCompositeFailure
                            .WindowCaptureFailed;
                return new(
                    ProductWorkspaceWindowCompositeUndoStatus.CaptureFailed,
                    captureFailure,
                    InputClosed: !reopened,
                    HostsHidden: false);
            }

            IProductWorkspaceWindowCompositeSnapshot appliedConfigurationSnapshot =
                appliedConfiguration.Snapshot!;
            IProductWorkspaceWindowCompositeSnapshot appliedWindowSnapshot =
                appliedWindows.Snapshot!;
            bool restoredConfiguration = TryCall(() => configuration.Restore(
                pendingUndo.Configuration,
                token.Undo));
            bool verifiedConfiguration = restoredConfiguration
                && TryCall(() => configuration.VerifyRestored(
                    pendingUndo.Configuration,
                    token.Undo));
            bool restoredWindows = verifiedConfiguration
                && TryCall(() => windows.Restore(
                    pendingUndo.Windows,
                    token.Undo.WindowRegistryGeneration));
            bool verifiedWindows = restoredWindows
                && TryCall(() => windows.VerifyRestored(
                    pendingUndo.Windows,
                    token.Undo.WindowRegistryGeneration));
            bool undone = verifiedConfiguration
                && verifiedWindows
                && MatchesCurrent(token.Undo);
            if (!undone)
            {
                bool rolledForward = TryCall(() => windows.Restore(
                        appliedWindowSnapshot,
                        token.Applied.WindowRegistryGeneration))
                    && TryCall(() => windows.VerifyRestored(
                        appliedWindowSnapshot,
                        token.Applied.WindowRegistryGeneration))
                    && TryCall(() => configuration.Restore(
                        appliedConfigurationSnapshot,
                        token.Applied))
                    && TryCall(() => configuration.VerifyRestored(
                        appliedConfigurationSnapshot,
                        token.Applied))
                    && MatchesCurrent(token.Applied)
                    && TryCall(input.Reopen);
                Dispose(
                    appliedConfigurationSnapshot,
                    appliedWindowSnapshot);
                if (rolledForward)
                {
                    return UndoResult(
                        ProductWorkspaceWindowCompositeUndoStatus.RolledForward,
                        ProductWorkspaceWindowCompositeFailure
                            .ConfigurationRestoreVerificationFailed);
                }

                bool hidden = TryCall(input.HideAffectedHosts);
                pendingUndo.Configuration.Dispose();
                pendingUndo.Windows.Dispose();
                pendingUndo = null;
                return new(
                    ProductWorkspaceWindowCompositeUndoStatus.RecoveryFailed,
                    hidden
                        ? ProductWorkspaceWindowCompositeFailure
                            .ConfigurationRestoreVerificationFailed
                        : ProductWorkspaceWindowCompositeFailure
                            .EmergencyHideFailed,
                    InputClosed: true,
                    HostsHidden: hidden);
            }

            bool inputReopened = TryCall(input.Reopen);
            Dispose(
                appliedConfigurationSnapshot,
                appliedWindowSnapshot,
                pendingUndo.Configuration,
                pendingUndo.Windows);
            pendingUndo = null;
            if (!inputReopened)
            {
                bool hidden = TryCall(input.HideAffectedHosts);
                return new(
                    ProductWorkspaceWindowCompositeUndoStatus.RecoveryFailed,
                    hidden
                        ? ProductWorkspaceWindowCompositeFailure.InputReopenFailed
                        : ProductWorkspaceWindowCompositeFailure
                            .EmergencyHideFailed,
                    InputClosed: true,
                    HostsHidden: hidden);
            }

            return UndoResult(ProductWorkspaceWindowCompositeUndoStatus.Undone);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingUndo?.Configuration.Dispose();
            pendingUndo?.Windows.Dispose();
            pendingUndo = null;
        }

        GC.SuppressFinalize(this);
    }

    private ProductWorkspaceWindowCompositeFailure Validate(
        ProductWorkspaceWindowCompositeRequest request)
    {
        if (request.BeforeState is null
            || request.RecoveredState is null
            || request.Plan is null
            || request.RegisteredContainerIds is null
            || request.Token is null)
        {
            return ProductWorkspaceWindowCompositeFailure.InvalidRequest;
        }

        if (!request.UserConfirmed)
        {
            return ProductWorkspaceWindowCompositeFailure
                .ConfirmationRequired;
        }

        ProductWorkspaceWindowCompositeToken? expected = PrepareToken(
            request.BeforeState,
            request.RecoveredState,
            request.Plan,
            request.RegisteredContainerIds,
            request.WindowOwnershipAttested,
            request.Token.Before.TopologyGeneration,
            request.Token.Before.EditRevision,
            request.Token.Before.WindowRegistryGeneration,
            request.Token.Before.DesktopHostInstanceId,
            request.Token.Before.DesktopHostGeneration,
            request.Token.ReviewApproved,
            request.Token.OperationId);
        if (expected is null || expected != request.Token)
        {
            return ProductWorkspaceWindowCompositeFailure.InvalidRequest;
        }

        return MatchesCurrent(request.Token.Before)
            ? ProductWorkspaceWindowCompositeFailure.None
            : ProductWorkspaceWindowCompositeFailure.BindingChanged;
    }

    private ProductWorkspaceWindowCompositeResult FinishCaptureFailure(
        ProductWorkspaceWindowCompositeRequest request,
        ProductWorkspaceWindowCompositeFailure failure)
    {
        bool reopened = TryCall(input.Reopen);
        if (reopened)
        {
            return Result(
                ProductWorkspaceWindowCompositeStatus.CaptureFailed,
                failure,
                request,
                inputClosed: false,
                hostsHidden: false,
                undoToken: null);
        }

        bool hidden = TryCall(input.HideAffectedHosts);
        return Result(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            hidden
                ? ProductWorkspaceWindowCompositeFailure.InputReopenFailed
                : ProductWorkspaceWindowCompositeFailure.EmergencyHideFailed,
            request,
            inputClosed: true,
            hostsHidden: hidden,
            undoToken: null);
    }

    private ProductWorkspaceWindowCompositeResult FinishWithoutMutation(
        ProductWorkspaceWindowCompositeRequest request,
        ProductWorkspaceWindowCompositeStatus status,
        ProductWorkspaceWindowCompositeFailure failure)
    {
        bool reopened = TryCall(input.Reopen);
        return Result(
            reopened ? status : ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            reopened ? failure : ProductWorkspaceWindowCompositeFailure.InputReopenFailed,
            request,
            inputClosed: !reopened,
            hostsHidden: false,
            undoToken: null);
    }

    private ProductWorkspaceWindowCompositeResult RollBack(
        ProductWorkspaceWindowCompositeRequest request,
        IProductWorkspaceWindowCompositeSnapshot configurationSnapshot,
        IProductWorkspaceWindowCompositeSnapshot windowSnapshot,
        bool configurationTouched,
        bool windowTouched,
        ProductWorkspaceWindowCompositeFailure failure)
    {
        bool configurationRestored = !configurationTouched
            || TryCall(() => configuration.Restore(
                configurationSnapshot,
                request.Token.Before));
        bool configurationVerified = configurationRestored
            && (!configurationTouched
                || TryCall(() => configuration.VerifyRestored(
                    configurationSnapshot,
                    request.Token.Before)));
        bool windowsRestored = !windowTouched
            || TryCall(() => windows.Restore(
                windowSnapshot,
                request.Token.Before.WindowRegistryGeneration));
        bool windowsVerified = windowsRestored
            && (!windowTouched
                || TryCall(() => windows.VerifyRestored(
                    windowSnapshot,
                    request.Token.Before.WindowRegistryGeneration)));
        bool restored = configurationVerified
            && windowsVerified
            && MatchesCurrent(request.Token.Before);
        if (restored && TryCall(input.Reopen))
        {
            Dispose(configurationSnapshot, windowSnapshot);
            return Result(
                ProductWorkspaceWindowCompositeStatus.RolledBack,
                failure,
                request,
                inputClosed: false,
                hostsHidden: false,
                undoToken: null);
        }

        ProductWorkspaceWindowCompositeFailure rollbackFailure =
            !configurationRestored
                ? ProductWorkspaceWindowCompositeFailure
                    .ConfigurationRestoreFailed
                : !configurationVerified
                    ? ProductWorkspaceWindowCompositeFailure
                        .ConfigurationRestoreVerificationFailed
                    : !windowsRestored
                        ? ProductWorkspaceWindowCompositeFailure
                            .WindowRestoreFailed
                        : !windowsVerified
                            ? ProductWorkspaceWindowCompositeFailure
                                .WindowRestoreVerificationFailed
                            : failure == ProductWorkspaceWindowCompositeFailure
                                .InputReopenFailed
                                ? failure
                                : ProductWorkspaceWindowCompositeFailure
                                    .BindingChanged;
        bool hidden = TryCall(input.HideAffectedHosts);
        Dispose(configurationSnapshot, windowSnapshot);
        return Result(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            hidden
                ? rollbackFailure
                : ProductWorkspaceWindowCompositeFailure.EmergencyHideFailed,
            request,
            inputClosed: true,
            hostsHidden: hidden,
            undoToken: null);
    }

    private bool MatchesCurrent(ProductWorkspaceWindowCompositeBinding expected)
    {
        try
        {
            return currentBinding() == expected;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or OverflowException)
        {
            return false;
        }
    }

    private static ProductWorkspaceWindowCompositeCapture TryCapture(
        Func<ProductWorkspaceWindowCompositeCapture> capture)
    {
        try
        {
            return capture() ?? ProductWorkspaceWindowCompositeCapture.Failed;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or OverflowException)
        {
            return ProductWorkspaceWindowCompositeCapture.Failed;
        }
    }

    private static bool TryCall(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or OverflowException)
        {
            return false;
        }
    }

    private static bool IsValid(ProductWorkspaceWindowCompositeCapture capture) =>
        capture.Succeeded && capture.Snapshot is not null;

    private static void DisposeCapture(
        ProductWorkspaceWindowCompositeCapture capture) =>
        capture.Snapshot?.Dispose();

    private static void Dispose(
        params IProductWorkspaceWindowCompositeSnapshot[] snapshots)
    {
        foreach (IProductWorkspaceWindowCompositeSnapshot snapshot in snapshots)
        {
            snapshot.Dispose();
        }
    }

    private static bool HasExactContainerSet(
        LayoutRecoveryPlan plan,
        IReadOnlyList<string> registered)
    {
        string[] planned = plan.ContainerPlacements
            .Select(placement => placement.ContainerId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = registered.Order(StringComparer.Ordinal).ToArray();
        return actual.All(id => !string.IsNullOrWhiteSpace(id))
            && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length
            && planned.SequenceEqual(actual, StringComparer.Ordinal);
    }

    private static bool HasExactStateContainerSet(
        ProductWorkspaceState state,
        LayoutRecoveryPlan plan) =>
        state.Containers.Select(container => container.Id)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                plan.ContainerPlacements
                    .Select(placement => placement.ContainerId)
                    .Order(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static ProductWorkspaceWindowCompositeResult Result(
        ProductWorkspaceWindowCompositeStatus status,
        ProductWorkspaceWindowCompositeFailure failure,
        ProductWorkspaceWindowCompositeRequest request,
        bool inputClosed,
        bool hostsHidden,
        ProductWorkspaceWindowCompositeUndoToken? undoToken) => new(
            status,
            failure,
            request.Token?.OperationId ?? Guid.Empty,
            request.Plan?.ContainerPlacements?.Count ?? 0,
            inputClosed,
            hostsHidden,
            undoToken);

    private static ProductWorkspaceWindowCompositeUndoResult UndoResult(
        ProductWorkspaceWindowCompositeUndoStatus status,
        ProductWorkspaceWindowCompositeFailure failure =
            ProductWorkspaceWindowCompositeFailure.None) => new(
                status,
                failure,
                InputClosed: false,
                HostsHidden: false);
}
