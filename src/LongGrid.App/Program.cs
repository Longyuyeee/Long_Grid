using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace LongGrid.App;

public static class Program
{
    private const string MainInstanceKey = "LongGrid.Main";
    private static readonly object ActivationGate = new();
    private static readonly ConcurrentQueue<AppActivationArguments> PendingActivations = new();
    private static App? runningApp;
    private static AppInstance? registeredMainInstance;

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        ProductStartupDiagnosticLog.Current.RecordStage(ProductStartupStage.ProcessStarting);
        System.UnhandledExceptionEventHandler handler = (_, eventArgs) =>
            ProductStartupDiagnosticLog.Current.RecordFailure(
                ProductStartupFailureOrigin.ManagedUnhandled, eventArgs.ExceptionObject as Exception);
        AppDomain.CurrentDomain.UnhandledException += handler;
        try
        {
            int result = await RunAsync(args);
            ProductStartupDiagnosticLog.Current.RecordStage(ProductStartupStage.Exited);
            return result;
        }
        catch (Exception exception)
        {
            ProductStartupDiagnosticLog.Current.RecordFailure(ProductStartupFailureOrigin.EntryPoint, exception);
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= handler;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppActivationArguments activation = AppInstance
            .GetCurrent()
            .GetActivatedEventArgs();
        string instanceKey = ProductM1ManualEvidenceSession.ResolveInstanceKey(
            ProductBoxesRuntimeEnableEvidenceSession.ResolveInstanceKey(
                ProductBoxR1ActivationEvidenceSession.ResolveInstanceKey(
                    ProductUiR1eEvidenceSession.ResolveInstanceKey(
                        ProductDesktopFirstStartupEvidenceSession.ResolveInstanceKey(
                            ProductPf002AppEvidenceSession.ResolveInstanceKey(
                                MainInstanceKey))))));
        AppInstance mainInstance = AppInstance.FindOrRegisterForKey(instanceKey);
        ProductStartupDiagnosticLog.Current.RecordStage(ProductStartupStage.InstanceResolved);
        ProductM1ManualEvidenceSession.TryRecordStage(
            mainInstance.IsCurrent
                ? "AppInstanceCurrent"
                : "AppInstanceRedirected");

        if (!mainInstance.IsCurrent)
        {
            try
            {
                await mainInstance.RedirectActivationToAsync(activation);
                if (ProductExplorerCreateActivation.Parse(
                        App.GetLaunchActivationArguments(activation),
                        DateTimeOffset.UtcNow).ShouldForegroundControlCenter)
                {
                    TryBringToForeground(mainInstance.ProcessId);
                }
                return 0;
            }
            catch (Exception exception)
            {
                ProductStartupDiagnosticLog.Current.RecordFailure(ProductStartupFailureOrigin.ActivationRedirect, exception);
                // A failed redirect must not create a competing desktop owner.
                return 1;
            }
        }

        registeredMainInstance = mainInstance;
        mainInstance.Activated += MainInstance_Activated;
        try
        {
            Application.Start(_ =>
            {
                DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(dispatcher));

                ProductStartupDiagnosticLog.Current.RecordStage(ProductStartupStage.AppConstructing);
                App app = new();
                ProductStartupDiagnosticLog.Current.RecordStage(ProductStartupStage.AppConstructed);
                ProductM1ManualEvidenceSession.TryRecordStage("AppConstructed");
                Attach(app);
            });
        }
        finally
        {
            ReleaseMainInstance();
        }

        return 0;
    }

    internal static void ReleaseMainInstance()
    {
        AppInstance? instance = Interlocked.Exchange(
            ref registeredMainInstance,
            null);
        if (instance is null)
        {
            return;
        }

        instance.Activated -= MainInstance_Activated;
        instance.UnregisterKey();
    }

    private static void MainInstance_Activated(
        object? sender,
        AppActivationArguments activation)
    {
        App? app;
        lock (ActivationGate)
        {
            app = runningApp;
            if (app is null)
            {
                PendingActivations.Enqueue(activation);
                return;
            }
        }

        app.HandleActivation(activation);
    }

    private static void Attach(App app)
    {
        lock (ActivationGate)
        {
            while (PendingActivations.TryDequeue(
                out AppActivationArguments? activation))
            {
                app.HandleActivation(activation);
            }

            runningApp = app;
        }
    }

    private static void TryBringToForeground(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            nint windowHandle = process.MainWindowHandle;
            if (windowHandle != nint.Zero)
            {
                _ = SetForegroundWindow(windowHandle);
            }
        }
        catch (ArgumentException)
        {
            // The primary may finish closing after accepting the redirect.
        }
        catch (InvalidOperationException)
        {
            // The primary may finish closing after accepting the redirect.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
