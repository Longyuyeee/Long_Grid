using System.Diagnostics.CodeAnalysis;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using LongGrid.Infrastructure.DesktopItems;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LongGrid.App;

[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WinUI owns the Application lifetime; the audited closing handler awaits every controller before releasing the main instance.")]
public partial class App : Application
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly ProductConfigurationStore configurationStore;
    private readonly ProductWorkspaceSaveController productWorkspaceSaves;
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private readonly ProductWorkspaceCatalogRevisionSynchronizer
        workspaceCatalogRevisions;
    private readonly ProductDesktopCatalogController productDesktopCatalog;
    private readonly ProductDisplayTopologyController productDisplayTopology;
    private readonly ProductDesktopHostLifecycleController productDesktopHostLifecycle;
    private readonly ProductDesktopInteractionDevelopmentController
        productDesktopInteraction;
    private readonly WindowsProductDesktopInteractionSystemSurfaceEventSource?
        productDesktopSystemSurfaceEvents;
    private ProductWorkspaceSessionSnapshot productWorkspaceSession =
        ProductWorkspaceSessionSnapshot.Initial;
    private ProductConfigurationLoadResult? currentConfigurationLoadResult;
    private MainWindow? window;
    private bool closeAfterDrain;
    private bool closingDrainInProgress;
    private bool activationPending;

    public App()
    {
        InitializeComponent();
        string configurationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LongGrid");
        configurationStore = new ProductConfigurationStore(configurationDirectory);
        var saveWorkflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(configurationStore));
        productWorkspaceSaves = new(saveWorkflow);
        workspaceCommits = new(productWorkspaceSaves);
        workspaceCatalogRevisions = new(workspaceCommits);
        productDesktopCatalog = new(
            ProductDesktopCatalogReader.CreateForCurrentUser());
        productDisplayTopology = new(
            ProductDisplayTopologyReader.CreateForCurrentSession());
        ProductDesktopHostFeatureDecision desktopHostFeature =
            ProductDesktopHostFeaturePolicy.Evaluate(
                Environment.GetEnvironmentVariable(
                    ProductDesktopHostFeaturePolicy.EnvironmentVariableName));
        ProductDesktopInteractionFeatureDecision interactionFeature =
            ProductDesktopInteractionFeaturePolicy.Evaluate(
                desktopHostFeature,
                Environment.GetEnvironmentVariable(
                    ProductDesktopInteractionFeaturePolicy
                        .EnvironmentVariableName),
                Environment.GetEnvironmentVariable(
                    ProductDesktopInteractionFeaturePolicy
                        .EmergencyDisableEnvironmentVariableName));
        productDesktopInteraction = new(interactionFeature);
        ProductDesktopInteractionIntentBridgeFeatureDecision intentBridgeFeature =
            ProductDesktopInteractionIntentBridgePolicy.Evaluate(
                interactionFeature,
                Environment.GetEnvironmentVariable(
                    ProductDesktopInteractionIntentBridgePolicy
                        .EnvironmentVariableName),
                Environment.GetEnvironmentVariable(
                    ProductDesktopInteractionIntentBridgePolicy
                        .ManualSessionEnvironmentVariableName));
        var productDesktopIntentPreparation =
            new ProductDesktopInteractionIntentPreparationBridge(
                intentBridgeFeature);
        ProductDesktopInteractionInputForwardingFeatureDecision
            inputForwardingFeature =
                ProductDesktopInteractionInputForwardingPolicy.Evaluate(
                    intentBridgeFeature,
                    Environment.GetEnvironmentVariable(
                        ProductDesktopInteractionInputForwardingPolicy
                            .EnvironmentVariableName),
                    Environment.GetEnvironmentVariable(
                        ProductDesktopInteractionInputForwardingPolicy
                            .ManualSessionEnvironmentVariableName));
        var productDesktopInputForwarding =
            new ProductDesktopInteractionInputForwardingAdapter(
                inputForwardingFeature,
                productDesktopIntentPreparation);
        var productDesktopIntentConsumption =
            new ProductDesktopInteractionIntentConsumptionController(
                interactionFeature,
                inputForwardingFeature,
                productDesktopIntentPreparation);
        productDesktopSystemSurfaceEvents = interactionFeature.IsEnabled
            ? new()
            : null;
        productDesktopHostLifecycle = new(
            desktopHostFeature,
            productDesktopInteraction,
            productDesktopIntentPreparation,
            productDesktopInputForwarding,
            productDesktopIntentConsumption);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(
            RecoverConfigurationAsync,
            PrepareConfigurationImportAsync,
            CommitConfigurationImportAsync,
            () => configurationStore.PrepareExportAsync(),
            ExportConfigurationAsync,
            () => configurationStore.GetEvidenceInventoryAsync(),
            ExportConfigurationEvidenceAsync,
            item => configurationStore.RemoveEvidenceAsync(item, userConfirmed: true),
            () => productWorkspaceSaves.Retry(),
            RefreshProductDesktopCatalogAsync,
            CommitProductWorkspaceReferenceAction,
            CommitProductWorkspaceResolvedReferenceBatch,
            CommitProductWorkspaceReferenceBatchAdditionUndo,
            CommitProductWorkspaceResolvedReferenceBatchRemoval,
            CommitProductWorkspaceReferenceRemovalUndo,
            CommitProductWorkspaceResolvedReferenceReassignment,
            CommitProductWorkspaceReferenceReassignmentUndo,
            CommitProductWorkspaceContainerAction,
            CommitProductWorkspaceContainerRemovalUndo,
            CommitProductWorkspaceLayoutRecovery,
            CommitProductWorkspaceLayoutRecoveryUndo);
        productWorkspaceSaves.SnapshotChanged += ProductWorkspaceSaves_SnapshotChanged;
        productDesktopCatalog.SnapshotChanged += ProductDesktopCatalog_SnapshotChanged;
        productDisplayTopology.SnapshotChanged +=
            ProductDisplayTopology_SnapshotChanged;
        productDesktopHostLifecycle.SnapshotChanged +=
            ProductDesktopHostLifecycle_SnapshotChanged;
        window.DesktopKeyboardInteractionRequested +=
            MainWindow_DesktopKeyboardInteractionRequested;
        if (productDesktopSystemSurfaceEvents is not null)
        {
            productDesktopSystemSurfaceEvents.SurfaceChanged +=
                ProductDesktopSystemSurfaceEvents_SurfaceChanged;
            productDesktopSystemSurfaceEvents.Start();
        }

        window.Activated += MainWindow_Activated;
        window.ApplyProductWorkspaceSaveState(productWorkspaceSaves.Snapshot);
        window.ApplyProductDesktopCatalogState(productDesktopCatalog.Snapshot);
        window.ApplyProductDesktopHostLifecycleState(
            productDesktopHostLifecycle.Snapshot,
            productDesktopHostLifecycle.CanRequestKeyboardInteraction);
        ApplyProductWorkspaceSessionViews();
        window.AppWindow.Closing += AppWindow_Closing;
        window.Activate();
        _ = LoadConfigurationStartupStateAsync();
        _ = RefreshProductDesktopCatalogAsync();
        _ = RefreshProductDisplayTopologyAsync();

        if (activationPending)
        {
            activationPending = false;
            ActivateMainWindow();
        }
    }

    private async Task LoadConfigurationStartupStateAsync()
    {
        await Task.Yield();
        ProductConfigurationLoadResult loadResult =
            await configurationStore.LoadAsync();
        ApplyProductConfigurationLoadResult(loadResult);
    }

    private async Task<ProductConfigurationStartupState> RecoverConfigurationAsync(
        ProductConfigurationRecoveryAction action)
    {
        await configurationStore.RecoverAsync(
            new(
                action,
                UserConfirmed: true));
        ProductConfigurationLoadResult loadResult =
            await configurationStore.LoadAsync();
        return ApplyProductConfigurationLoadResult(loadResult);
    }

    private async Task<ProductConfigurationImportPlan?> PrepareConfigurationImportAsync()
    {
        if (window is null)
        {
            return null;
        }

        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".json");
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        bool isLocal = !string.IsNullOrWhiteSpace(file.Path);
        bool isReparsePoint = false;
        if (isLocal)
        {
            try
            {
                isReparsePoint = File.GetAttributes(file.Path)
                    .HasFlag(System.IO.FileAttributes.ReparsePoint);
            }
            catch (IOException)
            {
                throw new ProductConfigurationImportException(
                    ProductConfigurationImportError.SourceUnavailable);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ProductConfigurationImportException(
                    ProductConfigurationImportError.SourceUnavailable);
            }
        }

        try
        {
            await using Stream stream = await file.OpenStreamForReadAsync();
            return await configurationStore.PrepareImportAsync(
                stream,
                new(
                    UserSelected: true,
                    FileExtension: file.FileType,
                    IsLocalFileSystem: isLocal,
                    IsReparsePoint: isReparsePoint));
        }
        catch (ProductConfigurationImportException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.SourceUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProductConfigurationImportException(
                ProductConfigurationImportError.SourceUnavailable);
        }
    }

    private async Task<ProductConfigurationStartupState> CommitConfigurationImportAsync(
        ProductConfigurationImportPlan plan)
    {
        await configurationStore.ImportAsync(plan, userConfirmed: true);
        ProductConfigurationLoadResult loadResult = await configurationStore.LoadAsync();
        return ApplyProductConfigurationLoadResult(loadResult);
    }

    private ProductConfigurationStartupState ApplyProductConfigurationLoadResult(
        ProductConfigurationLoadResult loadResult)
    {
        currentConfigurationLoadResult = loadResult;
        _ = workspaceCommits.AdvanceExternalRevision();
        ProductConfigurationStartupState startupState =
            ProductConfigurationStartupState.FromLoadResult(loadResult);
        productWorkspaceSession = ProductWorkspaceSessionLoader.Load(
            loadResult,
            CreateWorkspaceCatalogSnapshot(productDesktopCatalog.Snapshot));
        _ = workspaceCatalogRevisions.ResetBaseline(productDesktopCatalog.Snapshot);
        window?.ApplyConfigurationStartupState(startupState);
        ApplyProductWorkspaceSessionViews();
        return startupState;
    }

    private async Task RefreshProductDesktopCatalogAsync()
    {
        _ = await productDesktopCatalog.RefreshAsync();
    }

    private async Task RefreshProductDisplayTopologyAsync()
    {
        _ = await productDisplayTopology.RefreshAsync();
    }

    private void ProductDesktopCatalog_SnapshotChanged(
        object? sender,
        ProductDesktopCatalogSnapshot snapshot)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        if (currentWindow.DispatcherQueue.HasThreadAccess)
        {
            ApplyProductDesktopCatalogSnapshot(snapshot);
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () => ApplyProductDesktopCatalogSnapshot(snapshot));
    }

    private void ApplyProductDesktopCatalogSnapshot(
        ProductDesktopCatalogSnapshot snapshot)
    {
        if (currentConfigurationLoadResult is null)
        {
            window?.ApplyProductDesktopCatalogState(snapshot);
            return;
        }

        ProductWorkspaceCatalogRevisionSyncResult revisionSync =
            workspaceCatalogRevisions.Observe(snapshot);
        if (revisionSync.Status ==
            ProductWorkspaceCatalogRevisionSyncStatus.StaleIgnored)
        {
            return;
        }

        window?.ApplyProductDesktopCatalogState(snapshot);
        productWorkspaceSession = ProductWorkspaceSessionLoader.Load(
            currentConfigurationLoadResult,
            CreateWorkspaceCatalogSnapshot(snapshot));
        ApplyProductWorkspaceSessionViews();
    }

    private void ProductDisplayTopology_SnapshotChanged(
        object? sender,
        ProductDisplayTopologySnapshot snapshot)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        if (currentWindow.DispatcherQueue.HasThreadAccess)
        {
            ApplyProductWorkspaceSessionViews();
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            ApplyProductWorkspaceSessionViews);
    }

    private void ProductDesktopHostLifecycle_SnapshotChanged(
        object? sender,
        ProductDesktopHostLifecycleSnapshot snapshot)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        if (currentWindow.DispatcherQueue.HasThreadAccess)
        {
            currentWindow.ApplyProductDesktopHostLifecycleState(
                snapshot,
                productDesktopHostLifecycle.CanRequestKeyboardInteraction);
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () => currentWindow.ApplyProductDesktopHostLifecycleState(
                snapshot,
                productDesktopHostLifecycle.CanRequestKeyboardInteraction));
    }

    private void MainWindow_DesktopKeyboardInteractionRequested(
        object? sender,
        EventArgs e)
    {
        _ = productDesktopHostLifecycle.RequestKeyboardInteraction();
        window?.ApplyProductDesktopHostLifecycleState(
            productDesktopHostLifecycle.Snapshot,
            productDesktopHostLifecycle.CanRequestKeyboardInteraction);
    }

    private void MainWindow_Activated(
        object sender,
        WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (!productDesktopHostLifecycle.OwnsForegroundActivationSource)
            {
                productDesktopSystemSurfaceEvents?.ReportFocusLost();
            }
        }
    }

    private void ProductDesktopSystemSurfaceEvents_SurfaceChanged(
        object? sender,
        ProductDesktopInteractionSystemSurfaceEvent systemEvent)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        void Apply() =>
            _ = closingDrainInProgress
                ? productDesktopHostLifecycle.Snapshot
                : productDesktopHostLifecycle.ApplySystemSurfaceEvent(systemEvent);
        if (currentWindow.DispatcherQueue.HasThreadAccess)
        {
            Apply();
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(Apply);
    }

    private void ApplyProductWorkspaceSessionViews()
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        currentWindow.ApplyProductWorkspaceSessionState(productWorkspaceSession);
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        ProductWorkspaceLayoutRecoveryReviewResult layoutReview =
            ProductWorkspaceLayoutRecoveryReview.Prepare(
                productWorkspaceSession.State,
                currentTopology: topology.IsAuthoritative
                    ? topology.Displays
                    : null,
                currentTopologyAuthoritative: topology.IsAuthoritative,
                topologyGeneration: topology.Generation,
                editRevision: workspaceCommits.CurrentEditRevision);
        if (productWorkspaceSession.IsReadOnly)
        {
            layoutReview = layoutReview with { Token = null };
        }
        ProductWorkspaceLayoutRecoveryUndoToken? undoToken =
            productWorkspaceSession.IsReadOnly
                ? null
                : workspaceCommits.CurrentLayoutRecoveryUndoToken;
        currentWindow.ApplyProductWorkspaceLayoutRecoveryPreview(
            ProductWorkspaceLayoutRecoveryPresentation.Create(
                layoutReview,
                undoToken));
        ProductWorkspaceReadResult readModel = productWorkspaceSession.State is null
            ? new(
                ProductWorkspaceProjectionError.InvalidState,
                ProductConfigurationError.None,
                null)
            : ProductWorkspaceReadModel.Create(productWorkspaceSession.State);
        _ = productDesktopHostLifecycle.ApplyProjectionUpdate(
            ProductDesktopHostProjectionBuilder.BuildUpdate(
                productWorkspaceSession.State,
                readModel.Snapshot,
                topology,
                workspaceCommits.CurrentEditRevision));
        ProductWorkspaceReadPresentation readPresentation = readModel.IsSuccess
            ? ProductWorkspaceReadPresentation.Create(readModel.Snapshot!)
            : productWorkspaceSession.Status ==
                ProductWorkspaceSessionStatus.NoSavedConfiguration
                ? ProductWorkspaceReadPresentation.NoSavedConfiguration
                : ProductWorkspaceReadPresentation.Unavailable;
        currentWindow.ApplyProductWorkspaceReadModel(readPresentation);
        ProductWorkspaceContainerEditPresentation containerEditor =
            readModel.IsSuccess
                ? ProductWorkspaceContainerEditPresentation.Create(
                    workspaceCommits.CurrentEditRevision,
                    !productWorkspaceSession.IsReadOnly,
                    readModel.Snapshot!.Containers,
                    workspaceCommits.CurrentContainerRemovalUndoToken)
                : productWorkspaceSession.Status ==
                    ProductWorkspaceSessionStatus.NoSavedConfiguration
                    ? ProductWorkspaceContainerEditPresentation.CreateEmpty(
                        workspaceCommits.CurrentEditRevision)
                    : ProductWorkspaceContainerEditPresentation.Unavailable;
        currentWindow.ApplyProductWorkspaceContainerEditor(containerEditor);
        ProductWorkspaceResolvedReferenceAddPresentation referenceAdder =
            readModel.IsSuccess
                ? ProductWorkspaceResolvedReferenceAddPresentation.Create(
                    workspaceCommits.CurrentEditRevision,
                    !productWorkspaceSession.IsReadOnly,
                    productWorkspaceSession.State!,
                    productDesktopCatalog.Snapshot,
                    workspaceCommits.CurrentReferenceBatchAdditionUndoToken)
                : ProductWorkspaceResolvedReferenceAddPresentation.Unavailable;
        currentWindow.ApplyProductWorkspaceResolvedReferenceAdd(referenceAdder);
        ProductWorkspaceResolvedReferenceRemovalPresentation referenceRemover =
            readModel.IsSuccess
                ? ProductWorkspaceResolvedReferenceRemovalPresentation.Create(
                    workspaceCommits.CurrentEditRevision,
                    !productWorkspaceSession.IsReadOnly,
                    readModel.Snapshot!,
                    workspaceCommits.CurrentReferenceRemovalUndoToken)
                : ProductWorkspaceResolvedReferenceRemovalPresentation.Unavailable;
        currentWindow.ApplyProductWorkspaceResolvedReferenceRemoval(referenceRemover);
        ProductWorkspaceResolvedReferenceReassignmentPresentation referenceReassignment =
            readModel.IsSuccess
                ? ProductWorkspaceResolvedReferenceReassignmentPresentation.Create(
                    workspaceCommits.CurrentEditRevision,
                    !productWorkspaceSession.IsReadOnly,
                    readModel.Snapshot!,
                    workspaceCommits.CurrentReferenceReassignmentUndoToken)
                : ProductWorkspaceResolvedReferenceReassignmentPresentation.Unavailable;
        currentWindow.ApplyProductWorkspaceResolvedReferenceReassignment(
            referenceReassignment);
        currentWindow.ApplyProductWorkspaceLatestUndo(
            ProductWorkspaceLatestUndoPresentation.Create(
                workspaceCommits.CurrentLayoutRecoveryUndoToken,
                workspaceCommits.CurrentContainerRemovalUndoToken,
                workspaceCommits.CurrentReferenceBatchAdditionUndoToken,
                workspaceCommits.CurrentReferenceRemovalUndoToken,
                workspaceCommits.CurrentReferenceReassignmentUndoToken));
        ApplyProductWorkspaceReferenceReview();
    }

    private void ApplyProductWorkspaceReferenceReview()
    {
        MainWindow? currentWindow = window;
        ProductDesktopCatalogSnapshot catalog = productDesktopCatalog.Snapshot;
        if (currentWindow is null || !catalog.IsAuthoritative
            || productWorkspaceSession.State is null)
        {
            currentWindow?.ApplyProductWorkspaceReferenceReview(
                ProductWorkspaceReferenceReviewPresentation.Unavailable);
            return;
        }

        ProductWorkspaceReferenceReviewResult review =
            ProductWorkspaceReferenceReview.Create(
                productWorkspaceSession.State,
                catalog.Generation,
                workspaceCommits.CurrentEditRevision);
        IReadOnlyList<ProductWorkspaceReferenceCandidatePresentation> candidates =
            catalog.Entries
                .Select((entry, index) => new ProductWorkspaceReferenceCandidatePresentation(
                    index + 1,
                    DescribeDesktopItemKind(entry.Kind),
                    catalog.Generation,
                    index))
                .ToArray();
        currentWindow.ApplyProductWorkspaceReferenceReview(
            new(
                review.Snapshot,
                candidates,
                productWorkspaceSession.IsReadOnly,
                review.Error));
    }

    private ProductWorkspaceReferenceCommitResult CommitProductWorkspaceReferenceAction(
        ProductWorkspaceReferenceReviewToken token,
        ProductWorkspaceReferenceAction action,
        bool confirmed,
        ProductWorkspaceReferenceCandidatePresentation? replacement)
    {
        ProductDesktopCatalogSnapshot catalog = productDesktopCatalog.Snapshot;
        if (!catalog.IsAuthoritative || productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceReferenceCommitStatus.InvalidState,
                ProductWorkspaceReferenceGateError.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        DesktopCatalogEntry? replacementEntry = replacement is not null
            && replacement.CatalogGeneration == catalog.Generation
            && replacement.CatalogIndex >= 0
            && replacement.CatalogIndex < catalog.Entries.Count
                ? catalog.Entries[replacement.CatalogIndex]
                : null;

        ProductWorkspaceReferenceCommitResult result = workspaceCommits.Commit(
            StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
            catalog.Generation,
            catalog.Entries,
            new(token, action, confirmed, replacementEntry));
        if (!result.IsAccepted)
        {
            return result;
        }

        ApplyAcceptedProductWorkspaceDocument(result.Document!, catalog);
        return result;
    }

    private ProductWorkspaceResolvedReferenceBatchCommitResult
        CommitProductWorkspaceResolvedReferenceBatch(
            long expectedEditRevision,
            int containerOrdinal,
            IReadOnlyList<ProductWorkspaceResolvedReferenceCandidatePresentation> candidates)
    {
        ProductDesktopCatalogSnapshot catalog = productDesktopCatalog.Snapshot;
        if (!catalog.IsAuthoritative
            || productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly
            || candidates.Count == 0
            || candidates.Any(candidate =>
                candidate.CatalogGeneration != catalog.Generation
                || candidate.CatalogIndex < 0
                || candidate.CatalogIndex >= catalog.Entries.Count))
        {
            return new(
                ProductWorkspaceResolvedReferenceBatchCommitStatus.InvalidRequest,
                ProductWorkspaceEditError.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null,
                null);
        }

        ProductWorkspaceResolvedReferenceBatchCommitResult result =
            workspaceCommits.CommitResolvedReferenceBatch(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                catalog.Generation,
                catalog.Entries,
                new(
                    expectedEditRevision,
                    catalog.Generation,
                    containerOrdinal,
                    candidates.Select(candidate => candidate.CatalogIndex).ToArray()));
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(result.Document!, catalog);
        }

        return result;
    }

    private ProductWorkspaceReferenceBatchAdditionUndoCommitResult
        CommitProductWorkspaceReferenceBatchAdditionUndo(
            ProductWorkspaceReferenceBatchAdditionUndoToken token,
            bool confirmed)
    {
        if (productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceReferenceBatchAdditionUndoCommitStatus.InvalidState,
                ProductWorkspaceReferenceBatchAdditionUndoStatus.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceReferenceBatchAdditionUndoCommitResult result =
            workspaceCommits.CommitReferenceBatchAdditionUndo(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                token,
                confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }

        return result;
    }

    private ProductWorkspaceResolvedReferenceBatchRemovalCommitResult
        CommitProductWorkspaceResolvedReferenceBatchRemoval(
            long expectedEditRevision,
            IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>
                candidates)
    {
        if (productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly
            || candidates.Count == 0
            || candidates.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() != 1)
        {
            return new(
                ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.InvalidRequest,
                ProductWorkspaceEditError.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null,
                null);
        }

        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult result =
            workspaceCommits.CommitResolvedReferenceBatchRemoval(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                new(
                    expectedEditRevision,
                    candidates[0].ContainerOrdinal,
                    candidates.Select(candidate => candidate.ItemOrdinal).ToArray()));
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }

        return result;
    }

    private ProductWorkspaceReferenceRemovalUndoCommitResult
        CommitProductWorkspaceReferenceRemovalUndo(
            ProductWorkspaceReferenceRemovalUndoToken token,
            bool confirmed)
    {
        if (productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceReferenceRemovalUndoCommitStatus.InvalidState,
                ProductWorkspaceReferenceRemovalUndoStatus.Unavailable,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceReferenceRemovalUndoCommitResult result =
            workspaceCommits.CommitReferenceRemovalUndo(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                token,
                confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }

        return result;
    }

    private ProductWorkspaceResolvedReferenceReassignmentCommitResult
        CommitProductWorkspaceResolvedReferenceReassignment(
            long expectedEditRevision,
            IReadOnlyList<
                ProductWorkspaceResolvedReferenceRemovalCandidatePresentation> sources,
            ProductWorkspaceReferenceReassignmentTargetPresentation target)
    {
        if (productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly
            || sources.Count == 0
            || sources.Count > ProductWorkspaceCommitCoordinator
                .MaximumResolvedReferenceReassignmentBatchSize
            || sources.Select(source => source.ContainerOrdinal)
                .Distinct()
                .Count() != 1)
        {
            return new(
                ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                    .InvalidRequest,
                ProductWorkspaceEditError.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null,
                null);
        }

        ProductWorkspaceResolvedReferenceReassignmentCommitResult result =
            workspaceCommits.CommitResolvedReferenceReassignment(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                new(
                    expectedEditRevision,
                    sources[0].ContainerOrdinal,
                    sources.Select(source => source.ItemOrdinal).ToArray(),
                    target.ContainerOrdinal));
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }

        return result;
    }

    private ProductWorkspaceReferenceReassignmentUndoCommitResult
        CommitProductWorkspaceReferenceReassignmentUndo(
            ProductWorkspaceReferenceReassignmentUndoToken token,
            bool confirmed)
    {
        if (productWorkspaceSession.State is null
            || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceReferenceReassignmentUndoCommitStatus.InvalidState,
                ProductWorkspaceReferenceReassignmentUndoStatus.Unavailable,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceReferenceReassignmentUndoCommitResult result =
            workspaceCommits.CommitReferenceReassignmentUndo(
                StampAuthoritativeDisplayTopology(productWorkspaceSession.State),
                token,
                confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }

        return result;
    }

    private ProductWorkspaceContainerCommitResult CommitProductWorkspaceContainerAction(
        ProductWorkspaceContainerCommitAction action,
        long expectedEditRevision,
        int containerOrdinal,
        string name,
        bool? stateValue,
        ProductWorkspaceContainerColorPreset? colorPreset,
        ProductWorkspaceContainerOpacityPreset? opacityPreset,
        ProductWorkspaceContainerPositionPreset? positionPreset,
        ProductWorkspaceContainerSizePreset? sizePreset,
        bool confirmed)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        bool creatingFirstConfiguration =
            action == ProductWorkspaceContainerCommitAction.Create
            && productWorkspaceSession.Status ==
                ProductWorkspaceSessionStatus.NoSavedConfiguration
            && currentConfigurationLoadResult?.Status ==
                ProductConfigurationLoadStatus.Missing;
        if (creatingFirstConfiguration)
        {
            state = ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(),
                Array.Empty<DesktopCatalogEntry>()).State;
        }

        if (state is not null)
        {
            state = StampAuthoritativeDisplayTopology(state);
        }

        if (state is null
            || (productWorkspaceSession.IsReadOnly && !creatingFirstConfiguration))
        {
            return new(
                ProductWorkspaceContainerCommitStatus.InvalidRequest,
                ProductWorkspaceEditError.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        string normalizedName = name.Trim();
        ProductContainerState? newContainer = action ==
            ProductWorkspaceContainerCommitAction.Create
            ? CreateDefaultContainer(normalizedName, state.Containers.Count)
            : null;
        ProductWorkspaceContainerCommitResult result =
            workspaceCommits.CommitContainer(
                state,
                new(
                    action,
                    expectedEditRevision,
                    containerOrdinal,
                    normalizedName,
                    newContainer,
                    stateValue,
                    colorPreset,
                    opacityPreset,
                    positionPreset,
                    sizePreset,
                    confirmed));
        if (!result.IsAccepted)
        {
            return result;
        }

        ApplyAcceptedProductWorkspaceDocument(
            result.Document!,
            productDesktopCatalog.Snapshot);
        return result;
    }

    private ProductWorkspaceContainerRemovalUndoCommitResult
        CommitProductWorkspaceContainerRemovalUndo(
            ProductWorkspaceContainerRemovalUndoToken token,
            bool confirmed)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (state is null || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceContainerRemovalUndoCommitStatus.InvalidState,
                ProductWorkspaceContainerRemovalUndoStatus.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceContainerRemovalUndoCommitResult result =
            workspaceCommits.CommitContainerRemovalUndo(
                StampAuthoritativeDisplayTopology(state),
                token,
                confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }
        else
        {
            ApplyProductWorkspaceSessionViews();
        }

        return result;
    }

    private void ApplyAcceptedProductWorkspaceDocument(
        ProductConfigurationDocument document,
        ProductDesktopCatalogSnapshot catalog)
    {
        currentConfigurationLoadResult = new(
            ProductConfigurationLoadStatus.LoadedPrimary,
            document,
            ProductConfigurationStorageFailure.None,
            ProductConfigurationStorageFailure.None,
            ProductConfigurationError.None,
            ProductConfigurationError.None);
        productWorkspaceSession = ProductWorkspaceSessionLoader.Load(
            currentConfigurationLoadResult,
            CreateWorkspaceCatalogSnapshot(catalog));
        ApplyProductWorkspaceSessionViews();
    }

    private ProductWorkspaceLayoutRecoveryCommitResult
        CommitProductWorkspaceLayoutRecovery(
            ProductWorkspaceLayoutRecoveryReviewToken token,
            bool confirmed)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        if (state is null || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceLayoutRecoveryCommitStatus.InvalidState,
                ProductWorkspaceLayoutRecoveryConfirmationStatus.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceLayoutRecoveryCommitResult result =
            workspaceCommits.CommitLayoutRecovery(
                state,
                topology.IsAuthoritative ? topology.Displays : null,
                topology.IsAuthoritative,
                topology.Generation,
                token,
                confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }
        else
        {
            ApplyProductWorkspaceSessionViews();
        }

        return result;
    }

    private ProductWorkspaceState StampAuthoritativeDisplayTopology(
        ProductWorkspaceState state)
    {
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        return ProductSavedDisplayTopology.StampForSave(
            state,
            topology.Displays,
            topology.IsAuthoritative);
    }

    private ProductWorkspaceLayoutRecoveryUndoCommitResult
        CommitProductWorkspaceLayoutRecoveryUndo(
            ProductWorkspaceLayoutRecoveryUndoToken token,
            bool confirmed)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (state is null || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceLayoutRecoveryUndoCommitStatus.InvalidState,
                ProductWorkspaceLayoutRecoveryUndoStatus.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceLayoutRecoveryUndoCommitResult result =
            workspaceCommits.CommitLayoutRecoveryUndo(state, token, confirmed);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }
        else
        {
            ApplyProductWorkspaceSessionViews();
        }

        return result;
    }

    private static ProductContainerState CreateDefaultContainer(
        string name,
        int existingContainerCount)
    {
        int offset = existingContainerCount % 8;
        return new()
        {
            Id = $"container-{Guid.NewGuid():N}",
            Name = name,
            Appearance = new()
            {
                Color = "#2563EB",
                Opacity = 0.88,
                Collapsed = false,
            },
            Placement = new()
            {
                DisplayKey = "display-unassigned",
                XDip = 32 + (offset * 24),
                YDip = 48 + (offset * 24),
                WidthDip = 360,
                HeightDip = 240,
            },
            Items = Array.Empty<ProductItemReferenceState>(),
        };
    }

    private static string DescribeDesktopItemKind(DesktopItemKind kind) => kind switch
    {
        DesktopItemKind.File => "文件",
        DesktopItemKind.Directory => "文件夹",
        DesktopItemKind.Shortcut => "快捷方式",
        DesktopItemKind.InternetShortcut => "网址快捷方式",
        _ => "未知类型",
    };

    private static ProductWorkspaceCatalogSnapshot CreateWorkspaceCatalogSnapshot(
        ProductDesktopCatalogSnapshot snapshot) =>
        snapshot.IsAuthoritative
            ? ProductWorkspaceCatalogSnapshot.Available(snapshot.Entries)
            : ProductWorkspaceCatalogSnapshot.Unavailable;

    private async Task<ProductConfigurationExportResult?> ExportConfigurationAsync(
        ProductConfigurationExportPlan plan)
    {
        SelectedExportDestination? selected = await PickExportDestinationAsync();
        if (selected is null)
        {
            return null;
        }

        return await configurationStore.ExportAsync(
            plan,
            selected.Path,
            selected.Metadata,
            userConfirmed: true);
    }

    private async Task<ProductConfigurationExportResult?> ExportConfigurationEvidenceAsync(
        ProductConfigurationEvidenceItem item)
    {
        SelectedExportDestination? selected = await PickExportDestinationAsync();
        if (selected is null)
        {
            return null;
        }

        return await configurationStore.ExportEvidenceAsync(
            item,
            selected.Path,
            selected.Metadata,
            userConfirmed: true);
    }

    private async Task<SelectedExportDestination?> PickExportDestinationAsync()
    {
        if (window is null)
        {
            return null;
        }

        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        bool isLocal = !string.IsNullOrWhiteSpace(folder.Path);
        bool isReparsePoint = false;
        if (isLocal)
        {
            try
            {
                isReparsePoint = File.GetAttributes(folder.Path)
                    .HasFlag(System.IO.FileAttributes.ReparsePoint);
            }
            catch (IOException)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.DestinationUnavailable);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ProductConfigurationExportException(
                    ProductConfigurationExportError.DestinationUnavailable);
            }
        }

        return new(
            folder.Path,
            new(
                UserSelected: true,
                IsLocalFileSystem: isLocal,
                IsReparsePoint: isReparsePoint));
    }

    private sealed record SelectedExportDestination(
        string Path,
        ProductConfigurationExportDestination Metadata);

    internal void HandleActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        if (window is null)
        {
            activationPending = true;
            return;
        }

        if (!window.DispatcherQueue.HasThreadAccess)
        {
            _ = window.DispatcherQueue.TryEnqueue(ActivateMainWindow);
            return;
        }

        ActivateMainWindow();
    }

    private void ActivateMainWindow()
    {
        if (window is null)
        {
            activationPending = true;
            return;
        }

        if (window.AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized,
            } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
    }

    private void ProductWorkspaceSaves_SnapshotChanged(
        object? sender,
        LongGrid.Core.Configuration.ProductWorkspaceSaveSnapshot snapshot)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        if (currentWindow.DispatcherQueue.HasThreadAccess)
        {
            currentWindow.ApplyProductWorkspaceSaveState(snapshot);
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () => currentWindow.ApplyProductWorkspaceSaveState(snapshot));
    }

    private async void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (closeAfterDrain)
        {
            return;
        }

        args.Cancel = true;
        if (closingDrainInProgress)
        {
            return;
        }

        closingDrainInProgress = true;
        using CancellationTokenSource timeout = new(ShutdownDrainTimeout);
        ProductWorkspaceSaveCompletionResult completion;
        try
        {
            completion = await productWorkspaceSaves.CompleteAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            closingDrainInProgress = false;
            return;
        }

        if (completion.Status == ProductWorkspaceSaveCompletionStatus.BlockedByFailure)
        {
            window?.ApplyProductWorkspaceSaveState(completion.Snapshot);
            closingDrainInProgress = false;
            return;
        }

        productDesktopCatalog.SnapshotChanged -= ProductDesktopCatalog_SnapshotChanged;
        productDisplayTopology.SnapshotChanged -=
            ProductDisplayTopology_SnapshotChanged;
        productDesktopHostLifecycle.SnapshotChanged -=
            ProductDesktopHostLifecycle_SnapshotChanged;
        if (productDesktopSystemSurfaceEvents is not null)
        {
            productDesktopSystemSurfaceEvents.SurfaceChanged -=
                ProductDesktopSystemSurfaceEvents_SurfaceChanged;
            productDesktopSystemSurfaceEvents.Dispose();
        }

        if (window is not null)
        {
            window.Activated -= MainWindow_Activated;
            window.DesktopKeyboardInteractionRequested -=
                MainWindow_DesktopKeyboardInteractionRequested;
        }

        _ = productDesktopInteraction.Complete(DateTimeOffset.UtcNow);
        await productDesktopHostLifecycle.DisposeAsync();
        await productDisplayTopology.DisposeAsync();
        await productDesktopCatalog.DisposeAsync();
        await productWorkspaceSaves.DisposeAsync();

        closeAfterDrain = true;
        Program.ReleaseMainInstance();
        productWorkspaceSaves.SnapshotChanged -= ProductWorkspaceSaves_SnapshotChanged;
        sender.Closing -= AppWindow_Closing;
        window?.Close();
    }
}
