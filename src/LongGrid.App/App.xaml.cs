using LongGrid.Infrastructure.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LongGrid.App;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly ProductConfigurationStore configurationStore;
    private readonly ProductConfigurationSaveCoordinator configurationSaves;
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
        configurationSaves = new(configurationStore);
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
            ExportConfigurationEvidenceAsync);
        window.AppWindow.Closing += AppWindow_Closing;
        window.Activate();
        _ = LoadConfigurationStartupStateAsync();

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
        window?.ApplyConfigurationStartupState(
            ProductConfigurationStartupState.FromLoadResult(loadResult));
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
        return ProductConfigurationStartupState.FromLoadResult(loadResult);
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
        return ProductConfigurationStartupState.FromLoadResult(loadResult);
    }

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
        try
        {
            await configurationSaves.CompleteAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            closingDrainInProgress = false;
            return;
        }

        closeAfterDrain = true;
        Program.ReleaseMainInstance();
        sender.Closing -= AppWindow_Closing;
        window?.Close();
    }
}
