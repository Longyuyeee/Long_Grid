namespace LongGrid.App;

internal sealed class ProductDesktopFirstStartupEvidenceSession
{
    internal const string EnvironmentVariableName =
        "LONGGRID_DESKTOP_FIRST_STARTUP_EVIDENCE_SESSION";
    private const string EvidenceDirectoryName = "LongGridDesktopFirstEvidence";

    private ProductDesktopFirstStartupEvidenceSession(
        Guid sessionId,
        string directoryPath)
    {
        SessionId = sessionId;
        DirectoryPath = directoryPath;
    }

    internal Guid SessionId { get; }

    internal string DirectoryPath { get; }

    internal static ProductDesktopFirstStartupEvidenceSession?
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
                "Desktop-first evidence session id must be a 32-character GUID.");
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
                "Desktop-first evidence directory must already exist under the system temporary evidence root.");
        }

        FileAttributes attributes = File.GetAttributes(directoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            throw new InvalidOperationException(
                "Desktop-first evidence directory must be empty and must not be a reparse point.");
        }

        return new(sessionId, directoryPath);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Guid.TryParseExact(raw, "N", out Guid sessionId)
            ? $"LongGrid.DesktopFirstEvidence.{sessionId:N}"
            : defaultKey;
    }
}
