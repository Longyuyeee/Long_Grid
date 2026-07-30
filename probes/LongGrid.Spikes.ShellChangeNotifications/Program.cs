using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    public static int Main(string[] args)
    {
        return ShellChangeNotificationProbe.Run(args);
    }
}

internal static class ShellChangeNotificationProbe
{
    private const int FileCount = 400;
    private const int DirectoryCount = 40;

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

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            Console.Error.WriteLine("P0-02 requires Windows 7 or later.");
            return 3;
        }

        string ownedRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "LongGrid-P0-02"));
        string sandbox = Path.Combine(ownedRoot, Guid.NewGuid().ToString("N"));
        bool cleanupSucceeded = false;

        try
        {
            Directory.CreateDirectory(sandbox);
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stopwatch = Stopwatch.StartNew();
            NotificationSnapshot notifications;
            bool reconciliationTriggered;

            using (var listener = new ShellChangeListener(sandbox))
            {
                PerformBurst(sandbox, expected);
                reconciliationTriggered = WaitForReconciliation(listener, TimeSpan.FromSeconds(12));
                notifications = listener.GetSnapshot();

                var actual = Directory
                    .EnumerateFileSystemEntries(sandbox, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string[] missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase).ToArray();
                string[] unexpected = actual.Except(expected, StringComparer.OrdinalIgnoreCase).ToArray();
                stopwatch.Stop();

                var report = new ProbeReport(
                    Probe: "P0-02-shell-change-notifications-and-reconciliation",
                    TimestampUtc: DateTimeOffset.UtcNow,
                    OperatingSystem: Environment.OSVersion.VersionString,
                    Architecture: RuntimeInformation.OSArchitecture.ToString(),
                    DesktopRegistrationSucceeded: listener.DesktopRegistrationSucceeded,
                    SandboxRegistrationSucceeded: listener.SandboxRegistrationSucceeded,
                    PlannedOperationCount: CalculateOperationCount(),
                    Notifications: notifications,
                    ReconciliationTriggered: reconciliationTriggered,
                    ExpectedFinalItemCount: expected.Count,
                    ActualFinalItemCount: actual.Count,
                    MissingItemCount: missing.Length,
                    UnexpectedItemCount: unexpected.Length,
                    FinalStateMatched: missing.Length == 0 && unexpected.Length == 0,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                    CleanupSucceeded: true,
                    Limitations:
                    [
                        "The real Desktop namespace is registered read-only; all generated changes are confined to an owned temporary sandbox.",
                        "Shell notifications are hints and can be combined; notification count is not expected to equal operation count.",
                        "Correctness comes from a quiet-window or maximum-delay full reconciliation, not from perfect event delivery.",
                        "This single-machine run does not cover Explorer restart, event loss injection, OneDrive, SMB, ReFS, ARM64, or restricted accounts.",
                        "No names, paths, PIDLs, or file identities are emitted.",
                    ]);

                cleanupSucceeded = CleanupSandbox(ownedRoot, sandbox);
                report = report with { CleanupSucceeded = cleanupSucceeded };
                WriteReport(report, options.Json);

                bool passed = report.DesktopRegistrationSucceeded
                    && report.SandboxRegistrationSucceeded
                    && report.Notifications.SandboxNotificationCount > 0
                    && report.ReconciliationTriggered
                    && report.FinalStateMatched
                    && report.CleanupSucceeded;

                return passed ? 0 : 2;
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            Console.Error.WriteLine($"P0-02 failed safely: {exception.GetType().Name}.");
            return 2;
        }
        finally
        {
            if (!cleanupSucceeded && Directory.Exists(sandbox))
            {
                cleanupSucceeded = CleanupSandbox(ownedRoot, sandbox);
            }

            if (!cleanupSucceeded && Directory.Exists(sandbox))
            {
                Console.Error.WriteLine("P0-02 temporary sandbox cleanup failed.");
            }
        }
    }

    private static void PerformBurst(
        string sandbox,
        HashSet<string> expected)
    {
        for (int index = 0; index < FileCount; index++)
        {
            string name = $"file-{index:D4}.tmp";
            File.WriteAllText(Path.Combine(sandbox, name), "Long Grid P0-02.");
        }

        for (int index = 0; index < FileCount; index++)
        {
            string oldName = $"file-{index:D4}.tmp";
            string newName = $"renamed-{index:D4}.tmp";
            File.Move(
                Path.Combine(sandbox, oldName),
                Path.Combine(sandbox, newName));

            if (index % 2 == 0)
            {
                File.Delete(Path.Combine(sandbox, newName));
            }
            else
            {
                expected.Add(newName);
            }
        }

        for (int index = 0; index < DirectoryCount; index++)
        {
            string name = $"directory-{index:D3}";
            Directory.CreateDirectory(Path.Combine(sandbox, name));
        }

        for (int index = 0; index < DirectoryCount; index++)
        {
            string oldName = $"directory-{index:D3}";
            string newName = $"folder-{index:D3}";
            Directory.Move(
                Path.Combine(sandbox, oldName),
                Path.Combine(sandbox, newName));

            if (index % 2 == 0)
            {
                Directory.Delete(Path.Combine(sandbox, newName));
            }
            else
            {
                expected.Add(newName);
            }
        }
    }

    private static bool WaitForReconciliation(
        ShellChangeListener listener,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (listener.TryBeginReconciliation(DateTimeOffset.UtcNow))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static int CalculateOperationCount()
    {
        int fileOperations = FileCount + FileCount + (FileCount / 2);
        int directoryOperations =
            DirectoryCount + DirectoryCount + (DirectoryCount / 2);
        return fileOperations + directoryOperations;
    }

    private static bool CleanupSandbox(string ownedRoot, string sandbox)
    {
        string canonicalSandbox = Path.GetFullPath(sandbox);
        string requiredPrefix = ownedRoot.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!canonicalSandbox.StartsWith(
            requiredPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(canonicalSandbox))
            {
                Directory.Delete(canonicalSandbox, recursive: true);
            }

            return !Directory.Exists(canonicalSandbox);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteReport(ProbeReport report, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return;
        }

        Console.WriteLine(report.Probe);
        Console.WriteLine($"UTC: {report.TimestampUtc:O}");
        Console.WriteLine($"OS: {report.OperatingSystem}");
        Console.WriteLine($"Architecture: {report.Architecture}");
        Console.WriteLine($"Desktop registration: {report.DesktopRegistrationSucceeded}");
        Console.WriteLine($"Sandbox registration: {report.SandboxRegistrationSucceeded}");
        Console.WriteLine($"Planned operations: {report.PlannedOperationCount}");
        Console.WriteLine($"All notifications: {report.Notifications.TotalNotificationCount}");
        Console.WriteLine($"Sandbox notifications: {report.Notifications.SandboxNotificationCount}");
        Console.WriteLine($"Reconciliation triggered: {report.ReconciliationTriggered}");
        Console.WriteLine($"Expected final items: {report.ExpectedFinalItemCount}");
        Console.WriteLine($"Actual final items: {report.ActualFinalItemCount}");
        Console.WriteLine($"Missing: {report.MissingItemCount}");
        Console.WriteLine($"Unexpected: {report.UnexpectedItemCount}");
        Console.WriteLine($"Final state matched: {report.FinalStateMatched}");
        Console.WriteLine($"Cleanup succeeded: {report.CleanupSucceeded}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LongGrid.Spikes.ShellChangeNotifications

            P0-02 Shell notification and final reconciliation probe.

            Usage:
              dotnet run --project probes/LongGrid.Spikes.ShellChangeNotifications -- [options]

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
    bool DesktopRegistrationSucceeded,
    bool SandboxRegistrationSucceeded,
    int PlannedOperationCount,
    NotificationSnapshot Notifications,
    bool ReconciliationTriggered,
    int ExpectedFinalItemCount,
    int ActualFinalItemCount,
    int MissingItemCount,
    int UnexpectedItemCount,
    bool FinalStateMatched,
    long DurationMilliseconds,
    bool CleanupSucceeded,
    IReadOnlyList<string> Limitations);
