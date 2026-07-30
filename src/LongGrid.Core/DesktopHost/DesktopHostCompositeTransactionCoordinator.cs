namespace LongGrid.Core.DesktopHost;

public enum DesktopHostTransactionLayerKind
{
    Bounds,
    Region,
    Composition,
    UiAutomation,
}

public enum DesktopHostCompositeTransactionStatus
{
    Applied,
    Superseded,
    InputGateFailed,
    CaptureFailed,
    RolledBack,
    RollbackFailed,
}

public enum DesktopHostCompositeTransactionFailure
{
    None,
    GenerationChanged,
    InputCloseFailed,
    CaptureFailed,
    ApplyFailed,
    VerificationFailed,
    InputReopenFailed,
    RestoreFailed,
    RestoreVerificationFailed,
    EmergencyHideFailed,
}

public interface IDesktopHostLayerSnapshot : IDisposable
{
}

public sealed record DesktopHostLayerCapture(
    bool Succeeded,
    IDesktopHostLayerSnapshot? Snapshot)
{
    public static DesktopHostLayerCapture Failed { get; } =
        new(false, null);
}

public interface IDesktopHostTransactionLayer
{
    DesktopHostTransactionLayerKind Kind { get; }

    DesktopHostLayerCapture Capture();

    bool Apply(long generation);

    bool Verify(long generation);

    bool Restore(IDesktopHostLayerSnapshot snapshot);

    bool VerifyRestored(IDesktopHostLayerSnapshot snapshot);
}

public interface IDesktopHostInputGate
{
    bool Close();

    bool Reopen();

    bool HideAffectedHosts();
}

public sealed record DesktopHostCompositeTransactionResult(
    DesktopHostCompositeTransactionStatus Status,
    DesktopHostCompositeTransactionFailure Failure,
    DesktopHostTransactionLayerKind? FailedLayer,
    long Generation,
    int AppliedLayerCount,
    int RestoredLayerCount,
    bool InputClosed,
    bool HostsHidden)
{
    public bool KeepsProposedState =>
        Status == DesktopHostCompositeTransactionStatus.Applied;
}

public sealed class DesktopHostCompositeTransactionCoordinator
{
    private static readonly DesktopHostTransactionLayerKind[]
        RequiredOrder =
        [
            DesktopHostTransactionLayerKind.Bounds,
            DesktopHostTransactionLayerKind.Region,
            DesktopHostTransactionLayerKind.Composition,
            DesktopHostTransactionLayerKind.UiAutomation,
        ];

    private readonly Func<long> _currentGeneration;
    private readonly IDesktopHostInputGate _inputGate;
    private readonly IDesktopHostTransactionLayer[] _layers;

    public DesktopHostCompositeTransactionCoordinator(
        Func<long> currentGeneration,
        IDesktopHostInputGate inputGate,
        IEnumerable<IDesktopHostTransactionLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(currentGeneration);
        ArgumentNullException.ThrowIfNull(inputGate);
        ArgumentNullException.ThrowIfNull(layers);
        _currentGeneration = currentGeneration;
        _inputGate = inputGate;
        _layers = layers.ToArray();
        if (!_layers.Select(layer => layer.Kind)
            .SequenceEqual(RequiredOrder))
        {
            throw new ArgumentException(
                "DesktopHost transaction layers must be Bounds, Region, "
                + "Composition, and UI Automation in that exact order.",
                nameof(layers));
        }
    }

