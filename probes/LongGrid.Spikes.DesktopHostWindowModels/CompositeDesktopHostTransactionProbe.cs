using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using LongGrid.Core.DesktopHost;

internal static class CompositeDesktopHostTransactionProbe
{
    private const string HostId = "host";
    private const long ExpectedGeneration = 10;
    private static readonly PixelRect InitialBounds =
        new(-780, -460, 300, 180);
    private static readonly PixelRect AppliedBounds =
        new(-690, -390, 320, 200);
    private static readonly PixelRect FailureBounds =
        new(-580, -300, 340, 220);
    private static readonly RegionLayout InitialRegion =
        new(
        [
            new PixelRect(0, 0, 130, 80),
            new PixelRect(170, 100, 130, 80),
        ]);
    private static readonly RegionLayout AppliedRegion =
        new(
        [
            new PixelRect(0, 0, 150, 90),
            new PixelRect(180, 110, 140, 90),
        ]);
    private static readonly RegionLayout FailureRegion =
        new(
        [
            new PixelRect(0, 0, 160, 100),
            new PixelRect(190, 120, 150, 100),
        ]);

    internal static CompositeDesktopHostTransactionReport Run(
        bool perMonitorV2Requested)
    {
        CompositeScenarioOutcome warmUp =
            ExecuteScenario();
        if (!warmUp.Passed)
        {
            throw new InvalidOperationException(
                "The composite DesktopHost transaction warm-up failed: "
                + warmUp);
        }

        using Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        ResourceSnapshot created = before;
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        CompositeScenarioOutcome outcome =
            ExecuteScenario(
                () => created = ResourceSnapshot.Capture(process));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        bool foregroundPreserved =
            outcome.ForegroundPreserved
            && foregroundBefore == NativeMethods.GetForegroundWindow();
        bool cleanupPassed =
            outcome.CleanupPassed
            && after.UserObjects == before.UserObjects
            && after.GdiObjects == before.GdiObjects
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool passed =
            perMonitorV2Requested
            && outcome.Passed
            && foregroundPreserved
            && cleanupPassed;

        return new CompositeDesktopHostTransactionReport(
            Probe:
                "P0-07b2b2b2b3-composite-desktop-host-transaction",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            LayerOrder:
            [
                DesktopHostTransactionLayerKind.Bounds,
                DesktopHostTransactionLayerKind.Region,
                DesktopHostTransactionLayerKind.Composition,
                DesktopHostTransactionLayerKind.UiAutomation,
            ],
            SuccessfulStatus: outcome.SuccessfulStatus,
            ForcedLayerFailureCount:
                outcome.ForcedLayerFailureCount,
            ForcedLayerFailuresRolledBack:
                outcome.ForcedLayerFailuresRolledBack,
            GenerationRollbackStatus:
                outcome.GenerationRollbackStatus,
            GenerationRollbackVerified:
                outcome.GenerationRollbackVerified,
            FinalSweepVerificationCount:
                outcome.FinalSweepVerificationCount,
            EmergencyStatus: outcome.EmergencyStatus,
            EmergencyFailure: outcome.EmergencyFailure,
            EmergencyInputRemainedClosed:
                outcome.EmergencyInputRemainedClosed,
            EmergencyHostHidden: outcome.EmergencyHostHidden,
            EmergencyUnderlyingStateRestored:
                outcome.EmergencyUnderlyingStateRestored,
            FinalBoundsMatch: outcome.FinalBoundsMatch,
            FinalRegionMatch: outcome.FinalRegionMatch,
            FinalCompositionMatch:
                outcome.FinalCompositionMatch,
            FinalUiaClientMatch: outcome.FinalUiaClientMatch,
            DCompositionCommitCalls:
                outcome.DCompositionCommitCalls,
            DCompositionWaitCalls:
                outcome.DCompositionWaitCalls,
            HiddenThroughout: outcome.HiddenThroughout,
            ForegroundPreserved: foregroundPreserved,
            DisplayStateChanged: false,
            ExternalWindowStateChanged: false,
            SyntheticFailureInjection: true,
            UserObjectsBefore: before.UserObjects,
            UserObjectsCreated: created.UserObjects,
            UserObjectsAfter: after.UserObjects,
            GdiObjectsBefore: before.GdiObjects,
            GdiObjectsCreated: created.GdiObjects,
            GdiObjectsAfter: after.GdiObjects,
            ProcessHandlesBefore: before.ProcessHandles,
            ProcessHandlesCreated: created.ProcessHandles,
            ProcessHandlesAfter: after.ProcessHandles,
            CleanupPassed: cleanupPassed,
            Result: passed ? "Conditional Pass" : "Fail",
            Limitations:
            [
                "Only one hidden, same-thread, probe-owned HWND is used; the input gate is a real coordinator state gate but no visible pointer, keyboard, drag/drop, or UIA operation is routed.",
                "Layer failures are injected after each layer performs its real mutation; they are not naturally observed Win32, GDI, DirectComposition, or UIA failures.",
                "The emergency rollback-verification failure is synthetic after all real restores complete, so the probe can verify the host is hidden without leaving damaged system state.",
                "The DirectComposition visual has no rendered surface, and the UIA provider is an HWND root rather than the planned per-display Fragment tree.",
                "No display, DPI, rotation, projection, device, power, Explorer, or RDP transition was induced.",
            ]);
    }

