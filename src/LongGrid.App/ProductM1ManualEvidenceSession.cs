namespace LongGrid.App;

internal sealed class ProductM1ManualEvidenceSession
{
    internal const string EnvironmentVariableName =
        "LONGGRID_M1_MANUAL_EVIDENCE_SESSION";
    internal const string SessionDirectoryName = "LongGridM1ManualEvidence";
    internal const string MarkerFileName = ".longgrid-m1-session";

    private ProductM1ManualEvidenceSession(
        Guid sessionId,
        string sessionDirectory,
        string configurationDirectory)
    {
        SessionId = sessionId;
        SessionDirectory = sessionDirectory;
        ConfigurationDirectory = configurationDirectory;
    }

    internal Guid SessionId { get; }

    internal string SessionDirectory { get; }

    internal string ConfigurationDirectory { get; }

    internal string LaunchLogPath => Path.Combine(SessionDirectory, "launch.log");

    internal static ProductM1ManualEvidenceSession? TryCreateFromEnvironment()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!Guid.TryParseExact(raw, "N", out Guid sessionId))
        {
            throw new InvalidOperationException(
                "M1 manual evidence session id must be a 32-character GUID.");
        }

        string evidenceRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            SessionDirectoryName));
        string sessionDirectory = Path.GetFullPath(Path.Combine(
            evidenceRoot,
            sessionId.ToString("N")));
        string expectedPrefix = evidenceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sessionDirectory.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(sessionDirectory))
        {
            throw new InvalidOperationException(
                "M1 manual evidence directory must already exist under the dedicated system temporary root.");
        }

        EnsurePlainDirectory(sessionDirectory, "M1 manual evidence directory");

        string markerPath = Path.Combine(sessionDirectory, MarkerFileName);
        if (!File.Exists(markerPath)
            || !string.Equals(
                File.ReadAllText(markerPath),
                sessionId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "M1 manual evidence directory must contain its exact session marker.");
        }

        string configurationDirectory = Path.GetFullPath(Path.Combine(
            sessionDirectory,
            "config"));
        if (!Directory.Exists(configurationDirectory))
        {
            throw new InvalidOperationException(
                "M1 manual evidence configuration directory must already exist.");
        }
        EnsurePlainDirectory(
            configurationDirectory,
            "M1 manual evidence configuration directory");

        return new(sessionId, sessionDirectory, configurationDirectory);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!Guid.TryParseExact(raw, "N", out Guid sessionId))
        {
            return defaultKey;
        }

        TryCreateFromEnvironment()?.RecordStage("InstanceKeyResolved");
        return $"LongGrid.M1ManualEvidence.{sessionId:N}";
    }

    internal static void TryRecordStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        TryCreateFromEnvironment()?.RecordStage(stage);
    }

    internal void RecordStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        File.AppendAllText(
            LaunchLogPath,
            $"{DateTimeOffset.UtcNow:O}|{stage}{Environment.NewLine}");
    }

    private static void EnsurePlainDirectory(string path, string label)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"{label} must not be a reparse point.");
        }
    }
}
