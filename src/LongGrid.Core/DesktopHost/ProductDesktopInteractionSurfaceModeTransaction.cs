namespace LongGrid.Core.DesktopHost;

public enum ProductDesktopInteractionSurfaceMode
{
    Passive,
    Explicit,
    Hidden,
}

public sealed record ProductDesktopInteractionSurfaceEvidence(
    ProductDesktopInteractionSurfaceMode Mode,
    long WindowRegistryGeneration,
    bool Visible,
    bool HitTestTransparent,
    bool IsKeyboardFocusable,
    bool SelectionPatternAvailable,
    bool ToolWindow,
    bool NoActivate,
    bool Topmost,
    bool HasOwner,
    bool OwnsForeground)
{
    public bool IsPassiveContract =>
        Mode == ProductDesktopInteractionSurfaceMode.Passive
        && Visible
        && HitTestTransparent
        && !IsKeyboardFocusable
        && !SelectionPatternAvailable
        && HasStableWindowPolicy;

    public bool IsExplicitContract =>
        Mode == ProductDesktopInteractionSurfaceMode.Explicit
        && Visible
        && !HitTestTransparent
        && IsKeyboardFocusable
        && SelectionPatternAvailable
        && HasStableWindowPolicy;

    public bool IsHiddenContract =>
        Mode == ProductDesktopInteractionSurfaceMode.Hidden
        && !Visible
        && HitTestTransparent
        && !IsKeyboardFocusable
        && !SelectionPatternAvailable
        && HasStableWindowPolicy;

    private bool HasStableWindowPolicy =>
        WindowRegistryGeneration > 0
        && ToolWindow
        && NoActivate
        && !Topmost
        && !HasOwner
        && !OwnsForeground;
}

public sealed record ProductDesktopInteractionSurfaceCapture(
    bool Succeeded,
    ProductDesktopInteractionSurfaceEvidence? Evidence)
{
    public static ProductDesktopInteractionSurfaceCapture Failed { get; } =
        new(false, null);
}

public interface IProductDesktopInteractionSurfaceModeAdapter
{
    ProductDesktopInteractionSurfaceCapture Capture();

    bool ApplyExplicit(ProductDesktopInteractionLease lease);

    bool ApplyPassive(long expectedWindowRegistryGeneration);

    bool Restore(ProductDesktopInteractionSurfaceEvidence evidence);

    bool Hide(long expectedWindowRegistryGeneration);
}

public enum ProductDesktopInteractionSurfaceTransactionStatus
{
    Passive,
    Explicit,
    AdmissionRejected,
    AlreadyExplicit,
    CaptureFailed,
    PassiveContractRequired,
    SurfaceApplyFailed,
    SurfaceVerificationFailed,
    InvalidVisibleItems,
    ReturnedPassive,
    HiddenFailClosed,
    EmergencyHideFailed,
}

public sealed record ProductDesktopInteractionSurfaceTransactionSnapshot(
    ProductDesktopInteractionSurfaceTransactionStatus Status,
    ProductDesktopInteractionSnapshot Admission,
    ProductDesktopInteractionSurfaceEvidence? Surface,
    ProductDesktopSelectionSnapshot? Selection,
    ProductDesktopSelectionAccessibilitySnapshot Accessibility,
    long TransactionRevision)
{
    public bool IsExplicit =>
        Status == ProductDesktopInteractionSurfaceTransactionStatus.Explicit
        && Admission.HasActiveLease
        && Surface?.IsExplicitContract == true
        && Selection is not null
        && Accessibility.Mode
            == ProductDesktopSelectionAccessibilityMode.ExplicitInteraction;
}

public sealed class ProductDesktopInteractionSurfaceModeTransaction
{
    private readonly object sync = new();
    private readonly ProductDesktopInteractionAdmissionController admission;
    private readonly ProductDesktopInteractionCancellationAdapter cancellation;
    private readonly IProductDesktopInteractionSurfaceModeAdapter surface;
    private ProductDesktopInteractionSelectionController? selection;
    private ProductDesktopInteractionSurfaceTransactionSnapshot snapshot;