    private static CompositeScenarioOutcome ExecuteScenario(
        Action? afterHostCreated = null)
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        using var host = CompositionUiaHost.Create(
            new UiaGenerationSnapshot(
                ExpectedGeneration - 1,
                ToRect(InitialBounds)));
        afterHostCreated?.Invoke();
        var regionAdapter = new Win32WindowRegionAdapter(
            new Dictionary<string, nint>(StringComparer.Ordinal)
            {
                [HostId] = host.Window,
            });
        bool initialRegionApplied = regionAdapter.Apply(
            Regions(InitialRegion));
        long generation = ExpectedGeneration;
        DesktopHostCompositeTransactionResult success =
            ExecuteTransaction(
                host,
                regionAdapter,
                () => generation,
                AppliedBounds,
                AppliedRegion,
                compositionRevision: 2,
                injectedLayer: null);
        bool successVerified =
            success.Status
                == DesktopHostCompositeTransactionStatus.Applied
            && MatchesAppliedState(host, regionAdapter);

        int forcedRolledBack = 0;
        foreach (DesktopHostTransactionLayerKind layer in Enum
            .GetValues<DesktopHostTransactionLayerKind>())
        {
            DesktopHostCompositeTransactionResult failed =
                ExecuteTransaction(
                    host,
                    regionAdapter,
                    () => generation,
                    FailureBounds,
                    FailureRegion,
                    compositionRevision: 10 + (int)layer,
                    injectedLayer: layer);
            if (failed.Status
                    == DesktopHostCompositeTransactionStatus
                        .RolledBack
                && failed.Failure
                    == DesktopHostCompositeTransactionFailure
                        .ApplyFailed
                && failed.FailedLayer == layer
                && MatchesAppliedState(host, regionAdapter))
            {
                forcedRolledBack++;
            }
        }

        DesktopHostCompositeTransactionResult generationRollback =
            ExecuteTransaction(
                host,
                regionAdapter,
                () => generation,
                FailureBounds,
                FailureRegion,
                compositionRevision: 20,
                injectedLayer: null,
                afterCompositionVerify: () => generation++);
        bool generationRollbackVerified =
            generationRollback.Status
                == DesktopHostCompositeTransactionStatus.RolledBack
            && generationRollback.Failure
                == DesktopHostCompositeTransactionFailure
                    .GenerationChanged
            && MatchesAppliedState(host, regionAdapter);
        generation = ExpectedGeneration;

        var emergencyGate = new ProbeInputGate(host.Window);
        IDesktopHostTransactionLayer[] emergencyLayers =
            CreateLayers(
                host,
                regionAdapter,
                FailureBounds,
                FailureRegion,
                compositionRevision: 30,
                injectedLayer:
                    DesktopHostTransactionLayerKind.UiAutomation);
        ((BoundsCompositeLayer)emergencyLayers[0])
            .ForceRestoreVerificationFailure = true;
        var emergencyCoordinator =
            new DesktopHostCompositeTransactionCoordinator(
                () => generation,
                emergencyGate,
                emergencyLayers);
        DesktopHostCompositeTransactionResult emergency =
            emergencyCoordinator.Execute(generation);
        bool finalBounds =
            host.MatchesBounds(AppliedBounds);
        bool finalRegion =
            regionAdapter.Matches(Regions(AppliedRegion));
        bool finalComposition =
            host.MatchesVisual(
                ExpectedGeneration,
                revision: 2);
        bool finalUia = MatchesAppliedUia(host);
        bool emergencyUnderlyingStateRestored =
            finalBounds
            && finalRegion
            && finalComposition
            && finalUia;
        bool hiddenThroughout =
            !NativeMethods.IsWindowVisible(host.Window);
        bool regionsCleared =
            NativeMethods.SetWindowRgn(
                host.Window,
                nint.Zero,
                redraw: false) != 0;
        int commitCalls = host.CommitCalls;
        int waitCalls = host.WaitCalls;
        bool cleanupPassed = host.DisposeAndVerify();
        bool foregroundPreserved =
            foreground == NativeMethods.GetForegroundWindow();
        bool passed =
            initialRegionApplied
            && successVerified
            && forcedRolledBack == 4
            && generationRollbackVerified
            && emergency.Status
                == DesktopHostCompositeTransactionStatus
                    .RollbackFailed
            && emergency.Failure
                == DesktopHostCompositeTransactionFailure
                    .RestoreVerificationFailed
            && emergency.InputClosed
            && emergency.HostsHidden
            && emergencyUnderlyingStateRestored
            && hiddenThroughout
            && regionsCleared
            && foregroundPreserved
            && cleanupPassed;

