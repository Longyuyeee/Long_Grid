using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class DesktopHostCompositeTransactionCoordinatorTests
{
    [Fact]
    public void AppliesAndVerifiesAllLayersBeforeReopeningInput()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        var gate = new FakeInputGate(events);
        var coordinator = CreateCoordinator(() => 7, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.Applied,
            result.Status);
        Assert.True(result.KeepsProposedState);
        Assert.False(result.InputClosed);
        Assert.False(result.HostsHidden);
        Assert.Equal(4, result.AppliedLayerCount);
        Assert.Equal(
            [
                "input:close",
                "Bounds:capture",
                "Region:capture",
                "Composition:capture",
                "UiAutomation:capture",
                "Bounds:apply",
                "Bounds:verify",
                "Region:apply",
                "Region:verify",
                "Composition:apply",
                "Composition:verify",
                "UiAutomation:apply",
                "UiAutomation:verify",
                "Bounds:verify",
                "Region:verify",
                "Composition:verify",
                "UiAutomation:verify",
                "input:reopen",
            ],
            events.Take(18));
        Assert.All(layers, layer =>
            Assert.True(layer.SnapshotDisposed));
    }

    [Fact]
    public void RejectsStaleGenerationWithoutClosingInput()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        var gate = new FakeInputGate(events);
        var coordinator = CreateCoordinator(() => 8, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.Superseded,
            result.Status);
        Assert.Empty(events);
    }

    [Fact]
    public void StopsWhenInputCannotBeClosed()
    {
        var events = new List<string>();
        var gate = new FakeInputGate(events)
        {
            CloseResult = false,
        };
        var coordinator = CreateCoordinator(
            () => 7,
            gate,
            CreateLayers(events));

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.InputGateFailed,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.InputCloseFailed,
            result.Failure);
        Assert.Equal(["input:close"], events);
    }

    [Fact]
    public void DisposesCapturedSnapshotsWhenCaptureFails()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[2].CaptureResult = false;
        var gate = new FakeInputGate(events);
        var coordinator = CreateCoordinator(() => 7, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            DesktopHostTransactionLayerKind.Composition,
            result.FailedLayer);
        Assert.True(layers[0].SnapshotDisposed);
        Assert.True(layers[1].SnapshotDisposed);
        Assert.False(layers[2].ApplyCalled);
        Assert.Equal("input:reopen", events[^1]);
    }

    [Fact]
    public void ConvertsCaptureExceptionsIntoAClosedFailure()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[1].ThrowOnCapture = true;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            DesktopHostTransactionLayerKind.Region,
            result.FailedLayer);
        Assert.Equal("input:reopen", events[^1]);
    }

    [Theory]
    [InlineData(DesktopHostTransactionLayerKind.Bounds)]
    [InlineData(DesktopHostTransactionLayerKind.Region)]
    [InlineData(DesktopHostTransactionLayerKind.Composition)]
    [InlineData(DesktopHostTransactionLayerKind.UiAutomation)]
    public void RollsBackApplyFailuresInReverseOrder(
        DesktopHostTransactionLayerKind failedKind)
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers.Single(layer => layer.Kind == failedKind)
            .ApplyResult = false;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        int failedIndex = Array.FindIndex(
            layers,
            layer => layer.Kind == failedKind);
        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.ApplyFailed,
            result.Failure);
        Assert.Equal(failedIndex + 1, result.RestoredLayerCount);
        string[] restoreEvents = events
            .Where(item => item.EndsWith(
                ":restore",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            layers.Take(failedIndex + 1)
                .Reverse()
                .Select(layer => $"{layer.Kind}:restore"),
            restoreEvents);
        Assert.Equal("input:reopen", events[^1]);
    }

    [Fact]
    public void RollsBackWhenLayerVerificationFails()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[2].VerifyResult = false;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.VerificationFailed,
            result.Failure);
        Assert.Equal(3, result.RestoredLayerCount);
        Assert.False(layers[3].ApplyCalled);
    }

    [Fact]
    public void ConvertsApplyExceptionsIntoVerifiedRollback()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[2].ThrowOnApply = true;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.ApplyFailed,
            result.Failure);
        Assert.Equal(3, result.RestoredLayerCount);
    }

    [Fact]
    public void FinalVerificationSweepCatchesCrossLayerDrift()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[0].FailVerificationOnCall = 2;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.VerificationFailed,
            result.Failure);
        Assert.Equal(
            DesktopHostTransactionLayerKind.Bounds,
            result.FailedLayer);
        Assert.Equal(4, result.RestoredLayerCount);
        Assert.Equal(2, layers[0].VerificationCalls);
    }

    [Fact]
    public void RollsBackWhenGenerationChangesAfterARealLayer()
    {
        long generation = 7;
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[1].AfterVerify = () => generation = 8;
        var coordinator = CreateCoordinator(
            () => generation,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.GenerationChanged,
            result.Failure);
        Assert.Equal(2, result.RestoredLayerCount);
        Assert.False(layers[2].ApplyCalled);
    }

    [Fact]
    public void HidesHostsAndKeepsInputClosedWhenRestoreFails()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[2].VerifyResult = false;
        layers[1].RestoreResult = false;
        var gate = new FakeInputGate(events);
        var coordinator = CreateCoordinator(() => 7, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.RestoreFailed,
            result.Failure);
        Assert.Equal(
            DesktopHostTransactionLayerKind.Region,
            result.FailedLayer);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.Equal("input:hide", events[^1]);
        Assert.Contains("Bounds:restore", events);
    }

    [Fact]
    public void ConvertsRestoreExceptionsIntoEmergencyHide()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[2].ApplyResult = false;
        layers[1].ThrowOnRestore = true;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.RestoreFailed,
            result.Failure);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
    }

    [Fact]
    public void HidesHostsWhenRestoredStateCannotBeVerified()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[3].ApplyResult = false;
        layers[0].RestoreVerificationResult = false;
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure
                .RestoreVerificationFailed,
            result.Failure);
        Assert.True(result.HostsHidden);
    }

    [Fact]
    public void VerifiesRollbackOnlyAfterEveryTouchedLayerIsRestored()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[3].ApplyResult = false;
        layers[3].RestoreVerification =
            () => events.Contains("Bounds:restore");
        var coordinator = CreateCoordinator(
            () => 7,
            new FakeInputGate(events),
            layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RolledBack,
            result.Status);
        Assert.Equal(4, result.RestoredLayerCount);
        int lastRestore = events.FindLastIndex(item =>
            item.EndsWith(":restore", StringComparison.Ordinal));
        int firstRestoreVerification = events.FindIndex(item =>
            item.EndsWith(
                ":verify-restored",
                StringComparison.Ordinal));
        Assert.True(firstRestoreVerification > lastRestore);
    }

    [Fact]
    public void RollsBackAndHidesWhenInputCannotBeReopened()
    {
        var events = new List<string>();
        var gate = new FakeInputGate(events)
        {
            ReopenResult = false,
        };
        FakeLayer[] layers = CreateLayers(events);
        var coordinator = CreateCoordinator(() => 7, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            DesktopHostCompositeTransactionFailure.InputReopenFailed,
            result.Failure);
        Assert.Equal(4, result.RestoredLayerCount);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
    }

    [Fact]
    public void ReportsEmergencyHideFailure()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);
        layers[0].ApplyResult = false;
        layers[0].RestoreResult = false;
        var gate = new FakeInputGate(events)
        {
            HideResult = false,
        };
        var coordinator = CreateCoordinator(() => 7, gate, layers);

        DesktopHostCompositeTransactionResult result =
            coordinator.Execute(7);

        Assert.Equal(
            DesktopHostCompositeTransactionFailure
                .EmergencyHideFailed,
            result.Failure);
        Assert.False(result.HostsHidden);
        Assert.True(result.InputClosed);
    }

    [Fact]
    public void RequiresTheAuditedLayerOrder()
    {
        var events = new List<string>();
        FakeLayer[] layers = CreateLayers(events);

        Assert.Throws<ArgumentException>(() =>
            CreateCoordinator(
                () => 7,
                new FakeInputGate(events),
                layers.Reverse()));
    }

    private static DesktopHostCompositeTransactionCoordinator
        CreateCoordinator(
            Func<long> generation,
            FakeInputGate gate,
            IEnumerable<FakeLayer> layers) =>
        new(generation, gate, layers);

    private static FakeLayer[] CreateLayers(
        List<string> events) =>
        [
            new(DesktopHostTransactionLayerKind.Bounds, events),
            new(DesktopHostTransactionLayerKind.Region, events),
            new(DesktopHostTransactionLayerKind.Composition, events),
            new(DesktopHostTransactionLayerKind.UiAutomation, events),
        ];

    private sealed class FakeInputGate(List<string> events)
        : IDesktopHostInputGate
    {
        internal bool CloseResult { get; init; } = true;

        internal bool ReopenResult { get; init; } = true;

        internal bool HideResult { get; init; } = true;

        public bool Close()
        {
            events.Add("input:close");
            return CloseResult;
        }

        public bool Reopen()
        {
            events.Add("input:reopen");
            return ReopenResult;
        }

        public bool HideAffectedHosts()
        {
            events.Add("input:hide");
            return HideResult;
        }
    }

    private sealed class FakeLayer(
        DesktopHostTransactionLayerKind kind,
        List<string> events)
        : IDesktopHostTransactionLayer
    {
        public DesktopHostTransactionLayerKind Kind => kind;

        internal bool CaptureResult { get; set; } = true;

        internal bool ThrowOnCapture { get; set; }

        internal bool ApplyResult { get; set; } = true;

        internal bool ThrowOnApply { get; set; }

        internal bool VerifyResult { get; set; } = true;

        internal int? FailVerificationOnCall { get; set; }

        internal bool RestoreResult { get; set; } = true;

        internal bool ThrowOnRestore { get; set; }

        internal bool RestoreVerificationResult { get; set; } = true;

        internal Func<bool>? RestoreVerification { get; set; }

        internal Action? AfterVerify { get; set; }

        internal bool ApplyCalled { get; private set; }

        internal int VerificationCalls { get; private set; }

        internal bool SnapshotDisposed { get; private set; }

        public DesktopHostLayerCapture Capture()
        {
            events.Add($"{Kind}:capture");
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException();
            }

            if (!CaptureResult)
            {
                return DesktopHostLayerCapture.Failed;
            }

            var snapshot = new FakeSnapshot(
                () => SnapshotDisposed = true);
            return new DesktopHostLayerCapture(true, snapshot);
        }

        public bool Apply(long generation)
        {
            events.Add($"{Kind}:apply");
            ApplyCalled = true;
            if (ThrowOnApply)
            {
                throw new InvalidOperationException();
            }

            return ApplyResult;
        }

        public bool Verify(long generation)
        {
            events.Add($"{Kind}:verify");
            VerificationCalls++;
            AfterVerify?.Invoke();
            return VerifyResult
                && VerificationCalls != FailVerificationOnCall;
        }

        public bool Restore(IDesktopHostLayerSnapshot snapshot)
        {
            events.Add($"{Kind}:restore");
            if (ThrowOnRestore)
            {
                throw new InvalidOperationException();
            }

            return RestoreResult;
        }

        public bool VerifyRestored(
            IDesktopHostLayerSnapshot snapshot)
        {
            events.Add($"{Kind}:verify-restored");
            return RestoreVerification?.Invoke()
                ?? RestoreVerificationResult;
        }
    }

    private sealed class FakeSnapshot(Action onDispose)
        : IDesktopHostLayerSnapshot
    {
        public void Dispose() => onDispose();
    }
}