    public DesktopHostCompositeTransactionResult Execute(
        long generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        if (!IsCurrent(generation))
        {
            return Result(
                DesktopHostCompositeTransactionStatus.Superseded,
                DesktopHostCompositeTransactionFailure.GenerationChanged,
                null,
                generation,
                appliedLayerCount: 0,
                restoredLayerCount: 0,
                inputClosed: false,
                hostsHidden: false);
        }

        if (!TryCall(_inputGate.Close))
        {
            return Result(
                DesktopHostCompositeTransactionStatus.InputGateFailed,
                DesktopHostCompositeTransactionFailure.InputCloseFailed,
                null,
                generation,
                appliedLayerCount: 0,
                restoredLayerCount: 0,
                inputClosed: false,
                hostsHidden: false);
        }

        var captures = new List<DesktopHostLayerCapture>(
            _layers.Length);
        try
        {
            if (!IsCurrent(generation))
            {
                return FinishWithoutMutation(
                    DesktopHostCompositeTransactionStatus.Superseded,
                    DesktopHostCompositeTransactionFailure
                        .GenerationChanged,
                    null,
                    generation);
            }

            foreach (IDesktopHostTransactionLayer layer in _layers)
            {
                DesktopHostLayerCapture capture =
                    TryCapture(layer);
                captures.Add(capture);
                if (!capture.Succeeded
                    || capture.Snapshot is null)
                {
                    return FinishWithoutMutation(
                        DesktopHostCompositeTransactionStatus
                            .CaptureFailed,
                        DesktopHostCompositeTransactionFailure
                            .CaptureFailed,
                        layer.Kind,
                        generation);
                }
            }

            if (!IsCurrent(generation))
            {
                return FinishWithoutMutation(
                    DesktopHostCompositeTransactionStatus.Superseded,
                    DesktopHostCompositeTransactionFailure
                        .GenerationChanged,
                    null,
                    generation);
            }

            for (int index = 0; index < _layers.Length; index++)
            {
                IDesktopHostTransactionLayer layer = _layers[index];
                if (!IsCurrent(generation))
                {
                    return RollBack(
                        captures,
                        touchedLayerCount: index,
                        DesktopHostCompositeTransactionFailure
                            .GenerationChanged,
                        layer.Kind,
                        generation);
                }

                if (!TryCall(() => layer.Apply(generation)))
                {
                    return RollBack(
                        captures,
                        touchedLayerCount: index + 1,
                        DesktopHostCompositeTransactionFailure
                            .ApplyFailed,
                        layer.Kind,
                        generation);
                }

                if (!TryCall(() => layer.Verify(generation)))
                {
                    return RollBack(
                        captures,
                        touchedLayerCount: index + 1,
                        DesktopHostCompositeTransactionFailure
                            .VerificationFailed,
                        layer.Kind,
                        generation);
                }

                if (!IsCurrent(generation))
                {
                    return RollBack(
                        captures,
                        touchedLayerCount: index + 1,
                        DesktopHostCompositeTransactionFailure
                            .GenerationChanged,
                        layer.Kind,
                        generation);
                }
            }

            foreach (IDesktopHostTransactionLayer layer in _layers)
            {
                if (!TryCall(() => layer.Verify(generation)))
                {
                    return RollBack(
                        captures,
                        _layers.Length,
                        DesktopHostCompositeTransactionFailure
                            .VerificationFailed,
                        layer.Kind,
                        generation);
                }

                if (!IsCurrent(generation))
                {
                    return RollBack(
                        captures,
                        _layers.Length,
                        DesktopHostCompositeTransactionFailure
                            .GenerationChanged,
                        layer.Kind,
                        generation);
                }
            }

            if (!TryCall(_inputGate.Reopen))
            {
                return RollBack(
                    captures,
                    _layers.Length,
                    DesktopHostCompositeTransactionFailure
                        .InputReopenFailed,
                    null,
                    generation,
                    inputAlreadyFailedToReopen: true);
            }

            return Result(
                DesktopHostCompositeTransactionStatus.Applied,
                DesktopHostCompositeTransactionFailure.None,
                null,
                generation,
                _layers.Length,
                restoredLayerCount: 0,
                inputClosed: false,
                hostsHidden: false);
        }
        finally
        {
            foreach (DesktopHostLayerCapture capture in captures)
            {
                capture.Snapshot?.Dispose();
            }
        }
    }

    private DesktopHostCompositeTransactionResult FinishWithoutMutation(
        DesktopHostCompositeTransactionStatus status,
        DesktopHostCompositeTransactionFailure failure,
        DesktopHostTransactionLayerKind? failedLayer,
        long generation)
    {
        if (TryCall(_inputGate.Reopen))
        {
            return Result(
                status,
                failure,
                failedLayer,
                generation,
                appliedLayerCount: 0,
                restoredLayerCount: 0,
                inputClosed: false,
                hostsHidden: false);
        }

        bool hidden = TryCall(_inputGate.HideAffectedHosts);
        return Result(
            DesktopHostCompositeTransactionStatus.InputGateFailed,
            hidden
                ? DesktopHostCompositeTransactionFailure.InputReopenFailed
                : DesktopHostCompositeTransactionFailure
                    .EmergencyHideFailed,
            failedLayer,
            generation,
            appliedLayerCount: 0,
            restoredLayerCount: 0,
            inputClosed: true,
            hostsHidden: hidden);
    }