        return new CompositeScenarioOutcome(
            passed,
            success.Status,
            ForcedLayerFailureCount: 4,
            ForcedLayerFailuresRolledBack: forcedRolledBack,
            generationRollback.Status,
            generationRollbackVerified,
            FinalSweepVerificationCount: 4,
            emergency.Status,
            emergency.Failure,
            emergency.InputClosed,
            emergency.HostsHidden,
            emergencyUnderlyingStateRestored,
            finalBounds,
            finalRegion,
            finalComposition,
            finalUia,
            commitCalls,
            waitCalls,
            hiddenThroughout,
            foregroundPreserved,
            cleanupPassed);
    }

    private static DesktopHostCompositeTransactionResult
        ExecuteTransaction(
        CompositionUiaHost host,
        Win32WindowRegionAdapter regionAdapter,
        Func<long> currentGeneration,
        PixelRect proposedBounds,
        RegionLayout proposedRegion,
        long compositionRevision,
        DesktopHostTransactionLayerKind? injectedLayer,
        Action? afterCompositionVerify = null)
    {
        var gate = new ProbeInputGate(host.Window);
        IDesktopHostTransactionLayer[] layers =
            CreateLayers(
                host,
                regionAdapter,
                proposedBounds,
                proposedRegion,
                compositionRevision,
                injectedLayer,
                afterCompositionVerify);
        var coordinator =
            new DesktopHostCompositeTransactionCoordinator(
                currentGeneration,
                gate,
                layers);
        return coordinator.Execute(ExpectedGeneration);
    }

    private static IDesktopHostTransactionLayer[] CreateLayers(
        CompositionUiaHost host,
        Win32WindowRegionAdapter regionAdapter,
        PixelRect proposedBounds,
        RegionLayout proposedRegion,
        long compositionRevision,
        DesktopHostTransactionLayerKind? injectedLayer,
        Action? afterCompositionVerify = null) =>
        [
            new BoundsCompositeLayer(
                host,
                proposedBounds,
                injectedLayer
                    == DesktopHostTransactionLayerKind.Bounds),
            new RegionCompositeLayer(
                regionAdapter,
                proposedRegion,
                injectedLayer
                    == DesktopHostTransactionLayerKind.Region),
            new CompositionCompositeLayer(
                host,
                ExpectedGeneration,
                compositionRevision,
                injectedLayer
                    == DesktopHostTransactionLayerKind.Composition,
                afterCompositionVerify),
            new UiaCompositeLayer(
                host,
                new UiaGenerationSnapshot(
                    ExpectedGeneration,
                    ToRect(proposedBounds)),
                injectedLayer
                    == DesktopHostTransactionLayerKind.UiAutomation),
        ];

    private static bool MatchesAppliedState(
        CompositionUiaHost host,
        Win32WindowRegionAdapter regionAdapter)
    {
        return host.MatchesBounds(AppliedBounds)
            && regionAdapter.Matches(Regions(AppliedRegion))
            && host.MatchesVisual(
                ExpectedGeneration,
                revision: 2)
            && MatchesAppliedUia(host);
    }

    private static bool MatchesAppliedUia(
        CompositionUiaHost host)
    {
        UiaClientSnapshot client =
            CompositionUiaGenerationProbe.ReadWithUiaClient(
                host.Window);
        return host.Provider.Snapshot
                == new UiaGenerationSnapshot(
                    ExpectedGeneration,
                    ToRect(AppliedBounds))
            && client.Generation == ExpectedGeneration
            && client.Bounds == ToRect(AppliedBounds);
    }

    private static Dictionary<string, RegionLayout> Regions(
        RegionLayout layout) =>
        new Dictionary<string, RegionLayout>(StringComparer.Ordinal)
        {
            [HostId] = layout,
        };

    private static Rect ToRect(PixelRect bounds) =>
        new(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);
}

