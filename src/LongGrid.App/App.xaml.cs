using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using LongGrid.Infrastructure.DesktopItems;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    private readonly ProductBoxesSettingsStore boxesSettingsStore;
    private readonly ProductBoxesSettingsController boxesSettingsController;
    private readonly ProductDesktopThumbnailRequestController
        productDesktopThumbnails;
    private readonly ProductWorkspaceSaveController productWorkspaceSaves;
    private readonly ProductWorkspaceCommitCoordinator workspaceCommits;
    private readonly ProductDesktopContainerLayoutInteractionController
        desktopContainerLayoutInteractions;
    private readonly ProductDesktopContainerHeaderCommandController
        desktopContainerHeaderCommands;
    private readonly ProductDesktopContainerDeleteController
        desktopContainerDeletes;
    private readonly ProductWorkspaceCatalogRevisionSynchronizer
        workspaceCatalogRevisions;
    private readonly ProductDesktopCatalogController productDesktopCatalog;
    private readonly ProductDisplayTopologyController productDisplayTopology;
    private readonly ProductDesktopHostLifecycleController productDesktopHostLifecycle;
    private readonly ProductDesktopInteractionDevelopmentController
        productDesktopInteraction;
    private readonly WindowsProductDesktopInteractionSystemSurfaceEventSource?
        productDesktopSystemSurfaceEvents;
    private readonly ProductThumbnailWorkerLifecycleController
        productThumbnailWorker;
    private readonly ProductResourceTelemetryServer? productResourceTelemetry;
    private readonly ProductPf002AppEvidenceSession? pf002AppEvidenceSession;
    private readonly ProductBoxR1ActivationEvidenceSession?
        boxR1ActivationEvidenceSession;
    private readonly ProductDesktopFirstStartupEvidenceSession?
        desktopFirstStartupEvidenceSession;
    private readonly ProductUiR1eEvidenceSession? uiR1eEvidenceSession;
    private readonly ProductBoxesRuntimeEnableEvidenceSession?
        boxesRuntimeEnableEvidenceSession;
    private readonly bool backgroundStartup;
    private ProductWorkspaceSessionSnapshot productWorkspaceSession =
        ProductWorkspaceSessionSnapshot.Initial;
    private ProductConfigurationLoadResult? currentConfigurationLoadResult;
    private PendingControlCenterContainerEdit? pendingControlCenterContainerEdit;
    private MainWindow? window;
    private bool closeAfterDrain;
    private bool closingDrainInProgress;
    private bool activationPending;
    private ProductDesktopWorkspaceCreatePreviewSession?
        desktopWorkspaceCreatePreview;
    private DesktopWorkspaceCreatePreviewWindow?
        desktopWorkspaceCreatePreviewWindow;
    private ProductDesktopWorkspaceCreatePublicationToken?
        desktopWorkspaceCreatePublication;
    private CancellationTokenSource? desktopThumbnailRefreshCancellation;
    private long desktopThumbnailRefreshGeneration;
    private long desktopHostPresentationGeneration;
    private readonly Dictionary<string, int> desktopItemViewportStarts =
        new(StringComparer.Ordinal);
    private readonly ProductDesktopItemOpenController desktopItemOpens = new();

    public App()
    {
        InitializeComponent();
        string[] commandLineArguments = Environment.GetCommandLineArgs();
        ProductExplorerCreateActivationDecision explorerCreateActivation =
            ProductExplorerCreateActivation.Parse(
                commandLineArguments,
                DateTimeOffset.UtcNow);
        backgroundStartup = explorerCreateActivation.IsCommand
            || commandLineArguments.Any(argument =>
            string.Equals(
                argument,
                "--background",
                StringComparison.OrdinalIgnoreCase));
        _ = QueueExplorerCreateActivation(explorerCreateActivation);
        pf002AppEvidenceSession =
            ProductPf002AppEvidenceSession.TryCreateFromEnvironment();
        boxR1ActivationEvidenceSession =
            ProductBoxR1ActivationEvidenceSession.TryCreateFromEnvironment();
        desktopFirstStartupEvidenceSession =
            ProductDesktopFirstStartupEvidenceSession.TryCreateFromEnvironment();
        uiR1eEvidenceSession = ProductUiR1eEvidenceSession.TryCreateFromEnvironment();
        boxesRuntimeEnableEvidenceSession =
            ProductBoxesRuntimeEnableEvidenceSession.TryCreateFromEnvironment();
        int evidenceSessionCount =
            (pf002AppEvidenceSession is null ? 0 : 1)
            + (boxR1ActivationEvidenceSession is null ? 0 : 1)
            + (desktopFirstStartupEvidenceSession is null ? 0 : 1)
            + (uiR1eEvidenceSession is null ? 0 : 1)
            + (boxesRuntimeEnableEvidenceSession is null ? 0 : 1);
        if (evidenceSessionCount > 1)
        {
            throw new InvalidOperationException(
                "Product App evidence sessions cannot run together.");
        }
        string configurationDirectory = pf002AppEvidenceSession?.DirectoryPath
            ?? boxR1ActivationEvidenceSession?.DirectoryPath
            ?? desktopFirstStartupEvidenceSession?.DirectoryPath
            ?? uiR1eEvidenceSession?.DirectoryPath
            ?? boxesRuntimeEnableEvidenceSession?.DirectoryPath
            ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "LongGrid");
        configurationStore = new ProductConfigurationStore(configurationDirectory);
        boxesSettingsStore = new(configurationDirectory);
        boxesSettingsController = new(boxesSettingsStore);
        productDesktopThumbnails = new();
        var saveWorkflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(configurationStore));
        productWorkspaceSaves = new(saveWorkflow);
        workspaceCommits = new(productWorkspaceSaves);
        desktopContainerLayoutInteractions = new(workspaceCommits);
        desktopContainerHeaderCommands = new(
            workspaceCommits,
            productWorkspaceSaves);
        desktopContainerDeletes = new(
            workspaceCommits,
            productWorkspaceSaves);
        workspaceCatalogRevisions = new(workspaceCommits);
        productDesktopCatalog = new(
            ProductDesktopCatalogReader.CreateForCurrentUser());
        productDisplayTopology = new(
            ProductDisplayTopologyReader.CreateForCurrentSession());
        ProductDesktopHostFeatureDecision desktopHostFeature =
            ProductDesktopHostFeaturePolicy.Evaluate(
                Environment.GetEnvironmentVariable(
                    ProductDesktopHostFeaturePolicy.EnvironmentVariableName),
                Environment.GetEnvironmentVariable(
                    ProductDesktopHostFeaturePolicy
                        .EmergencyDisableEnvironmentVariableName));
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
            productDesktopIntentConsumption,
            userEnabled: false);
        ProductResourceTelemetryFeatureDecision telemetryFeature =
            ProductResourceTelemetryFeaturePolicy.Evaluate(
                desktopHostFeature,
                Environment.GetEnvironmentVariable(
                    ProductResourceTelemetryFeaturePolicy
                        .PipeEnvironmentVariableName),
                Environment.GetEnvironmentVariable(
                    ProductResourceTelemetryFeaturePolicy
                        .SessionEnvironmentVariableName));
        productThumbnailWorker =
            ProductThumbnailWorkerLifecycleController.Start(telemetryFeature);
        productResourceTelemetry = ProductResourceTelemetryServer.TryStart(
            telemetryFeature,
            CaptureProductResourceTelemetry,
            productThumbnailWorker.Dispose);
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
            CaptureAnonymousInteractionEvidenceAsync,
            ExportConfigurationEvidenceAsync,
            item => configurationStore.RemoveEvidenceAsync(item, userConfirmed: true),
            () => productWorkspaceSaves.Retry(),
            RefreshProductDesktopCatalogAsync,
            RefreshProductWorkspaceFolderContentsAsync,
            CommitProductWorkspaceReferenceAction,
            CommitProductWorkspaceResolvedReferenceBatch,
            CommitProductWorkspaceReferenceBatchAdditionUndo,
            CommitProductWorkspaceResolvedReferenceBatchRemoval,
            RequestProductWorkspaceSelectedReferenceCreate,
            CommitProductWorkspaceReferenceRemovalUndo,
            CommitProductWorkspaceResolvedReferenceReassignment,
            CommitProductWorkspaceReferenceReassignmentUndo,
            CommitProductWorkspaceContainerAction,
            CommitProductWorkspaceContainerFolderBinding,
            CommitProductWorkspaceContainerRemovalUndo,
            CommitProductWorkspaceContainerEditUndo,
            CommitProductWorkspaceLayoutRecovery,
            CommitProductWorkspaceLayoutRecoveryUndo);
        productWorkspaceSaves.SnapshotChanged += ProductWorkspaceSaves_SnapshotChanged;
        productDesktopCatalog.SnapshotChanged += ProductDesktopCatalog_SnapshotChanged;
        productDisplayTopology.SnapshotChanged +=
            ProductDisplayTopology_SnapshotChanged;
        productDesktopHostLifecycle.SnapshotChanged +=
            ProductDesktopHostLifecycle_SnapshotChanged;
        productDesktopHostLifecycle.BindWorkspaceCreate(
            RequestDesktopWorkspaceCreate);
        productDesktopHostLifecycle.BindContainerLayout(
            RequestDesktopContainerLayout);
        productDesktopHostLifecycle.BindContainerHeaderCommand(
            RequestDesktopContainerHeaderCommand);
        productDesktopHostLifecycle.BindContainerMenu(
            GetDesktopContainerMenuAvailability,
            RequestDesktopContainerMenuNavigation);
        productDesktopHostLifecycle.BindItemViewport(
            RequestDesktopItemViewport);
        productDesktopHostLifecycle.BindItemOpen(RequestDesktopItemOpen);
        productDesktopHostLifecycle.BindExplorerReferenceDrop(
            RequestDesktopExplorerReferenceDrop);
        folderContentWatcher.Invalidated +=
            ProductWorkspaceFolderContentWatcher_Invalidated;
        window.DesktopKeyboardInteractionRequested +=
            MainWindow_DesktopKeyboardInteractionRequested;
        window.BoxesEnabledChangeRequested +=
            MainWindow_BoxesEnabledChangeRequested;
        window.ThumbnailsEnabledChangeRequested +=
            MainWindow_ThumbnailsEnabledChangeRequested;
        window.SingleClickOpenChangeRequested +=
            MainWindow_SingleClickOpenChangeRequested;
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
        if (pf002AppEvidenceSession is not null)
        {
            window.Activate();
            _ = RunPf002AppEvidenceSessionAsync(pf002AppEvidenceSession);
        }
        else if (boxR1ActivationEvidenceSession is not null)
        {
            _ = RunBoxR1ActivationEvidenceSessionAsync(
                boxR1ActivationEvidenceSession);
        }
        else if (boxesRuntimeEnableEvidenceSession is not null)
        {
            _ = RunBoxesRuntimeEnableEvidenceSessionAsync(
                boxesRuntimeEnableEvidenceSession);
        }
        else if (uiR1eEvidenceSession is not null)
        {
            window.Activate();
            _ = RunUiR1eEvidenceSessionAsync(uiR1eEvidenceSession);
        }
        else
        {
            _ = InitializeDesktopFirstStartupAsync();
        }

        if (activationPending)
        {
            activationPending = false;
            ActivateMainWindow();
        }
    }

    private async Task RunUiR1eEvidenceSessionAsync(
        ProductUiR1eEvidenceSession evidence)
    {
        object result;
        IReadOnlyList<ProductUiR1eRenderCapture>? captures = null;
        try
        {
            await LoadBoxesSettingsAsync();
            await LoadConfigurationStartupStateAsync();
            MainWindow currentWindow = window ?? throw new InvalidOperationException(
                "UI-R1E evidence requires the formal main window.");
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!currentWindow.IsProductXamlReady && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            ProductUiR1eRenderResult render =
                await currentWindow.CaptureUiR1eRenderAsync();
            result = render.Evidence;
            captures = render.Captures;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            result = new
            {
                SchemaVersion = 1,
                Purpose = "UiR1eRealXamlRenderingEvidence",
                Expected = "Pass",
                Actual = new
                {
                    Error = exception.GetType().Name,
                    ErrorDetail = exception.Message,
                },
                Difference = "EvidenceSessionFailed",
                Outcome = "Fail",
            };
        }

        try
        {
            await evidence.WriteResultAsync(result, captures);
        }
        finally
        {
            window?.Close();
        }
    }

    private async Task InitializeDesktopFirstStartupAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadBoxesSettingsAsync(),
                LoadConfigurationStartupStateAsync(),
                RefreshProductDesktopCatalogAsync(),
                RefreshProductDisplayTopologyAsync());

            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline
                && productDesktopHostLifecycle.Snapshot.Status
                    == ProductDesktopHostLifecycleStatus.AwaitingHost)
            {
                await Task.Delay(100);
            }

            ProductConfigurationStartupState? configuration =
                currentConfigurationLoadResult is null
                    ? null
                    : ProductConfigurationStartupState.FromLoadResult(
                        currentConfigurationLoadResult);
            ProductDesktopFirstStartupDecision decision =
                ProductDesktopFirstStartupPolicy.Evaluate(
                    new(
                        EvidenceSession: false,
                        RedirectedActivationPending: activationPending,
                        ExplicitUserLaunch: !backgroundStartup,
                        BoxesEnabled: boxesSettingsController.Current.BoxesEnabled,
                        ConfigurationRequiresAttention:
                            configuration?.RequiresRecoveryNotice != false,
                        HostReadiness: MapDesktopFirstHostReadiness(
                            productDesktopHostLifecycle.Snapshot.Status)));
            if (decision.ActivateControlCenter)
            {
                ActivateMainWindow();
            }

            _ = TryDispatchExplorerCreateActivation();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            ActivateMainWindow();
        }
    }

    private async Task RunBoxesRuntimeEnableEvidenceSessionAsync(
        ProductBoxesRuntimeEnableEvidenceSession evidence)
    {
        try
        {
            await Task.WhenAll(
                LoadBoxesSettingsAsync(),
                LoadConfigurationStartupStateAsync(),
                RefreshProductDesktopCatalogAsync(),
                RefreshProductDisplayTopologyAsync());

            ProductDesktopHostLifecycleSnapshot initial =
                await WaitForRuntimeEnableHostAsync(
                    requireOwnedWindow: true,
                    TimeSpan.FromSeconds(10));
            await evidence.WriteJsonAsync(
                evidence.InitialReadyPath,
                new
                {
                    Status = initial.Status.ToString(),
                    initial.OwnedWindowCount,
                    initial.NativeHostConnected,
                    initial.PassiveWindowContractAttested,
                });
            await evidence.WaitForAckAsync(
                evidence.InitialObservedAckPath,
                TimeSpan.FromSeconds(5));

            ProductDesktopHostLifecycleSnapshot disabled =
                await ChangeBoxesEnabledAsync(false);
            if (disabled.Status != ProductDesktopHostLifecycleStatus.DisabledByUser
                || disabled.OwnedWindowCount != 0)
            {
                throw new InvalidOperationException(
                    "The product boxes change path did not disable DesktopHost.");
            }
            await evidence.WriteJsonAsync(
                evidence.DisabledReadyPath,
                new
                {
                    Status = disabled.Status.ToString(),
                    disabled.OwnedWindowCount,
                    disabled.NativeHostConnected,
                });
            await evidence.WaitForAckAsync(
                evidence.DisabledObservedAckPath,
                TimeSpan.FromSeconds(5));

            long started = Stopwatch.GetTimestamp();
            ProductDesktopHostLifecycleSnapshot restored =
                await ChangeBoxesEnabledAsync(true);
            if (restored.OwnedWindowCount == 0)
            {
                ApplyProductDesktopHostProjection(productDisplayTopology.Snapshot);
                restored = await WaitForRuntimeEnableHostAsync(
                    requireOwnedWindow: true,
                    TimeSpan.FromSeconds(2));
            }
            int elapsedMilliseconds = checked((int)Math.Ceiling(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            const int budgetMilliseconds = 1000;
            int differenceMilliseconds =
                elapsedMilliseconds - budgetMilliseconds;
            bool passed = restored.OwnedWindowCount > 0
                && restored.NativeHostConnected
                && restored.PassiveWindowContractAttested
                && differenceMilliseconds <= 0;
            await evidence.WriteJsonAsync(
                evidence.ResultPath,
                new
                {
                    SchemaVersion = 1,
                    Purpose = "Pf001RuntimeBoxesEnableProductEvidence",
                    Expected = new
                    {
                        InitialOwnedWindowCountMinimum = 1,
                        DisabledOwnedWindowCount = 0,
                        RestoredOwnedWindowCountMinimum = 1,
                        RuntimeBoxesEnableBudgetMilliseconds =
                            budgetMilliseconds,
                    },
                    Actual = new
                    {
                        InitialOwnedWindowCount = initial.OwnedWindowCount,
                        DisabledOwnedWindowCount = disabled.OwnedWindowCount,
                        RestoredOwnedWindowCount = restored.OwnedWindowCount,
                        restored.NativeHostConnected,
                        restored.PassiveWindowContractAttested,
                        RuntimeBoxesEnableMilliseconds = elapsedMilliseconds,
                        RuntimeBoxesEnableDifferenceMilliseconds =
                            differenceMilliseconds,
                    },
                    Difference = passed
                        ? "None"
                        : differenceMilliseconds > 0
                            ? $"RuntimeBoxesEnableExceededBy{differenceMilliseconds}Milliseconds"
                            : "RuntimeBoxesEnableWindowContractMismatch",
                    Outcome = passed ? "Pass" : "Fail",
                });
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            await evidence.WriteJsonAsync(
                evidence.ResultPath,
                new
                {
                    SchemaVersion = 1,
                    Purpose = "Pf001RuntimeBoxesEnableProductEvidence",
                    Difference = "ProductEvidenceSessionFailed",
                    Outcome = "Fail",
                    FailureType = exception.GetType().Name,
                });
        }
    }

    private async Task<ProductDesktopHostLifecycleSnapshot>
        WaitForRuntimeEnableHostAsync(
            bool requireOwnedWindow,
            TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ProductDesktopHostLifecycleSnapshot snapshot =
                productDesktopHostLifecycle.Snapshot;
            bool readyStatus = snapshot.Status is
                ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                or ProductDesktopHostLifecycleStatus.ReadyReadOnly;
            if (readyStatus
                && (!requireOwnedWindow || snapshot.OwnedWindowCount > 0))
            {
                return snapshot;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            "The product DesktopHost did not reach the runtime-enable evidence state.");
    }

    private static ProductDesktopFirstHostReadiness MapDesktopFirstHostReadiness(
        ProductDesktopHostLifecycleStatus status) => status switch
        {
            ProductDesktopHostLifecycleStatus.AwaitingHost =>
                ProductDesktopFirstHostReadiness.AwaitingHost,
            ProductDesktopHostLifecycleStatus.AwaitingWorkspace =>
                ProductDesktopFirstHostReadiness.AwaitingWorkspace,
            ProductDesktopHostLifecycleStatus.SuspendedSystemSurface =>
                ProductDesktopFirstHostReadiness.SuspendedSystemSurface,
            ProductDesktopHostLifecycleStatus.ReadyReadOnly =>
                ProductDesktopFirstHostReadiness.Ready,
            ProductDesktopHostLifecycleStatus.DisabledByUser =>
                ProductDesktopFirstHostReadiness.DisabledByUser,
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy =>
                ProductDesktopFirstHostReadiness.DisabledBySafetyPolicy,
            ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology =>
                ProductDesktopFirstHostReadiness.SuspendedUnsafeTopology,
            ProductDesktopHostLifecycleStatus.Faulted =>
                ProductDesktopFirstHostReadiness.Faulted,
            ProductDesktopHostLifecycleStatus.Completed =>
                ProductDesktopFirstHostReadiness.Faulted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown DesktopHost lifecycle status."),
        };

    private async Task LoadConfigurationStartupStateAsync()
    {
        await Task.Yield();
        ProductConfigurationLoadResult loadResult =
            await configurationStore.LoadAsync();
        ApplyProductConfigurationLoadResult(loadResult);
    }

    private async Task RunPf002AppEvidenceSessionAsync(
        ProductPf002AppEvidenceSession evidence)
    {
        object result;
        string stage = "WaitingForReadiness";
        evidence.RecordStage(stage);
        try
        {
            stage = "InitializingEvidenceDependencies";
            evidence.RecordStage(stage);
            await LoadBoxesSettingsAsync();
            await LoadConfigurationStartupStateAsync();
            await RefreshProductDisplayTopologyAsync();

            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                ProductDesktopHostLifecycleStatus hostStatus =
                    productDesktopHostLifecycle.Snapshot.Status;
                if (window?.IsProductXamlReady == true
                    && currentConfigurationLoadResult is not null
                    && productDisplayTopology.Snapshot.IsAuthoritative
                    && hostStatus is (
                        ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                        or ProductDesktopHostLifecycleStatus.ReadyReadOnly))
                {
                    break;
                }

                await Task.Delay(100);
            }

            MainWindow currentWindow = window ?? throw new InvalidOperationException(
                "PF-002 App evidence requires the formal main window.");
            stage = "HidingFormalWindowFromKnownUnsafeUiaRuntime";
            evidence.RecordStage(stage);
            currentWindow.AppWindow.Hide();
            await Task.Delay(250);
            stage = "ReadingFormalTopology";
            evidence.RecordStage(stage);
            ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
            stage = "ResolvingFormalWorkspace";
            evidence.RecordStage(stage);
            ProductWorkspaceState beforeState = ResolveDesktopWorkspaceCreateState()
                ?? throw new InvalidOperationException(
                    "PF-002 App evidence requires a writable workspace.");
            stage = "SelectingFormalDisplay";
            evidence.RecordStage(stage);
            DisplayTopologyNode display = topology.IsAuthoritative
                ? topology.Displays.FirstOrDefault(candidate => candidate.IsPrimary)
                    ?? (topology.Displays.Count > 0 ? topology.Displays[0] : null)
                    ?? throw new InvalidOperationException(
                        "PF-002 App evidence requires one display.")
                : throw new InvalidOperationException(
                    "PF-002 App evidence requires authoritative topology.");
            stage = "CheckingFormalHost";
            evidence.RecordStage(stage);
            ProductDesktopHostLifecycleStatus readyHostStatus =
                productDesktopHostLifecycle.Snapshot.Status;
            if (readyHostStatus is not (
                    ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                    or ProductDesktopHostLifecycleStatus.ReadyReadOnly))
            {
                throw new InvalidOperationException(
                    "PF-002 App evidence requires the formal DesktopHost.");
            }

            ProductConfigurationLoadResult diskBefore =
                await configurationStore.LoadAsync();
            stage = "CancellingFormalPreview";
            evidence.RecordStage(stage);
            long cancelRevision = workspaceCommits.CurrentEditRevision;
            var cancelRequest = new ProductDesktopWorkspaceCreateRequest(
                ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
                display.StableId,
                cancelRevision,
                topology.Generation,
                SourceAttested: true,
                IsInjected: false,
                IsAutoRepeat: false);
            await RunDesktopWorkspaceCreatePreviewAsync(
                currentWindow,
                cancelRequest);
            ProductWorkspaceState afterCancelState =
                ResolveDesktopWorkspaceCreateState()
                ?? throw new InvalidOperationException(
                    "PF-002 cancel evidence lost the writable workspace.");
            ProductConfigurationLoadResult diskAfterCancel =
                await configurationStore.LoadAsync();

            stage = "ConfirmingFormalPreview";
            evidence.RecordStage(stage);
            topology = productDisplayTopology.Snapshot;
            long createRevision = workspaceCommits.CurrentEditRevision;
            var createRequest = new ProductDesktopWorkspaceCreateRequest(
                ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
                display.StableId,
                createRevision,
                topology.Generation,
                SourceAttested: true,
                IsInjected: false,
                IsAutoRepeat: false);
            await RunDesktopWorkspaceCreatePreviewAsync(
                currentWindow,
                createRequest);
            ProductWorkspaceState afterCreateState = productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-002 confirm evidence did not publish a workspace.");
            long createSaveRevision =
                productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalCreateSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot createSave =
                await WaitForProductWorkspaceSaveAsync(createSaveRevision);
            stage = "ReloadingFormalCreateStore";
            evidence.RecordStage(stage);
            ProductConfigurationLoadResult diskAfterCreate =
                await configurationStore.LoadAsync();

            stage = "RemovingCreatedContainerThroughFormalAppCommit";
            evidence.RecordStage(stage);
            long removalRevision = workspaceCommits.CurrentEditRevision;
            ProductWorkspaceContainerCommitResult removal =
                CommitProductWorkspaceContainerAction(
                    ProductWorkspaceContainerCommitAction.Remove,
                    removalRevision,
                    containerOrdinal: 1,
                    name: string.Empty,
                    stateValue: null,
                    colorPreset: null,
                    opacityPreset: null,
                    positionPreset: null,
                    sizePreset: null,
                    titleVisibility: null,
                    titleDoubleClickAction: null,
                    confirmed: true);
            ProductWorkspaceState afterRemovalState = productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-002 removal evidence lost the writable workspace.");
            long removalSaveRevision =
                productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalRemovalSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot removalSave =
                await WaitForProductWorkspaceSaveAsync(removalSaveRevision);
            stage = "ReloadingFormalRemovalStore";
            evidence.RecordStage(stage);
            ProductConfigurationLoadResult diskAfterRemoval =
                await configurationStore.LoadAsync();

            stage = "PublishingFormalLatestUndoSelection";
            evidence.RecordStage(stage);
            ProductWorkspaceLatestUndoPresentation latestUndo =
                ProductWorkspaceLatestUndoPresentation.Create(
                    workspaceCommits.CurrentLayoutRecoveryUndoToken,
                    workspaceCommits.CurrentContainerRemovalUndoToken,
                    workspaceCommits.CurrentReferenceBatchAdditionUndoToken,
                    workspaceCommits.CurrentSelectedReferenceContainerUndoToken,
                    workspaceCommits.CurrentReferenceRemovalUndoToken,
                    workspaceCommits.CurrentReferenceReassignmentUndoToken);
            currentWindow.ApplyProductWorkspaceLatestUndo(latestUndo);
            stage = "ExecutingFormalLatestUndo";
            evidence.RecordStage(stage);
            ProductWorkspaceLatestUndoKind executedUndoKind =
                currentWindow.ExecuteProductWorkspaceLatestUndoForEvidence();
            ProductWorkspaceState afterUndoState = productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-002 latest undo evidence lost the writable workspace.");
            long undoSaveRevision = productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalUndoSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot undoSave =
                await WaitForProductWorkspaceSaveAsync(undoSaveRevision);
            stage = "ReloadingFormalUndoStore";
            evidence.RecordStage(stage);
            ProductConfigurationLoadResult diskAfterUndo =
                await configurationStore.LoadAsync();

            stage = "DrivingFormalContainerLayout";
            evidence.RecordStage(stage);
            topology = productDisplayTopology.Snapshot;
            long layoutRevision = workspaceCommits.CurrentEditRevision;
            ProductContainerState layoutContainer = afterUndoState.Containers.Single();
            double layoutOriginalX = layoutContainer.Placement.XDip;
            double layoutOriginalY = layoutContainer.Placement.YDip;
            ProductDesktopContainerLayoutRequest LayoutRequest(
                ProductDesktopContainerLayoutInputPhase phase,
                double deltaX,
                double deltaY) =>
                new(
                    phase,
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    layoutContainer.Id,
                    layoutContainer.Placement.DisplayKey,
                    layoutRevision,
                    topology.Generation,
                    deltaX,
                    deltaY,
                    SnapEnabled: false,
                    ShiftPressed: false,
                    ProductDesktopContainerLayoutCancellationReason.None);
            bool layoutBegin = RequestDesktopContainerLayout(LayoutRequest(
                ProductDesktopContainerLayoutInputPhase.Begin,
                0,
                0));
            bool layoutUpdate = RequestDesktopContainerLayout(LayoutRequest(
                ProductDesktopContainerLayoutInputPhase.Update,
                32,
                16));
            bool layoutComplete = RequestDesktopContainerLayout(LayoutRequest(
                ProductDesktopContainerLayoutInputPhase.Complete,
                32,
                16));
            ProductWorkspaceState afterLayoutState = productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-003D2 layout evidence lost the writable workspace.");
            long layoutSaveRevision = productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalLayoutSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot layoutSave =
                await WaitForProductWorkspaceSaveAsync(layoutSaveRevision);
            stage = "ReloadingFormalLayoutStore";
            evidence.RecordStage(stage);
            ProductConfigurationLoadResult diskAfterLayout =
                await configurationStore.LoadAsync();
            stage = "DrivingFormalKeyboardFineMove";
            evidence.RecordStage(stage);
            long keyboardMoveRevision = workspaceCommits.CurrentEditRevision;
            bool KeyboardMoveRequest(
                ProductDesktopContainerLayoutInputPhase phase) =>
                RequestDesktopContainerLayout(new(
                    phase,
                    ProductWorkspaceContainerLayoutGestureKind.Move,
                    layoutContainer.Id,
                    layoutContainer.Placement.DisplayKey,
                    keyboardMoveRevision,
                    topology.Generation,
                    phase == ProductDesktopContainerLayoutInputPhase.Begin
                        ? 0
                        : 1,
                    0,
                    SnapEnabled: false,
                    ShiftPressed: false,
                    ProductDesktopContainerLayoutCancellationReason.None));
            bool keyboardMoveBegin = KeyboardMoveRequest(
                ProductDesktopContainerLayoutInputPhase.Begin);
            bool keyboardMoveUpdate = KeyboardMoveRequest(
                ProductDesktopContainerLayoutInputPhase.Update);
            bool keyboardMoveComplete = KeyboardMoveRequest(
                ProductDesktopContainerLayoutInputPhase.Complete);
            ProductWorkspaceState afterKeyboardMoveState =
                productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-003D3 keyboard move evidence lost the writable workspace.");
            long keyboardMoveSaveRevision =
                productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalKeyboardMoveSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot keyboardMoveSave =
                await WaitForProductWorkspaceSaveAsync(
                    keyboardMoveSaveRevision);
            stage = "DrivingFormalKeyboardLargeResize";
            evidence.RecordStage(stage);
            long keyboardResizeRevision = workspaceCommits.CurrentEditRevision;
            bool KeyboardResizeRequest(
                ProductDesktopContainerLayoutInputPhase phase) =>
                RequestDesktopContainerLayout(new(
                    phase,
                    ProductWorkspaceContainerLayoutGestureKind.ResizeRight,
                    layoutContainer.Id,
                    layoutContainer.Placement.DisplayKey,
                    keyboardResizeRevision,
                    topology.Generation,
                    phase == ProductDesktopContainerLayoutInputPhase.Begin
                        ? 0
                        : 8,
                    0,
                    SnapEnabled: true,
                    ShiftPressed: true,
                    ProductDesktopContainerLayoutCancellationReason.None));
            bool keyboardResizeBegin = KeyboardResizeRequest(
                ProductDesktopContainerLayoutInputPhase.Begin);
            bool keyboardResizeUpdate = KeyboardResizeRequest(
                ProductDesktopContainerLayoutInputPhase.Update);
            bool keyboardResizeComplete = KeyboardResizeRequest(
                ProductDesktopContainerLayoutInputPhase.Complete);
            ProductWorkspaceState afterKeyboardResizeState =
                productWorkspaceSession.State
                ?? throw new InvalidOperationException(
                    "PF-003D3 keyboard resize evidence lost the writable workspace.");
            long keyboardResizeSaveRevision =
                productWorkspaceSaves.Snapshot.CurrentRevision;
            stage = "WaitingForFormalKeyboardResizeSave";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveSnapshot keyboardResizeSave =
                await WaitForProductWorkspaceSaveAsync(
                    keyboardResizeSaveRevision);
            stage = "ReloadingFormalKeyboardLayoutStore";
            evidence.RecordStage(stage);
            ProductConfigurationLoadResult diskAfterKeyboardLayout =
                await configurationStore.LoadAsync();
            bool crossDisplayHardwareAvailable = topology.Displays.Count >= 2;
            bool crossDisplayBegin = false;
            bool crossDisplayUpdate = false;
            bool crossDisplayComplete = false;
            long crossDisplaySavedRevision =
                keyboardResizeSave.SavedRevision;
            ProductWorkspaceState afterCrossDisplayState =
                afterKeyboardResizeState;
            ProductConfigurationLoadResult diskAfterCrossDisplay =
                diskAfterKeyboardLayout;
            string crossSourcePlacementKey =
                afterKeyboardResizeState.Containers.Single()
                    .Placement.DisplayKey;
            uint? crossDisplaySourceDpi = null;
            uint? crossDisplayTargetDpi = null;
            if (crossDisplayHardwareAvailable)
            {
                stage = "DrivingFormalHardwareCrossDisplayMove";
                evidence.RecordStage(stage);
                ProductContainerPlacementState crossSourcePlacement =
                    afterKeyboardResizeState.Containers.Single().Placement;
                DisplayTopologyNode crossSource = topology.Displays.Single(
                    display => string.Equals(
                        display.StableId,
                        crossSourcePlacement.DisplayKey,
                        StringComparison.Ordinal));
                DisplayTopologyNode crossTarget = topology.Displays
                    .Where(display => !string.Equals(
                        display.StableId,
                        crossSource.StableId,
                        StringComparison.Ordinal))
                    .OrderByDescending(display => display.WorkArea.Width)
                    .First();
                double targetScale = crossTarget.EffectiveDpi / 96d;
                double targetWorkWidth =
                    crossTarget.WorkArea.Width / targetScale;
                double targetWorkHeight =
                    crossTarget.WorkArea.Height / targetScale;
                double targetXDip = Math.Clamp(
                    48,
                    0,
                    Math.Max(
                        0,
                        targetWorkWidth - crossSourcePlacement.WidthDip));
                double targetYDip = Math.Clamp(
                    48,
                    0,
                    Math.Max(
                        0,
                        targetWorkHeight - crossSourcePlacement.HeightDip));
                const double pointerOffsetDip = 20;
                double sourceScale = crossSource.EffectiveDpi / 96d;
                int startPointerX = checked((int)Math.Round(
                    crossSource.WorkArea.Left
                    + ((crossSourcePlacement.XDip + pointerOffsetDip)
                        * sourceScale)));
                int startPointerY = checked((int)Math.Round(
                    crossSource.WorkArea.Top
                    + ((crossSourcePlacement.YDip + pointerOffsetDip)
                        * sourceScale)));
                int targetPointerX = checked((int)Math.Round(
                    crossTarget.WorkArea.Left
                    + ((targetXDip + pointerOffsetDip) * targetScale)));
                int targetPointerY = checked((int)Math.Round(
                    crossTarget.WorkArea.Top
                    + ((targetYDip + pointerOffsetDip) * targetScale)));
                long crossDisplayRevision = workspaceCommits.CurrentEditRevision;
                ProductDesktopContainerLayoutRequest CrossDisplayRequest(
                    ProductDesktopContainerLayoutInputPhase phase) =>
                    new(
                        phase,
                        ProductWorkspaceContainerLayoutGestureKind.Move,
                        layoutContainer.Id,
                        crossSource.StableId,
                        crossDisplayRevision,
                        topology.Generation,
                        phase == ProductDesktopContainerLayoutInputPhase.Begin
                            ? 0
                            : (targetPointerX - startPointerX) / sourceScale,
                        phase == ProductDesktopContainerLayoutInputPhase.Begin
                            ? 0
                            : (targetPointerY - startPointerY) / sourceScale,
                        SnapEnabled: false,
                        ShiftPressed: false,
                        ProductDesktopContainerLayoutCancellationReason.None,
                        phase == ProductDesktopContainerLayoutInputPhase.Begin
                            ? startPointerX
                            : targetPointerX,
                        phase == ProductDesktopContainerLayoutInputPhase.Begin
                            ? startPointerY
                            : targetPointerY);
                crossDisplayBegin = RequestDesktopContainerLayout(
                    CrossDisplayRequest(
                        ProductDesktopContainerLayoutInputPhase.Begin));
                crossDisplayUpdate = RequestDesktopContainerLayout(
                    CrossDisplayRequest(
                        ProductDesktopContainerLayoutInputPhase.Update));
                crossDisplayComplete = RequestDesktopContainerLayout(
                    CrossDisplayRequest(
                        ProductDesktopContainerLayoutInputPhase.Complete));
                afterCrossDisplayState = productWorkspaceSession.State
                    ?? throw new InvalidOperationException(
                        "PF-003D4 cross-display evidence lost the writable workspace.");
                long crossDisplaySaveRevision =
                    productWorkspaceSaves.Snapshot.CurrentRevision;
                stage = "WaitingForFormalCrossDisplaySave";
                evidence.RecordStage(stage);
                ProductWorkspaceSaveSnapshot crossDisplaySave =
                    await WaitForProductWorkspaceSaveAsync(
                        crossDisplaySaveRevision);
                crossDisplaySavedRevision = crossDisplaySave.SavedRevision;
                stage = "ReloadingFormalCrossDisplayStore";
                evidence.RecordStage(stage);
                diskAfterCrossDisplay = await configurationStore.LoadAsync();
                crossDisplaySourceDpi = crossSource.EffectiveDpi;
                crossDisplayTargetDpi = crossTarget.EffectiveDpi;
            }
            stage = "CompletingFormalEvidenceSaves";
            evidence.RecordStage(stage);
            ProductWorkspaceSaveCompletionResult saveCompletion =
                await productWorkspaceSaves.CompleteAsync();

            const string expectedName = "PF-002 证据方格";
            string? createdName = diskAfterCreate.Document?.Containers
                .SingleOrDefault()?.Name;
            string? restoredName = diskAfterUndo.Document?.Containers
                .SingleOrDefault()?.Name;
            bool passed =
                beforeState.Containers.Count == 0
                && diskBefore.Status == ProductConfigurationLoadStatus.Missing
                && afterCancelState.Containers.Count == 0
                && diskAfterCancel.Status == ProductConfigurationLoadStatus.Missing
                && workspaceCommits.CurrentEditRevision == createRevision
                    + (crossDisplayHardwareAvailable ? 7 : 6)
                && afterCreateState.Containers.Count == 1
                && createSaveRevision == 1
                && createSave.Status == ProductWorkspaceSaveStatus.Saved
                && createSave.SavedRevision == createSaveRevision
                && diskAfterCreate.Status ==
                    ProductConfigurationLoadStatus.LoadedPrimary
                && diskAfterCreate.Document?.Containers.Count == 1
                && string.Equals(createdName, expectedName, StringComparison.Ordinal)
                && removal.IsAccepted
                && afterRemovalState.Containers.Count == 0
                && removalSaveRevision == 2
                && removalSave.Status == ProductWorkspaceSaveStatus.Saved
                && removalSave.SavedRevision == removalSaveRevision
                && diskAfterRemoval.Status ==
                    ProductConfigurationLoadStatus.LoadedPrimary
                && diskAfterRemoval.Document?.Containers.Count == 0
                && latestUndo.Selection.Kind ==
                    ProductWorkspaceLatestUndoKind.ContainerRemoval
                && executedUndoKind ==
                    ProductWorkspaceLatestUndoKind.ContainerRemoval
                && afterUndoState.Containers.Count == 1
                && undoSave.Status == ProductWorkspaceSaveStatus.Saved
                && undoSave.SavedRevision == 3
                && saveCompletion.Status ==
                    ProductWorkspaceSaveCompletionStatus.Completed
                && saveCompletion.Snapshot.SavedRevision ==
                    (crossDisplayHardwareAvailable ? 7 : 6)
                && diskAfterUndo.Status ==
                    ProductConfigurationLoadStatus.LoadedPrimary
                && diskAfterUndo.Document?.Containers.Count == 1
                && string.Equals(restoredName, expectedName, StringComparison.Ordinal)
                && layoutBegin
                && layoutUpdate
                && layoutComplete
                && afterLayoutState.Containers.Count == 1
                && Math.Abs(afterLayoutState.Containers[0].Placement.XDip
                    - (layoutOriginalX + 32)) <= 1
                && Math.Abs(afterLayoutState.Containers[0].Placement.YDip
                    - (layoutOriginalY + 16)) <= 1
                && layoutSave.Status == ProductWorkspaceSaveStatus.Saved
                && layoutSave.SavedRevision == 4
                && diskAfterLayout.Status ==
                    ProductConfigurationLoadStatus.LoadedPrimary
                && Math.Abs(diskAfterLayout.Document!.Containers[0].Placement.XDip
                    - (layoutOriginalX + 32)) <= 1
                && Math.Abs(diskAfterLayout.Document.Containers[0].Placement.YDip
                    - (layoutOriginalY + 16)) <= 1
                && keyboardMoveBegin
                && keyboardMoveUpdate
                && keyboardMoveComplete
                && Math.Abs(afterKeyboardMoveState.Containers[0].Placement.XDip
                    - (layoutOriginalX + 33)) <= 1
                && keyboardMoveSave.Status == ProductWorkspaceSaveStatus.Saved
                && keyboardMoveSave.SavedRevision == 5
                && keyboardResizeBegin
                && keyboardResizeUpdate
                && keyboardResizeComplete
                && Math.Abs(
                    afterKeyboardResizeState.Containers[0].Placement.WidthDip
                    - (layoutContainer.Placement.WidthDip + 8)) <= 1
                && keyboardResizeSave.Status ==
                    ProductWorkspaceSaveStatus.Saved
                && keyboardResizeSave.SavedRevision == 6
                && diskAfterKeyboardLayout.Status ==
                    ProductConfigurationLoadStatus.LoadedPrimary
                && Math.Abs(
                    diskAfterKeyboardLayout.Document!.Containers[0].Placement.XDip
                    - (layoutOriginalX + 33)) <= 1
                && Math.Abs(
                    diskAfterKeyboardLayout.Document.Containers[0].Placement.WidthDip
                    - (layoutContainer.Placement.WidthDip + 8)) <= 1
                && (!crossDisplayHardwareAvailable
                    || (crossDisplayBegin
                        && crossDisplayUpdate
                        && crossDisplayComplete
                        && afterCrossDisplayState.Containers.Count == 1
                        && !string.Equals(
                            afterCrossDisplayState.Containers[0].Placement.DisplayKey,
                            crossSourcePlacementKey,
                            StringComparison.Ordinal)
                        && diskAfterCrossDisplay.Status ==
                            ProductConfigurationLoadStatus.LoadedPrimary
                        && string.Equals(
                            diskAfterCrossDisplay.Document!.Containers[0]
                                .Placement.DisplayKey,
                            afterCrossDisplayState.Containers[0]
                                .Placement.DisplayKey,
                            StringComparison.Ordinal)
                        && Math.Abs(
                            diskAfterCrossDisplay.Document.Containers[0]
                                .Placement.XDip
                            - afterCrossDisplayState.Containers[0]
                                .Placement.XDip) <= 1
                        && Math.Abs(
                            diskAfterCrossDisplay.Document.Containers[0]
                                .Placement.YDip
                            - afterCrossDisplayState.Containers[0]
                                .Placement.YDip) <= 1
                        && crossDisplaySavedRevision == 7))
                && evidence.PreviewVisualTreeCount == 2
                && evidence.PreviewActivatedCount == 0
                && evidence.PreviewDrivenCount == 2;
            result = new
            {
                SchemaVersion = 1,
                Purpose = "Pf002AndPf003D4FormalAppPersistenceEvidence",
                Expected = new
                {
                    InitialContainerCount = 0,
                    CancelContainerCount = 0,
                    CancelDiskStatus = "Missing",
                    ConfirmContainerCount = 1,
                    ConfirmedName = expectedName,
                    PersistedDiskStatus = "LoadedPrimary",
                    RemovedContainerCount = 0,
                    RemovedDiskStatus = "LoadedPrimary",
                    LatestUndoKind = "ContainerRemoval",
                    RestoredContainerCount = 1,
                    RestoredName = expectedName,
                    LayoutBegin = true,
                    LayoutUpdate = true,
                    LayoutComplete = true,
                    LayoutDeltaXDip = 32,
                    LayoutDeltaYDip = 16,
                    LayoutSavedRevision = 4,
                    KeyboardFineMoveDeltaXDip = 1,
                    KeyboardLargeResizeDeltaWidthDip = 8,
                    KeyboardLayoutSavedRevision = 6,
                    CrossDisplayHardwareStatus =
                        "PassedWhenTwoAuthoritativeDisplays",
                    PreviewVisualTreeCount = 2,
                    PreviewActivatedCount = 0,
                    PreviewDrivenCount = 2,
                    VisibleInteractionStatus = "BlockedByKnownUpstream",
                    VisibleViewPublication = "BlockedByKnownUpstream",
                    DesktopFilesChanged = false,
                    UserConfigurationChanged = false,
                },
                Actual = new
                {
                    InitialContainerCount = beforeState.Containers.Count,
                    InitialDiskStatus = diskBefore.Status.ToString(),
                    CancelContainerCount = afterCancelState.Containers.Count,
                    CancelDiskStatus = diskAfterCancel.Status.ToString(),
                    ConfirmContainerCount = afterCreateState.Containers.Count,
                    ConfirmedName = createdName,
                    PersistedContainerCount =
                        diskAfterCreate.Document?.Containers.Count ?? 0,
                    PersistedDiskStatus = diskAfterCreate.Status.ToString(),
                    CreateSavedRevision = createSave.SavedRevision,
                    RemovalCommit = removal.Status.ToString(),
                    RemovedContainerCount = afterRemovalState.Containers.Count,
                    RemovedPersistedContainerCount =
                        diskAfterRemoval.Document?.Containers.Count ?? 0,
                    RemovedDiskStatus = diskAfterRemoval.Status.ToString(),
                    RemovalSavedRevision = removalSave.SavedRevision,
                    LatestUndoSelection = latestUndo.Selection.Kind.ToString(),
                    LatestUndoExecuted = executedUndoKind.ToString(),
                    RestoredContainerCount = afterUndoState.Containers.Count,
                    RestoredPersistedContainerCount =
                        diskAfterUndo.Document?.Containers.Count ?? 0,
                    RestoredName = restoredName,
                    RestoredDiskStatus = diskAfterUndo.Status.ToString(),
                    UndoSavedRevision = undoSave.SavedRevision,
                    LayoutBegin = layoutBegin,
                    LayoutUpdate = layoutUpdate,
                    LayoutComplete = layoutComplete,
                    LayoutDeltaXDip =
                        afterLayoutState.Containers[0].Placement.XDip
                        - layoutOriginalX,
                    LayoutDeltaYDip =
                        afterLayoutState.Containers[0].Placement.YDip
                        - layoutOriginalY,
                    LayoutPersistedDeltaXDip =
                        diskAfterLayout.Document?.Containers[0].Placement.XDip
                        - layoutOriginalX,
                    LayoutPersistedDeltaYDip =
                        diskAfterLayout.Document?.Containers[0].Placement.YDip
                        - layoutOriginalY,
                    LayoutSavedRevision = layoutSave.SavedRevision,
                    KeyboardMoveBegin = keyboardMoveBegin,
                    KeyboardMoveUpdate = keyboardMoveUpdate,
                    KeyboardMoveComplete = keyboardMoveComplete,
                    KeyboardFineMoveDeltaXDip =
                        afterKeyboardMoveState.Containers[0].Placement.XDip
                        - afterLayoutState.Containers[0].Placement.XDip,
                    KeyboardMoveSavedRevision = keyboardMoveSave.SavedRevision,
                    KeyboardResizeBegin = keyboardResizeBegin,
                    KeyboardResizeUpdate = keyboardResizeUpdate,
                    KeyboardResizeComplete = keyboardResizeComplete,
                    KeyboardLargeResizeDeltaWidthDip =
                        afterKeyboardResizeState.Containers[0].Placement.WidthDip
                        - afterKeyboardMoveState.Containers[0].Placement.WidthDip,
                    KeyboardPersistedDeltaXDip =
                        diskAfterKeyboardLayout.Document?.Containers[0].Placement.XDip
                        - diskAfterLayout.Document?.Containers[0].Placement.XDip,
                    KeyboardPersistedDeltaWidthDip =
                        diskAfterKeyboardLayout.Document?.Containers[0].Placement.WidthDip
                        - diskAfterLayout.Document?.Containers[0].Placement.WidthDip,
                    KeyboardLayoutSavedRevision =
                        keyboardResizeSave.SavedRevision,
                    CrossDisplayHardwareAvailable =
                        crossDisplayHardwareAvailable,
                    CrossDisplayStatus = crossDisplayHardwareAvailable
                        ? crossDisplayBegin
                            && crossDisplayUpdate
                            && crossDisplayComplete
                                ? "Passed"
                                : "Failed"
                        : "Unavailable",
                    CrossDisplayBegin = crossDisplayBegin,
                    CrossDisplayUpdate = crossDisplayUpdate,
                    CrossDisplayComplete = crossDisplayComplete,
                    CrossDisplayChangedDisplay =
                        crossDisplayHardwareAvailable
                        && !string.Equals(
                            afterCrossDisplayState.Containers[0]
                                .Placement.DisplayKey,
                            diskAfterKeyboardLayout.Document?.Containers[0]
                                .Placement.DisplayKey,
                            StringComparison.Ordinal),
                    CrossDisplayPersistedSameDisplay =
                        crossDisplayHardwareAvailable
                        && string.Equals(
                            diskAfterCrossDisplay.Document?.Containers[0]
                                .Placement.DisplayKey,
                            afterCrossDisplayState.Containers[0]
                                .Placement.DisplayKey,
                            StringComparison.Ordinal),
                    CrossDisplayPersistedDeltaXDip =
                        crossDisplayHardwareAvailable
                            ? diskAfterCrossDisplay.Document?.Containers[0]
                                .Placement.XDip
                                - afterCrossDisplayState.Containers[0]
                                    .Placement.XDip
                            : null,
                    CrossDisplayPersistedDeltaYDip =
                        crossDisplayHardwareAvailable
                            ? diskAfterCrossDisplay.Document?.Containers[0]
                                .Placement.YDip
                                - afterCrossDisplayState.Containers[0]
                                    .Placement.YDip
                            : null,
                    CrossDisplaySourceDpi = crossDisplaySourceDpi,
                    CrossDisplayTargetDpi = crossDisplayTargetDpi,
                    CrossDisplayMixedDpi =
                        crossDisplaySourceDpi is not null
                        && crossDisplayTargetDpi is not null
                        && crossDisplaySourceDpi != crossDisplayTargetDpi,
                    CrossDisplaySavedRevision = crossDisplaySavedRevision,
                    SaveCompletion = saveCompletion.Status.ToString(),
                    PreviewVisualTreeCount = evidence.PreviewVisualTreeCount,
                    PreviewActivatedCount = evidence.PreviewActivatedCount,
                    PreviewDrivenCount = evidence.PreviewDrivenCount,
                    VisibleInteractionStatus = "BlockedByKnownUpstream",
                    VisibleViewPublication = "BlockedByKnownUpstream",
                    DesktopFilesChanged = false,
                    UserConfigurationChanged = false,
                },
                Difference = passed ? "None" : "Pf002FormalAppEvidenceMismatch",
                Outcome = passed ? "Pass" : "Fail",
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            result = new
            {
                SchemaVersion = 1,
                Purpose = "Pf002AndPf003D4FormalAppPersistenceEvidence",
                Expected = "Pass",
                Actual = new
                {
                    Stage = stage,
                    Error = exception.GetType().Name,
                    ErrorDetail = exception.Message.StartsWith(
                        "PF-002 App evidence",
                        StringComparison.Ordinal)
                            ? exception.Message
                            : "RedactedNonEvidenceDetail",
                },
                Difference = "EvidenceSessionFailed",
                Outcome = "Fail",
            };
        }

        try
        {
            await evidence.WriteResultAsync(result);
        }
        finally
        {
            window?.Close();
        }
    }

    private async Task<ProductWorkspaceSaveSnapshot>
        WaitForProductWorkspaceSaveAsync(long minimumSavedRevision)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            ProductWorkspaceSaveSnapshot snapshot = productWorkspaceSaves.Snapshot;
            if (snapshot.Status == ProductWorkspaceSaveStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"PF-002 App evidence save failed at revision {snapshot.CurrentRevision}.");
            }
            if (snapshot.Status == ProductWorkspaceSaveStatus.Saved
                && snapshot.SavedRevision >= minimumSavedRevision)
            {
                return snapshot;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            $"PF-002 App evidence save did not reach revision {minimumSavedRevision}.");
    }

    private async Task LoadBoxesSettingsAsync()
    {
        ProductBoxesSettingsLoadResult result =
            await boxesSettingsStore.LoadAsync();
        boxesSettingsController.Initialize(result.Settings);
        ProductDesktopHostLifecycleSnapshot snapshot =
            productDesktopHostLifecycle.SetUserEnabled(
                result.Settings.BoxesEnabled);
        bool canChange = snapshot.Status !=
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy;
        string status = result.Status switch
        {
            ProductBoxesSettingsLoadStatus.MissingDefaulted =>
                "桌面方格默认开启；关闭后仍会保留全部布局配置。",
            ProductBoxesSettingsLoadStatus.LoadedPrimary =>
                "已恢复上次的桌面方格开关状态。",
            ProductBoxesSettingsLoadStatus.RecoveredBackup =>
                "主设置不可用，已恢复上次有效开关状态。",
            ProductBoxesSettingsLoadStatus.CorruptSafeDisabled =>
                "设置文件和备份均不可用，已安全关闭桌面方格；可重新开启并保存。",
            _ => throw new InvalidOperationException(
                "Boxes settings load status must be finite."),
        };
        if (!canChange)
        {
            status =
                "桌面方格被紧急安全策略关闭；用户开关值已保留，当前不能从界面覆盖。";
        }
        window?.ApplyBoxesEnabledState(
            result.Settings.BoxesEnabled,
            canChange,
            status);
        window?.ApplyThumbnailsEnabledState(
            result.Settings.ThumbnailsEnabled,
            canChange: true,
            result.Status == ProductBoxesSettingsLoadStatus.CorruptSafeDisabled
                ? "设置损坏时已安全关闭图片缩略图。"
                : result.Settings.ThumbnailsEnabled
                    ? "图片缩略图已开启；失败时自动回退类型图标。"
                    : "图片缩略图已关闭；不会启动缩略图工作进程。");
        productDesktopHostLifecycle.ApplyItemOpenPolicy(
            result.Settings.OpenItemsWithSingleClick);
        window?.ApplySingleClickOpenState(
            result.Settings.OpenItemsWithSingleClick,
            canChange: true,
            result.Settings.OpenItemsWithSingleClick
                ? "单击打开已开启；关闭后恢复推荐的双击打开。"
                : "推荐设置：单击选择，双击打开。");
        ApplyProductDesktopHostProjection(productDisplayTopology.Snapshot);
    }

    private async void MainWindow_BoxesEnabledChangeRequested(bool requestedValue)
    {
        _ = await ChangeBoxesEnabledAsync(requestedValue);
    }

    private async Task<ProductDesktopHostLifecycleSnapshot>
        ChangeBoxesEnabledAsync(bool requestedValue)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return productDesktopHostLifecycle.Snapshot;
        }

        currentWindow.ApplyBoxesEnabledChangePending(requestedValue);
        ProductBoxesSettingsChangeResult result =
            await boxesSettingsController.ChangeAsync(requestedValue);
        bool applied = result.Status is
            ProductBoxesSettingsChangeStatus.Saved
            or ProductBoxesSettingsChangeStatus.Unchanged;
        ProductDesktopHostLifecycleSnapshot snapshot = applied
            ? productDesktopHostLifecycle.SetUserEnabled(
                result.Settings.BoxesEnabled)
            : productDesktopHostLifecycle.Snapshot;
        string status = result.Status switch
        {
            ProductBoxesSettingsChangeStatus.Saved =>
                result.Settings.BoxesEnabled
                    ? "桌面方格已开启并保存；正在恢复上次布局。"
                    : "桌面方格已关闭并保存；布局配置保持不变。",
            ProductBoxesSettingsChangeStatus.Unchanged =>
                "桌面方格状态未变化，没有重复写入设置。",
            ProductBoxesSettingsChangeStatus.Failed =>
                "设置保存失败，已恢复原开关状态；桌面方格状态未改变。",
            _ => throw new InvalidOperationException(
                "Boxes settings change status must be finite."),
        };
        currentWindow.ApplyBoxesEnabledState(
            result.Settings.BoxesEnabled,
            snapshot.Status !=
                ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            status);
        if (applied)
        {
            ApplyProductDesktopHostProjection(productDisplayTopology.Snapshot);
        }

        return snapshot;
    }

    private async void MainWindow_ThumbnailsEnabledChangeRequested(
        bool requestedValue)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return;
        }
        currentWindow.ApplyThumbnailsEnabledChangePending(requestedValue);
        ProductBoxesSettingsChangeResult result =
            await boxesSettingsController.ChangeThumbnailsAsync(requestedValue);
        string status = result.Status switch
        {
            ProductBoxesSettingsChangeStatus.Saved =>
                result.Settings.ThumbnailsEnabled
                    ? "图片缩略图已开启并保存；正在按需刷新首屏项目。"
                    : "图片缩略图已关闭并保存；已停止工作进程并回退类型图标。",
            ProductBoxesSettingsChangeStatus.Unchanged =>
                "图片缩略图状态未变化，没有重复写入设置。",
            ProductBoxesSettingsChangeStatus.Failed =>
                "设置保存失败，已恢复原缩略图状态。",
            _ => throw new InvalidOperationException(
                "Thumbnail settings change status must be finite."),
        };
        currentWindow.ApplyThumbnailsEnabledState(
            result.Settings.ThumbnailsEnabled,
            canChange: true,
            status);
        if (result.Status is ProductBoxesSettingsChangeStatus.Saved
            or ProductBoxesSettingsChangeStatus.Unchanged)
        {
            ApplyProductDesktopHostProjection(productDisplayTopology.Snapshot);
        }
    }

    private async void MainWindow_SingleClickOpenChangeRequested(
        bool requestedValue)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return;
        }
        currentWindow.ApplySingleClickOpenChangePending(requestedValue);
        ProductBoxesSettingsChangeResult result =
            await boxesSettingsController.ChangeSingleClickOpenAsync(
                requestedValue);
        if (result.Status is ProductBoxesSettingsChangeStatus.Saved
            or ProductBoxesSettingsChangeStatus.Unchanged)
        {
            productDesktopHostLifecycle.ApplyItemOpenPolicy(
                result.Settings.OpenItemsWithSingleClick);
        }
        currentWindow.ApplySingleClickOpenState(
            result.Settings.OpenItemsWithSingleClick,
            canChange: true,
            result.Status switch
            {
                ProductBoxesSettingsChangeStatus.Saved =>
                    result.Settings.OpenItemsWithSingleClick
                        ? "已保存：单击选择并打开项目。"
                        : "已保存：单击选择，双击打开。",
                ProductBoxesSettingsChangeStatus.Unchanged => "设置未变化。",
                ProductBoxesSettingsChangeStatus.Failed =>
                    "保存失败，已恢复之前的打开方式。",
                _ => throw new InvalidOperationException(
                    "Single-click settings change status must be finite."),
            });
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
        _ = productWorkspaceSaves.DiscardFailedRetryForExternalBaseline();
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
            CancelDesktopWorkspaceCreatePreview(
                ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology);
            ApplyProductWorkspaceSessionViews();
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () =>
            {
                CancelDesktopWorkspaceCreatePreview(
                    ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology);
                ApplyProductWorkspaceSessionViews();
            });
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
            CancelDesktopWorkspaceCreatePreviewIfHostUnavailable(snapshot);
            currentWindow.ApplyProductDesktopHostLifecycleState(
                snapshot,
                productDesktopHostLifecycle.CanRequestKeyboardInteraction);
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () =>
            {
                CancelDesktopWorkspaceCreatePreviewIfHostUnavailable(snapshot);
                currentWindow.ApplyProductDesktopHostLifecycleState(
                    snapshot,
                    productDesktopHostLifecycle.CanRequestKeyboardInteraction);
            });
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

        if (desktopWorkspaceCreatePreview?.Snapshot.Request.WorkspaceRevision
            is long previewRevision
            && previewRevision != workspaceCommits.CurrentEditRevision)
        {
            CancelDesktopWorkspaceCreatePreview(
                ProductDesktopWorkspaceCreatePreviewFailure.StaleWorkspace);
        }

        currentWindow.ApplyProductWorkspaceSessionState(productWorkspaceSession);
        EnsureProductWorkspaceFolderContents();
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
            : ProductWorkspaceReadModel.Create(
                productWorkspaceSession.State,
                folderContents);
        ApplyProductDesktopHostProjection(topology);
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
                workspaceCommits.CurrentSelectedReferenceContainerUndoToken,
                workspaceCommits.CurrentReferenceRemovalUndoToken,
                workspaceCommits.CurrentReferenceReassignmentUndoToken,
                workspaceCommits.CurrentContainerEditUndoToken));
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

    private bool RequestDesktopExplorerReferenceDrop(
        object dataObject,
        string targetContainerId)
    {
        ProductDesktopCatalogSnapshot catalog = productDesktopCatalog.Snapshot;
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (!catalog.IsAuthoritative
            || state is null
            || productWorkspaceSession.IsReadOnly)
        {
            return false;
        }

        ProductWorkspaceState authoritativeState =
            StampAuthoritativeDisplayTopology(state);
        ProductDesktopExplorerReferenceDropPreparation preparation =
            ProductDesktopExplorerReferenceDropAdapter.Prepare(
                dataObject,
                authoritativeState,
                workspaceCommits.CurrentEditRevision,
                catalog.Generation,
                catalog.Entries,
                targetContainerId);
        if (!preparation.IsAccepted)
        {
            return false;
        }

        ProductWorkspaceResolvedReferenceBatchCommitResult result =
            workspaceCommits.CommitResolvedReferenceBatch(
                authoritativeState,
                catalog.Generation,
                catalog.Entries,
                preparation.CommitRequest!);
        if (result.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(result.Document!, catalog);
        }
        return result.IsAccepted;
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
        ProductContainerTitleVisibilityPolicy? titleVisibility,
        ProductContainerTitleDoubleClickAction? titleDoubleClickAction,
        bool confirmed) =>
        CommitProductWorkspaceContainerActionCore(
            action,
            expectedEditRevision,
            containerOrdinal,
            name,
            stateValue,
            colorPreset,
            opacityPreset,
            positionPreset,
            sizePreset,
            confirmed,
            createDisplayId: null,
            createBoundsPixels: null,
            useDefaultName: false,
            titleVisibility: titleVisibility,
            titleDoubleClickAction: titleDoubleClickAction);

    private ProductWorkspaceContainerCommitResult
        CommitProductWorkspaceContainerFolderBinding(
            long expectedEditRevision,
            int containerOrdinal,
            ProductContainerFolderBindingState? folderBinding,
            bool unbind) =>
        CommitProductWorkspaceContainerActionCore(
            unbind
                ? ProductWorkspaceContainerCommitAction.UnbindFolder
                : ProductWorkspaceContainerCommitAction.BindFolder,
            expectedEditRevision,
            containerOrdinal,
            name: string.Empty,
            stateValue: null,
            colorPreset: null,
            opacityPreset: null,
            positionPreset: null,
            sizePreset: null,
            confirmed: unbind,
            createDisplayId: null,
            createBoundsPixels: null,
            useDefaultName: false,
            folderBinding: folderBinding);

    private ProductWorkspaceContainerCommitResult
        CommitProductWorkspaceContainerActionCore(
            ProductWorkspaceContainerCommitAction action,
            long expectedEditRevision,
            int containerOrdinal,
            string name,
            bool? stateValue,
            ProductWorkspaceContainerColorPreset? colorPreset,
            ProductWorkspaceContainerOpacityPreset? opacityPreset,
            ProductWorkspaceContainerPositionPreset? positionPreset,
            ProductWorkspaceContainerSizePreset? sizePreset,
            bool confirmed,
            string? createDisplayId,
            PixelRect? createBoundsPixels,
            bool useDefaultName,
            ProductContainerTitleVisibilityPolicy? titleVisibility = null,
            ProductContainerTitleDoubleClickAction? titleDoubleClickAction = null,
            ProductContainerFolderBindingState? folderBinding = null)
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

        string normalizedName = useDefaultName ? string.Empty : name.Trim();
        ProductContainerState? newContainer = action ==
            ProductWorkspaceContainerCommitAction.Create
            ? CreateDefaultContainer(
                state,
                useDefaultName ? null : normalizedName,
                createDisplayId,
                createBoundsPixels)
            : null;
        if (newContainer is not null)
        {
            normalizedName = newContainer.Name;
        }
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
                    confirmed,
                    titleVisibility,
                    titleDoubleClickAction,
                    FolderBinding: folderBinding));
        if (!result.IsAccepted)
        {
            return result;
        }

        if (result.EditUndoToken is { } editUndoToken)
        {
            pendingControlCenterContainerEdit = new(
                editUndoToken,
                result.EditRevision,
                productWorkspaceSaves.Snapshot.CurrentRevision);
        }

        ApplyAcceptedProductWorkspaceDocument(
            result.Document!,
            productDesktopCatalog.Snapshot);
        return result;
    }

    private bool RequestDesktopWorkspaceCreate(
        ProductDesktopWorkspaceCreateRequest request)
    {
        MainWindow? currentWindow = window;
        ProductDisplayTopologySnapshot currentTopology =
            productDisplayTopology.Snapshot;
        if (currentWindow is null
            || closingDrainInProgress
            || !ProductDesktopWorkspaceCreateAdmission.Evaluate(
                request,
                workspaceCommits.CurrentEditRevision,
                currentTopology.Generation).CanCreate)
        {
            return false;
        }

        return currentWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RunDesktopWorkspaceCreatePreviewAsync(
                    currentWindow,
                    request);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or ArgumentException
                    or System.Runtime.InteropServices.COMException)
            {
                CancelDesktopWorkspaceCreatePreview(
                    ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable);
                currentWindow.ApplyDesktopWorkspaceCreateResult(false);
            }
        });
    }

    private bool RequestDesktopContainerHeaderCommand(
        ProductDesktopContainerHeaderCommandRequest request)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return false;
        }

        return currentWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ProductDesktopContainerHeaderCommandResult result =
                desktopContainerHeaderCommands.Handle(
                    request,
                    productWorkspaceSession.State,
                    productWorkspaceSession.IsReadOnly,
                    workspaceCommits.CurrentEditRevision,
                    productDisplayTopology.Snapshot);
            if (result.IsAccepted)
            {
                ApplyAcceptedProductWorkspaceDocument(
                    result.Document!,
                    productDesktopCatalog.Snapshot);
                ApplyProductWorkspaceSaveSnapshot(
                    currentWindow,
                    productWorkspaceSaves.Snapshot);
            }
        });
    }

    private ProductDesktopContainerMenuAvailability
        GetDesktopContainerMenuAvailability(
            string containerId,
            string displayId)
    {
        ProductDesktopContainerMenuAvailability availability =
            ProductDesktopContainerMenuNavigationController.EvaluateAvailability(
                productWorkspaceSession.State,
                productWorkspaceSession.IsReadOnly || closingDrainInProgress,
                productWorkspaceSaves.Snapshot,
                containerId,
                displayId);
        return desktopContainerDeletes.CanStart
            ? availability
            : availability with { CanDeleteContainerConfiguration = false };
    }

    private bool RequestDesktopContainerMenuNavigation(
        ProductDesktopContainerMenuRequest request)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return false;
        }

        return currentWindow.DispatcherQueue.TryEnqueue(() =>
            _ = HandleDesktopContainerMenuRequestAsync(currentWindow, request));
    }

    private async Task HandleDesktopContainerMenuRequestAsync(
        MainWindow currentWindow,
        ProductDesktopContainerMenuRequest request)
    {
        ProductDesktopContainerMenuNavigationResult result =
            ProductDesktopContainerMenuNavigationController.Handle(
                request,
                productWorkspaceSession.State,
                productWorkspaceSession.IsReadOnly,
                workspaceCommits.CurrentEditRevision,
                productWorkspaceSaves.Snapshot,
                productDisplayTopology.Snapshot);
        if (!result.IsAccepted)
        {
            return;
        }

        ActivateMainWindow();
        if (result.Action !=
            ProductDesktopContainerMenuAction.DeleteContainerConfiguration)
        {
            _ = currentWindow.OpenProductWorkspaceContainerMenuTarget(
                result.ContainerOrdinal,
                result.Action);
            return;
        }

        ProductContainerState? target = productWorkspaceSession.State?.Containers
            .ElementAtOrDefault(result.ContainerOrdinal - 1);
        if (target is null
            || !string.Equals(target.Id, result.ContainerId, StringComparison.Ordinal)
            || !string.Equals(
                target.Placement.DisplayKey,
                result.DisplayId,
                StringComparison.Ordinal))
        {
            currentWindow.ApplyDesktopContainerDeleteRevalidationFailure();
            return;
        }

        bool confirmed = await currentWindow.ConfirmDesktopContainerDeletionAsync(
            result.ContainerOrdinal,
            target.Name,
            target.Items.Count,
            result.EditRevision,
            result.TopologyGeneration);
        if (!confirmed)
        {
            return;
        }
        if (closingDrainInProgress || !ReferenceEquals(window, currentWindow))
        {
            currentWindow.ApplyDesktopContainerDeleteRevalidationFailure();
            return;
        }

        ProductDesktopContainerMenuNavigationResult revalidated =
            ProductDesktopContainerMenuNavigationController.Handle(
                request,
                productWorkspaceSession.State,
                productWorkspaceSession.IsReadOnly,
                workspaceCommits.CurrentEditRevision,
                productWorkspaceSaves.Snapshot,
                productDisplayTopology.Snapshot);
        if (!revalidated.IsAccepted
            || revalidated.Action !=
                ProductDesktopContainerMenuAction.DeleteContainerConfiguration
            || revalidated.ContainerOrdinal != result.ContainerOrdinal
            || !string.Equals(
                revalidated.ContainerId,
                result.ContainerId,
                StringComparison.Ordinal)
            || !string.Equals(
                revalidated.DisplayId,
                result.DisplayId,
                StringComparison.Ordinal))
        {
            currentWindow.ApplyDesktopContainerDeleteRevalidationFailure();
            return;
        }

        ProductDesktopContainerDeleteResult commit =
            desktopContainerDeletes.CommitConfirmed(
                revalidated,
                productWorkspaceSession.State);
        if (commit.IsAccepted)
        {
            ApplyAcceptedProductWorkspaceDocument(
                commit.Document!,
                productDesktopCatalog.Snapshot);
        }
        currentWindow.ApplyDesktopContainerDeleteCommitResult(commit);
    }

    private bool RequestDesktopItemViewport(
        ProductDesktopItemViewportRequest request)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null
            || closingDrainInProgress
            || !request.SourceAttested
            || request.IsInjected
            || request.IsAutoRepeat
            || request.WheelDelta == 0)
        {
            return false;
        }
        return currentWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ProductWorkspaceState? state = productWorkspaceSession.State;
            ProductDisplayTopologySnapshot topology =
                productDisplayTopology.Snapshot;
            if (state is null
                || !topology.IsAuthoritative
                || request.WorkspaceRevision !=
                    workspaceCommits.CurrentEditRevision
                || request.TopologyGeneration != topology.Generation)
            {
                return;
            }
            ProductContainerState[] targets = state.Containers
                .Where(container => string.Equals(
                    container.Id,
                    request.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            ProductWorkspaceReadContainer? visible = targets.Length == 1
                ? ProductWorkspaceReadModel.Create(state, folderContents)
                    .Snapshot?.Containers.SingleOrDefault(container =>
                        container.Ordinal == Array.FindIndex(
                            state.Containers.ToArray(),
                            candidate => ReferenceEquals(candidate, targets[0])) + 1)
                : null;
            if (targets.Length != 1
                || visible is null
                || visible.Items.Count <=
                    ProductDesktopHostReadOnlyProjection.MaximumVisibleItems)
            {
                return;
            }
            string expectedDisplay = topology.Displays.Any(display =>
                string.Equals(
                    display.StableId,
                    targets[0].Placement.DisplayKey,
                    StringComparison.Ordinal))
                ? targets[0].Placement.DisplayKey
                : topology.Displays.Single(display => display.IsPrimary).StableId;
            if (!string.Equals(
                expectedDisplay,
                request.DisplayId,
                StringComparison.Ordinal))
            {
                return;
            }
            int currentStart = desktopItemViewportStarts.TryGetValue(
                request.ContainerId,
                out int storedStart)
                    ? storedStart
                    : 0;
            int nextStart = ProductDesktopItemViewportPolicy.Move(
                currentStart,
                visible.Items.Count,
                request.WheelDelta);
            if (nextStart == currentStart)
            {
                return;
            }
            desktopItemViewportStarts[request.ContainerId] = nextStart;
            ApplyProductDesktopHostProjection(topology);
        });
    }

    private ProductDesktopItemOpenResult RequestDesktopItemOpen(
        ProductDesktopItemOpenRequest request)
    {
        if (closingDrainInProgress)
        {
            return new(
                ProductDesktopItemOpenStatus.InvalidRequest,
                request.Source);
        }
        return desktopItemOpens.Open(
            request,
            productWorkspaceSession.State,
            workspaceCommits.CurrentEditRevision,
            productDisplayTopology.Snapshot,
            folderContents);
    }

    private bool RequestDesktopContainerLayout(
        ProductDesktopContainerLayoutRequest request)
    {
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (state is not null && topology.IsAuthoritative)
        {
            state = StampAuthoritativeDisplayTopology(state);
        }

        ProductDesktopContainerLayoutInteractionResult result =
            desktopContainerLayoutInteractions.Handle(
                request,
                state,
                productWorkspaceSession.IsReadOnly || closingDrainInProgress,
                workspaceCommits.CurrentEditRevision,
                topology);
        if (result.ClearPreview)
        {
            _ = productDesktopHostLifecycle.ApplyContainerLayoutPreview(
                result.DisplayId,
                result.ContainerId,
                result.ExpectedWorkspaceRevision,
                result.ExpectedTopologyGeneration,
                placement: null);
        }

        if (result.Status ==
            ProductDesktopContainerLayoutInteractionStatus.PreviewUpdated)
        {
            bool applied = productDesktopHostLifecycle.ApplyContainerLayoutPreview(
                result.DisplayId,
                result.ContainerId,
                result.ExpectedWorkspaceRevision,
                result.ExpectedTopologyGeneration,
                result.PreviewPlacement);
            if (!applied)
            {
                ProductDesktopContainerLayoutInteractionResult cancelled =
                    desktopContainerLayoutInteractions.CancelActive(
                        ProductDesktopContainerLayoutCancellationReason
                            .HostInvalidated);
                if (cancelled.ClearPreview)
                {
                    _ = productDesktopHostLifecycle.ApplyContainerLayoutPreview(
                        cancelled.DisplayId,
                        cancelled.ContainerId,
                        cancelled.ExpectedWorkspaceRevision,
                        cancelled.ExpectedTopologyGeneration,
                        placement: null);
                }
                return false;
            }
        }

        if (result.Status ==
            ProductDesktopContainerLayoutInteractionStatus.Committed)
        {
            ApplyAcceptedProductWorkspaceDocument(
                result.Document!,
                productDesktopCatalog.Snapshot);
        }
        return result.IsAccepted;
    }

    private bool RequestProductWorkspaceSelectedReferenceCreate(
        long expectedEditRevision,
        IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>
            candidates)
    {
        ProductWorkspaceState? state = ResolveDesktopWorkspaceCreateState();
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        if (state is null
            || expectedEditRevision != workspaceCommits.CurrentEditRevision
            || candidates.Count is <= 0 or >
                ProductWorkspaceSelectedReferenceCreateSnapshot.MaximumItemCount
            || candidates.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() != 1
            || !topology.IsAuthoritative)
        {
            return false;
        }

        int sourceOrdinal = candidates[0].ContainerOrdinal;
        ProductWorkspaceSelectedReferenceCreateSnapshotResult captured =
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                sourceOrdinal,
                candidates.Select(candidate => candidate.ItemOrdinal).ToArray());
        if (!captured.IsReady)
        {
            return false;
        }

        ProductContainerState source = state.Containers[sourceOrdinal - 1];
        DisplayTopologyNode? display = topology.Displays.FirstOrDefault(candidate =>
            string.Equals(
                candidate.StableId,
                source.Placement.DisplayKey,
                StringComparison.Ordinal))
            ?? topology.Displays.FirstOrDefault(candidate => candidate.IsPrimary)
            ?? (topology.Displays.Count > 0 ? topology.Displays[0] : null);
        return display is not null && RequestDesktopWorkspaceCreate(new(
            ProductDesktopWorkspaceCreateInputKind.SelectedReferences,
            display.StableId,
            expectedEditRevision,
            topology.Generation,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false,
            RequestedBoundsPixels: null,
            SelectedReferences: captured.Snapshot));
    }

    private async Task RunDesktopWorkspaceCreatePreviewAsync(
        MainWindow currentWindow,
        ProductDesktopWorkspaceCreateRequest request)
    {
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        ProductWorkspaceState? state = ResolveDesktopWorkspaceCreateState();
        DisplayTopologyNode? display = topology.IsAuthoritative
            ? topology.Displays.FirstOrDefault(candidate => string.Equals(
                candidate.StableId,
                request.DisplayId,
                StringComparison.Ordinal))
            : null;
        bool requestIsCurrent =
            ProductDesktopWorkspaceCreateAdmission.Evaluate(
                request,
                workspaceCommits.CurrentEditRevision,
                topology.Generation).CanCreate;
        if (!requestIsCurrent
            || state is null
            || !SelectedReferenceCreateStillCurrent(
                currentWindow,
                request.SelectedReferences,
                state)
            || display is null
            || productDesktopHostLifecycle.Snapshot.Status is not (
                ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                or ProductDesktopHostLifecycleStatus.ReadyReadOnly))
        {
            currentWindow.ApplyDesktopWorkspaceCreateResult(false);
            return;
        }

        ProductWorkspaceContainerCreationDefaultsDecision defaults =
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                state.Containers,
                requestedName: null,
                display.StableId,
                display.WorkArea,
                display.EffectiveDpi,
                request.RequestedBoundsPixels);
        ProductDesktopWorkspaceCreatePreviewSession session =
            ProductDesktopWorkspaceCreatePreviewSession.Start(request, defaults);
        if (!session.Snapshot.CanSubmit)
        {
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                session.Snapshot,
                created: false);
            return;
        }

        CancelDesktopWorkspaceCreatePreview(
            ProductDesktopWorkspaceCreatePreviewFailure.Replaced);
        desktopWorkspaceCreatePreview = session;
        PixelRect? previewBounds =
            ProductDesktopWorkspaceCreatePreviewPlacement.ResolveWindowBounds(
                session.Snapshot.CandidatePlacement,
                display.WorkArea,
                display.EffectiveDpi);
        if (previewBounds is null)
        {
            ProductDesktopWorkspaceCreatePreviewSnapshot rejected =
                session.Cancel(
                    ProductDesktopWorkspaceCreatePreviewFailure
                        .PlacementUnavailable);
            desktopWorkspaceCreatePreview = null;
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                rejected,
                created: false);
            return;
        }

        string? confirmedName = null;
        IProductDesktopWorkspaceCreateEvidenceSession? evidenceSession =
            (IProductDesktopWorkspaceCreateEvidenceSession?)
                pf002AppEvidenceSession
            ?? boxR1ActivationEvidenceSession;
        string? evidenceResponse = null;
        bool hasEvidenceResponse = evidenceSession is not null
            && evidenceSession.TryTakePreviewResponse(out evidenceResponse);
        bool singleWindowSafetyRequired =
            ProductWinUiRuntimeSafety.RequiresSingleWindowPreview();
        bool useFallbackPreview = false;
        if (singleWindowSafetyRequired)
        {
            evidenceSession?.RecordStage(
                "UsingSingleWindowPreviewForKnownUnsafeRuntime");
        }
        else
        {
            try
            {
                evidenceSession?.RecordStage("CreatingInlinePreview");
                var previewWindow = new DesktopWorkspaceCreatePreviewWindow(
                    session.Snapshot,
                    previewBounds.Value,
                    enteredName => EvaluateDesktopWorkspaceCreatePreviewName(
                        session,
                        enteredName));
                desktopWorkspaceCreatePreviewWindow = previewWindow;
                currentWindow.ApplyDesktopWorkspaceCreatePreviewOpened(
                    session.Snapshot,
                    inline: true);
                if (hasEvidenceResponse)
                {
                    evidenceSession!.RecordStage("DrivingInlinePreview");
                    confirmedName = await previewWindow.ShowForEvidenceAsync(
                        evidenceResponse,
                        evidenceSession.RecordStage);
                    evidenceSession.ObservePreview(previewWindow);
                }
                else
                {
                    confirmedName = await previewWindow.ShowAsync();
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or ArgumentException
                    or System.Runtime.InteropServices.COMException)
            {
                evidenceSession?.RecordStage(
                    $"UsingFallbackPreview:{exception.GetType().Name}:" +
                    $"0x{exception.HResult:X8}");
                useFallbackPreview = true;
            }
            finally
            {
                desktopWorkspaceCreatePreviewWindow = null;
            }
        }

        if (singleWindowSafetyRequired)
        {
            bool showBoxR1Evidence = boxR1ActivationEvidenceSession is not null;
            if (showBoxR1Evidence)
            {
                currentWindow.Activate();
                DateTime xamlReadyDeadline = DateTime.UtcNow.AddSeconds(2);
                while (!currentWindow.IsProductXamlReady
                    && DateTime.UtcNow < xamlReadyDeadline)
                {
                    await Task.Delay(25);
                }
            }
            currentWindow.ApplyDesktopWorkspaceCreatePreviewOpened(
                session.Snapshot,
                inline: false);
            confirmedName = await currentWindow.ShowDesktopWorkspaceCreateSafePreviewAsync(
                session.Snapshot,
                enteredName => EvaluateDesktopWorkspaceCreatePreviewName(
                    session,
                    enteredName),
                evidenceMode: hasEvidenceResponse,
                evidenceResponse,
                showInEvidenceMode: showBoxR1Evidence,
                observeEvidence: hasEvidenceResponse
                    ? evidenceSession!.ObserveSafePreview
                    : null);
        }
        else if (useFallbackPreview)
        {
            desktopWorkspaceCreatePreviewWindow = null;
            currentWindow.Activate();
            currentWindow.ApplyDesktopWorkspaceCreatePreviewOpened(
                session.Snapshot,
                inline: false);
            confirmedName = await currentWindow.ShowDesktopWorkspaceCreatePreviewAsync(
                session.Snapshot,
                enteredName => EvaluateDesktopWorkspaceCreatePreviewName(
                    session,
                    enteredName),
                evidenceMode: hasEvidenceResponse,
                evidenceResponse,
                observeEvidence: hasEvidenceResponse
                    ? evidenceSession!.ObserveFallbackPreview
                    : null);
        }
        if (!ReferenceEquals(desktopWorkspaceCreatePreview, session))
        {
            return;
        }

        if (confirmedName is null)
        {
            ProductDesktopWorkspaceCreatePreviewSnapshot cancelled =
                session.Cancel(
                    ProductDesktopWorkspaceCreatePreviewFailure.UserCancelled);
            desktopWorkspaceCreatePreview = null;
            if (pf002AppEvidenceSession is null)
            {
                currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                    cancelled,
                    created: false);
            }
            return;
        }

        topology = productDisplayTopology.Snapshot;
        bool displayStillAvailable = topology.IsAuthoritative
            && topology.Displays.Any(candidate => string.Equals(
                candidate.StableId,
                request.DisplayId,
                StringComparison.Ordinal));
        ProductDesktopHostLifecycleSnapshot host =
            productDesktopHostLifecycle.Snapshot;
        if (!displayStillAvailable
            || host.Status is not (
                ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                or ProductDesktopHostLifecycleStatus.ReadyReadOnly)
            || (host.ExplicitInteractionActive
                && request.Kind !=
                    ProductDesktopWorkspaceCreateInputKind.PointerDrag))
        {
            ProductDesktopWorkspaceCreatePreviewSnapshot cancelled =
                session.Cancel(displayStillAvailable
                    ? ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable
                    : ProductDesktopWorkspaceCreatePreviewFailure.DisplayUnavailable);
            desktopWorkspaceCreatePreview = null;
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                cancelled,
                created: false);
            return;
        }

        state = ResolveDesktopWorkspaceCreateState();
        if (state is null
            || !SelectedReferenceCreateStillCurrent(
                currentWindow,
                request.SelectedReferences,
                state))
        {
            ProductDesktopWorkspaceCreatePreviewSnapshot cancelled =
                session.Cancel(
                    ProductDesktopWorkspaceCreatePreviewFailure.StaleSelection);
            desktopWorkspaceCreatePreview = null;
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                cancelled,
                created: false);
            return;
        }

        ProductDesktopWorkspaceCreatePreviewSnapshot submitting =
            session.PrepareSubmit(
                workspaceCommits.CurrentEditRevision,
                topology.Generation);
        if (submitting.Status !=
            ProductDesktopWorkspaceCreatePreviewStatus.Submitting)
        {
            desktopWorkspaceCreatePreview = null;
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                submitting,
                created: false);
            return;
        }

        bool createdSuccessfully;
        if (request.SelectedReferences is { } selection)
        {
            ProductContainerState? newContainer = CreateDefaultContainer(
                state,
                submitting.Name,
                request.DisplayId,
                request.RequestedBoundsPixels);
            ProductWorkspaceSelectedReferenceContainerCommitResult selectedResult =
                newContainer is null
                    ? new(
                        ProductWorkspaceSelectedReferenceContainerCommitStatus
                            .InvalidRequest,
                        ProductWorkspaceEditError.InvalidState,
                        null,
                        workspaceCommits.CurrentEditRevision,
                        null,
                        null,
                        null)
                    : workspaceCommits.CommitSelectedReferenceContainer(
                        state,
                        new(
                            workspaceCommits.CurrentEditRevision,
                            selection.SourceContainerOrdinal,
                            selection.ItemIds,
                            newContainer));
            createdSuccessfully = selectedResult.IsAccepted;
            if (createdSuccessfully)
            {
                ApplyAcceptedProductWorkspaceDocument(
                    selectedResult.Document!,
                    productDesktopCatalog.Snapshot);
                ProductContainerState created =
                    selectedResult.State!.Containers[^1];
                desktopWorkspaceCreatePublication = new(
                    created.Id,
                    selectedResult.EditRevision,
                    productWorkspaceSaves.Snapshot.CurrentRevision,
                    selectedResult.UndoToken);
            }
        }
        else
        {
            ProductWorkspaceContainerCommitResult result =
                CommitProductWorkspaceContainerActionCore(
                    ProductWorkspaceContainerCommitAction.Create,
                    workspaceCommits.CurrentEditRevision,
                    containerOrdinal: 0,
                    name: submitting.Name,
                    stateValue: null,
                    colorPreset: null,
                    opacityPreset: null,
                    positionPreset: null,
                    sizePreset: null,
                    confirmed: false,
                    createDisplayId: request.DisplayId,
                    createBoundsPixels: request.RequestedBoundsPixels,
                    useDefaultName: false);
            createdSuccessfully = result.IsAccepted;
            if (createdSuccessfully)
            {
                ProductContainerState created = result.State!.Containers[^1];
                desktopWorkspaceCreatePublication = new(
                    created.Id,
                    result.EditRevision,
                    productWorkspaceSaves.Snapshot.CurrentRevision);
            }
        }
        desktopWorkspaceCreatePreview = null;
        if (pf002AppEvidenceSession is null)
        {
            currentWindow.ApplyDesktopWorkspaceCreatePreviewResult(
                submitting,
                createdSuccessfully);
        }
    }

    private static bool SelectedReferenceCreateStillCurrent(
        MainWindow currentWindow,
        ProductWorkspaceSelectedReferenceCreateSnapshot? expected,
        ProductWorkspaceState state)
    {
        if (expected is null)
        {
            return true;
        }
        if (ProductWorkspaceSelectedReferenceCreateSnapshots.Evaluate(
                expected,
                state) != ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready)
        {
            return false;
        }

        IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>
            current = currentWindow.CaptureProductWorkspaceSelectedReferences();
        if (current.Count != expected.ItemIds.Count
            || current.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .SingleOrDefault() != expected.SourceContainerOrdinal)
        {
            return false;
        }

        ProductWorkspaceSelectedReferenceCreateSnapshotResult captured =
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                expected.SourceContainerOrdinal,
                current.Select(candidate => candidate.ItemOrdinal).ToArray());
        return captured.Snapshot is { } actual
            && actual.ItemIds.ToHashSet(StringComparer.Ordinal).SetEquals(
                expected.ItemIds)
            && string.Equals(
                actual.ConfigurationFingerprint,
                expected.ConfigurationFingerprint,
                StringComparison.Ordinal);
    }

    private ProductDesktopWorkspaceCreatePreviewSnapshot
        EvaluateDesktopWorkspaceCreatePreviewName(
            ProductDesktopWorkspaceCreatePreviewSession session,
            string enteredName)
    {
        if (!ReferenceEquals(desktopWorkspaceCreatePreview, session))
        {
            return session.Snapshot;
        }

        ProductWorkspaceState? state = ResolveDesktopWorkspaceCreateState();
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        DisplayTopologyNode? display = topology.IsAuthoritative
            ? topology.Displays.FirstOrDefault(candidate => string.Equals(
                candidate.StableId,
                session.Snapshot.Request.DisplayId,
                StringComparison.Ordinal))
            : null;
        if (state is null || display is null)
        {
            return session.Cancel(display is null
                ? ProductDesktopWorkspaceCreatePreviewFailure.DisplayUnavailable
                : ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable);
        }
        if (session.Snapshot.Request.WorkspaceRevision !=
            workspaceCommits.CurrentEditRevision)
        {
            return session.Cancel(
                ProductDesktopWorkspaceCreatePreviewFailure.StaleWorkspace);
        }
        if (session.Snapshot.Request.TopologyGeneration != topology.Generation)
        {
            return session.Cancel(
                ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology);
        }
        if (window is not { } currentWindow
            || !SelectedReferenceCreateStillCurrent(
                currentWindow,
                session.Snapshot.Request.SelectedReferences,
                state))
        {
            return session.Cancel(
                ProductDesktopWorkspaceCreatePreviewFailure.StaleSelection);
        }

        ProductWorkspaceContainerCreationDefaultsDecision decision =
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                state.Containers,
                enteredName,
                display.StableId,
                display.WorkArea,
                display.EffectiveDpi,
                session.Snapshot.Request.RequestedBoundsPixels);
        return session.UpdateName(enteredName, decision);
    }

    private ProductWorkspaceState? ResolveDesktopWorkspaceCreateState()
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        bool creatingFirstConfiguration =
            productWorkspaceSession.Status ==
                ProductWorkspaceSessionStatus.NoSavedConfiguration
            && currentConfigurationLoadResult?.Status ==
                ProductConfigurationLoadStatus.Missing;
        if (creatingFirstConfiguration)
        {
            state = ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(),
                Array.Empty<DesktopCatalogEntry>()).State;
        }
        if (state is null
            || (productWorkspaceSession.IsReadOnly && !creatingFirstConfiguration))
        {
            return null;
        }
        return StampAuthoritativeDisplayTopology(state);
    }

    private void CancelDesktopWorkspaceCreatePreviewIfHostUnavailable(
        ProductDesktopHostLifecycleSnapshot snapshot)
    {
        if (snapshot.Status is not (
                ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                or ProductDesktopHostLifecycleStatus.ReadyReadOnly)
            || snapshot.ExplicitInteractionActive)
        {
            CancelDesktopWorkspaceCreatePreview(
                ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable);
        }
    }

    private void CancelDesktopWorkspaceCreatePreview(
        ProductDesktopWorkspaceCreatePreviewFailure failure)
    {
        ProductDesktopWorkspaceCreatePreviewSession? session =
            desktopWorkspaceCreatePreview;
        if (session is null)
        {
            return;
        }
        _ = session.Cancel(failure);
        desktopWorkspaceCreatePreview = null;
        desktopWorkspaceCreatePreviewWindow?.Cancel();
        desktopWorkspaceCreatePreviewWindow = null;
        window?.CancelDesktopWorkspaceCreatePreview();
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

    private ProductWorkspaceContainerEditUndoCommitResult
        CommitProductWorkspaceContainerEditUndo(
            ProductWorkspaceContainerEditUndoToken token,
            bool confirmed)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (state is null || productWorkspaceSession.IsReadOnly)
        {
            return new(
                ProductWorkspaceContainerEditUndoCommitStatus.InvalidState,
                ProductWorkspaceContainerEditUndoStatus.InvalidState,
                null,
                workspaceCommits.CurrentEditRevision,
                null,
                null);
        }

        ProductWorkspaceContainerEditUndoCommitResult result =
            workspaceCommits.CommitContainerEditUndo(
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
        if (pf002AppEvidenceSession is null)
        {
            ApplyProductWorkspaceSessionViews();
        }
        else
        {
            ApplyProductDesktopHostProjection(productDisplayTopology.Snapshot);
        }
    }

    private void ApplyProductDesktopHostProjection(
        ProductDisplayTopologySnapshot topology)
    {
        ProductWorkspaceState? desktopHostState = productWorkspaceSession.State;
        if (desktopHostState is null
            && productWorkspaceSession.Status ==
                ProductWorkspaceSessionStatus.NoSavedConfiguration
            && currentConfigurationLoadResult?.Status ==
                ProductConfigurationLoadStatus.Missing)
        {
            desktopHostState = ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(),
                Array.Empty<DesktopCatalogEntry>()).State;
        }
        ProductWorkspaceReadResult desktopHostReadModel = desktopHostState is null
            ? new(
                ProductWorkspaceProjectionError.InvalidState,
                ProductConfigurationError.None,
                null)
            : ProductWorkspaceReadModel.Create(desktopHostState, folderContents);
        NormalizeDesktopItemViewports(desktopHostState, desktopHostReadModel.Snapshot);
        long workspaceRevision = workspaceCommits.CurrentEditRevision;
        IReadOnlyList<ProductDesktopThumbnailCandidate> candidates =
            ProductDesktopThumbnailCandidateBuilder.Build(
                desktopHostState,
                desktopItemViewportStarts);
        bool thumbnailsEnabled =
            boxesSettingsController.Current.ThumbnailsEnabled
            && boxesSettingsController.Current.BoxesEnabled;
        IReadOnlyDictionary<string, ProductDesktopThumbnailResult> loading =
            thumbnailsEnabled
                ? candidates.ToDictionary(
                    candidate => candidate.AnonymousItemKey,
                    candidate => new ProductDesktopThumbnailResult(
                        candidate.AnonymousItemKey,
                        ProductDesktopThumbnailStatus.LoadingThumbnail,
                        CacheHit: false,
                        Frame: null),
                    StringComparer.Ordinal)
                : new Dictionary<string, ProductDesktopThumbnailResult>(
                    StringComparer.Ordinal);
        ApplyProductDesktopHostProjectionCore(
            desktopHostState,
            desktopHostReadModel.Snapshot,
            topology,
            workspaceRevision,
            loading);

        desktopThumbnailRefreshCancellation?.Cancel();
        desktopThumbnailRefreshCancellation?.Dispose();
        desktopThumbnailRefreshCancellation = new CancellationTokenSource();
        long refreshGeneration = checked(++desktopThumbnailRefreshGeneration);
        _ = RunProductDesktopThumbnailRefreshAsync(
            desktopHostState,
            desktopHostReadModel.Snapshot,
            topology,
            workspaceRevision,
            thumbnailsEnabled,
            candidates,
            refreshGeneration,
            desktopThumbnailRefreshCancellation.Token);
    }

    private void NormalizeDesktopItemViewports(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot)
    {
        if (state is null)
        {
            desktopItemViewportStarts.Clear();
            return;
        }
        var valid = state.Containers
            .Select((container, index) => new
            {
                container.Id,
                Count = readSnapshot is not null
                    && index < readSnapshot.Containers.Count
                        ? readSnapshot.Containers[index].Items.Count
                        : container.Items.Count,
            })
            .ToDictionary(
                entry => entry.Id,
                entry => entry.Count,
                StringComparer.Ordinal);
        foreach (string stale in desktopItemViewportStarts.Keys
            .Where(key => !valid.ContainsKey(key))
            .ToArray())
        {
            _ = desktopItemViewportStarts.Remove(stale);
        }
        foreach (string key in desktopItemViewportStarts.Keys.ToArray())
        {
            desktopItemViewportStarts[key] =
                ProductDesktopItemViewportPolicy.ClampStart(
                    desktopItemViewportStarts[key],
                    valid[key]);
        }
    }

    private void ApplyProductDesktopHostProjectionCore(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision,
        IReadOnlyDictionary<string, ProductDesktopThumbnailResult>
            thumbnails)
    {
        _ = productDesktopHostLifecycle.ApplyProjectionUpdate(
            ProductDesktopHostProjectionBuilder.BuildUpdate(
                state,
                readSnapshot,
                topology,
                workspaceRevision,
                thumbnails,
                desktopItemViewportStarts,
                checked(++desktopHostPresentationGeneration)));
    }

    private async Task RunProductDesktopThumbnailRefreshAsync(
        ProductWorkspaceState? state,
        ProductWorkspaceReadSnapshot? readSnapshot,
        ProductDisplayTopologySnapshot topology,
        long workspaceRevision,
        bool enabled,
        IReadOnlyList<ProductDesktopThumbnailCandidate> candidates,
        long refreshGeneration,
        CancellationToken cancellationToken)
    {
        ProductDesktopThumbnailRefreshResult result;
        try
        {
            result = await productDesktopThumbnails.RefreshAsync(
                enabled,
                candidates,
                pixelSize: 64,
                themeKey: RequestedTheme.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        MainWindow? currentWindow = window;
        if (currentWindow is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        _ = currentWindow.DispatcherQueue.TryEnqueue(() =>
        {
            bool currentEnabled =
                boxesSettingsController.Current.ThumbnailsEnabled
                && boxesSettingsController.Current.BoxesEnabled;
            if (cancellationToken.IsCancellationRequested
                || !ProductDesktopThumbnailRefreshAdmission.CanPublish(
                    refreshGeneration,
                    desktopThumbnailRefreshGeneration,
                    workspaceRevision,
                    workspaceCommits.CurrentEditRevision,
                    topology.Generation,
                    productDisplayTopology.Snapshot.Generation,
                    enabled,
                    currentEnabled)
                || (state is not null
                    && !ReferenceEquals(state, productWorkspaceSession.State)))
            {
                return;
            }
            IReadOnlyDictionary<string, ProductDesktopThumbnailResult> resolved =
                result.Results.ToDictionary(
                    item => item.AnonymousItemKey,
                    StringComparer.Ordinal);
            ApplyProductDesktopHostProjectionCore(
                state,
                readSnapshot,
                topology,
                workspaceRevision,
                resolved);
        });
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

    private ProductContainerState? CreateDefaultContainer(
        ProductWorkspaceState state,
        string? requestedName,
        string? requestedDisplayId,
        PixelRect? requestedBoundsPixels)
    {
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        DisplayTopologyNode? display = topology.IsAuthoritative
            ? string.IsNullOrWhiteSpace(requestedDisplayId)
                ? topology.Displays.FirstOrDefault(candidate => candidate.IsPrimary)
                    ?? (topology.Displays.Count > 0
                        ? topology.Displays[0]
                        : null)
                : topology.Displays.FirstOrDefault(candidate => string.Equals(
                    candidate.StableId,
                    requestedDisplayId,
                    StringComparison.Ordinal))
            : null;
        if (!string.IsNullOrWhiteSpace(requestedDisplayId) && display is null)
        {
            return null;
        }

        ProductWorkspaceContainerCreationDefaultsDecision decision =
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                state.Containers,
                requestedName,
                display?.StableId ?? "display-unassigned",
                display?.WorkArea ?? new(0, 0, 1920, 1040),
                display?.EffectiveDpi ?? 96,
                requestedBoundsPixels);
        if (!decision.CanCreate)
        {
            return null;
        }

        string? id = ProductWorkspaceContainerCreationDefaults.CreateUniqueId(
            state.Containers);
        if (id is null)
        {
            return null;
        }

        return new()
        {
            Id = id,
            Name = decision.Name!,
            Appearance = new()
            {
                Color = "#2563EB",
                Opacity = 0.88,
                Collapsed = false,
            },
            Placement = decision.Placement!,
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

    private Task<ProductAnonymousInteractionEvidenceCaptureResult>
        CaptureAnonymousInteractionEvidenceAsync()
    {
        ProductDesktopHostLifecycleSnapshot snapshot =
            productDesktopHostLifecycle.Snapshot;
        ProductAnonymousInteractionHostStatus hostStatus = snapshot.Status switch
        {
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy =>
                ProductAnonymousInteractionHostStatus.Disabled,
            ProductDesktopHostLifecycleStatus.DisabledByUser =>
                ProductAnonymousInteractionHostStatus.Disabled,
            ProductDesktopHostLifecycleStatus.AwaitingHost =>
                ProductAnonymousInteractionHostStatus.AwaitingHost,
            ProductDesktopHostLifecycleStatus.AwaitingWorkspace =>
                ProductAnonymousInteractionHostStatus.AwaitingWorkspace,
            ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology =>
                ProductAnonymousInteractionHostStatus.SuspendedUnsafeTopology,
            ProductDesktopHostLifecycleStatus.SuspendedSystemSurface =>
                ProductAnonymousInteractionHostStatus.SuspendedSystemSurface,
            ProductDesktopHostLifecycleStatus.ReadyReadOnly =>
                ProductAnonymousInteractionHostStatus.ReadyReadOnly,
            ProductDesktopHostLifecycleStatus.Faulted =>
                ProductAnonymousInteractionHostStatus.Faulted,
            ProductDesktopHostLifecycleStatus.Completed =>
                ProductAnonymousInteractionHostStatus.Completed,
            _ => throw new InvalidOperationException(
                "The DesktopHost lifecycle status is not finite."),
        };
        return configurationStore.CaptureAnonymousInteractionEvidenceAsync(
            new(
                hostStatus,
                snapshot.Generation,
                snapshot.WorkspaceRevision,
                snapshot.TopologyGeneration,
                snapshot.ExplicitInteractionActive,
                snapshot.SelectedItemCount,
                snapshot.FocusedItemAvailable,
                snapshot.SelectionRevision),
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
            ApplyProductWorkspaceSaveSnapshot(currentWindow, snapshot);
            return;
        }

        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () => ApplyProductWorkspaceSaveSnapshot(currentWindow, snapshot));
    }

    private void ApplyProductWorkspaceSaveSnapshot(
        MainWindow currentWindow,
        ProductWorkspaceSaveSnapshot snapshot)
    {
        if (pendingControlCenterContainerEdit is { } pendingEdit)
        {
            if (snapshot.Status == ProductWorkspaceSaveStatus.Saved
                && snapshot.SavedRevision == pendingEdit.SaveRevision)
            {
                pendingControlCenterContainerEdit = null;
            }
            else if (snapshot.Status == ProductWorkspaceSaveStatus.Failed
                && snapshot.CurrentRevision == pendingEdit.SaveRevision
                && workspaceCommits.CurrentEditRevision == pendingEdit.EditRevision
                && productWorkspaceSession.State is { } failedState)
            {
                pendingControlCenterContainerEdit = null;
                ProductWorkspaceContainerEditUndoCommitResult compensation =
                    workspaceCommits.CommitContainerEditUndo(
                        failedState,
                        pendingEdit.Token,
                        confirmed: true);
                if (compensation.IsAccepted)
                {
                    ApplyAcceptedProductWorkspaceDocument(
                        compensation.Document!,
                        productDesktopCatalog.Snapshot);
                    currentWindow.ApplyProductWorkspaceSaveState(
                        productWorkspaceSaves.Snapshot);
                    return;
                }
            }
            else if (workspaceCommits.CurrentEditRevision != pendingEdit.EditRevision
                || snapshot.CurrentRevision != pendingEdit.SaveRevision)
            {
                pendingControlCenterContainerEdit = null;
            }
        }

        ProductDesktopContainerDeleteResult deletePublication =
            desktopContainerDeletes.ObserveSave(
                productWorkspaceSession.State,
                workspaceCommits.CurrentEditRevision,
                snapshot);
        if (deletePublication.IsCompensated)
        {
            ApplyAcceptedProductWorkspaceDocument(
                deletePublication.Document!,
                productDesktopCatalog.Snapshot);
            currentWindow.ApplyDesktopContainerDeleteCommitResult(
                deletePublication);
            currentWindow.ApplyProductWorkspaceSaveState(
                productWorkspaceSaves.Snapshot);
            return;
        }

        ProductDesktopContainerHeaderCommandResult headerPublication =
            desktopContainerHeaderCommands.ObserveSave(
                productWorkspaceSession.State,
                workspaceCommits.CurrentEditRevision,
                snapshot);
        if (headerPublication.IsCompensated)
        {
            ApplyAcceptedProductWorkspaceDocument(
                headerPublication.Document!,
                productDesktopCatalog.Snapshot);
            currentWindow.ApplyProductWorkspaceSaveState(
                productWorkspaceSaves.Snapshot);
            return;
        }

        ProductDesktopContainerLayoutPublicationResult layoutPublication =
            desktopContainerLayoutInteractions.ObserveSave(
                productWorkspaceSession.State,
                workspaceCommits.CurrentEditRevision,
                snapshot);
        if (layoutPublication.IsCompensated)
        {
            ApplyAcceptedProductWorkspaceDocument(
                layoutPublication.Document!,
                productDesktopCatalog.Snapshot);
            currentWindow.ApplyProductWorkspaceSaveState(
                productWorkspaceSaves.Snapshot);
            return;
        }

        ProductDesktopWorkspaceCreatePublicationToken? publication =
            desktopWorkspaceCreatePublication;
        ProductWorkspaceState? state = productWorkspaceSession.State;
        if (publication is not null)
        {
            int createdOrdinal = state?.Containers
                .Select((container, index) => new { container.Id, Ordinal = index + 1 })
                .Where(candidate => string.Equals(
                    candidate.Id,
                    publication.ContainerId,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.Ordinal)
                .FirstOrDefault() ?? 0;
            ProductDesktopWorkspaceCreatePublicationDecision decision =
                ProductDesktopWorkspaceCreatePublication.Evaluate(
                    publication,
                    snapshot,
                    workspaceCommits.CurrentEditRevision,
                    createdOrdinal > 0);
            if (decision is ProductDesktopWorkspaceCreatePublicationDecision.Published
                or ProductDesktopWorkspaceCreatePublicationDecision.Superseded)
            {
                desktopWorkspaceCreatePublication = null;
            }
            else if (decision ==
                ProductDesktopWorkspaceCreatePublicationDecision.RollbackRequired)
            {
                desktopWorkspaceCreatePublication = null;
                if (publication.RestoreToken is { } restoreToken)
                {
                    ProductWorkspaceReferenceBatchAdditionUndoCommitResult restore =
                        CommitProductWorkspaceReferenceBatchAdditionUndo(
                            restoreToken,
                            confirmed: true);
                    if (restore.IsAccepted)
                    {
                        currentWindow.ApplyProductWorkspaceCreateSaveRollbackState(
                            snapshot.Failure,
                            productWorkspaceSaves.Snapshot.CurrentRevision);
                        return;
                    }
                    currentWindow.ApplyProductWorkspaceSaveState(snapshot);
                    return;
                }
                ProductWorkspaceContainerCommitResult rollback =
                    CommitProductWorkspaceContainerActionCore(
                        ProductWorkspaceContainerCommitAction.Remove,
                        publication.WorkspaceRevision,
                        createdOrdinal,
                        name: string.Empty,
                        stateValue: null,
                        colorPreset: null,
                        opacityPreset: null,
                        positionPreset: null,
                        sizePreset: null,
                        confirmed: true,
                        createDisplayId: null,
                        createBoundsPixels: null,
                        useDefaultName: false);
                if (rollback.IsAccepted)
                {
                    currentWindow.ApplyProductWorkspaceCreateSaveRollbackState(
                        snapshot.Failure,
                        productWorkspaceSaves.Snapshot.CurrentRevision);
                    return;
                }
            }
        }

        currentWindow.ApplyProductWorkspaceSaveState(snapshot);
    }

    private sealed record PendingControlCenterContainerEdit(
        ProductWorkspaceContainerEditUndoToken Token,
        long EditRevision,
        long SaveRevision);

    private async void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (closeAfterDrain)
        {
            return;
        }

        args.Cancel = true;
        CancelDesktopWorkspaceCreatePreview(
            ProductDesktopWorkspaceCreatePreviewFailure.WindowClosing);
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
            window.BoxesEnabledChangeRequested -=
                MainWindow_BoxesEnabledChangeRequested;
            window.ThumbnailsEnabledChangeRequested -=
                MainWindow_ThumbnailsEnabledChangeRequested;
            window.SingleClickOpenChangeRequested -=
                MainWindow_SingleClickOpenChangeRequested;
        }

        _ = productDesktopInteraction.Complete(DateTimeOffset.UtcNow);
        if (productResourceTelemetry is not null)
        {
            await productResourceTelemetry.DisposeAsync();
        }
        productThumbnailWorker.Dispose();
        folderContentWatcher.Invalidated -=
            ProductWorkspaceFolderContentWatcher_Invalidated;
        folderContentWatcher.Dispose();
        folderContentRefreshCancellation?.Cancel();
        folderContentRefreshCancellation?.Dispose();
        folderContentRefreshCancellation = null;
        desktopThumbnailRefreshCancellation?.Cancel();
        desktopThumbnailRefreshCancellation?.Dispose();
        desktopThumbnailRefreshCancellation = null;
        productDesktopThumbnails.Dispose();
        boxesSettingsController.Dispose();
        boxesSettingsStore.Dispose();
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

    private ProductResourceTelemetrySnapshot CaptureProductResourceTelemetry(
        long sequence)
    {
        LongGrid.Core.Configuration.ProductWorkspaceSaveSnapshot save =
            productWorkspaceSaves.Snapshot;
        ProductDesktopCatalogSnapshot catalog = productDesktopCatalog.Snapshot;
        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        ProductDesktopHostLifecycleSnapshot desktopHost =
            productDesktopHostLifecycle.Snapshot;
        ProductDesktopInteractionDevelopmentSnapshot interaction =
            productDesktopInteraction.Snapshot;
        ProductThumbnailWorkerLifecycleSnapshot worker =
            productThumbnailWorker.Snapshot;
        return new(
            1,
            sequence,
            DateTimeOffset.UtcNow,
            save.Status,
            save.CurrentRevision,
            save.SavedRevision,
            catalog.Status,
            catalog.Generation,
            catalog.Entries.Count,
            topology.Status,
            topology.Generation,
            topology.Displays.Count,
            desktopHost.Status,
            desktopHost.Generation,
            desktopHost.OwnedWindowCount,
            desktopHost.WorkspaceRevision,
            desktopHost.TopologyGeneration,
            desktopHost.RenderedContainerCount,
            desktopHost.ReadOnlyAccessibilityAvailable,
            desktopHost.PassiveWindowContractAttested,
            desktopHost.ExplicitInteractionActive,
            desktopHost.SelectionRevision,
            interaction.Status,
            interaction.Revision,
            worker.FormalIntegrationAvailable,
            worker.WorkerProcessCount,
            worker.ActiveOwnedProfileCount,
            productThumbnailWorker.OwnedProfileDeletionConfirmed,
            ContainsPathsNamesContentHandlesOrProcessIds: false);
    }
}
