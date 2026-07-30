using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.DesktopHost;

internal static class Program
{
    private const int IterationCount = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

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
            Console.Error.WriteLine("P0-07a requires Windows 10 version 1809 or later.");
            return 3;
        }

        bool perMonitorV2Requested =
            NativeMethods.SetProcessDpiAwarenessContext(
                NativeMethods.PerMonitorAwareV2);
        _ = DisplayEnumerator.Enumerate();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot resourcesBefore = ResourceSnapshot.Capture(process);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var durations = new long[IterationCount];
        DisplayEnumerationResult? last = null;

        for (int index = 0; index < IterationCount; index++)
        {
            long started = Stopwatch.GetTimestamp();
            last = DisplayEnumerator.Enumerate();
            fingerprints.Add(
                DisplayTopologyFingerprint.Compute(last.Displays));
            durations[index] = Stopwatch.GetTimestamp() - started;
        }

        ResourceSnapshot resourcesAfter = ResourceSnapshot.Capture(process);
        DisplayEnumerationResult result = last
            ?? throw new InvalidOperationException("No display snapshot was captured.");
        PixelRect virtualScreen = DisplayEnumerator.GetVirtualScreenBounds();
        PixelRect displayBoundingBox = Union(result.Displays.Select(display =>
            display.Bounds));
        uint[] dpiValues = result.Displays
            .Select(display => display.EffectiveDpi)
            .Distinct()
            .Order()
            .ToArray();
        Array.Sort(durations);

        var report = new ProbeReport(
            Probe: "P0-07a-display-topology-and-dpi",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            Iterations: IterationCount,
            MonitorCount: result.Displays.Count,
            PrimaryMonitorCount: result.Displays.Count(display =>
                display.IsPrimary),
            StrongIdentityCount: result.StrongIdentityCount,
            FallbackIdentityCount: result.FallbackIdentityCount,
            DistinctTopologyFingerprints: fingerprints.Count,
            EffectiveDpiValues: dpiValues,
            MixedDpi: dpiValues.Length > 1,
            HasNegativeVirtualCoordinates: result.Displays.Any(display =>
                display.Bounds.Left < 0 || display.Bounds.Top < 0),
            WorkAreasInsideMonitorBounds: result.Displays.Count(display =>
                Contains(display.Bounds, display.WorkArea)),
            VirtualScreenMatchesMonitorBoundingBox:
                virtualScreen == displayBoundingBox,
            P50EnumerationMilliseconds: PercentileMilliseconds(durations, 0.50),
            P95EnumerationMilliseconds: PercentileMilliseconds(durations, 0.95),
            UserObjectsBefore: resourcesBefore.UserObjects,
            UserObjectsAfter: resourcesAfter.UserObjects,
            GdiObjectsBefore: resourcesBefore.GdiObjects,
            GdiObjectsAfter: resourcesAfter.GdiObjects,
            ProcessHandlesBefore: resourcesBefore.ProcessHandles,
            ProcessHandlesAfter: resourcesAfter.ProcessHandles,
            Result: "Conditional Pass",
            Privacy:
            [
                "No display name, PNP identifier, device path, serial-like value, or raw topology fingerprint is printed.",
                "Device identity is hashed in memory and used only to compare snapshots within this local process.",
                "The probe is read-only and does not change resolution, scale, orientation, topology, brightness, or color settings.",
            ],
            Limitations:
            [
                "The current static topology was sampled; no monitor was attached, detached, rotated, or rescaled.",
                "Landscape versus portrait is inferred from bounds; flipped orientation requires QueryDisplayConfig or display settings data.",
                "Device ID or device-key availability is provider-dependent and is not guaranteed across driver reinstall, RDP, or virtualization.",
                "WM_DPICHANGED handling, suggested rectangles, negative-coordinate movement, and live layout remapping remain P0-07b.",
                "Only the current Windows build, session, GPU driver, and hardware topology were measured.",
            ]);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintText(report);
        }

        bool passed = perMonitorV2Requested
            && result.Displays.Count > 0
            && report.PrimaryMonitorCount == 1
            && report.DistinctTopologyFingerprints == 1
            && report.WorkAreasInsideMonitorBounds == result.Displays.Count
            && report.VirtualScreenMatchesMonitorBoundingBox
            && resourcesAfter.UserObjects <= resourcesBefore.UserObjects + 1
            && resourcesAfter.GdiObjects <= resourcesBefore.GdiObjects + 1
            && resourcesAfter.ProcessHandles <= resourcesBefore.ProcessHandles + 2;
        return passed ? 0 : 2;
    }

    private static PixelRect Union(IEnumerable<PixelRect> rectangles)
    {
        PixelRect[] values = rectangles.ToArray();
        int left = values.Min(value => value.Left);
        int top = values.Min(value => value.Top);
        int right = values.Max(value => value.Right);
        int bottom = values.Max(value => value.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;

    private static double PercentileMilliseconds(
        long[] sortedStopwatchTicks,
        double percentile)
    {
        int index = (int)Math.Ceiling(
            percentile * sortedStopwatchTicks.Length) - 1;
        return sortedStopwatchTicks[Math.Max(index, 0)]
            * 1000d
            / Stopwatch.Frequency;
    }

    private static void PrintText(ProbeReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine($"Monitors: {report.MonitorCount}");
        Console.WriteLine(
            $"Topology fingerprints: {report.DistinctTopologyFingerprints}");
        Console.WriteLine(
            $"DPI values: {string.Join(", ", report.EffectiveDpiValues)}");
        Console.WriteLine(
            $"Enumeration p50/p95: {report.P50EnumerationMilliseconds:F3}/"
            + $"{report.P95EnumerationMilliseconds:F3} ms");
        Console.WriteLine($"Result: {report.Result}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LongGrid.Spikes.DisplayTopology

            P0-07a read-only display topology, effective DPI, identity fallback,
            fingerprint stability, coordinate, and native-resource audit.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.DisplayTopology -- [options]

            Options:
              --json  Write a machine-readable, redacted report.
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

internal sealed record ResourceSnapshot(
    uint UserObjects,
    uint GdiObjects,
    int ProcessHandles)
{
    internal static ResourceSnapshot Capture(Process process) =>
        new(
            NativeMethods.GetGuiResources(
                process.Handle,
                NativeMethods.GrUserObjects),
            NativeMethods.GetGuiResources(
                process.Handle,
                NativeMethods.GrGdiObjects),
            process.HandleCount);
}

internal sealed record ProbeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int Iterations,
    int MonitorCount,
    int PrimaryMonitorCount,
    int StrongIdentityCount,
    int FallbackIdentityCount,
    int DistinctTopologyFingerprints,
    IReadOnlyList<uint> EffectiveDpiValues,
    bool MixedDpi,
    bool HasNegativeVirtualCoordinates,
    int WorkAreasInsideMonitorBounds,
    bool VirtualScreenMatchesMonitorBoundingBox,
    double P50EnumerationMilliseconds,
    double P95EnumerationMilliseconds,
    uint UserObjectsBefore,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesAfter,
    string Result,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);
