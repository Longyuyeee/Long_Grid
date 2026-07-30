using System.Runtime.InteropServices;
using LongGrid.Core.DesktopHost;

internal enum ControlledDisplayScenario
{
    Baseline,
    Scale,
    Rotate,
    Attach,
    Detach,
    Projection,
    LockUnlock,
    RemoteSession,
    SleepResume,
}

internal static class ControlledDisplayMatrixProbe
{
    internal static ControlledDisplayMatrixReport Run(
        ControlledDisplayScenario scenario,
        int watchSeconds)
    {
        DisplayChangeMessageProbeReport observation =
            DisplayChangeMessageProbe.Run(
                watchSeconds,
                dynamicChangesExpected: true);
        IReadOnlyList<DisplaySignalExpectation> expectations =
            GetExpectations(scenario);
        var evidence = new List<DisplaySignalEvidence>(
            expectations.Count);
        double minimumElapsedMilliseconds = 0;
        foreach (DisplaySignalExpectation expectation in expectations)
        {
            ObservedDisplaySignal? match = observation.ObservedSignals
                .FirstOrDefault(signal =>
                    signal.ElapsedMilliseconds
                        >= minimumElapsedMilliseconds
                    && expectation.Alternatives.Contains(
                        signal.Reason));
            evidence.Add(new DisplaySignalEvidence(
                expectation.Name,
                expectation.Alternatives,
                match is not null,
                match?.ElapsedMilliseconds));
            if (match is not null)
            {
                minimumElapsedMilliseconds =
                    match.ElapsedMilliseconds;
            }
        }
        bool infrastructurePassed =
            observation.Result == "Conditional Pass"
            && observation.DynamicChangesExpected
            && observation.SnapshotFailures == 0
            && observation.FinalState
                == DisplayTopologyStabilizationState.Ready;
        bool expectedSignalsObserved =
            evidence.All(item => item.Observed);
        string result = !infrastructurePassed
            ? "Fail"
            : expectedSignalsObserved
                ? "Observed Pass"
                : "Inconclusive";

        return new ControlledDisplayMatrixReport(
            Probe: "P0-07b2b2b2b4b-controlled-display-matrix",
            TimestampUtc: observation.TimestampUtc,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            Scenario: scenario,
            WatchSeconds: watchSeconds,
            ExpectedSignals: evidence,
            ExpectedSignalsObserved: expectedSignalsObserved,
            FinalState: observation.FinalState,
            FinalGeneration: observation.FinalGeneration,
            FinalReasons: observation.FinalReasons,
            ObservedReasonCounts: observation.ObservedReasonCounts,
            SnapshotAttempts: observation.SnapshotAttempts,
            SnapshotFailures: observation.SnapshotFailures,
            StaleSnapshotsDiscarded: observation.StaleSnapshots,
            ReadyTransitions: observation.ReadyTransitions,
            DpiSuggestedRectsApplied:
                observation.DpiSuggestedRectsApplied,
            WindowLifecyclePassed:
                observation.WtsRegistrationSucceeded
                && observation.WtsUnregistrationSucceeded
                && observation.WindowClassRegistered
                && observation.WindowClassUnregistered,
            UserObjectsBefore: observation.UserObjectsBefore,
            UserObjectsAfter: observation.UserObjectsAfter,
            GdiObjectsBefore: observation.GdiObjectsBefore,
            GdiObjectsAfter: observation.GdiObjectsAfter,
            ProcessHandlesBefore: observation.ProcessHandlesBefore,
            ProcessHandlesAfter: observation.ProcessHandlesAfter,
            SystemMutationRequestedByProbe: false,
            Result: result,
            Privacy:
            [
                "The scenario is a fixed enum; no operator-entered label is written to the report.",
                "No monitor name, device path, adapter/target ID, topology fingerprint, window title, or session ID is printed.",
                "Counts and public event categories are retained; raw identifiers and message payloads are discarded.",
            ],
            Limitations:
            [
                "Observed Pass means the expected public notifications arrived and the topology stabilized on this machine; it is not a cross-hardware certification.",
                "The operator, Windows Settings, docking hardware, projection UI, power controls, or RDP client performs the transition outside this process.",
                "The observer cannot prove visual correctness, Narrator speech, input behavior, or successful rollback by itself.",
                "A missing expected signal is Inconclusive rather than Pass; repeat with screen recording and OS/GPU/dock metadata stored outside the redacted JSON.",
            ]);
    }

