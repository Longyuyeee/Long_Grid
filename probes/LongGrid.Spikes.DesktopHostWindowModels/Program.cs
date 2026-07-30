using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.DesktopHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintHelp();
            return 64;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Console.Error.WriteLine("P0-04/P0-05a requires Windows 10 version 1809 or later.");
            return 3;
        }

        bool perMonitorV2Requested =
            NativeMethods.SetProcessDpiAwarenessContext(
                NativeMethods.PerMonitorAwareV2);
        ProbeScenario scenario = WindowModelProbe.CreateScenario();
        _ = WindowModelProbe.Run(DesktopHostWindowModel.PerDisplay, scenario);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        ModelAuditResult perContainer = WindowModelProbe.Run(
            DesktopHostWindowModel.PerContainer,
            scenario);
        ModelAuditResult perDisplay = WindowModelProbe.Run(
            DesktopHostWindowModel.PerDisplay,
            scenario);

        var report = new ProbeReport(
            Probe: "P0-04-P0-05a-desktop-host-window-models",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            WorkAreaWidth: scenario.WorkArea.Width,
            WorkAreaHeight: scenario.WorkArea.Height,
            ContainerCount: scenario.Containers.Count,
            PerContainer: perContainer,
            PerDisplay: perDisplay,
            InteractiveSystemStateChanged: false,
            PreferredModelForNextPrototype: "PerDisplayWithExplicitInteractiveRegions",
            Result: "Conditional Pass",
            Limitations:
            [
                "The probe does not synthesize mouse or keyboard input and does not invoke Win+D.",
                "Alt+Tab and taskbar exclusion are backed by WS_EX_TOOLWINDOW semantics, not UI automation of the switcher.",
                "Narrator, UI Automation fragments, drag-and-drop, touch, pen, Peek, full-screen suppression, and Explorer restart remain unverified.",
                "Only the primary display and current DPI/session/build were measured.",
                "SetWindowRgn validates rectangular interactive islands; rounded visuals and resize animation require region synchronization or a different documented input strategy.",
                "The nearly transparent probe windows are visible only for the duration of local sampling and never become topmost or foreground.",
            ]);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintText(report);
        }

        bool perContainerPassed = Passed(perContainer);
        bool perDisplayPassed = Passed(perDisplay);
        bool comparisonPassed =
            perDisplay.SurfaceCount < perContainer.SurfaceCount
            && PeakUserDelta(perDisplay) < PeakUserDelta(perContainer);
        return perMonitorV2Requested
            && perContainerPassed
            && perDisplayPassed
            && comparisonPassed
            ? 0
            : 2;
    }

    private static bool Passed(ModelAuditResult result) =>
        result.InsideHits == result.ExpectedInsideHits
        && result.GapEscapes == result.GapSamples
        && result.ExternalProcessGapHits == result.GapSamples
        && result.ActivationPreserved
        && result.PassiveStylesPresent
        && result.TopmostStyleAbsent
        && result.CleanupPassed;

    private static long PeakUserDelta(ModelAuditResult result) =>
        (long)result.UserObjectsCreated - result.UserObjectsBefore;

    private static void PrintText(ProbeReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine($"Containers: {report.ContainerCount}");
        PrintModel(report.PerContainer);
        PrintModel(report.PerDisplay);
        Console.WriteLine($"Result: {report.Result}");
        Console.WriteLine(
            $"Preferred next prototype: {report.PreferredModelForNextPrototype}");
    }

    private static void PrintModel(ModelAuditResult result)
    {
        Console.WriteLine(
            $"{result.Model}: surfaces {result.SurfaceCount}; "
            + $"inside {result.InsideHits}/{result.ExpectedInsideHits}; "
            + $"gaps {result.GapEscapes}/{result.GapSamples}; "
            + $"external gaps {result.ExternalProcessGapHits}; "
            + $"USER {result.UserObjectsBefore}->{result.UserObjectsCreated}->{result.UserObjectsAfter}; "
            + $"GDI {result.GdiObjectsBefore}->{result.GdiObjectsCreated}->{result.GdiObjectsAfter}; "
            + $"cleanup {result.CleanupPassed}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LongGrid.Spikes.DesktopHostWindowModels

            P0-04/P0-05a compares per-container and per-display native HWND models.
            It creates nearly transparent, non-activating tool windows temporarily,
            samples hit testing without synthetic input, and restores all resources.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels -- [options]

            Options:
              --json  Write a machine-readable report.
              --help  Show this help.
            """);
    }
}

internal sealed record ProbeOptions(bool Json, bool ShowHelp)
{
    internal static ProbeOptions Parse(IEnumerable<string> args)
    {
        bool json = false;
        bool showHelp = false;

        foreach (string argument in args)
        {
            switch (argument)
            {
                case "--json":
                    json = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        return new ProbeOptions(json, showHelp);
    }
}

internal sealed record ProbeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int WorkAreaWidth,
    int WorkAreaHeight,
    int ContainerCount,
    ModelAuditResult PerContainer,
    ModelAuditResult PerDisplay,
    bool InteractiveSystemStateChanged,
    string PreferredModelForNextPrototype,
    string Result,
    IReadOnlyList<string> Limitations);
