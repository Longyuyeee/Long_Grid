using System.Diagnostics;
using LongGrid.Core.DesktopHost;

internal static class WindowRegionTransactionProbe
{
    private static readonly IReadOnlyDictionary<string, PixelRect>
        WindowBounds =
            new Dictionary<string, PixelRect>(StringComparer.Ordinal)
            {
                ["one"] = new(-900, -520, 320, 220),
                ["two"] = new(-520, -280, 360, 240),
            };

    private static readonly IReadOnlyDictionary<string, RegionLayout>
        InitialRegions =
            new Dictionary<string, RegionLayout>(StringComparer.Ordinal)
            {
                ["one"] = new(
                    [
                        new PixelRect(0, 0, 120, 80),
                        new PixelRect(170, 120, 130, 80),
                    ]),
                ["two"] = new(
                    [
                        new PixelRect(10, 10, 140, 90),
                        new PixelRect(200, 130, 140, 90),
                    ]),
            };

    private static readonly IReadOnlyDictionary<string, RegionLayout>
        AppliedRegions =
            new Dictionary<string, RegionLayout>(StringComparer.Ordinal)
            {
                ["one"] = new(
                    [
                        new PixelRect(15, 20, 135, 95),
                        new PixelRect(180, 125, 115, 75),
                    ]),
                ["two"] = new(
                    [
                        new PixelRect(20, 15, 150, 100),
                        new PixelRect(215, 135, 120, 80),
                    ]),
            };

    private static readonly IReadOnlyDictionary<string, RegionLayout>
        ChangedRegions =
            new Dictionary<string, RegionLayout>(StringComparer.Ordinal)
            {
                ["one"] = new(
                    [
                        new PixelRect(30, 25, 145, 105),
                        new PixelRect(195, 140, 105, 65),
                    ]),
                ["two"] = new(
                    [
                        new PixelRect(35, 25, 160, 110),
                        new PixelRect(230, 145, 100, 70),
                    ]),
            };

