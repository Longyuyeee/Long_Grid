using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.DesktopItems;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return ShellDesktopCatalogProbe.Run(args);
    }
}

internal static class ShellDesktopCatalogProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static int Run(string[] args)
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

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("P0-01b requires Windows.");
            return 3;
        }

        try
        {
            IReadOnlyList<DesktopCatalogEntry> physicalItems = EnumeratePhysicalDesktop();
            IReadOnlyList<ShellDesktopItem> shellItems = ShellDesktopEnumerator.Enumerate();
            DesktopInventoryComparisonResult comparison = DesktopInventoryComparison.Compare(
                physicalItems.Select(item => item.Identity.CanonicalTarget),
                shellItems
                    .Where(item => item.FileSystemPath is not null)
                    .Select(item => item.FileSystemPath!));

            var report = new ProbeReport(
                Probe: "P0-01b-shell-desktop-namespace",
                TimestampUtc: DateTimeOffset.UtcNow,
                OperatingSystem: Environment.OSVersion.VersionString,
                Architecture: RuntimeInformation.OSArchitecture.ToString(),
                ShellItemCount: shellItems.Count,
                ShellFileSystemItemCount: shellItems.Count(item => item.FileSystemPath is not null),
                ShellVirtualItemCount: shellItems.Count(item => item.FileSystemPath is null),
                ShellFolderCount: shellItems.Count(item => item.IsFolder),
                ShellLinkCount: shellItems.Count(item => item.IsLink),
                PhysicalItemCount: physicalItems.Count,
                MatchedFileSystemItemCount: comparison.MatchedPaths.Count,
                PhysicalOnlyItemCount: comparison.PhysicalOnlyPaths.Count,
                ShellOnlyFileSystemItemCount: comparison.ShellOnlyPaths.Count,
                Entries: options.IncludeNames
                    ? shellItems.Select(item => new EntrySummary(
                        item.DisplayName,
                        item.FileSystemPath is null ? "Virtual" : "FileSystem",
                        item.IsFolder,
                        item.IsLink,
                        item.IsHidden)).ToArray()
                    : null,
                Limitations:
                [
                    "This is a single-machine, single-session observation.",
                    "The comparison uses normalized file-system paths only; PIDL persistence and file IDs remain unverified.",
                    "Shell namespace extensions can omit or lazily return items.",
                    "No item is opened, invoked, copied, moved, renamed, or deleted.",
                    "Names are excluded unless --include-names is explicitly supplied; full paths are never reported.",
                ]);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            }
            else
            {
                PrintTextReport(report);
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is COMException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            Console.Error.WriteLine(
                $"P0-01b failed safely: {exception.GetType().Name}: {exception.Message}");
            return 2;
        }
    }

    private static IReadOnlyList<DesktopCatalogEntry> EnumeratePhysicalDesktop()
    {
        var candidates = new List<DesktopCatalogCandidate>();
        var paths = new[]
        {
            ("user-desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("public-desktop", Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
        };
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        foreach ((string sourceId, string path) in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            candidates.AddRange(
                Directory
                    .EnumerateFileSystemEntries(path, "*", enumerationOptions)
                    .Select(itemPath => new DesktopCatalogCandidate(
                        sourceId,
                        itemPath,
                        File.GetAttributes(itemPath).HasFlag(FileAttributes.Directory))));
        }

        return DesktopCatalog.Build(candidates);
    }

    private static void PrintTextReport(ProbeReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine();
        Console.WriteLine($"Shell items: {report.ShellItemCount}");
        Console.WriteLine($"  File-system: {report.ShellFileSystemItemCount}");
        Console.WriteLine($"  Virtual: {report.ShellVirtualItemCount}");
        Console.WriteLine($"  Folders: {report.ShellFolderCount}");
        Console.WriteLine($"  Links: {report.ShellLinkCount}");
        Console.WriteLine();
        Console.WriteLine($"Physical directory items: {report.PhysicalItemCount}");
        Console.WriteLine($"  Matched in Shell: {report.MatchedFileSystemItemCount}");
        Console.WriteLine($"  Physical only: {report.PhysicalOnlyItemCount}");
        Console.WriteLine($"  Shell file-system only: {report.ShellOnlyFileSystemItemCount}");

        if (report.Entries is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Names were explicitly requested:");
            foreach (EntrySummary entry in report.Entries)
            {
                Console.WriteLine(
                    $"  [{entry.StorageKind}] folder={entry.IsFolder}, link={entry.IsLink}, hidden={entry.IsHidden}: {entry.DisplayName}");
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
            LongGrid.Spikes.ShellDesktopCatalog

            Read-only Phase 0 probe for the Shell Desktop namespace.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.ShellDesktopCatalog -- [options]

            Options:
              --json           Write a machine-readable, path-free JSON report.
              --include-names  Include display names. Off by default for privacy.
              --help           Show this help.
            """);
    }
}

internal sealed record ProbeOptions(bool Json, bool IncludeNames, bool ShowHelp)
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

internal sealed record ProbeReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    int ShellItemCount,
    int ShellFileSystemItemCount,
    int ShellVirtualItemCount,
    int ShellFolderCount,
    int ShellLinkCount,
    int PhysicalItemCount,
    int MatchedFileSystemItemCount,
    int PhysicalOnlyItemCount,
    int ShellOnlyFileSystemItemCount,
    IReadOnlyList<EntrySummary>? Entries,
    IReadOnlyList<string> Limitations);

internal sealed record EntrySummary(
    string DisplayName,
    string StorageKind,
    bool IsFolder,
    bool IsLink,
    bool IsHidden);
