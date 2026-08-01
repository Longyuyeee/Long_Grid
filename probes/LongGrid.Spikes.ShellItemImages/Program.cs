using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.Concurrency;
using LongGrid.Core.DesktopItems;

internal static class Program
{
    private const int MinimumStressRequests = 500;
    private const int DefaultConcurrency = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3
            && string.Equals(args[0], "--thumbnail-worker", StringComparison.Ordinal)
            && string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            && int.TryParse(
                args[2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parentProcessId)
            && parentProcessId > 0)
        {
            return await ThumbnailWorkerServer.RunAsync(parentProcessId);
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "--thumbnail-parent-exit-probe",
                StringComparison.Ordinal))
        {
            using var client = new ThumbnailWorkerClient(
                maximumRequestsPerProcess: 1);
            int workerProcessId = client.EnsureWorkerStarted();
            _ = client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    "parent-exit-hang",
                    ThumbnailWorkerRequestKind.Hang,
                    Path: null,
                    Size: 0,
                    Flags: 0),
                TimeSpan.FromMinutes(1));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            await File.WriteAllTextAsync(
                args[1],
                workerProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            Environment.Exit(0);
            return 0;
        }

        if (args.Contains("--worker-matrix", StringComparer.Ordinal))
        {
            bool json = args.Contains("--json", StringComparer.Ordinal);
            if (args.Any(argument =>
                argument is not "--worker-matrix" and not "--json"))
            {
                Console.Error.WriteLine("Unknown worker-matrix option.");
                return 64;
            }

            return await ThumbnailWorkerIsolationProbe.RunAsync(json);
        }

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

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            Console.Error.WriteLine("P0-03 requires Windows 8 or later.");
            return 3;
        }

        IReadOnlyList<DesktopCatalogEntry> desktopItems =
            DesktopDiscovery.EnumeratePhysical();
        if (desktopItems.Count == 0)
        {
            Console.Error.WriteLine("No physical Desktop items were available.");
            return 2;
        }

        BatchSummary warmupIcons = await RunBatchAsync(
            desktopItems.Select(item => item.Identity.CanonicalTarget),
            DefaultConcurrency,
            ShellItemImageFactoryFlags.IconOnly
                | ShellItemImageFactoryFlags.BiggerSizeOk,
            size: 64);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int releaseCountBefore = SafeGdiBitmapHandle.ReleasedCount;
        uint gdiBefore = GetGdiObjectCount();

        BatchSummary cacheIcons = await RunBatchAsync(
            desktopItems.Select(item => item.Identity.CanonicalTarget),
            DefaultConcurrency,
            ShellItemImageFactoryFlags.IconOnly
                | ShellItemImageFactoryFlags.InCacheOnly,
            size: 64);

        BatchSummary cacheThumbnails = await RunBatchAsync(
            desktopItems
                .Where(item => item.Kind == DesktopItemKind.File)
                .Select(item => item.Identity.CanonicalTarget),
            DefaultConcurrency,
            ShellItemImageFactoryFlags.ThumbnailOnly
                | ShellItemImageFactoryFlags.InCacheOnly,
            size: 128);

        string[] stressPaths = Enumerable.Range(0, MinimumStressRequests)
            .Select(index =>
                desktopItems[index % desktopItems.Count].Identity.CanonicalTarget)
            .ToArray();
        BatchSummary stressIcons = await RunBatchAsync(
            stressPaths,
            DefaultConcurrency,
            ShellItemImageFactoryFlags.IconOnly
                | ShellItemImageFactoryFlags.BiggerSizeOk,
            size: 64);

        QueueCancellationSummary cancellation = await AuditQueueCancellationAsync();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        uint gdiAfter = GetGdiObjectCount();
        int released = SafeGdiBitmapHandle.ReleasedCount - releaseCountBefore;
        int successfulImages =
            cacheIcons.Succeeded + cacheThumbnails.Succeeded + stressIcons.Succeeded;
        int gdiDelta = unchecked((int)gdiAfter - (int)gdiBefore);

        var report = new ProbeReport(
            Probe: "P0-03-shell-item-image-factory",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            DesktopItems: desktopItems.Count,
            WarmupIcons: warmupIcons,
            CacheIcons: cacheIcons,
            CacheThumbnails: cacheThumbnails,
            StressIcons: stressIcons,
            QueueCancellation: cancellation,
            GdiObjectsBefore: gdiBefore,
            GdiObjectsAfter: gdiAfter,
            GdiObjectDelta: gdiDelta,
            SuccessfulImages: successfulImages,
            ReleasedBitmapHandles: released,
            NativeInFlightCancellationSupported: false,
            Privacy:
            [
                "The physical Desktop was read-only.",
                "No bitmap, file content, name, path, extension, HRESULT, or Shell identity was persisted or printed.",
                "Thumbnail requests used cache-only mode, so a cache miss did not request fresh thumbnail extraction.",
            ],
            Limitations:
            [
                "IShellItemImageFactory.GetImage cannot be forcibly canceled after entering native code.",
                "Production must isolate cache-miss extraction in a recyclable low-privilege worker process.",
                "Cache-only thumbnail misses are expected and do not mean that a file lacks a thumbnail provider.",
                "Cloud-provider-specific hydration behavior still requires a dedicated compatibility matrix.",
                "This run covers one Windows build, account, theme, DPI context, and provider/cache state.",
                "The probe validates HBITMAP ownership but does not validate UI composition, color fidelity, alpha, or DPI presentation.",
            ]);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintText(report);
        }

        bool passed = stressIcons.Succeeded == stressIcons.Requested
            && stressIcons.MaxObservedConcurrency <= DefaultConcurrency
            && cancellation.CanceledWorkDidNotStart
            && successfulImages == released
            && gdiDelta <= 4;
        return passed ? 0 : 2;
    }

    private static async Task<BatchSummary> RunBatchAsync(
        IEnumerable<string> paths,
        int maximumConcurrency,
        ShellItemImageFactoryFlags flags,
        int size)
    {
        using var executor = new BoundedAsyncExecutor(maximumConcurrency);
        Task<ImageExtractionResult>[] tasks = paths
            .Select(path => executor.RunAsync(
                cancellationToken => Task.Run(
                    () => ShellImageExtractor.Extract(path, size, flags),
                    cancellationToken)))
            .ToArray();
        ImageExtractionResult[] results = await Task.WhenAll(tasks);
        long[] durations = results
            .Select(result => result.Duration.Ticks)
            .Order()
            .ToArray();

        return new BatchSummary(
            Requested: results.Length,
            Succeeded: results.Count(result => result.Success),
            Failed: results.Count(result => !result.Success),
            MaxObservedConcurrency: executor.MaxObservedConcurrency,
            P50Milliseconds: PercentileMilliseconds(durations, 0.50),
            P95Milliseconds: PercentileMilliseconds(durations, 0.95));
    }

    private static async Task<QueueCancellationSummary> AuditQueueCancellationAsync()
    {
        using var executor = new BoundedAsyncExecutor(1);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> occupyingTask = executor.RunAsync(
            async _ =>
            {
                entered.SetResult();
                await release.Task;
                return true;
            });
        await entered.Task;

        using var cancellation = new CancellationTokenSource();
        bool canceledWorkStarted = false;
        Task<bool> canceledTask = executor.RunAsync(
            _ =>
            {
                canceledWorkStarted = true;
                return Task.FromResult(true);
            },
            cancellation.Token);
        cancellation.Cancel();

        bool cancellationObserved;
        try
        {
            await canceledTask;
            cancellationObserved = false;
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }
        finally
        {
            release.SetResult();
        }

        await occupyingTask;
        return new QueueCancellationSummary(
            cancellationObserved,
            !canceledWorkStarted,
            executor.MaxObservedConcurrency);
    }

    private static double PercentileMilliseconds(long[] sortedTicks, double percentile)
    {
        if (sortedTicks.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(percentile * sortedTicks.Length) - 1;
        return TimeSpan.FromTicks(sortedTicks[Math.Max(index, 0)]).TotalMilliseconds;
    }

    private static uint GetGdiObjectCount()
    {
        using Process process = Process.GetCurrentProcess();
        return NativeMethods.GetGuiResources(process.Handle, NativeMethods.GrGdiObjects);
    }

    private static void PrintText(ProbeReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine($"Desktop items: {report.DesktopItems}");
        PrintBatch("Warm-up icons", report.WarmupIcons);
        PrintBatch("Cache-only icons", report.CacheIcons);
        PrintBatch("Cache-only thumbnails", report.CacheThumbnails);
        PrintBatch("Background icon stress", report.StressIcons);
        Console.WriteLine(
            $"Queued cancellation observed: {report.QueueCancellation.CancellationObserved}");
        Console.WriteLine(
            $"Canceled work did not start: {report.QueueCancellation.CanceledWorkDidNotStart}");
        Console.WriteLine(
            $"GDI objects: {report.GdiObjectsBefore} -> {report.GdiObjectsAfter} (delta {report.GdiObjectDelta})");
        Console.WriteLine(
            $"HBITMAP success/released: {report.SuccessfulImages}/{report.ReleasedBitmapHandles}");
        Console.WriteLine(
            $"Native in-flight cancellation supported: {report.NativeInFlightCancellationSupported}");
    }

    private static void PrintBatch(string name, BatchSummary summary)
    {
        Console.WriteLine(
            $"{name}: {summary.Succeeded}/{summary.Requested} succeeded; "
            + $"failed {summary.Failed}; max concurrency {summary.MaxObservedConcurrency}; "
            + $"p50 {summary.P50Milliseconds:F2} ms; p95 {summary.P95Milliseconds:F2} ms");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LongGrid.Spikes.ShellItemImages

            P0-03 read-only Shell icon/thumbnail extraction and HBITMAP ownership audit.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.ShellItemImages -- [options]

            Options:
              --json  Write a machine-readable, fully redacted report.
              --worker-matrix  Run the owned-sandbox worker isolation matrix.
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
    int DesktopItems,
    BatchSummary WarmupIcons,
    BatchSummary CacheIcons,
    BatchSummary CacheThumbnails,
    BatchSummary StressIcons,
    QueueCancellationSummary QueueCancellation,
    uint GdiObjectsBefore,
    uint GdiObjectsAfter,
    int GdiObjectDelta,
    int SuccessfulImages,
    int ReleasedBitmapHandles,
    bool NativeInFlightCancellationSupported,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);

internal sealed record BatchSummary(
    int Requested,
    int Succeeded,
    int Failed,
    int MaxObservedConcurrency,
    double P50Milliseconds,
    double P95Milliseconds);

internal sealed record QueueCancellationSummary(
    bool CancellationObserved,
    bool CanceledWorkDidNotStart,
    int MaxObservedConcurrency);