    internal static WindowRegionTransactionReport Run(
        bool perMonitorV2Requested)
    {
        WarmUpLifecycle();
        using Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot before = ResourceSnapshot.Capture(process);
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        HiddenTransactionWindowSet? windows = null;
        ResourceSnapshot created = before;
        RegionTransactionResult applied;
        RegionTransactionResult generationRollback;
        RegionTransactionResult partialRollback;
        bool finalRegionsMatch;
        bool hiddenThroughout;
        bool foregroundPreserved;
        bool cleanupPassed;
        int regionCaptures;
        int regionApplications;
        int ownershipTransfers;
        int injectedPartialApplications;

        try
        {
            windows = HiddenTransactionWindowSet.Create(
                WindowBounds);
            var adapter = new Win32WindowRegionAdapter(
                windows.Handles);
            if (!adapter.Apply(InitialRegions))
            {
                throw new InvalidOperationException(
                    "The initial Window Region setup failed.");
            }

            created = ResourceSnapshot.Capture(process);
            hiddenThroughout = AreHidden(
                windows.Handles.Values);
            foregroundPreserved =
                foregroundBefore == NativeMethods.GetForegroundWindow();
            long generation = 1;
            applied = WindowRegionTransaction.Execute(
                adapter,
                generation,
                () => generation,
                AppliedRegions,
                RegionFailureInjection.None);
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreserved &=
                foregroundBefore == NativeMethods.GetForegroundWindow();

            generation = 2;
            generationRollback =
                WindowRegionTransaction.Execute(
                    adapter,
                    generation,
                    () => generation,
                    ChangedRegions,
                    RegionFailureInjection.AfterAllApplied,
                    () => generation = 3);
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreserved &=
                foregroundBefore == NativeMethods.GetForegroundWindow();

            partialRollback =
                WindowRegionTransaction.Execute(
                    adapter,
                    generation,
                    () => generation,
                    ChangedRegions,
                    RegionFailureInjection.AfterFirstApplied);
            hiddenThroughout &=
                AreHidden(windows.Handles.Values);
            foregroundPreserved &=
                foregroundBefore == NativeMethods.GetForegroundWindow();
            finalRegionsMatch =
                adapter.Matches(AppliedRegions);
            regionCaptures = adapter.CaptureCalls;
            regionApplications = adapter.ApplyCalls;
            ownershipTransfers = adapter.OwnershipTransfers;
            injectedPartialApplications =
                partialRollback.AppliedBeforeFailure;
        }
        finally
        {
            bool regionsCleared =
                windows is not null
                && ClearRegions(windows.Handles.Values);
            windows?.Dispose();
            cleanupPassed =
                regionsCleared
                && (windows?.CleanupSucceeded ?? false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        ResourceSnapshot after = ResourceSnapshot.Capture(process);
        foregroundPreserved &=
            foregroundBefore == NativeMethods.GetForegroundWindow();
        cleanupPassed =
            cleanupPassed
            && after.UserObjects == before.UserObjects
            && after.GdiObjects == before.GdiObjects
            && after.ProcessHandles <= before.ProcessHandles + 2;
        bool passed =
            perMonitorV2Requested
            && applied.Status == RegionTransactionStatus.Applied
            && generationRollback.Status
                == RegionTransactionStatus.RolledBack
            && generationRollback.Failure
                == RegionTransactionFailure.GenerationChanged
            && generationRollback.RollbackVerified
            && partialRollback.Status
                == RegionTransactionStatus.RolledBack
            && partialRollback.Failure
                == RegionTransactionFailure.InjectedPartialFailure
            && partialRollback.AppliedBeforeFailure == 1
            && partialRollback.RollbackVerified
            && finalRegionsMatch
            && hiddenThroughout
            && foregroundPreserved
            && cleanupPassed;

        return new WindowRegionTransactionReport(
            Probe:
                "P0-07b2b2b2b1-window-region-transaction",
            TimestampUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Architecture:
                System.Runtime.InteropServices.RuntimeInformation
                    .OSArchitecture.ToString(),
            PerMonitorV2Requested: perMonitorV2Requested,
            WindowCount: WindowBounds.Count,
            RegionCaptures: regionCaptures,
            RegionApplications: regionApplications,
            OwnershipTransfers: ownershipTransfers,
            AppliedStatus: applied.Status,
            GenerationRollbackStatus:
                generationRollback.Status,
            GenerationRollbackFailure:
                generationRollback.Failure,
            GenerationRollbackVerified:
                generationRollback.RollbackVerified,
            PartialRollbackStatus: partialRollback.Status,
            PartialRollbackFailure: partialRollback.Failure,
            PartialAppliedBeforeFailure:
                partialRollback.AppliedBeforeFailure,
            PartialRollbackVerified:
                partialRollback.RollbackVerified,
            FinalRegionsMatch: finalRegionsMatch,
            HiddenThroughout: hiddenThroughout,
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
                "Only hidden, same-thread, probe-owned HWND regions were changed.",
                "The partial and generation failures are injected after real SetWindowRgn ownership transfers.",
                "DirectComposition, UI Automation providers, visible rendering, and cross-thread HWNDs are not part of this probe.",
                "No display, DPI, rotation, projection, device, power, or RDP transition was induced.",
            ]);
    }

    private static void WarmUpLifecycle()
    {
        HiddenTransactionWindowSet windows =
            HiddenTransactionWindowSet.Create(WindowBounds);
        bool regionsCleared;
        try
        {
            var adapter = new Win32WindowRegionAdapter(
                windows.Handles);
            if (!adapter.Apply(InitialRegions)
                || !adapter.Matches(InitialRegions))
            {
                throw new InvalidOperationException(
                    "The Window Region warm-up failed.");
            }

            long generation = 1;
            RegionTransactionResult applied =
                WindowRegionTransaction.Execute(
                    adapter,
                    generation,
                    () => generation,
                    AppliedRegions,
                    RegionFailureInjection.None);
            generation = 2;
            RegionTransactionResult generationRollback =
                WindowRegionTransaction.Execute(
                    adapter,
                    generation,
                    () => generation,
                    ChangedRegions,
                    RegionFailureInjection.AfterAllApplied,
                    () => generation = 3);
            RegionTransactionResult partialRollback =
                WindowRegionTransaction.Execute(
                    adapter,
                    generation,
                    () => generation,
                    ChangedRegions,
                    RegionFailureInjection.AfterFirstApplied);
            if (applied.Status
                    != RegionTransactionStatus.Applied
                || generationRollback.Status
                    != RegionTransactionStatus.RolledBack
                || partialRollback.Status
                    != RegionTransactionStatus.RolledBack
                || !adapter.Matches(AppliedRegions))
            {
                throw new InvalidOperationException(
                    "The Window Region transaction warm-up failed.");
            }
        }
        finally
        {
            regionsCleared =
                ClearRegions(windows.Handles.Values);
            windows.Dispose();
        }

        if (!regionsCleared || !windows.CleanupSucceeded)
        {
            throw new InvalidOperationException(
                "The Window Region warm-up leaked resources.");
        }
    }

    private static bool AreHidden(IEnumerable<nint> windows) =>
        windows.All(window =>
            !NativeMethods.IsWindowVisible(window));

    private static bool ClearRegions(
        IEnumerable<nint> windows)
    {
        bool cleared = true;
        foreach (nint window in windows)
        {
            cleared &=
                NativeMethods.SetWindowRgn(
                    window,
                    nint.Zero,
                    redraw: false) != 0;
        }

        return cleared;
    }
}

internal static class WindowRegionTransaction
{
    internal static RegionTransactionResult Execute(
        Win32WindowRegionAdapter adapter,
        long expectedGeneration,
        Func<long> currentGeneration,
        IReadOnlyDictionary<string, RegionLayout> proposed,
        RegionFailureInjection injection,
        Action? afterApply = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(currentGeneration);
        ArgumentNullException.ThrowIfNull(proposed);
        if (currentGeneration() != expectedGeneration)
        {
            return new RegionTransactionResult(
                RegionTransactionStatus.Superseded,
                RegionTransactionFailure.GenerationChanged,
                RollbackVerified: false,
                AppliedBeforeFailure: 0);
        }

        RegionCaptureResult original =
            adapter.Capture(proposed.Keys);
        if (!original.Succeeded)
        {
            original.Dispose();
            return new RegionTransactionResult(
                RegionTransactionStatus.CaptureFailed,
                RegionTransactionFailure.CaptureFailed,
                RollbackVerified: false,
                AppliedBeforeFailure: 0);
        }

        using (original)
        {
            if (currentGeneration() != expectedGeneration)
            {
                return new RegionTransactionResult(
                    RegionTransactionStatus.Superseded,
                    RegionTransactionFailure.GenerationChanged,
                    RollbackVerified: false,
                    AppliedBeforeFailure: 0);
            }

            RegionApplyResult apply = adapter.ApplySequential(
                proposed,
                injection
                    == RegionFailureInjection.AfterFirstApplied
                    ? 1
                    : null);
            if (!apply.Succeeded)
            {
                return RollBack(
                    adapter,
                    original,
                    RegionTransactionFailure
                        .InjectedPartialFailure,
                    apply.AppliedCount);
            }

            afterApply?.Invoke();
            if (currentGeneration() != expectedGeneration)
            {
                return RollBack(
                    adapter,
                    original,
                    RegionTransactionFailure.GenerationChanged,
                    apply.AppliedCount);
            }

            if (!adapter.Matches(proposed))
            {
                return RollBack(
                    adapter,
                    original,
                    RegionTransactionFailure.VerificationFailed,
                    apply.AppliedCount);
            }

            return new RegionTransactionResult(
                RegionTransactionStatus.Applied,
                RegionTransactionFailure.None,
                RollbackVerified: false,
                AppliedBeforeFailure: apply.AppliedCount);
        }
    }

