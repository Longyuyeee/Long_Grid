namespace LongGrid.Core.Taskbar;

public static class TaskbarWorkerProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumResponseCharacters = 64 * 1024;
    public const string ProbePurpose = "TaskbarR1ReadOnlyCompatibilityProbe";
    public const string StartupRecoveryPurpose =
        "TaskbarR2StartupRecoveryPreflight";
    public const string NativeAdapterCertificationPurpose =
        "TaskbarR2NativeAdapterCertificationPreflight";
}

public sealed record TaskbarWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    TaskbarCompatibilityReport Report);

public enum TaskbarStartupRecoveryStatus
{
    NoRecoveryRequired,
    LeaseContended,
    RecoveryJournalInvalid,
    RecoveryJournalIoFailure,
    RecoveryDeferredCompatibility,
    RecoveryDeferredTargetChanged,
    RecoveryDeferredAdapterUnavailable,
}

public sealed record TaskbarStartupRecoveryWorkerResponse(
    int ProtocolVersion,
    string Purpose,
    string RequestId,
    TaskbarStartupRecoveryStatus Status,
    string DiagnosticCode,
    TaskbarAppearanceRecoveryPhase? RecoveryPhase,
    bool JournalPreserved,
    bool ModifiedSystemState,
    TaskbarCompatibilityReport? Report);

public sealed record TaskbarNativeAdapterCertificationResponse(
    int ProtocolVersion,
    string Purpose,
    string RequestId,
    TaskbarNativeAdapterAvailability AdapterAvailability,
    string AdapterId,
    bool ModifiedSystemState,
    TaskbarCompatibilityReport Report);

public static class TaskbarAppearanceRecoveryPath
{
    public static string ResolveDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LongGrid",
        "TaskbarRecovery");
}
