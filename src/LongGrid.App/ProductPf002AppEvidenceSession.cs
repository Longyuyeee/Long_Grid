using System.Text.Json;

namespace LongGrid.App;

internal sealed class ProductPf002AppEvidenceSession
{
    internal const string EnvironmentVariableName =
        "LONGGRID_PF002_APP_EVIDENCE_SESSION";
    private const string EvidenceDirectoryName = "LongGridEvidence";
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly Queue<string?> previewResponses = new();

    private ProductPf002AppEvidenceSession(Guid sessionId, string directoryPath)
    {
        SessionId = sessionId;
        DirectoryPath = directoryPath;
        ResultPath = Path.Combine(directoryPath, "result.json");
        previewResponses.Enqueue(null);
        previewResponses.Enqueue("PF-002 证据方格");
    }

    internal Guid SessionId { get; }

    internal string DirectoryPath { get; }

    internal string ResultPath { get; }

    internal string ProgressPath => Path.Combine(DirectoryPath, "progress.txt");

    internal int PreviewVisualTreeCount { get; private set; }

    internal int PreviewActivatedCount { get; private set; }

    internal int PreviewDrivenCount { get; private set; }

    internal static ProductPf002AppEvidenceSession? TryCreateFromEnvironment()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!Guid.TryParseExact(raw, "N", out Guid sessionId))
        {
            throw new InvalidOperationException(
                "PF-002 App evidence session id must be a 32-character GUID.");
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
        if (!directoryPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException(
                "PF-002 App evidence directory must already exist under the system temporary evidence root.");
        }

        FileAttributes attributes = File.GetAttributes(directoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            throw new InvalidOperationException(
                "PF-002 App evidence directory must be empty and must not be a reparse point.");
        }

        return new(sessionId, directoryPath);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Guid.TryParseExact(raw, "N", out Guid sessionId)
            ? $"LongGrid.Pf002Evidence.{sessionId:N}"
            : defaultKey;
    }

    internal bool TryTakePreviewResponse(out string? response) =>
        previewResponses.TryDequeue(out response);

    internal void RecordStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        File.AppendAllText(ProgressPath, stage + Environment.NewLine);
    }

    internal void ObservePreview(
        DesktopWorkspaceCreatePreviewWindow previewWindow)
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
    }

    internal void ObserveFallbackPreview(bool hasEvidenceVisualTree)
    {
        if (hasEvidenceVisualTree)
        {
            PreviewVisualTreeCount++;
        }
        PreviewActivatedCount++;
    }

    internal void ObserveSafePreview(bool hasEvidenceVisualTree)
    {
        if (hasEvidenceVisualTree)
        {
            PreviewVisualTreeCount++;
        }
        PreviewDrivenCount++;
    }

    internal async Task WriteResultAsync(object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string temporaryPath = ResultPath + ".new";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            result,
            EvidenceJsonOptions);
        await File.WriteAllBytesAsync(temporaryPath, payload);
        File.Move(temporaryPath, ResultPath, overwrite: true);
    }
}