    private static RegionTransactionResult RollBack(
        Win32WindowRegionAdapter adapter,
        RegionCaptureResult original,
        RegionTransactionFailure failure,
        int appliedBeforeFailure)
    {
        using RegionCaptureResult expected =
            original.Clone();
        bool restored = adapter.Restore(original);
        bool verified =
            restored && adapter.Matches(expected);
        return new RegionTransactionResult(
            verified
                ? RegionTransactionStatus.RolledBack
                : RegionTransactionStatus.RollbackFailed,
            failure,
            verified,
            appliedBeforeFailure);
    }
}

internal sealed class Win32WindowRegionAdapter(
    IReadOnlyDictionary<string, nint> handles)
{
    private readonly Dictionary<string, nint> _handles =
        new(handles, StringComparer.Ordinal);

    internal int CaptureCalls { get; private set; }

    internal int ApplyCalls { get; private set; }

    internal int OwnershipTransfers { get; private set; }

    internal RegionCaptureResult Capture(
        IEnumerable<string> containerIds)
    {
        CaptureCalls++;
        var regions = new Dictionary<string, OwnedRegion>(
            StringComparer.Ordinal);
        try
        {
            foreach (string containerId in containerIds
                .Order(StringComparer.Ordinal))
            {
                if (!_handles.TryGetValue(
                    containerId,
                    out nint window))
                {
                    return RegionCaptureResult.Failed(regions);
                }

                OwnedRegion region = OwnedRegion.CreateEmpty();
                if (NativeMethods.GetWindowRgn(
                    window,
                    region.Handle) == 0)
                {
                    region.Dispose();
                    return RegionCaptureResult.Failed(regions);
                }

                regions.Add(containerId, region);
            }

            return new RegionCaptureResult(true, regions);
        }
        catch
        {
            foreach (OwnedRegion region in regions.Values)
            {
                region.Dispose();
            }

            throw;
        }
    }

    internal bool Apply(
        IReadOnlyDictionary<string, RegionLayout> layouts) =>
        ApplySequential(layouts, null).Succeeded;

    internal RegionApplyResult ApplySequential(
        IReadOnlyDictionary<string, RegionLayout> layouts,
        int? failAfterApplied)
    {
        ApplyCalls++;
        int applied = 0;
        foreach (KeyValuePair<string, RegionLayout> pair
            in layouts.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal))
        {
            if (!pair.Value.IsValid)
            {
                return new RegionApplyResult(false, applied);
            }

            if (!_handles.TryGetValue(
                pair.Key,
                out nint window))
            {
                return new RegionApplyResult(false, applied);
            }

            using OwnedRegion region =
                OwnedRegion.Create(pair.Value);
            if (!region.TransferTo(window))
            {
                return new RegionApplyResult(false, applied);
            }

            OwnershipTransfers++;
            applied++;
            if (failAfterApplied == applied)
            {
                return new RegionApplyResult(false, applied);
            }
        }

        return new RegionApplyResult(true, applied);
    }

