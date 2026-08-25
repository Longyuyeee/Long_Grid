using System.Text.Json;

namespace LongGrid.App;

internal sealed class ProductBoxesRuntimeEnableEvidenceSession
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    internal const string EnvironmentVariableName =
        "LONGGRID_BOXES_RUNTIME_ENABLE_EVIDENCE_SESSION";
    private const string EvidenceDirectoryName =
        "LongGridBoxesRuntimeEnableEvidence";

    private ProductBoxesRuntimeEnableEvidenceSession(
        Guid sessionId,
        string directoryPath)
    {
        SessionId = sessionId;
        DirectoryPath = directoryPath;
    }

    internal Guid SessionId { get; }

    internal string DirectoryPath { get; }

    internal string InitialReadyPath => Path.Combine(
        DirectoryPath,
        "initial-ready.json");

    internal string InitialObservedAckPath => Path.Combine(
        DirectoryPath,
        "initial-observed.ack");

    internal string DisabledReadyPath => Path.Combine(
        DirectoryPath,
        "disabled-ready.json");

    internal string DisabledObservedAckPath => Path.Combine(
        DirectoryPath,
        "disabled-observed.ack");

    internal string ResultPath => Path.Combine(
        DirectoryPath,
        "runtime-enable-result.json");

    internal async Task WriteJsonAsync(string path, object value)
    {
        string fullPath = ValidateChildPath(path);
        string temporaryPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, fullPath, overwrite: false);
    }

    internal async Task WaitForAckAsync(
        string path,
        TimeSpan timeout)
    {
        string fullPath = ValidateChildPath(path);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(fullPath))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Runtime-enable evidence acknowledgement timed out: {Path.GetFileName(fullPath)}");
    }

    internal static ProductBoxesRuntimeEnableEvidenceSession?
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
                "Runtime-enable evidence session id must be a 32-character GUID.");
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
                "Runtime-enable evidence directory must already exist under the system temporary evidence root.");
        }

        FileAttributes attributes = File.GetAttributes(directoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            throw new InvalidOperationException(
                "Runtime-enable evidence directory must be empty and must not be a reparse point.");
        }

        return new(sessionId, directoryPath);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Guid.TryParseExact(raw, "N", out Guid sessionId)
            ? $"LongGrid.BoxesRuntimeEnableEvidence.{sessionId:N}"
            : defaultKey;
    }

    private string ValidateChildPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string expectedPrefix = DirectoryPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(
            expectedPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Runtime-enable evidence path escaped its session directory.");
        }

        return fullPath;
    }
}
