using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

namespace LongGrid.App;

internal enum ProductStartupStage
{
    ProcessStarting,
    InstanceResolved,
    AppConstructing,
    AppConstructed,
    WindowCreating,
    WindowCreated,
    WorkspaceInitializing,
    WorkspaceInitialized,
    Exited,
}

internal enum ProductStartupFailureOrigin
{
    EntryPoint,
    ActivationRedirect,
    ManagedUnhandled,
    XamlUnhandled,
    WorkspaceInitialization,
}

internal sealed class ProductStartupDiagnosticLog
{
    internal static ProductStartupDiagnosticLog Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LongGrid", "diagnostics", "startup"));

    private readonly string directory;
    private readonly object gate = new();
    private readonly Guid runId = Guid.NewGuid();
    private ProductStartupStage stage;

    internal ProductStartupDiagnosticLog(string directory) => this.directory = directory;

    internal static bool IsRedirected(bool exists, FileAttributes attributes, string? linkTarget) =>
        linkTarget is not null || (exists && (attributes & FileAttributes.ReparsePoint) != 0);

    private static bool IsRedirected(FileSystemInfo entry) =>
        IsRedirected(entry.Exists, entry.Attributes, entry.LinkTarget);

    internal void RecordStage(ProductStartupStage value)
    {
        lock (gate)
        {
            stage = value;
            Write("latest-startup.json", null, null);
        }
    }

    internal void RecordFailure(ProductStartupFailureOrigin origin, Exception? exception)
    {
        lock (gate) Write("latest-failure.json", origin, exception);
    }

    private void Write(string name, ProductStartupFailureOrigin? origin, Exception? exception)
    {
        try
        {
            // Two fixed-size records, no arguments, exception messages, paths or stack traces.
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1,
                RunId = runId,
                ProcessId = Environment.ProcessId,
                Utc = DateTimeOffset.UtcNow,
                Stage = stage.ToString(),
                Origin = origin?.ToString(),
                Category = exception switch
                {
                    COMException => "COM",
                    UnauthorizedAccessException => "AccessDenied",
                    IOException => "IO",
                    OperationCanceledException => "Cancelled",
                    null => (string?)null,
                    _ => "Managed",
                },
                HResult = exception?.HResult,
            });
            if (payload.Length > 4096) return;
            for (DirectoryInfo? ancestor = new(directory); ancestor is not null; ancestor = ancestor.Parent)
            {
                if (IsRedirected(ancestor)) return;
            }
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            if (IsRedirected(new FileInfo(path))) return;
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exceptionToIgnore) when (exceptionToIgnore is
            IOException or UnauthorizedAccessException or SecurityException)
        {
            // Diagnostics must not replace the application's original outcome.
        }
    }
}
