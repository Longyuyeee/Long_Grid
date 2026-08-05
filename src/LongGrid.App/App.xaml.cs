using System.Diagnostics.CodeAnalysis;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;
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
    Justification = "WinUI owns the Application lifetime; the audited closing handler awaits both controllers before releasing the main instance.")]
public partial class App : Application
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly ProductConfigurationStore configurationStore;
    private readonly ProductWorkspaceSaveController productWorkspaceSaves;
    private readonly ProductDesktopCatalogController productDesktopCatalog;
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
        productDesktopCatalog = new(
            ProductDesktopCatalogReader.CreateForCurrentUser());
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
            RefreshProductDesktopCatalogAsync);
        productWorkspaceSaves.SnapshotChanged += ProductWorkspaceSaves_SnapshotChanged;
        productDesktopCatalog.SnapshotChanged += ProductDesktopCatalog_SnapshotChanged;
        window.ApplyProductWorkspaceSaveState(productWorkspaceSaves.Snapshot);
        window.ApplyProductWorkspaceSessionState(productWorkspaceSession);
        window.ApplyProductDesktopCatalogState(productDesktopCatalog.Snapshot);
        window.AppWindow.Closing += AppWindow_Closing;
        window.Activate();
        _ = LoadConfigurationStartupStateAsync();
        _ = RefreshProductDesktopCatalogAsync();

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
        ProductConfigurationStartupState startupState =
            ProductConfigurationStartupState.FromLoadResult(loadResult);
        productWorkspaceSession = ProductWorkspaceSessionLoader.Load(
            loadResult,
            CreateWorkspaceCatalogSnapshot(productDesktopCatalog.Snapshot));
        window?.ApplyConfigurationStartupState(startupState);
        window?.ApplyProductWorkspaceSessionState(productWorkspaceSession);
        return startupState;
    }

    private async Task RefreshProductDesktopCatalogAsync()
    {
        _ = await productDesktopCatalog.RefreshAsync();
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
        window?.ApplyProductDesktopCatalogState(snapshot);
        if (currentConfigurationLoadResult is null)
        {
            return;
        }

        productWorkspaceSession = ProductWorkspaceSessionLoader.Load(
            currentConfigurationLoadResult,
            CreateWorkspaceCatalogSnapshot(snapshot));
        window?.ApplyProductWorkspaceSessionState(productWorkspaceSession);
    }

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
        await productDesktopCatalog.DisposeAsync();
        await productWorkspaceSaves.DisposeAsync();

        closeAfterDrain = true;
        Program.ReleaseMainInstance();
        productWorkspaceSaves.SnapshotChanged -= ProductWorkspaceSaves_SnapshotChanged;
        sender.Closing -= AppWindow_Closing;
        window?.Close();
    }
}