    private DesktopHostCompositeTransactionResult RollBack(
        IReadOnlyList<DesktopHostLayerCapture> captures,
        int touchedLayerCount,
        DesktopHostCompositeTransactionFailure failure,
        DesktopHostTransactionLayerKind? failedLayer,
        long generation,
        bool inputAlreadyFailedToReopen = false)
    {
        int restored = 0;
        DesktopHostCompositeTransactionFailure rollbackFailure =
            DesktopHostCompositeTransactionFailure.None;
        DesktopHostTransactionLayerKind? rollbackFailedLayer = null;
        var restoreSucceeded = new bool[touchedLayerCount];
        for (int index = touchedLayerCount - 1; index >= 0; index--)
        {
            IDesktopHostTransactionLayer layer = _layers[index];
            IDesktopHostLayerSnapshot snapshot =
                captures[index].Snapshot
                ?? throw new InvalidOperationException(
                    "A touched layer must have a captured snapshot.");
            if (!TryCall(() => layer.Restore(snapshot)))
            {
                if (rollbackFailure
                    == DesktopHostCompositeTransactionFailure.None)
                {
                    rollbackFailure =
                        DesktopHostCompositeTransactionFailure
                            .RestoreFailed;
                    rollbackFailedLayer = layer.Kind;
                }

                continue;
            }

            restoreSucceeded[index] = true;
        }

        for (int index = 0; index < touchedLayerCount; index++)
        {
            IDesktopHostTransactionLayer layer = _layers[index];
            IDesktopHostLayerSnapshot snapshot =
                captures[index].Snapshot
                ?? throw new InvalidOperationException(
                    "A touched layer must have a captured snapshot.");
            bool verified = TryCall(
                () => layer.VerifyRestored(snapshot));
            if (!verified)
            {
                if (rollbackFailure
                    == DesktopHostCompositeTransactionFailure.None)
                {
                    rollbackFailure =
                        DesktopHostCompositeTransactionFailure
                            .RestoreVerificationFailed;
                    rollbackFailedLayer = layer.Kind;
                }

                continue;
            }

            if (restoreSucceeded[index])
            {
                restored++;
            }
        }

        if (rollbackFailure
                == DesktopHostCompositeTransactionFailure.None
            && !inputAlreadyFailedToReopen
            && TryCall(_inputGate.Reopen))
        {
            return Result(
                DesktopHostCompositeTransactionStatus.RolledBack,
                failure,
                failedLayer,
                generation,
                touchedLayerCount,
                restored,
                inputClosed: false,
                hostsHidden: false);
        }

        bool hidden = TryCall(_inputGate.HideAffectedHosts);
        return Result(
            DesktopHostCompositeTransactionStatus.RollbackFailed,
            hidden
                ? rollbackFailure
                    == DesktopHostCompositeTransactionFailure.None
                        ? DesktopHostCompositeTransactionFailure
                            .InputReopenFailed
                        : rollbackFailure
                : DesktopHostCompositeTransactionFailure
                    .EmergencyHideFailed,
            rollbackFailedLayer ?? failedLayer,
            generation,
            touchedLayerCount,
            restored,
            inputClosed: true,
            hostsHidden: hidden);
    }

    private bool IsCurrent(long generation) =>
        _currentGeneration() == generation;

    private static DesktopHostLayerCapture TryCapture(
        IDesktopHostTransactionLayer layer)
    {
        try
        {
            return layer.Capture();
        }
        catch (Exception)
        {
            return DesktopHostLayerCapture.Failed;
        }
    }

    private static bool TryCall(Func<bool> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DesktopHostCompositeTransactionResult Result(
        DesktopHostCompositeTransactionStatus status,
        DesktopHostCompositeTransactionFailure failure,
        DesktopHostTransactionLayerKind? failedLayer,
        long generation,
        int appliedLayerCount,
        int restoredLayerCount,
        bool inputClosed,
        bool hostsHidden) =>
        new(
            status,
            failure,
            failedLayer,
            generation,
            appliedLayerCount,
            restoredLayerCount,
            inputClosed,
            hostsHidden);
}
