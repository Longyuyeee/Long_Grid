using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.DesktopItems;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return FileIdentityProbe.Run(args);
    }
}

internal static class FileIdentityProbe
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

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            Console.Error.WriteLine("P0-01c requires Windows 8 or later.");
            return 3;
        }

        IReadOnlyList<DesktopCatalogEntry> desktopItems = DesktopDiscovery.EnumeratePhysical();
        DesktopIdentitySummary desktopSummary = AuditDesktopIdentities(desktopItems);
        ShortcutIdentitySummary shortcutSummary = AuditShortcuts(desktopItems);
        SandboxIdentityResult sandboxResult = IdentitySandbox.Run();

        var report = new ProbeReport(
            Probe: "P0-01c-stable-file-and-shortcut-identity",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            Desktop: desktopSummary,
            Shortcuts: shortcutSummary,
            Sandbox: sandboxResult,
            Limitations:
            [
                "Real Desktop and shortcut targets are read-only; mutation occurs only inside an owned temporary sandbox.",
                "File IDs are scoped to a volume and computer; they are not portable cloud or cross-device identities.",
                "File systems, providers, or remote shares can reject FileIdInfo and require a documented fallback.",
                "IShellLink.GetPath does not produce a file-system path for every Shell namespace or application shortcut.",
                "PIDL persistence, change notifications, cross-volume moves, and OneDrive hydration behavior remain separate probes.",
                "No names, paths, volume serials, or file IDs are emitted by this report.",
            ]);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            PrintText(report);
        }

        bool passed = desktopSummary.IdentitySucceeded > 0
            && sandboxResult.FileRenamePreservedIdentity
            && sandboxResult.DirectoryRenamePreservedIdentity
            && sandboxResult.CopyCreatedNewIdentity
            && sandboxResult.CleanupSucceeded;

        return passed ? 0 : 2;
    }

    private static DesktopIdentitySummary AuditDesktopIdentities(
        IReadOnlyList<DesktopCatalogEntry> desktopItems)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        int succeeded = 0;
        int failed = 0;
        int duplicates = 0;

        foreach (DesktopCatalogEntry item in desktopItems)
        {
            FileIdentityReadResult result =
                WindowsFileIdentityReader.TryRead(item.Identity.CanonicalTarget);

            if (result.Identity is null)
            {
                failed++;
                continue;
            }

            succeeded++;
            if (!identities.Add(result.Identity.StableKey))
            {
                duplicates++;
            }
        }

        return new DesktopIdentitySummary(
            desktopItems.Count,
            succeeded,
            failed,
            identities.Count,
            duplicates);
    }

    private static ShortcutIdentitySummary AuditShortcuts(
        IReadOnlyList<DesktopCatalogEntry> desktopItems)
    {
        DesktopCatalogEntry[] shortcuts = desktopItems
            .Where(item => item.Kind == DesktopItemKind.Shortcut)
            .ToArray();
        int loaded = 0;
        int pathTargets = 0;
        int existingTargets = 0;
        int targetIdentities = 0;
        int distinctPairs = 0;
        int sameIdentityPairs = 0;

        foreach (DesktopCatalogEntry shortcut in shortcuts)
        {
            ShortcutTargetReadResult target =
                ShellShortcutReader.TryReadTarget(shortcut.Identity.CanonicalTarget);

            if (target.Loaded)
            {
                loaded++;
            }

            if (target.TargetPath is null)
            {
                continue;
            }

            pathTargets++;
            if (!File.Exists(target.TargetPath) && !Directory.Exists(target.TargetPath))
            {
                continue;
            }

            existingTargets++;
            FileIdentityReadResult shortcutIdentity =
                WindowsFileIdentityReader.TryRead(shortcut.Identity.CanonicalTarget);
            FileIdentityReadResult targetIdentity =
                WindowsFileIdentityReader.TryRead(target.TargetPath);

            if (shortcutIdentity.Identity is null || targetIdentity.Identity is null)
            {
                continue;
            }

            targetIdentities++;
            if (shortcutIdentity.Identity == targetIdentity.Identity)
            {
                sameIdentityPairs++;
            }
            else
            {
                distinctPairs++;
            }
        }

        return new ShortcutIdentitySummary(
            shortcuts.Length,
            loaded,
            pathTargets,
            existingTargets,
            targetIdentities,
            distinctPairs,
            sameIdentityPairs);
    }

    private static void PrintText(ProbeReport report)
    {
        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine();
        Console.WriteLine($"Desktop items: {report.Desktop.Total}");
        Console.WriteLine($"  Identity succeeded: {report.Desktop.IdentitySucceeded}");
        Console.WriteLine($"  Identity failed: {report.Desktop.IdentityFailed}");
        Console.WriteLine($"  Unique identities: {report.Desktop.UniqueStableIdentityCount}");
        Console.WriteLine($"  Duplicate identities: {report.Desktop.DuplicateStableIdentityCount}");
        Console.WriteLine();
        Console.WriteLine($"Shortcuts: {report.Shortcuts.Total}");
        Console.WriteLine($"  Loaded read-only: {report.Shortcuts.Loaded}");
        Console.WriteLine($"  Path targets: {report.Shortcuts.PathTargets}");
        Console.WriteLine($"  Existing targets: {report.Shortcuts.ExistingTargets}");
        Console.WriteLine($"  Target identities: {report.Shortcuts.TargetIdentitySucceeded}");
        Console.WriteLine($"  Distinct shortcut/target pairs: {report.Shortcuts.DistinctIdentityPairs}");
        Console.WriteLine($"  Same shortcut/target identity: {report.Shortcuts.SameIdentityPairs}");
        Console.WriteLine();
        Console.WriteLine($"Sandbox file rename preserved identity: {report.Sandbox.FileRenamePreservedIdentity}");
        Console.WriteLine($"Sandbox directory rename preserved identity: {report.Sandbox.DirectoryRenamePreservedIdentity}");
        Console.WriteLine($"Sandbox copy created new identity: {report.Sandbox.CopyCreatedNewIdentity}");
        Console.WriteLine($"Sandbox cleanup succeeded: {report.Sandbox.CleanupSucceeded}");
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
            LongGrid.Spikes.FileIdentity

            P0-01c read-only Desktop identity audit plus an owned temporary rename sandbox.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.FileIdentity -- [options]

            Options:
              --json  Write a machine-readable, fully redacted report.
              --help  Show this help.
            """);
    }
}

internal sealed record ProbeOptions(bool Json, bool ShowHelp)
{
    public static ProbeOptions Parse(IEnumerable<string> args)
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
    DesktopIdentitySummary Desktop,
    ShortcutIdentitySummary Shortcuts,
    SandboxIdentityResult Sandbox,
    IReadOnlyList<string> Limitations);

internal sealed record DesktopIdentitySummary(
    int Total,
    int IdentitySucceeded,
    int IdentityFailed,
    int UniqueStableIdentityCount,
    int DuplicateStableIdentityCount);

internal sealed record ShortcutIdentitySummary(
    int Total,
    int Loaded,
    int PathTargets,
    int ExistingTargets,
    int TargetIdentitySucceeded,
    int DistinctIdentityPairs,
    int SameIdentityPairs);
