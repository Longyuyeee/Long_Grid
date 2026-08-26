using System.Text.Json;

namespace LongGrid.App;

internal sealed class ProductBoxR1ActivationEvidenceSession :
    IProductDesktopWorkspaceCreateEvidenceSession
{
    internal const string EnvironmentVariableName =
        "LONGGRID_BOX_R1_ACTIVATION_EVIDENCE_SESSION";
    private const string EvidenceDirectoryName = "LongGridBoxR1Evidence";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly Queue<string?> previewResponses = new();
    private readonly TaskCompletionSource previewCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private ProductBoxR1ActivationEvidenceSession(
        Guid sessionId,
        string directoryPath)
    {
        SessionId = sessionId;
        DirectoryPath = directoryPath;
        previewResponses.Enqueue(null);
    }

    internal Guid SessionId { get; }

    internal string DirectoryPath { get; }

    internal string ReadyPath => Path.Combine(DirectoryPath, "ready.json");

    internal string ResultPath => Path.Combine(DirectoryPath, "result.json");

    internal string ProgressPath => Path.Combine(DirectoryPath, "progress.txt");

    internal int PreviewVisualTreeCount { get; private set; }

    internal int PreviewActivatedCount { get; private set; }

    internal int PreviewDrivenCount { get; private set; }

    internal static ProductBoxR1ActivationEvidenceSession?
        TryCreateFromEnvironment()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!Guid.TryParseExact(raw, "N", out Guid sessionId))
        {
            throw new InvalidOperationException(
                "BOX-R1 evidence session id must be a 32-character GUID.");
        }

        string evidenceRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            EvidenceDirectoryName));
        string directoryPath = Path.GetFullPath(Path.Combine(
            evidenceRoot,
            sessionId.ToString("N")));
        string expectedPrefix = evidenceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directoryPath.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException(
                "BOX-R1 evidence directory must already exist under the system temporary evidence root.");
        }

        FileAttributes attributes = File.GetAttributes(directoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            throw new InvalidOperationException(
                "BOX-R1 evidence directory must be empty and must not be a reparse point.");
        }

        return new(sessionId, directoryPath);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Guid.TryParseExact(raw, "N", out Guid sessionId)
            ? $"LongGrid.BoxR1Evidence.{sessionId:N}"
            : defaultKey;
    }

    public bool TryTakePreviewResponse(out string? response) =>
        previewResponses.TryDequeue(out response);

    public void RecordStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        File.AppendAllText(ProgressPath, stage + Environment.NewLine);
    }

    public void ObservePreview(DesktopWorkspaceCreatePreviewWindow previewWindow)
    {
        ArgumentNullException.ThrowIfNull(previewWindow);
        if (previewWindow.HasEvidenceVisualTree)
        {
            PreviewVisualTreeCount++;
        }
        if (previewWindow.WasActivated)
        {
            PreviewActivatedCount++;
        }
        PreviewDrivenCount++;
        previewCompleted.TrySetResult();
    }

    public void ObserveFallbackPreview(bool hasEvidenceVisualTree)
    {
        if (hasEvidenceVisualTree)
        {
            PreviewVisualTreeCount++;
        }
        PreviewActivatedCount++;
        PreviewDrivenCount++;
        previewCompleted.TrySetResult();
    }

    public void ObserveSafePreview(bool hasEvidenceVisualTree)
    {
        if (hasEvidenceVisualTree)
        {
            PreviewVisualTreeCount++;
        }
        PreviewActivatedCount++;
        PreviewDrivenCount++;
        previewCompleted.TrySetResult();
    }

    internal Task WaitForPreviewAsync(TimeSpan timeout) =>
        previewCompleted.Task.WaitAsync(timeout);

    internal static async Task WriteJsonAsync(string path, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        string temporaryPath = path + ".new";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await File.WriteAllBytesAsync(temporaryPath, payload);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
