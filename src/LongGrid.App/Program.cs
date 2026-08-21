using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppActivationArguments activation = AppInstance
            .GetCurrent()
            .GetActivatedEventArgs();
        string instanceKey = ProductPf002AppEvidenceSession.ResolveInstanceKey(
            MainInstanceKey);
        AppInstance mainInstance = AppInstance.FindOrRegisterForKey(instanceKey);

        if (!mainInstance.IsCurrent)
        {
            try
            {
                await mainInstance.RedirectActivationToAsync(activation);
                TryBringToForeground(mainInstance.ProcessId);
                return 0;
            }
            catch
            {
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

                App app = new();
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
