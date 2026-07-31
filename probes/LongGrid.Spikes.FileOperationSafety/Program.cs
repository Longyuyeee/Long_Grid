using System.Runtime.InteropServices;
using System.Text.Json;
using LongGrid.Core.FileOperations;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("Usage: LongGrid.Spikes.FileOperationSafety [--json]");
            return 0;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            Console.Error.WriteLine("P0-08 requires Windows 10 or later.");
            return 3;
        }

        bool json = args.Contains("--json", StringComparer.Ordinal);

        try
        {
            FileOperationSafetyReport report = FileOperationSafetySandbox.Run();
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            }
            else
            {
                PrintText(report);
            }

            return report.Verdict == "ConditionalPass" ? 0 : 2;
        }
        catch (Exception exception)
        {
            var failure = new
            {
                Probe = "P0-08-file-operation-safety",
                Verdict = "Fail",
                ErrorType = exception.GetType().Name,
            };

            Console.Error.WriteLine(JsonSerializer.Serialize(failure, JsonOptions));
            return 2;
        }
    }

    private static void PrintText(FileOperationSafetyReport report)
    {
        Console.WriteLine($"Probe: {report.Probe}");
        Console.WriteLine($"Verdict: {report.Verdict}");
        Console.WriteLine($"Safe reference: {report.SafeReference.Passed}");
        Console.WriteLine($"Managed move: {report.ManagedMove.Passed}");
        Console.WriteLine($"Conflict preflight: {report.ConflictPreflight.Passed}");
        Console.WriteLine($"Cancellation: {report.Cancellation.Passed}");
        Console.WriteLine($"Partial success: {report.PartialSuccess.Passed}");
        Console.WriteLine($"Sandbox cleanup: {report.CleanupSucceeded}");
        Console.WriteLine($"Undo boundary: {report.UndoBoundary.Status}");
    }
}

internal static class FileOperationSafetySandbox
{
    internal static FileOperationSafetyReport Run()
    {
        string root = CreateOwnedSandbox();
        SafeReferenceResult safeReference;
        ManagedMoveResult managedMove;
        ConflictPreflightResult conflictPreflight;
        CancellationResult cancellation;
        PartialSuccessResult partialSuccess;
        bool cleanupSucceeded;

        try
        {
            safeReference = RunSafeReference(root);
            managedMove = RunManagedMove(root);
            conflictPreflight = RunConflictPreflight(root);
            cancellation = RunCancellation(root);
            partialSuccess = RunPartialSuccess(root);
        }
        finally
        {
            cleanupSucceeded = TryDeleteOwnedSandbox(root);
        }

        bool scenariosPassed = safeReference.Passed
            && managedMove.Passed
            && conflictPreflight.Passed
            && cancellation.Passed
            && partialSuccess.Passed
            && cleanupSucceeded;

        return new FileOperationSafetyReport(
            Probe: "P0-08-file-operation-safety",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            SafeReference: safeReference,
            ManagedMove: managedMove,
            ConflictPreflight: conflictPreflight,
            Cancellation: cancellation,
            PartialSuccess: partialSuccess,
            UndoBoundary: new UndoBoundaryResult(
                Status: "Inconclusive",
                UndoRegistrationRequested: false,
                Reason:
                    "The Shell undo stack is session-global and has no deterministic "
                    + "IFileOperation undo API. The automated probe intentionally does "
                    + "not pollute the user's Explorer undo history."),
            CleanupSucceeded: cleanupSucceeded,
            Verdict: scenariosPassed ? "ConditionalPass" : "Fail",
            Limitations:
            [
                "All mutations are confined to a newly created temporary sandbox.",
                "No file names, paths, contents, or stable file identities are emitted.",
                "Network paths, reparse points, cloud placeholders, and existing destinations are blocked before Shell execution.",
                "The cancellation matrix demonstrates a partial-success boundary; production must journal each completed item.",
                "Explorer UI undo, recycle-bin behavior, cross-volume moves, ACL failures, cloud hydration, and real user cancellation remain separate controlled tests.",
            ]);
    }