    public ProductDesktopInteractionSurfaceModeTransaction(
        ProductDesktopInteractionAdmissionController admission,
        IProductDesktopInteractionSurfaceModeAdapter surface)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(surface);
        this.admission = admission;
        this.surface = surface;
        cancellation = new(admission);
        snapshot = new(
            ProductDesktopInteractionSurfaceTransactionStatus.Passive,
            admission.Snapshot,
            Surface: null,
            Selection: null,
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreatePassive([]),
            TransactionRevision: 0);
    }

    public ProductDesktopInteractionSurfaceTransactionSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public ProductDesktopInteractionSurfaceTransactionSnapshot TryEnter(
        ProductDesktopInteractionIntent intent,
        ProductDesktopInteractionEvidence evidence,
        IReadOnlyList<string> visibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(visibleItemIds);
        lock (sync)
        {
            if (snapshot.IsExplicit || admission.Snapshot.HasActiveLease)
            {
                return snapshot with
                {
                    Status = ProductDesktopInteractionSurfaceTransactionStatus
                        .AlreadyExplicit,
                };
            }

            ProductDesktopInteractionSurfaceCapture baseline = TryCapture();
            if (!baseline.Succeeded || baseline.Evidence is null)
            {
                return PublishFailure(
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .CaptureFailed,
                    admission.Snapshot);
            }

            if (!baseline.Evidence.IsPassiveContract
                || baseline.Evidence.WindowRegistryGeneration
                    != evidence.WindowRegistryGeneration
                || baseline.Evidence.WindowRegistryGeneration
                    != intent.WindowRegistryGeneration)
            {
                return PublishFailure(
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .PassiveContractRequired,
                    admission.Snapshot,
                    baseline.Evidence);
            }

            ProductDesktopInteractionSnapshot admitted =
                admission.TryEnterExplicitInteraction(intent, evidence, nowUtc);
            if (!admitted.HasActiveLease)
            {
                return PublishFailure(
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .AdmissionRejected,
                    admitted,
                    baseline.Evidence);
            }

            if (!TryCall(() => surface.ApplyExplicit(admitted.Lease!)))
            {
                return RollBack(
                    baseline.Evidence,
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .SurfaceApplyFailed);
            }

            ProductDesktopInteractionSurfaceEvidence? explicitEvidence =
                CaptureEvidence();
            if (explicitEvidence?.IsExplicitContract != true
                || explicitEvidence.WindowRegistryGeneration
                    != admitted.Lease!.WindowRegistryGeneration)
            {
                return RollBack(
                    baseline.Evidence,
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .SurfaceVerificationFailed);
            }

            ProductDesktopSelectionCreationResult created =
                ProductDesktopInteractionSelectionController.TryCreate(
                    admitted.Lease,
                    visibleItemIds,
                    nowUtc);
            if (!created.IsCreated)
            {
                return RollBack(
                    baseline.Evidence,
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .InvalidVisibleItems);
            }

            selection = created.Controller;
            ProductDesktopSelectionSnapshot selectionSnapshot =
                selection!.Snapshot;
            return Publish(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                admitted,
                explicitEvidence,
                selectionSnapshot,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selectionSnapshot),
                incrementRevision: true);
        }
    }

    public ProductDesktopInteractionSurfaceTransactionSnapshot ApplySelection(
        ProductDesktopSelectionRequest request,
        ProductDesktopInteractionLease currentLease,
        IReadOnlyList<string> currentVisibleItemIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentLease);
        ArgumentNullException.ThrowIfNull(currentVisibleItemIds);
        lock (sync)
        {
            if (!snapshot.IsExplicit || selection is null)
            {
                return snapshot;
            }

            ProductDesktopSelectionSnapshot updated = selection.Apply(
                currentLease,
                currentVisibleItemIds,
                request,
                nowUtc);
            ProductDesktopSelectionAccessibilitySnapshot accessibility =
                updated.Status is ProductDesktopSelectionStatus.Ready
                    or ProductDesktopSelectionStatus.Applied
                    ? ProductDesktopInteractionSelectionAccessibilityAdapter
                        .CreateExplicit(updated)
                    : snapshot.Accessibility;
            return Publish(
                snapshot.Status,
                snapshot.Admission,
                snapshot.Surface,
                updated,
                accessibility,
                incrementRevision:
                    updated.SelectionRevision
                    != snapshot.Selection!.SelectionRevision);
        }
    }

    public ProductDesktopInteractionSurfaceTransactionSnapshot
        ReconcileVisibleItems(
            ProductDesktopInteractionLease currentLease,
            IReadOnlyList<string> currentVisibleItemIds,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currentLease);
        ArgumentNullException.ThrowIfNull(currentVisibleItemIds);
        lock (sync)
        {
            if (!snapshot.IsExplicit || selection is null)
            {
                return snapshot;
            }

            ProductDesktopSelectionSnapshot updated =
                selection.ReconcileVisibleItems(
                    currentLease,
                    currentVisibleItemIds,
                    nowUtc);
            if (updated.Status != ProductDesktopSelectionStatus.Reconciled)
            {
                return Publish(
                    snapshot.Status,
                    snapshot.Admission,
                    snapshot.Surface,
                    updated,
                    snapshot.Accessibility,
                    incrementRevision: false);
            }

            return Publish(
                snapshot.Status,
                snapshot.Admission,
                snapshot.Surface,
                updated,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(updated),
                incrementRevision:
                    updated.SelectionRevision
                    != snapshot.Selection!.SelectionRevision);
        }
    }

    public ProductDesktopInteractionSurfaceTransactionSnapshot Cancel(
        ProductDesktopInteractionCancellationSignal signal,
        DateTimeOffset nowUtc,
        ProductDesktopInteractionEvidence? evidence = null)
    {
        lock (sync)
        {
            ProductDesktopInteractionSnapshot current =
                cancellation.Handle(signal, nowUtc, evidence);
            if (current.HasActiveLease)
            {
                return snapshot;
            }

            if (!snapshot.IsExplicit)
            {
                return Publish(
                    snapshot.Status,
                    current,
                    snapshot.Surface,
                    snapshot.Selection,
                    snapshot.Accessibility,
                    incrementRevision: false);
            }

            long generation = snapshot.Surface!.WindowRegistryGeneration;
            bool applied = TryCall(() => surface.ApplyPassive(generation));
            ProductDesktopInteractionSurfaceEvidence? passive =
                applied ? CaptureEvidence() : null;
            selection = null;
            if (passive?.IsPassiveContract == true
                && passive.WindowRegistryGeneration == generation)
            {
                return Publish(
                    ProductDesktopInteractionSurfaceTransactionStatus
                        .ReturnedPassive,
                    current,
                    passive,
                    currentSelection: null,
                    ProductDesktopInteractionSelectionAccessibilityAdapter
                        .CreatePassive([]),
                    incrementRevision: true);
            }

            return HideFailClosed(current, generation);
        }
    }

    private ProductDesktopInteractionSurfaceTransactionSnapshot RollBack(
        ProductDesktopInteractionSurfaceEvidence baseline,
        ProductDesktopInteractionSurfaceTransactionStatus failure)
    {
        ProductDesktopInteractionSnapshot passive = admission.Cancel();
        selection = null;
        bool restored = TryCall(() => surface.Restore(baseline));
        ProductDesktopInteractionSurfaceEvidence? restoredEvidence =
            restored ? CaptureEvidence() : null;
        if (restoredEvidence?.IsPassiveContract == true
            && restoredEvidence == baseline)
        {
            return Publish(
                failure,
                passive,
                restoredEvidence,
                currentSelection: null,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreatePassive([]),
                incrementRevision: true);
        }

        return HideFailClosed(
            passive,
            baseline.WindowRegistryGeneration);
    }

    private ProductDesktopInteractionSurfaceTransactionSnapshot HideFailClosed(
        ProductDesktopInteractionSnapshot passive,
        long generation)
    {
        bool hideSucceeded = TryCall(() => surface.Hide(generation));
        ProductDesktopInteractionSurfaceEvidence? hidden = CaptureEvidence();
        return Publish(
            hideSucceeded
                && hidden?.IsHiddenContract == true
                && hidden.WindowRegistryGeneration == generation
                    ? ProductDesktopInteractionSurfaceTransactionStatus
                        .HiddenFailClosed
                    : ProductDesktopInteractionSurfaceTransactionStatus
                        .EmergencyHideFailed,
            passive,
            hidden,
            currentSelection: null,
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreatePassive([]),
            incrementRevision: true);
    }

    private ProductDesktopInteractionSurfaceTransactionSnapshot PublishFailure(
        ProductDesktopInteractionSurfaceTransactionStatus status,
        ProductDesktopInteractionSnapshot currentAdmission,
        ProductDesktopInteractionSurfaceEvidence? currentSurface = null) =>
        Publish(
            status,
            currentAdmission,
            currentSurface,
            currentSelection: null,
            ProductDesktopInteractionSelectionAccessibilityAdapter
                .CreatePassive([]),
            incrementRevision: false);

    private ProductDesktopInteractionSurfaceTransactionSnapshot Publish(
        ProductDesktopInteractionSurfaceTransactionStatus status,
        ProductDesktopInteractionSnapshot currentAdmission,
        ProductDesktopInteractionSurfaceEvidence? currentSurface,
        ProductDesktopSelectionSnapshot? currentSelection,
        ProductDesktopSelectionAccessibilitySnapshot accessibility,
        bool incrementRevision)
    {
        snapshot = new(
            status,
            currentAdmission,
            currentSurface,
            currentSelection,
            accessibility,
            incrementRevision
                ? checked(snapshot.TransactionRevision + 1)
                : snapshot.TransactionRevision);
        return snapshot;
    }

    private ProductDesktopInteractionSurfaceCapture TryCapture()
    {
        try
        {
            return surface.Capture();
        }
        catch (Exception)
        {
            return ProductDesktopInteractionSurfaceCapture.Failed;
        }
    }

    private ProductDesktopInteractionSurfaceEvidence? CaptureEvidence()
    {
        ProductDesktopInteractionSurfaceCapture capture = TryCapture();
        return capture.Succeeded ? capture.Evidence : null;
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
}
