using LongGrid.Infrastructure.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace LongGrid.App;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly ProductConfigurationSaveCoordinator configurationSaves;
    private MainWindow? window;
    private bool closeAfterDrain;
    private bool closingDrainInProgress;

    public App()
    {
        InitializeComponent();
        string configurationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LongGrid");
        configurationSaves = new(
            new ProductConfigurationStore(configurationDirectory));
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.AppWindow.Closing += AppWindow_Closing;
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
        sender.Closing -= AppWindow_Closing;
        window?.Close();
    }
}
