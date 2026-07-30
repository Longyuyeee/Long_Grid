using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.DesktopHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
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
        if (options.BatchTransaction)
        {
            WindowBatchTransactionReport transactionReport =
                WindowBatchTransactionProbe.Run(perMonitorV2Requested);
            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        transactionReport,
                        JsonOptions));
            }
            else
            {
                Console.WriteLine(transactionReport.Probe);
                Console.WriteLine(
                    $"Applied: {transactionReport.AppliedStatus}");
                Console.WriteLine(
                    $"Generation rollback: "
                    + $"{transactionReport.GenerationRollbackStatus}");
                Console.WriteLine(
                    $"Partial rollback: "
                    + $"{transactionReport.PartialFailureRollbackStatus}");
                Console.WriteLine($"Result: {transactionReport.Result}");
            }

            return transactionReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

        if (options.RegionTransaction)
        {
            WindowRegionTransactionReport regionReport =
                WindowRegionTransactionProbe.Run(
                    perMonitorV2Requested);
            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        regionReport,
                        JsonOptions));
            }
            else
            {
                Console.WriteLine(regionReport.Probe);
                Console.WriteLine(
                    $"Applied: {regionReport.AppliedStatus}");
                Console.WriteLine(
                    $"Generation rollback: "
                    + $"{regionReport.GenerationRollbackStatus}");
                Console.WriteLine(
                    $"Partial rollback: "
                    + $"{regionReport.PartialRollbackStatus}");
                Console.WriteLine($"Result: {regionReport.Result}");
            }

            return regionReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

        if (options.CompositionUiaGeneration)
        {
            CompositionUiaGenerationReport compositionReport =
                CompositionUiaGenerationProbe.Run(
                    perMonitorV2Requested);
            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        compositionReport,
                        JsonOptions));
            }
            else
            {
                Console.WriteLine(compositionReport.Probe);
                Console.WriteLine(
                    $"Applied generation: "
                    + $"{compositionReport.AppliedGeneration}");
                Console.WriteLine(
                    $"Superseded rollback: "
                    + $"{compositionReport.SupersededRollbackVerified}");
                Console.WriteLine(
                    $"UIA client verified: "
                    + $"{compositionReport.UiaClientVerified}");
                Console.WriteLine($"Result: {compositionReport.Result}");
            }

            return compositionReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

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
              --batch-transaction  Run the hidden HWND batch/rollback probe.
              --region-transaction Run the Window Region ownership/rollback probe.
              --composition-uia     Run the DirectComposition/UIA generation probe.
              --json               Write a machine-readable report.
              --help               Show this help.
            """);
    }
}

internal sealed record ProbeOptions(
    bool Json,
    bool ShowHelp,
    bool BatchTransaction,
    bool RegionTransaction,
    bool CompositionUiaGeneration)
{
    internal static ProbeOptions Parse(IEnumerable<string> args)
    {
        bool json = false;
        bool showHelp = false;
        bool batchTransaction = false;
        bool regionTransaction = false;
        bool compositionUiaGeneration = false;

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
                case "--batch-transaction":
                    batchTransaction = true;
                    break;
                case "--region-transaction":
                    regionTransaction = true;
                    break;
                case "--composition-uia":
                    compositionUiaGeneration = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        if ((batchTransaction ? 1 : 0)
            + (regionTransaction ? 1 : 0)
            + (compositionUiaGeneration ? 1 : 0) > 1)
        {
            throw new ArgumentException(
                "Choose only one transaction probe mode.");
        }

        return new ProbeOptions(
            json,
            showHelp,
            batchTransaction,
            regionTransaction,
            compositionUiaGeneration);
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
