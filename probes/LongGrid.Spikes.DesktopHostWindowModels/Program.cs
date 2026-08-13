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

        if (options.CompositeTransaction)
        {
            CompositeDesktopHostTransactionReport compositeReport =
                CompositeDesktopHostTransactionProbe.Run(
                    perMonitorV2Requested);
            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        compositeReport,
                        JsonOptions));
            }
            else
            {
                Console.WriteLine(compositeReport.Probe);
                Console.WriteLine(
                    $"Successful transaction: "
                    + $"{compositeReport.SuccessfulStatus}");
                Console.WriteLine(
                    $"Forced rollbacks: "
                    + $"{compositeReport.ForcedLayerFailuresRolledBack}"
                    + $"/{compositeReport.ForcedLayerFailureCount}");
                Console.WriteLine(
                    $"Emergency fallback: "
                    + $"{compositeReport.EmergencyStatus}");
                Console.WriteLine($"Result: {compositeReport.Result}");
            }

            return compositeReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

        if (options.VisibleInputUiaFragment)
        {
            VisibleInputUiaFragmentReport visibleReport =
                VisibleInputUiaFragmentProbe.Run(
                    perMonitorV2Requested);
            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        visibleReport,
                        JsonOptions));
            }
            else
            {
                Console.WriteLine(visibleReport.Probe);
                Console.WriteLine(
                    $"Input open hits: "
                    + $"{visibleReport.OpenHitCount}"
                    + $"/{visibleReport.ContainerCount}");
                Console.WriteLine(
                    $"Input closed escapes: "
                    + $"{visibleReport.ClosedEscapeCount}"
                    + $"/{visibleReport.ContainerCount}");
                Console.WriteLine(
                    $"UIA Fragment tree: "
                    + $"{visibleReport.UiaTreeVerified}");
                Console.WriteLine($"Result: {visibleReport.Result}");
            }

            return visibleReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

        if (options.InteractiveSliceSmoke)
        {
            InteractiveDesktopHostSliceReport sliceReport =
                InteractiveDesktopHostSliceProbe.RunSmoke(
                    perMonitorV2Requested);
            Console.WriteLine(
                options.Json
                    ? JsonSerializer.Serialize(
                        sliceReport,
                        JsonOptions)
                    : $"{sliceReport.Probe}{Environment.NewLine}"
                        + $"UIA tree: {sliceReport.UiaTreeVerified}"
                        + $"{Environment.NewLine}"
                        + $"Patterns: {sliceReport.PatternsVerified}"
                        + $"{Environment.NewLine}"
                        + $"Result: {sliceReport.Result}");
            return sliceReport.Result == "Conditional Pass"
                ? 0
                : 2;
        }

        if (options.NativeInteractionSurfaceMode)
        {
            NativeInteractionSurfaceModeReport nativeReport =
                NativeInteractionSurfaceModeProbe.Run(perMonitorV2Requested);
            Console.WriteLine(
                options.Json
                    ? JsonSerializer.Serialize(nativeReport, JsonOptions)
                    : $"{nativeReport.Probe}{Environment.NewLine}"
                        + $"Round trip: "
                        + $"{nativeReport.PassiveExplicitPassiveRoundTrip}"
                        + $"{Environment.NewLine}"
                        + $"Cleanup: {nativeReport.CleanupPassed}"
                        + $"{Environment.NewLine}"
                        + $"Result: {nativeReport.Result}");
            return nativeReport.Result == "Conditional Pass" ? 0 : 2;
        }

        if (options.NativeInputForwardingSource)
        {
            NativeInputForwardingSourceReport inputReport =
                NativeInputForwardingSourceProbe.Run(perMonitorV2Requested);
            Console.WriteLine(
                options.Json
                    ? JsonSerializer.Serialize(inputReport, JsonOptions)
                    : $"{inputReport.Probe}{Environment.NewLine}"
                        + $"Pointer: {inputReport.PointerMessagePreparedOnce}"
                        + $"{Environment.NewLine}"
                        + $"Keyboard: {inputReport.KeyboardMessagePreparedOnce}"
                        + $"{Environment.NewLine}"
                        + $"UIA Invoke: {inputReport.UiaInvokePreparedOnce}"
                        + $"{Environment.NewLine}"
                        + $"Result: {inputReport.Result}");
            return inputReport.Result == "Conditional Pass" ? 0 : 2;
        }

        if (options.NativeInputForwardingSession)
        {
            return NativeInputForwardingSourceProbe.RunInteractive(
                perMonitorV2Requested);
        }

        if (options.NativeInputSystemSurfaceSession)
        {
            return NativeInputForwardingSourceProbe
                .RunSystemSurfaceInteractive(perMonitorV2Requested);
        }

        if (options.InteractiveSlice)
        {
            return InteractiveDesktopHostSliceProbe.RunInteractive(
                perMonitorV2Requested);
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
              --composite-transaction
                                    Run the four-layer compensation probe.
              --visible-input-uia   Run the visible input/UIA Fragment probe.
              --interactive-slice  Run the manual visible DesktopHost slice;
                                   use keyboard/mouse and press Esc to close.
              --interactive-slice-smoke
                                   Run its non-input automated smoke probe.
              --native-interaction-surface
                                   Run the B5 probe-owned HWND mode adapter.
              --native-input-forwarding
                                   Run the B6c4 probe-owned HWND input source.
              --native-input-forwarding-session
                                   Run the B6c5 acknowledged visible manual source;
                                   press Escape or close it to destroy the source.
              --native-input-system-surface-session
                                   Run the B6c6 acknowledged source with public
                                   Windows system-surface observation enabled.
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
    bool CompositionUiaGeneration,
    bool CompositeTransaction,
    bool VisibleInputUiaFragment,
    bool InteractiveSlice,
    bool InteractiveSliceSmoke,
    bool NativeInteractionSurfaceMode,
    bool NativeInputForwardingSource,
    bool NativeInputForwardingSession,
    bool NativeInputSystemSurfaceSession)
{
    internal static ProbeOptions Parse(IEnumerable<string> args)
    {
        bool json = false;
        bool showHelp = false;
        bool batchTransaction = false;
        bool regionTransaction = false;
        bool compositionUiaGeneration = false;
        bool compositeTransaction = false;
        bool visibleInputUiaFragment = false;
        bool interactiveSlice = false;
        bool interactiveSliceSmoke = false;
        bool nativeInteractionSurfaceMode = false;
        bool nativeInputForwardingSource = false;
        bool nativeInputForwardingSession = false;
        bool nativeInputSystemSurfaceSession = false;

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
                case "--composite-transaction":
                    compositeTransaction = true;
                    break;
                case "--visible-input-uia":
                    visibleInputUiaFragment = true;
                    break;
                case "--interactive-slice":
                    interactiveSlice = true;
                    break;
                case "--interactive-slice-smoke":
                    interactiveSliceSmoke = true;
                    break;
                case "--native-interaction-surface":
                    nativeInteractionSurfaceMode = true;
                    break;
                case "--native-input-forwarding":
                    nativeInputForwardingSource = true;
                    break;
                case "--native-input-forwarding-session":
                    nativeInputForwardingSession = true;
                    break;
                case "--native-input-system-surface-session":
                    nativeInputSystemSurfaceSession = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        if ((batchTransaction ? 1 : 0)
            + (regionTransaction ? 1 : 0)
            + (compositionUiaGeneration ? 1 : 0)
            + (compositeTransaction ? 1 : 0)
            + (visibleInputUiaFragment ? 1 : 0)
            + (interactiveSlice ? 1 : 0)
            + (interactiveSliceSmoke ? 1 : 0)
            + (nativeInteractionSurfaceMode ? 1 : 0)
            + (nativeInputForwardingSource ? 1 : 0)
            + (nativeInputForwardingSession ? 1 : 0)
            + (nativeInputSystemSurfaceSession ? 1 : 0) > 1)
        {
            throw new ArgumentException(
                "Choose only one transaction probe mode.");
        }

        return new ProbeOptions(
            json,
            showHelp,
            batchTransaction,
            regionTransaction,
            compositionUiaGeneration,
            compositeTransaction,
            visibleInputUiaFragment,
            interactiveSlice,
            interactiveSliceSmoke,
            nativeInteractionSurfaceMode,
            nativeInputForwardingSource,
            nativeInputForwardingSession,
            nativeInputSystemSurfaceSession);
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
