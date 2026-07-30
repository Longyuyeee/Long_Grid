using System.Text.Json;
using LongGrid.Core.DesktopItems;

return await DesktopCatalogProbe.RunAsync(args);

internal static class DesktopCatalogProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static Task<int> RunAsync(string[] args)
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
            return Task.FromResult(64);
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        var sources = new[]
        {
            new DesktopSource(
                "user-desktop",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new DesktopSource(
                "public-desktop",
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
        };

        var sourceResults = sources.Select(EnumerateSource).ToArray();
        IReadOnlyList<DesktopCatalogEntry> entries = DesktopCatalog.Build(
            sourceResults.SelectMany(result => result.Candidates));

        var report = new ProbeReport(
            Probe: "P0-01a-desktop-directory-discovery",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Sources: sourceResults.Select(ToSourceSummary).ToArray(),
            TotalUniqueEntries: entries.Count,
            CountsByKind: entries
                .GroupBy(entry => entry.Kind)
                .ToDictionary(group => group.Key.ToString(), group => group.Count()),
            Entries: options.IncludeNames
                ? entries.Select(entry => new EntrySummary(
                    entry.SourceId,
                    entry.DisplayName,
                    entry.Kind.ToString())).ToArray()
                : null,
            Limitations:
            [
                "This probe enumerates physical user and Public Desktop directories only.",
                "Shell namespace virtual items, PIDLs, stable file IDs, .lnk targets, and change notifications are out of scope.",
                "No files are opened, moved, copied, renamed, or deleted.",
            ]);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintTextReport(report, options.IncludeNames);
        }

        bool userDesktopFailed = sourceResults
            .Any(result => result.Source.Id == "user-desktop" && result.Error is not null);

        return Task.FromResult(userDesktopFailed ? 2 : 0);
    }

    private static DesktopSourceResult EnumerateSource(DesktopSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
        {
            return new DesktopSourceResult(
                source,
                Exists: false,
                Candidates: [],
                Error: "Known folder path was empty.");
        }

        if (!Directory.Exists(source.Path))
        {
            return new DesktopSourceResult(
                source,
                Exists: false,
                Candidates: [],
                Error: "Known folder does not exist.");
        }

        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0,
            };

            DesktopCatalogCandidate[] candidates = Directory
                .EnumerateFileSystemEntries(source.Path, "*", enumerationOptions)
                .Select(path => new DesktopCatalogCandidate(
                    source.Id,
                    path,
                    IsDirectory(path)))
                .ToArray();

            return new DesktopSourceResult(
                source,
                Exists: true,
                Candidates: candidates,
                Error: null);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return new DesktopSourceResult(
                source,
                Exists: true,
                Candidates: [],
                Error: $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static SourceSummary ToSourceSummary(DesktopSourceResult result)
    {
        return new SourceSummary(
            result.Source.Id,
            result.Exists,
            result.Candidates.Count,
            result.Error);
    }

    private static void PrintTextReport(ProbeReport report, bool includeNames)
    {
        Console.WriteLine($"{report.Probe}");
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine();

        foreach (SourceSummary source in report.Sources)
        {
            string status = source.Error is null ? "OK" : $"ERROR: {source.Error}";
            Console.WriteLine(
                $"{source.Id}: exists={source.Exists}, entries={source.EntryCount}, {status}");
        }

        Console.WriteLine();
        Console.WriteLine($"Unique entries: {report.TotalUniqueEntries}");
        foreach ((string kind, int count) in report.CountsByKind.OrderBy(pair => pair.Key))
        {
            Console.WriteLine($"  {kind}: {count}");
        }

        if (includeNames && report.Entries is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Names were explicitly requested:");
            foreach (EntrySummary entry in report.Entries)
            {
                Console.WriteLine($"  [{entry.SourceId}] {entry.Kind}: {entry.DisplayName}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Limitations:");
        foreach (string limitation in report.Limitations)
        {
            Console.WriteLine($"  - {limitation}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LongGrid.Spikes.DesktopCatalog

            Read-only Phase 0 probe for the physical user and Public Desktop directories.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.DesktopCatalog -- [options]

            Options:
              --json           Write a machine-readable JSON report.
              --include-names  Include item display names. Off by default for privacy.
              --help           Show this help.
            """);
    }
}

internal sealed record ProbeOptions(
    bool Json,
    bool IncludeNames,
    bool ShowHelp)
{
    public static ProbeOptions Parse(IEnumerable<string> args)
    {
        bool json = false;
        bool includeNames = false;
        bool showHelp = false;

        foreach (string argument in args)
        {
            switch (argument)
            {
                case "--json":
                    json = true;
                    break;
                case "--include-names":
                    includeNames = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        return new ProbeOptions(json, includeNames, showHelp);
    }
}

internal sealed record DesktopSource(string Id, string Path);

internal sealed record DesktopSourceResult(
    DesktopSource Source,
    bool Exists,
    IReadOnlyList<DesktopCatalogCandidate> Candidates,
    string? Error);

internal sealed record ProbeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<SourceSummary> Sources,
    int TotalUniqueEntries,
    IReadOnlyDictionary<string, int> CountsByKind,
    IReadOnlyList<EntrySummary>? Entries,
    IReadOnlyList<string> Limitations);

internal sealed record SourceSummary(
    string Id,
    bool Exists,
    int EntryCount,
    string? Error);

internal sealed record EntrySummary(
    string SourceId,
    string DisplayName,
    string Kind);
