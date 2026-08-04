using LongGrid.Infrastructure.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

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
        window = new MainWindow(RecoverConfigurationAsync);
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
