using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopInteractionSurfaceModeTransactionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EntersExplicitAndConnectsSelectionAndAccessibility()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot entered =
            transaction.TryEnter(Intent(), Evidence(), ["A", "B"], Now);
        ProductDesktopInteractionSurfaceTransactionSnapshot selected =
            transaction.ApplySelection(
                new(ProductDesktopSelectionAction.SelectItem, ItemId: "B"),
                entered.Admission.Lease!,
                ["A", "B"],
                Now.AddMilliseconds(1));

        Assert.True(entered.IsExplicit);
        Assert.Equal(1, entered.TransactionRevision);
        Assert.True(selected.IsExplicit);
        Assert.Equal(["B"], selected.Selection!.SelectedItemIds);
        Assert.Equal("B", selected.Selection.FocusedItemId);
        Assert.True(selected.Accessibility.SelectionPatternAvailable);
        Assert.True(selected.Accessibility.Items[1].HasKeyboardFocus);
        Assert.Equal(2, selected.TransactionRevision);
        Assert.Equal(["capture", "explicit", "capture"], adapter.Events);
    }

    [Theory]
    [InlineData("transparent")]
    [InlineData("focus")]
    [InlineData("selection")]
    [InlineData("tool")]
    [InlineData("noactivate")]
    [InlineData("topmost")]
    [InlineData("owner")]
    [InlineData("foreground")]
    [InlineData("generation")]
    public void RejectsIncompletePassiveContractWithoutAdmission(string drift)
    {
        var adapter = new FakeSurface { State = Drift(Passive(), drift) };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .PassiveContractRequired,
            result.Status);
        Assert.False(result.Admission.HasActiveLease);
        Assert.Equal(["capture"], adapter.Events);
        Assert.Equal(0, result.TransactionRevision);
    }

    [Fact]
    public void AdmissionRejectionDoesNotMutateSurface()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(
                Intent(),
                Evidence() with { LockedContainerIds = Set("container-1") },
                ["A"],
                Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .AdmissionRejected,
            result.Status);
        Assert.Equal(
            ProductDesktopInteractionAdmissionStatus.TargetLocked,
            result.Admission.LastAdmissionStatus);
        Assert.Equal(["capture"], adapter.Events);
    }

    [Fact]
    public void ApplyFailureRollsBackToExactPassiveEvidence()
    {
        var adapter = new FakeSurface { ApplyExplicitResult = false };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .SurfaceApplyFailed,
            result.Status);
        Assert.True(result.Surface!.IsPassiveContract);
        Assert.False(result.Admission.HasActiveLease);
        Assert.Equal(
            ["capture", "explicit", "restore", "capture"],
            adapter.Events);
    }

    [Fact]
    public void ExplicitVerificationFailureRollsBack()
    {
        var adapter = new FakeSurface { ExplicitState = Passive() };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .SurfaceVerificationFailed,
            result.Status);
        Assert.Equal(
            ["capture", "explicit", "capture", "restore", "capture"],
            adapter.Events);
    }

    [Fact]
    public void InvalidVisibleItemsRollBackBeforePublishingUia()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A", "A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .InvalidVisibleItems,
            result.Status);
        Assert.False(result.Accessibility.SelectionPatternAvailable);
        Assert.Null(result.Selection);
    }

    [Fact]
    public void FailedRestoreHidesSurfaceFailClosed()
    {
        var adapter = new FakeSurface
        {
            ApplyExplicitResult = false,
            RestoreResult = false,
        };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.HiddenFailClosed,
            result.Status);
        Assert.True(result.Surface!.IsHiddenContract);
        Assert.Equal(
            ["capture", "explicit", "restore", "hide", "capture"],
            adapter.Events);
    }

    [Fact]
    public void EscapeReturnsExactPassiveContractAndClearsSelection()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        ProductDesktopInteractionSurfaceTransactionSnapshot entered =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);
        _ = transaction.ApplySelection(
            new(ProductDesktopSelectionAction.SelectItem, ItemId: "A"),
            entered.Admission.Lease!,
            ["A"],
            Now.AddMilliseconds(1));

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.Cancel(
                ProductDesktopInteractionCancellationSignal.EscapePressed,
                Now.AddMilliseconds(2));

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.ReturnedPassive,
            result.Status);
        Assert.Equal(
            ProductDesktopInteractionCancellationReason.EscapePressed,
            result.Admission.LastCancellationReason);
        Assert.True(result.Surface!.IsPassiveContract);
        Assert.Null(result.Selection);
        Assert.False(result.Accessibility.SelectionPatternAvailable);
    }

    [Fact]
    public void LiveTimerKeepsExplicitMode()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        _ = transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.Cancel(
                ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed,
                Now.AddSeconds(4),
                Evidence());

        Assert.True(result.IsExplicit);
        Assert.DoesNotContain("passive", adapter.Events);
    }

    [Theory]
    [InlineData(ProductDesktopInteractionCancellationSignal.LeaseTimerElapsed)]
    [InlineData(ProductDesktopInteractionCancellationSignal.EvidenceChanged)]
    public void ExpiryOrGenerationDriftReturnsPassive(
        ProductDesktopInteractionCancellationSignal signal)
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        _ = transaction.TryEnter(Intent(), Evidence(), ["A"], Now);
        ProductDesktopInteractionEvidence evidence = signal
            == ProductDesktopInteractionCancellationSignal.EvidenceChanged
            ? Evidence() with { TopologyGeneration = 10 }
            : Evidence();

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.Cancel(
                signal,
                signal == ProductDesktopInteractionCancellationSignal
                    .LeaseTimerElapsed
                    ? Now.AddSeconds(5)
                    : Now.AddMilliseconds(1),
                evidence);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.ReturnedPassive,
            result.Status);
        Assert.False(result.Admission.HasActiveLease);
    }

    [Fact]
    public void PassiveRestoreFailureDuringCancelHidesSurface()
    {
        var adapter = new FakeSurface { ApplyPassiveResult = false };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        _ = transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.Cancel(
                ProductDesktopInteractionCancellationSignal.ExplorerRestarted,
                Now.AddMilliseconds(1));

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.HiddenFailClosed,
            result.Status);
        Assert.True(result.Surface!.IsHiddenContract);
    }

    [Fact]
    public void EmergencyHideFailureIsReportedWithoutClaimingSafety()
    {
        var adapter = new FakeSurface
        {
            ApplyExplicitResult = false,
            RestoreResult = false,
            HideResult = false,
        };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus
                .EmergencyHideFailed,
            result.Status);
        Assert.False(result.Surface?.IsHiddenContract ?? false);
        Assert.False(result.Admission.HasActiveLease);
    }

    [Fact]
    public void RejectedStaleSelectionDoesNotAdvanceTransactionRevision()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        ProductDesktopInteractionSurfaceTransactionSnapshot entered =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.ApplySelection(
                new(ProductDesktopSelectionAction.SelectItem, ItemId: "A"),
                entered.Admission.Lease! with { TopologyGeneration = 10 },
                ["A"],
                Now.AddMilliseconds(1));

        Assert.Equal(
            ProductDesktopSelectionStatus.LeaseMismatch,
            result.Selection!.Status);
        Assert.Equal(entered.TransactionRevision, result.TransactionRevision);
        Assert.Empty(result.Accessibility.SelectedItemIds);
    }

    [Fact]
    public void CancelWhilePassiveDoesNotTouchSurface()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.Cancel(
                ProductDesktopInteractionCancellationSignal.EscapePressed,
                Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.Passive,
            result.Status);
        Assert.Empty(adapter.Events);
        Assert.Equal(0, result.TransactionRevision);
    }

    [Fact]
    public void CaptureExceptionFailsClosedWithoutMutation()
    {
        var adapter = new FakeSurface { ThrowOnCapture = true };
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);

        ProductDesktopInteractionSurfaceTransactionSnapshot result =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.CaptureFailed,
            result.Status);
        Assert.False(result.Admission.HasActiveLease);
    }

    [Fact]
    public void RepeatedEntryIsIdempotentlyRejected()
    {
        var adapter = new FakeSurface();
        ProductDesktopInteractionSurfaceModeTransaction transaction =
            CreateTransaction(adapter);
        ProductDesktopInteractionSurfaceTransactionSnapshot first =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        ProductDesktopInteractionSurfaceTransactionSnapshot second =
            transaction.TryEnter(Intent(), Evidence(), ["A"], Now);

        Assert.True(first.IsExplicit);
        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.AlreadyExplicit,
            second.Status);
        Assert.Equal(first.TransactionRevision, second.TransactionRevision);
        Assert.True(transaction.Snapshot.IsExplicit);

        ProductDesktopInteractionSurfaceTransactionSnapshot cancelled =
            transaction.Cancel(
                ProductDesktopInteractionCancellationSignal.EscapePressed,
                Now.AddMilliseconds(1));
        Assert.Equal(
            ProductDesktopInteractionSurfaceTransactionStatus.ReturnedPassive,
            cancelled.Status);
        Assert.True(cancelled.Surface!.IsPassiveContract);
    }

    private static ProductDesktopInteractionSurfaceModeTransaction
        CreateTransaction(FakeSurface adapter) =>
        new(
            new(ProductDesktopInteractionFeaturePolicy.Evaluate(
                ProductDesktopHostFeaturePolicy.Evaluate("1"),
                "1")),
            adapter);

    private static ProductDesktopInteractionIntent Intent() =>
        new(
            Guid.Parse("be8ee58e-f43f-4580-a942-eebc31e55cbd"),
            "container-1",
            7,
            9,
            11,
            Now,
            Now.AddSeconds(5));

    private static ProductDesktopInteractionEvidence Evidence() =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            WorkspaceRevision: 7,
            TopologyGeneration: 9,
            WindowRegistryGeneration: 11,
            AvailableContainerIds: Set("container-1"),
            LockedContainerIds: Set());

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static ProductDesktopInteractionSurfaceEvidence Passive() =>
        new(
            ProductDesktopInteractionSurfaceMode.Passive,
            WindowRegistryGeneration: 11,
            Visible: true,
            HitTestTransparent: true,
            IsKeyboardFocusable: false,
            SelectionPatternAvailable: false,
            ToolWindow: true,
            NoActivate: true,
            Topmost: false,
            HasOwner: false,
            OwnsForeground: false);

    private static ProductDesktopInteractionSurfaceEvidence Explicit() =>
        Passive() with
        {
            Mode = ProductDesktopInteractionSurfaceMode.Explicit,
            HitTestTransparent = false,
            IsKeyboardFocusable = true,
            SelectionPatternAvailable = true,
        };

    private static ProductDesktopInteractionSurfaceEvidence Drift(
        ProductDesktopInteractionSurfaceEvidence state,
        string drift) =>
        drift switch
        {
            "transparent" => state with { HitTestTransparent = false },
            "focus" => state with { IsKeyboardFocusable = true },
            "selection" => state with { SelectionPatternAvailable = true },
            "tool" => state with { ToolWindow = false },
            "noactivate" => state with { NoActivate = false },
            "topmost" => state with { Topmost = true },
            "owner" => state with { HasOwner = true },
            "foreground" => state with { OwnsForeground = true },
            "generation" => state with { WindowRegistryGeneration = 12 },
            _ => throw new ArgumentOutOfRangeException(nameof(drift)),
        };

    private sealed class FakeSurface :
        IProductDesktopInteractionSurfaceModeAdapter
    {
        internal List<string> Events { get; } = [];

        internal ProductDesktopInteractionSurfaceEvidence State { get; set; }
            = Passive();

        internal ProductDesktopInteractionSurfaceEvidence ExplicitState
        { get; set; } = Explicit();

        internal bool ApplyExplicitResult { get; set; } = true;

        internal bool ApplyPassiveResult { get; set; } = true;

        internal bool RestoreResult { get; set; } = true;

        internal bool HideResult { get; set; } = true;

        internal bool ThrowOnCapture { get; set; }

        public ProductDesktopInteractionSurfaceCapture Capture()
        {
            Events.Add("capture");
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("controlled capture fault");
            }

            return new(true, State);
        }

        public bool ApplyExplicit(ProductDesktopInteractionLease lease)
        {
            Events.Add("explicit");
            if (ApplyExplicitResult)
            {
                State = ExplicitState;
            }

            return ApplyExplicitResult;
        }

        public bool ApplyPassive(long expectedWindowRegistryGeneration)
        {
            Events.Add("passive");
            if (ApplyPassiveResult)
            {
                State = Passive();
            }

            return ApplyPassiveResult;
        }

        public bool Restore(ProductDesktopInteractionSurfaceEvidence evidence)
        {
            Events.Add("restore");
            if (RestoreResult)
            {
                State = evidence;
            }

            return RestoreResult;
        }

        public bool Hide(long expectedWindowRegistryGeneration)
        {
            Events.Add("hide");
            if (HideResult)
            {
                State = Passive() with
                {
                    Mode = ProductDesktopInteractionSurfaceMode.Hidden,
                    Visible = false,
                };
            }

            return HideResult;
        }
    }
}