    internal bool Restore(RegionCaptureResult capture)
    {
        ApplyCalls++;
        foreach (KeyValuePair<string, OwnedRegion> pair
            in capture.Regions.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal))
        {
            if (!_handles.TryGetValue(
                pair.Key,
                out nint window)
                || !pair.Value.TransferTo(window))
            {
                return false;
            }

            OwnershipTransfers++;
        }

        return true;
    }

    internal bool Matches(
        IReadOnlyDictionary<string, RegionLayout> expected)
    {
        using RegionCaptureResult actual =
            Capture(expected.Keys);
        if (!actual.Succeeded)
        {
            return false;
        }

        foreach (KeyValuePair<string, RegionLayout> pair
            in expected)
        {
            using OwnedRegion expectedRegion =
                OwnedRegion.Create(pair.Value);
            if (!actual.Regions.TryGetValue(
                pair.Key,
                out OwnedRegion? actualRegion)
                || !NativeMethods.EqualRgn(
                    actualRegion.Handle,
                    expectedRegion.Handle))
            {
                return false;
            }
        }

        return true;
    }

    internal bool Matches(RegionCaptureResult expected)
    {
        using RegionCaptureResult actual =
            Capture(expected.Regions.Keys);
        if (!actual.Succeeded)
        {
            return false;
        }

        return expected.Regions.All(pair =>
            actual.Regions.TryGetValue(
                pair.Key,
                out OwnedRegion? actualRegion)
            && NativeMethods.EqualRgn(
                actualRegion.Handle,
                pair.Value.Handle));
    }
}

internal sealed class OwnedRegion : IDisposable
{
    private bool _owned = true;

    private OwnedRegion(nint handle)
    {
        Handle = handle;
    }

    internal nint Handle { get; }

    internal static OwnedRegion CreateEmpty()
    {
        nint handle = NativeMethods.CreateRectRgn(
            0,
            0,
            0,
            0);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException(
                "CreateRectRgn failed.");
        }