    private static SafeReferenceResult RunSafeReference(string root)
    {
        string directory = CreateScenarioDirectory(root, "reference");
        string source = Path.Combine(directory, "source.txt");
        string original = "reference-content";
        File.WriteAllText(source, original);

        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.SafeReference,
            [new FileOrganizationItemFacts(
                "reference-item",
                SourceAvailable: true,
                IsFileSystemItem: true,
                DestinationConfigured: true,
                SourceEqualsDestination: true,
                DestinationExists: true,
                IsReparsePoint: true,
                IsNetworkPath: true,
                IsCloudPlaceholder: true)]);

        bool sourceUnchanged = File.Exists(source)
            && string.Equals(
                File.ReadAllText(source),
                original,
                StringComparison.Ordinal);
        bool passed = plan.CanApplyWithoutFileApproval
            && !plan.HasFileSystemMutations
            && sourceUnchanged;

        return new SafeReferenceResult(
            passed,
            plan.HasFileSystemMutations,
            sourceUnchanged);
    }

    private static ManagedMoveResult RunManagedMove(string root)
    {
        string directory = CreateScenarioDirectory(root, "move");
        string sourceDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "source")).FullName;
        string destinationDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "destination")).FullName;
        string source = Path.Combine(sourceDirectory, "item.txt");
        string destination = Path.Combine(destinationDirectory, "item.txt");
        string content = "managed-move-content";
        File.WriteAllText(source, content);

        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.ManagedMove,
            [Movable("move-item")]);
        ShellMoveResult result = ShellFileOperation.Move(
            [new ShellMoveRequest(source, destinationDirectory)]);

        bool destinationVerified = File.Exists(destination)
            && string.Equals(
                File.ReadAllText(destination),
                content,
                StringComparison.Ordinal);
        bool passed = plan.RequiresExplicitApproval
            && !plan.CanApplyWithoutFileApproval
            && !File.Exists(source)
            && destinationVerified
            && !result.Aborted
            && result.Result >= 0;

        return new ManagedMoveResult(
            passed,
            plan.RequiresExplicitApproval,
            SourceRemoved: !File.Exists(source),
            DestinationVerified: destinationVerified,
            result.Aborted,
            result.Result);
    }

    private static ConflictPreflightResult RunConflictPreflight(string root)
    {
        string directory = CreateScenarioDirectory(root, "conflict");
        string sourceDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "source")).FullName;
        string destinationDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "destination")).FullName;
        string source = Path.Combine(sourceDirectory, "item.txt");
        string destination = Path.Combine(destinationDirectory, "item.txt");
        File.WriteAllText(source, "source-content");
        File.WriteAllText(destination, "destination-content");

        FileOrganizationPlan plan = FileOrganizationPlanner.Create(
            FileOrganizationMode.ManagedMove,
            [Movable("conflict-item") with { DestinationExists = true }]);
        bool conflictReported = plan.Entries
            .Single()
            .Issues
            .Contains(FileOrganizationIssueCode.DestinationConflict);
        bool sourceUnchanged = string.Equals(
            File.ReadAllText(source),
            "source-content",
            StringComparison.Ordinal);
        bool destinationUnchanged = string.Equals(
            File.ReadAllText(destination),
            "destination-content",
            StringComparison.Ordinal);

        return new ConflictPreflightResult(
            Passed: plan.HasBlockingIssues
                && conflictReported
                && sourceUnchanged
                && destinationUnchanged,
            conflictReported,
            OperationInvoked: false,
            sourceUnchanged,
            destinationUnchanged);
    }

    private static CancellationResult RunCancellation(string root)
    {
        string directory = CreateScenarioDirectory(root, "cancel");
        string sourceDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "source")).FullName;
        string destinationDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "destination")).FullName;
        string source = Path.Combine(sourceDirectory, "item.txt");
        string destination = Path.Combine(destinationDirectory, "item.txt");
        File.WriteAllText(source, "cancel-content");
        var sink = new CancelMoveProgressSink();

        ShellMoveResult result = ShellFileOperation.Move(
            [new ShellMoveRequest(source, destinationDirectory, sink)]);
        bool sourcePreserved = File.Exists(source);
        bool destinationAbsent = !File.Exists(destination);

        return new CancellationResult(
            Passed: result.CancellationSignaled
                && sourcePreserved
                && destinationAbsent
                && sink.PreMoveCount == 1,
            result.CancellationSignaled,
            result.Aborted,
            sourcePreserved,
            destinationAbsent,
            sink.PreMoveCount,
            sink.PostMoveCount,
            result.Result);
    }

    private static PartialSuccessResult RunPartialSuccess(string root)
    {
        string directory = CreateScenarioDirectory(root, "partial");
        string sourceDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "source")).FullName;
        string destinationDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "destination")).FullName;
        string firstSource = Path.Combine(sourceDirectory, "first.txt");
        string secondSource = Path.Combine(sourceDirectory, "second.txt");
        string firstDestination = Path.Combine(destinationDirectory, "first.txt");
        string secondDestination = Path.Combine(destinationDirectory, "second.txt");
        File.WriteAllText(firstSource, "first-content");
        File.WriteAllText(secondSource, "second-content");
        var sink = new CancelMoveProgressSink();

        ShellMoveResult result = ShellFileOperation.Move(
            [
                new ShellMoveRequest(firstSource, destinationDirectory),
                new ShellMoveRequest(secondSource, destinationDirectory, sink),
            ]);
        bool firstCompleted = !File.Exists(firstSource)
            && File.Exists(firstDestination);
        bool secondPreserved = File.Exists(secondSource)
            && !File.Exists(secondDestination);

        return new PartialSuccessResult(
            Passed: result.CancellationSignaled
                && firstCompleted
                && secondPreserved
                && sink.PreMoveCount == 1,
            result.CancellationSignaled,
            result.Aborted,
            CompletedCount: firstCompleted ? 1 : 0,
            PreservedCount: secondPreserved ? 1 : 0,
            sink.PreMoveCount,
            result.Result);
    }

    private static FileOrganizationItemFacts Movable(string itemId) =>
        new(
            itemId,
            SourceAvailable: true,
            IsFileSystemItem: true,
            DestinationConfigured: true);

    private static string CreateOwnedSandbox()
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetFullPath(Path.Combine(
            temporaryRoot,
            $"LongGrid-P0-08-{Guid.NewGuid():N}"));
        if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The sandbox escaped the temporary root.");
        }

        return Directory.CreateDirectory(root).FullName;
    }

    private static string CreateScenarioDirectory(string root, string name) =>
        Directory.CreateDirectory(Path.Combine(root, name)).FullName;

    private static bool TryDeleteOwnedSandbox(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
            return !Directory.Exists(root);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed record FileOperationSafetyReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    SafeReferenceResult SafeReference,
    ManagedMoveResult ManagedMove,
    ConflictPreflightResult ConflictPreflight,
    CancellationResult Cancellation,
    PartialSuccessResult PartialSuccess,
    UndoBoundaryResult UndoBoundary,
    bool CleanupSucceeded,
    string Verdict,
    IReadOnlyList<string> Limitations);

internal sealed record SafeReferenceResult(
    bool Passed,
    bool FileSystemMutationPlanned,
    bool SourceUnchanged);

internal sealed record ManagedMoveResult(
    bool Passed,
    bool ExplicitApprovalRequired,
    bool SourceRemoved,
    bool DestinationVerified,
    bool Aborted,
    int Result);

internal sealed record ConflictPreflightResult(
    bool Passed,
    bool ConflictReported,
    bool OperationInvoked,
    bool SourceUnchanged,
    bool DestinationUnchanged);

internal sealed record CancellationResult(
    bool Passed,
    bool CancellationSignaled,
    bool Aborted,
    bool SourcePreserved,
    bool DestinationAbsent,
    int PreMoveCount,
    int PostMoveCount,
    int Result);

internal sealed record PartialSuccessResult(
    bool Passed,
    bool CancellationSignaled,
    bool Aborted,
    int CompletedCount,
    int PreservedCount,
    int CancelPreMoveCount,
    int Result);

internal sealed record UndoBoundaryResult(
    string Status,
    bool UndoRegistrationRequested,
    string Reason);