internal sealed class ProbeInputGate(nint window)
    : IDesktopHostInputGate
{
    public bool InputClosed { get; private set; }

    public bool Close()
    {
        InputClosed = true;
        return true;
    }

    public bool Reopen()
    {
        InputClosed = false;
        return true;
    }

    public bool HideAffectedHosts()
    {
        _ = NativeMethods.ShowWindow(
            window,
            NativeMethods.SwHide);
        return !NativeMethods.IsWindowVisible(window);
    }
}

internal sealed class BoundsSnapshot(PixelRect bounds)
    : IDesktopHostLayerSnapshot
{
    internal PixelRect Bounds { get; } = bounds;

    public void Dispose()
    {
    }
}

internal sealed class BoundsCompositeLayer(
    CompositionUiaHost host,
    PixelRect proposed,
    bool failAfterMutation)
    : IDesktopHostTransactionLayer
{
    public DesktopHostTransactionLayerKind Kind =>
        DesktopHostTransactionLayerKind.Bounds;

    internal bool ForceRestoreVerificationFailure { get; set; }

    public DesktopHostLayerCapture Capture() =>
        new(true, new BoundsSnapshot(host.CaptureBounds()));

    public bool Apply(long generation) =>
        host.ApplyBounds(proposed) && !failAfterMutation;

    public bool Verify(long generation) =>
        host.MatchesBounds(proposed);

    public bool Restore(IDesktopHostLayerSnapshot snapshot) =>
        snapshot is BoundsSnapshot bounds
        && host.ApplyBounds(bounds.Bounds);

    public bool VerifyRestored(
        IDesktopHostLayerSnapshot snapshot) =>
        !ForceRestoreVerificationFailure
        && snapshot is BoundsSnapshot bounds
        && host.MatchesBounds(bounds.Bounds);
}

internal sealed class RegionCompositeSnapshot(
    RegionCaptureResult restore,
    RegionCaptureResult verification)
    : IDesktopHostLayerSnapshot
{
    internal RegionCaptureResult RestoreCapture { get; } = restore;

    internal RegionCaptureResult VerificationCapture { get; } =
        verification;

    public void Dispose()
    {
        RestoreCapture.Dispose();
        VerificationCapture.Dispose();
    }
}

internal sealed class RegionCompositeLayer(
    Win32WindowRegionAdapter adapter,
    RegionLayout proposed,
    bool failAfterMutation)
    : IDesktopHostTransactionLayer
{
    private static readonly string[] ContainerIds = ["host"];

    public DesktopHostTransactionLayerKind Kind =>
        DesktopHostTransactionLayerKind.Region;

    public DesktopHostLayerCapture Capture()
    {
        RegionCaptureResult restore =
            adapter.Capture(ContainerIds);
        if (!restore.Succeeded)
        {
            restore.Dispose();
            return DesktopHostLayerCapture.Failed;
        }

        try
        {
            return new DesktopHostLayerCapture(
                true,
                new RegionCompositeSnapshot(
                    restore,
                    restore.Clone()));
        }
        catch
        {
            restore.Dispose();
            throw;
        }
    }

    public bool Apply(long generation) =>
        adapter.Apply(
            new Dictionary<string, RegionLayout>(
                StringComparer.Ordinal)
            {
                ["host"] = proposed,
            })
        && !failAfterMutation;

    public bool Verify(long generation) =>
        adapter.Matches(
            new Dictionary<string, RegionLayout>(
                StringComparer.Ordinal)
            {
                ["host"] = proposed,
            });

    public bool Restore(IDesktopHostLayerSnapshot snapshot) =>
        snapshot is RegionCompositeSnapshot region
        && adapter.Restore(region.RestoreCapture);

    public bool VerifyRestored(
        IDesktopHostLayerSnapshot snapshot) =>
        snapshot is RegionCompositeSnapshot region
        && adapter.Matches(region.VerificationCapture);
}

internal sealed class CompositionCompositeLayer(
    CompositionUiaHost host,
    long proposedGeneration,
    long proposedRevision,
    bool failAfterMutation,
    Action? afterVerify)
    : IDesktopHostTransactionLayer
{
    public DesktopHostTransactionLayerKind Kind =>
        DesktopHostTransactionLayerKind.Composition;

    public DesktopHostLayerCapture Capture() =>
        new(true, host.CaptureVisual());

    public bool Apply(long generation) =>
        host.ApplyVisual(
            proposedGeneration,
            proposedRevision)
        && !failAfterMutation;

    public bool Verify(long generation)
    {
        bool matches = host.MatchesVisual(
            proposedGeneration,
            proposedRevision);
        afterVerify?.Invoke();
        afterVerify = null;
        return matches;
    }

    public bool Restore(IDesktopHostLayerSnapshot snapshot) =>
        snapshot is CompositionVisualSnapshot visual
        && host.RestoreVisual(visual);

    public bool VerifyRestored(
        IDesktopHostLayerSnapshot snapshot) =>
        snapshot is CompositionVisualSnapshot visual
        && host.MatchesVisual(
            visual.Generation,
            visual.Revision);
}