        return new OwnedRegion(handle);
    }

    internal static OwnedRegion Create(RegionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        OwnedRegion aggregate = CreateEmpty();
        try
        {
            foreach (PixelRect rectangle in layout.Rectangles)
            {
                using OwnedRegion item = new(
                    NativeMethods.CreateRectRgn(
                        rectangle.Left,
                        rectangle.Top,
                        rectangle.Right,
                        rectangle.Bottom));
                if (item.Handle == nint.Zero
                    || NativeMethods.CombineRgn(
                        aggregate.Handle,
                        aggregate.Handle,
                        item.Handle,
                        NativeMethods.RgnOr) == 0)
                {
                    throw new InvalidOperationException(
                        "Window Region construction failed.");
                }
            }

            return aggregate;
        }
        catch
        {
            aggregate.Dispose();
            throw;
        }
    }

    internal bool TransferTo(nint window)
    {
        if (!_owned
            || NativeMethods.SetWindowRgn(
                window,
                Handle,
                redraw: false) == 0)
        {
            return false;
        }

        _owned = false;
        return true;
    }

    internal OwnedRegion Clone()
    {
        if (!_owned)
        {
            throw new InvalidOperationException(
                "A transferred region cannot be cloned.");
        }

        OwnedRegion clone = CreateEmpty();
        if (NativeMethods.CombineRgn(
            clone.Handle,
            Handle,
            Handle,
            NativeMethods.RgnCopy) == 0)
        {
            clone.Dispose();
            throw new InvalidOperationException(
                "Window Region copy failed.");
        }

        return clone;
    }

    public void Dispose()
    {
        if (_owned && Handle != nint.Zero)
        {
            NativeMethods.DeleteObject(Handle);
            _owned = false;
        }
    }
}

internal sealed class RegionCaptureResult : IDisposable
{
    internal RegionCaptureResult(
        bool succeeded,
        IReadOnlyDictionary<string, OwnedRegion> regions)
    {
        Succeeded = succeeded;
        Regions = regions;
    }

    internal bool Succeeded { get; }

    internal IReadOnlyDictionary<string, OwnedRegion> Regions
    {
        get;
    }

    internal static RegionCaptureResult Failed(
        Dictionary<string, OwnedRegion> regions) =>
        new(false, regions);

    internal RegionCaptureResult Clone()
    {
        var clones = new Dictionary<string, OwnedRegion>(
            StringComparer.Ordinal);
        try
        {
            foreach (KeyValuePair<string, OwnedRegion> pair
                in Regions)
            {
                clones.Add(pair.Key, pair.Value.Clone());
            }

            return new RegionCaptureResult(
                Succeeded,
                clones);
        }
        catch
        {
            foreach (OwnedRegion region in clones.Values)
            {
                region.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        foreach (OwnedRegion region in Regions.Values)
        {
            region.Dispose();
        }
    }
}

internal sealed record RegionLayout(
    IReadOnlyList<PixelRect> Rectangles)
{
    internal bool IsValid =>
        Rectangles.Count > 0
        && Rectangles.All(rectangle =>
            rectangle.HasArea);
}

internal enum RegionFailureInjection
{
    None,
    AfterFirstApplied,
    AfterAllApplied,
}

internal enum RegionTransactionStatus
{
    Applied,
    Superseded,
    CaptureFailed,
    RolledBack,
    RollbackFailed,
}

internal enum RegionTransactionFailure
{
    None,
    GenerationChanged,
    CaptureFailed,
    InjectedPartialFailure,
    VerificationFailed,
}

internal sealed record RegionApplyResult(
    bool Succeeded,
    int AppliedCount);

internal sealed record RegionTransactionResult(
    RegionTransactionStatus Status,
    RegionTransactionFailure Failure,
    bool RollbackVerified,
    int AppliedBeforeFailure);

internal sealed record WindowRegionTransactionReport(
    string Probe,
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Architecture,
    bool PerMonitorV2Requested,
    int WindowCount,
    int RegionCaptures,
    int RegionApplications,
    int OwnershipTransfers,
    RegionTransactionStatus AppliedStatus,
    RegionTransactionStatus GenerationRollbackStatus,
    RegionTransactionFailure GenerationRollbackFailure,
    bool GenerationRollbackVerified,
    RegionTransactionStatus PartialRollbackStatus,
    RegionTransactionFailure PartialRollbackFailure,
    int PartialAppliedBeforeFailure,
    bool PartialRollbackVerified,
    bool FinalRegionsMatch,
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
