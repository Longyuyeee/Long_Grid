using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.DesktopHost;

internal static class Program
{
    private const int IterationCount = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
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
        _ = CaptureSnapshot();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using Process process = Process.GetCurrentProcess();
        ResourceSnapshot resourcesBefore = ResourceSnapshot.Capture(process);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var pathFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var durations = new long[IterationCount];
        int maximumBufferAttempts = 0;
        CombinedDisplaySnapshot? last = null;

        for (int index = 0; index < IterationCount; index++)
        {
            long started = Stopwatch.GetTimestamp();
            last = CaptureSnapshot();
            fingerprints.Add(
                DisplayTopologyFingerprint.Compute(last.Displays.Displays));
            pathFingerprints.Add(
                ComputePathFingerprint(last.Configuration.ActivePaths));
            maximumBufferAttempts = Math.Max(
                maximumBufferAttempts,
                last.Configuration.BufferAttempts);
            durations[index] = Stopwatch.GetTimestamp() - started;
        }

        ResourceSnapshot resourcesAfter = ResourceSnapshot.Capture(process);
        CombinedDisplaySnapshot snapshot = last
            ?? throw new InvalidOperationException("No display snapshot was captured.");
        DisplayEnumerationResult result = snapshot.Displays;
        DisplayConfigurationResult configuration = snapshot.Configuration;
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
            Probe: "P0-07b1-displayconfig-path-identity",
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
            ActiveDisplayPathCount: configuration.ActivePaths.Count,
            DisplayConfigMappingCount: result.DisplayConfigMappingCount,
            SourceBoundsMatchCount: result.SourceBoundsMatchCount,
            AvailableTargetCount: configuration.ActivePaths.Count(path =>
                path.TargetAvailable),
            TargetDevicePathCount: configuration.ActivePaths.Count(path =>
                path.HasMonitorDevicePath),
            VirtualModePathCount: configuration.ActivePaths.Count(path =>
                path.UsesVirtualMode),
            DistinctPathFingerprints: pathFingerprints.Count,
            MaximumBufferAttempts: maximumBufferAttempts,
            Rotations: configuration.ActivePaths
                .Select(path => path.Rotation)
                .Distinct()
                .Order()
                .ToArray(),
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
                "DisplayConfig adapter LUIDs, target IDs, monitor paths, GDI source names, EDID values, and path fingerprints are not printed.",
                "The probe is read-only and does not change resolution, scale, orientation, topology, brightness, or color settings.",
            ],
            Limitations:
            [
                "The current static session was measured; no display transition was induced.",
                "Adapter LUID and target IDs are session-scoped correlation data, not durable hardware identities.",
                "Monitor device paths may change across driver reinstall, RDP, virtualization, or hardware replacement.",
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
            && report.DistinctPathFingerprints == 1
            && report.ActiveDisplayPathCount == result.Displays.Count
            && report.DisplayConfigMappingCount == result.Displays.Count
            && report.SourceBoundsMatchCount == result.Displays.Count
            && report.AvailableTargetCount == result.Displays.Count
            && report.TargetDevicePathCount == result.Displays.Count
            && report.WorkAreasInsideMonitorBounds == result.Displays.Count
            && report.VirtualScreenMatchesMonitorBoundingBox
            && resourcesAfter.UserObjects <= resourcesBefore.UserObjects + 1
            && resourcesAfter.GdiObjects <= resourcesBefore.GdiObjects + 1
            && resourcesAfter.ProcessHandles <= resourcesBefore.ProcessHandles + 2;
        return passed ? 0 : 2;
    }

    private static CombinedDisplaySnapshot CaptureSnapshot()
    {
        DisplayConfigurationResult configuration =
            DisplayConfigurationEnumerator.EnumerateActivePaths();
        var pathsBySource = configuration.ActivePaths.ToDictionary(
            path => path.SourceName,
            StringComparer.OrdinalIgnoreCase);
        DisplayEnumerationResult displays =
            DisplayEnumerator.Enumerate(pathsBySource);
        return new CombinedDisplaySnapshot(configuration, displays);
    }

    private static string ComputePathFingerprint(
        IEnumerable<DisplayConfigurationPath> paths)
    {
        string canonical = string.Join(
            "|",
            paths
                .OrderBy(path => path.StableTargetId, StringComparer.Ordinal)
                .Select(path => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{path.StableTargetId}:{path.Rotation}:{path.SourceBounds.Left},{path.SourceBounds.Top},{path.SourceBounds.Width},{path.SourceBounds.Height}:{path.TargetAvailable}:{path.UsesVirtualMode}")));
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical)));
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
        Console.WriteLine($"Active display paths: {report.ActiveDisplayPathCount}");
        Console.WriteLine(
            $"DisplayConfig mappings: {report.DisplayConfigMappingCount}");
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

            P0-07b1 read-only DisplayConfig adapter/target path correlation,
            rotation, buffer-race handling, and privacy audit.

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

internal sealed record CombinedDisplaySnapshot(
    DisplayConfigurationResult Configuration,
    DisplayEnumerationResult Displays);

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
    int ActiveDisplayPathCount,
    int DisplayConfigMappingCount,
    int SourceBoundsMatchCount,
    int AvailableTargetCount,
    int TargetDevicePathCount,
    int VirtualModePathCount,
    int DistinctPathFingerprints,
    int MaximumBufferAttempts,
    IReadOnlyList<DisplayRotation> Rotations,
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