    internal static bool TryParseScenario(
        string value,
        out ControlledDisplayScenario scenario)
    {
        scenario = value switch
        {
            "baseline" => ControlledDisplayScenario.Baseline,
            "scale" => ControlledDisplayScenario.Scale,
            "rotate" => ControlledDisplayScenario.Rotate,
            "attach" => ControlledDisplayScenario.Attach,
            "detach" => ControlledDisplayScenario.Detach,
            "projection" => ControlledDisplayScenario.Projection,
            "lock-unlock" => ControlledDisplayScenario.LockUnlock,
            "remote-session" => ControlledDisplayScenario.RemoteSession,
            "sleep-resume" => ControlledDisplayScenario.SleepResume,
            _ => default,
        };
        return value is "baseline"
            or "scale"
            or "rotate"
            or "attach"
            or "detach"
            or "projection"
            or "lock-unlock"
            or "remote-session"
            or "sleep-resume";
    }

    private static IReadOnlyList<DisplaySignalExpectation> GetExpectations(
        ControlledDisplayScenario scenario) =>
        scenario switch
        {
            ControlledDisplayScenario.Baseline => [],
            ControlledDisplayScenario.Scale =>
            [
                new(
                    "DPI or display-configuration notification",
                    [
                        DisplayChangeReason.Dpi,
                        DisplayChangeReason.DisplayConfiguration,
                    ]),
            ],
            ControlledDisplayScenario.Rotate
                or ControlledDisplayScenario.Attach
                or ControlledDisplayScenario.Detach
                or ControlledDisplayScenario.Projection =>
            [
                new(
                    "display-configuration or device notification",
                    [
                        DisplayChangeReason.DisplayConfiguration,
                        DisplayChangeReason.Device,
                    ]),
            ],
            ControlledDisplayScenario.LockUnlock
                or ControlledDisplayScenario.RemoteSession =>
            [
                new(
                    "session unavailable",
                    [DisplayChangeReason.SessionUnavailable]),
                new(
                    "session available",
                    [DisplayChangeReason.SessionAvailable]),
            ],
            ControlledDisplayScenario.SleepResume =>
            [
                new(
                    "power suspend",
                    [DisplayChangeReason.PowerSuspend]),
                new(
                    "power resume",
                    [DisplayChangeReason.PowerResume]),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
}

internal sealed record DisplaySignalExpectation(
    string Name,
    IReadOnlyList<DisplayChangeReason> Alternatives);

internal sealed record DisplaySignalEvidence(
    string Name,
    IReadOnlyList<DisplayChangeReason> Alternatives,
    bool Observed,
    double? ObservedAtMilliseconds);

internal sealed record ControlledDisplayMatrixReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    ControlledDisplayScenario Scenario,
    int WatchSeconds,
    IReadOnlyList<DisplaySignalEvidence> ExpectedSignals,
    bool ExpectedSignalsObserved,
    DisplayTopologyStabilizationState FinalState,
    long FinalGeneration,
    DisplayChangeReason FinalReasons,
    IReadOnlyDictionary<DisplayChangeReason, int> ObservedReasonCounts,
    int SnapshotAttempts,
    int SnapshotFailures,
    int StaleSnapshotsDiscarded,
    int ReadyTransitions,
    int DpiSuggestedRectsApplied,
    bool WindowLifecyclePassed,
    uint UserObjectsBefore,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesAfter,
    bool SystemMutationRequestedByProbe,
    string Result,
    IReadOnlyList<string> Privacy,
    IReadOnlyList<string> Limitations);