internal sealed class UiaCompositeLayer(
    CompositionUiaHost host,
    UiaGenerationSnapshot proposed,
    bool failAfterMutation)
    : IDesktopHostTransactionLayer
{
    public DesktopHostTransactionLayerKind Kind =>
        DesktopHostTransactionLayerKind.UiAutomation;

    public DesktopHostLayerCapture Capture() =>
        new(true, host.Provider.Snapshot);

    public bool Apply(long generation)
    {
        host.Provider.Publish(proposed);
        return !failAfterMutation;
    }

    public bool Verify(long generation) =>
        Matches(proposed);

    public bool Restore(IDesktopHostLayerSnapshot snapshot)
    {
        if (snapshot is not UiaGenerationSnapshot uia)
        {
            return false;
        }

        host.Provider.Publish(uia);
        return true;
    }

    public bool VerifyRestored(
        IDesktopHostLayerSnapshot snapshot) =>
        snapshot is UiaGenerationSnapshot uia
        && Matches(uia);

    private bool Matches(UiaGenerationSnapshot expected)
    {
        try
        {
            UiaClientSnapshot client =
                CompositionUiaGenerationProbe.ReadWithUiaClient(
                    host.Window);
            return host.Provider.Snapshot == expected
                && client.Generation == expected.Generation
                && client.AutomationId
                    == $"LongGrid.Generation.{expected.Generation}"
                && client.Bounds == expected.Bounds;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal sealed record CompositeScenarioOutcome(
    bool Passed,
    DesktopHostCompositeTransactionStatus SuccessfulStatus,
    int ForcedLayerFailureCount,
    int ForcedLayerFailuresRolledBack,
    DesktopHostCompositeTransactionStatus GenerationRollbackStatus,
    bool GenerationRollbackVerified,
    int FinalSweepVerificationCount,
    DesktopHostCompositeTransactionStatus EmergencyStatus,
    DesktopHostCompositeTransactionFailure EmergencyFailure,
    bool EmergencyInputRemainedClosed,
    bool EmergencyHostHidden,
    bool EmergencyUnderlyingStateRestored,
    bool FinalBoundsMatch,
    bool FinalRegionMatch,
    bool FinalCompositionMatch,
    bool FinalUiaClientMatch,
    int DCompositionCommitCalls,
    int DCompositionWaitCalls,
    bool HiddenThroughout,
    bool ForegroundPreserved,
    bool CleanupPassed);

internal sealed record CompositeDesktopHostTransactionReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    IReadOnlyList<DesktopHostTransactionLayerKind> LayerOrder,
    DesktopHostCompositeTransactionStatus SuccessfulStatus,
    int ForcedLayerFailureCount,
    int ForcedLayerFailuresRolledBack,
    DesktopHostCompositeTransactionStatus GenerationRollbackStatus,
    bool GenerationRollbackVerified,
    int FinalSweepVerificationCount,
    DesktopHostCompositeTransactionStatus EmergencyStatus,
    DesktopHostCompositeTransactionFailure EmergencyFailure,
    bool EmergencyInputRemainedClosed,
    bool EmergencyHostHidden,
    bool EmergencyUnderlyingStateRestored,
    bool FinalBoundsMatch,
    bool FinalRegionMatch,
    bool FinalCompositionMatch,
    bool FinalUiaClientMatch,
    int DCompositionCommitCalls,
    int DCompositionWaitCalls,
    bool HiddenThroughout,
    bool ForegroundPreserved,
    bool DisplayStateChanged,
    bool ExternalWindowStateChanged,
    bool SyntheticFailureInjection,
    uint UserObjectsBefore,
    uint UserObjectsCreated,
    uint UserObjectsAfter,
    uint GdiObjectsBefore,
    uint GdiObjectsCreated,
    uint GdiObjectsAfter,
    int ProcessHandlesBefore,
    int ProcessHandlesCreated,
    int ProcessHandlesAfter,
    bool CleanupPassed,
    string Result,
    IReadOnlyList<string> Limitations);
