namespace LongGrid.Core.Taskbar;

public static class TaskbarWorkerProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumResponseCharacters = 64 * 1024;
    public const string ProbePurpose = "TaskbarR1ReadOnlyCompatibilityProbe";
}

public sealed record TaskbarWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    TaskbarCompatibilityReport Report);
