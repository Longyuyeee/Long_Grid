using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceWindowCompositeTransactionTests
{
    private static readonly Guid OperationId =
        Guid.Parse("32d76bd1-f38d-4dd7-b9bc-20c91d065a71");

    [Fact]
    public void PrepareTokenBindsBothConfigurationsAndEveryGeneration()
    {
        Fixture fixture = CreateFixture();

        ProductWorkspaceWindowCompositeToken token = fixture.Token;

        Assert.Equal(OperationId, token.OperationId);
        Assert.Equal(5, token.Before.TopologyGeneration);
        Assert.Equal(7, token.Before.EditRevision);
        Assert.Equal(8, token.After.EditRevision);
        Assert.Equal(9, token.Undo.EditRevision);
        Assert.Equal(11, token.Before.WindowRegistryGeneration);
        Assert.Equal(13, token.Before.DesktopHostGeneration);
        Assert.Equal(64, token.PlanFingerprint.Length);
        Assert.NotEqual(
            token.Before.ConfigurationFingerprint,
            token.After.ConfigurationFingerprint);
        Assert.Equal(
            token.Before.ConfigurationFingerprint,
            token.Undo.ConfigurationFingerprint);
    }

    [Fact]
    public void AppliesWindowsThenConfigurationAndPublishesOneTimeUndo()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.True(result.IsApplied);
        Assert.Equal(ProductWorkspaceWindowCompositeFailure.None, result.Failure);
        Assert.Equal(fixture.Token.After, harness.Configuration.Binding);
        Assert.True(harness.Windows.Applied);
        Assert.Equal(result.UndoToken, harness.Coordinator.CurrentUndoToken);
        Assert.Equal(
            [
                "input:close",
                "configuration:capture",
                "windows:capture",
                "windows:apply",
                "windows:verify",
                "configuration:apply",
                "configuration:verify",
                "windows:verify",
                "configuration:verify",
                "input:reopen",
            ],
            harness.Events);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RejectsMissingConfirmationOrChangedBindingBeforeInput(
        bool confirmed,
        bool bindingChanged)
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        if (bindingChanged)
        {
            harness.Configuration.Binding = fixture.Token.After;
        }

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request with
            {
                UserConfirmed = confirmed,
            });

        Assert.Equal(
            bindingChanged
                ? ProductWorkspaceWindowCompositeStatus.Superseded
                : ProductWorkspaceWindowCompositeStatus.Rejected,
            result.Status);
        Assert.Empty(harness.Events);
    }

    [Fact]
    public void RejectsMutatedPlanOrContainerSetAsInvalidRequest()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeRequest changed = fixture.Request with
        {
            RegisteredContainerIds = ["other"],
        };

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(changed);

        Assert.Equal(ProductWorkspaceWindowCompositeStatus.Rejected, result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InvalidRequest,
            result.Failure);
        Assert.Empty(harness.Events);
    }

    [Fact]
    public void RejectsMalformedNestedRequestWithoutClosingInput()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request with { Token = null! });

        Assert.Equal(ProductWorkspaceWindowCompositeStatus.Rejected, result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InvalidRequest,
            result.Failure);
        Assert.Equal(Guid.Empty, result.OperationId);
        Assert.Empty(harness.Events);
    }

    [Fact]
    public void WindowFailureRestoresOnlyTouchedWindowState()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.ApplyResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RolledBack,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowApplyFailed,
            result.Failure);
        Assert.False(harness.Windows.Applied);
        Assert.Equal(fixture.Token.Before, harness.Configuration.Binding);
        Assert.DoesNotContain("configuration:restore", harness.Events);
        Assert.Contains("windows:restore", harness.Events);
    }

    [Fact]
    public void ConfigurationFailureRestoresBothSidesInReverseOrder()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Configuration.ApplyResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RolledBack,
            result.Status);
        Assert.Equal(fixture.Token.Before, harness.Configuration.Binding);
        Assert.False(harness.Windows.Applied);
        int configurationRestore = harness.Events.IndexOf("configuration:restore");
        int windowRestore = harness.Events.IndexOf("windows:restore");
        Assert.True(configurationRestore >= 0);
        Assert.True(windowRestore > configurationRestore);
    }

    [Fact]
    public void CrossLayerFinalVerificationDriftRollsBackBothSides()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.FailVerificationOnCall = 2;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RolledBack,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.FinalVerificationFailed,
            result.Failure);
        Assert.Equal(2, harness.Windows.VerificationCalls);
        Assert.Equal(fixture.Token.Before, harness.Configuration.Binding);
    }

    [Fact]
    public void RollbackFailureHidesHostsAndKeepsInputClosed()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Configuration.ApplyResult = false;
        harness.Windows.RestoreResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowRestoreFailed,
            result.Failure);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.Equal("input:hide", harness.Events[^1]);
    }

    [Fact]
    public void CaptureFailureDoesNotApplyEitherLayer()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.CaptureResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowCaptureFailed,
            result.Failure);
        Assert.DoesNotContain("windows:apply", harness.Events);
        Assert.DoesNotContain("configuration:apply", harness.Events);
        Assert.Equal("input:reopen", harness.Events[^1]);
    }

    [Fact]
    public void ConfigurationCaptureFailureReopensInputWithoutWindowCapture()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Configuration.CaptureResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.ConfigurationCaptureFailed,
            result.Failure);
        Assert.Equal(
            ["input:close", "configuration:capture", "input:reopen"],
            harness.Events);
    }

    [Fact]
    public void WindowVerificationFailureUsesVerifiedRollback()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.FailVerificationOnCall = 1;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RolledBack,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowVerificationFailed,
            result.Failure);
        Assert.False(harness.Windows.Applied);
        Assert.Equal(fixture.Token.Before, harness.Configuration.Binding);
    }

    [Fact]
    public void InputCloseFailureStopsBeforeCapture()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Input.CloseResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.InputGateFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InputCloseFailed,
            result.Failure);
        Assert.Equal(["input:close"], harness.Events);
    }

    [Fact]
    public void BindingChangeAfterWindowVerificationRollsWindowBack()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.AfterVerify = () =>
            harness.Configuration.Binding = fixture.Token.After;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.BindingChanged,
            result.Failure);
        Assert.True(result.HostsHidden);
        Assert.False(harness.Windows.Applied);
    }

    [Fact]
    public void BindingChangeAfterCaptureHidesHostsWhenInputCannotReopen()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Windows.AfterCapture = () =>
            harness.Configuration.Binding = fixture.Token.After;
        harness.Input.ReopenResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InputReopenFailed,
            result.Failure);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.DoesNotContain("windows:apply", harness.Events);
        Assert.Equal("input:hide", harness.Events[^1]);
    }

    [Fact]
    public void InputReopenFailureRestoresBothSidesAndHidesHosts()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        harness.Input.ReopenResult = false;

        ProductWorkspaceWindowCompositeResult result =
            harness.Coordinator.Execute(fixture.Request);

        Assert.Equal(
            ProductWorkspaceWindowCompositeStatus.RollbackFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InputReopenFailed,
            result.Failure);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.Equal(fixture.Token.Before, harness.Configuration.Binding);
        Assert.False(harness.Windows.Applied);
    }

    [Fact]
    public void ConcurrentExecutionSerializesAndOnlyFirstBindingCanApply()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);

        ProductWorkspaceWindowCompositeResult[] results =
            new ProductWorkspaceWindowCompositeResult[2];
        Parallel.For(0, 2, index =>
            results[index] = harness.Coordinator.Execute(fixture.Request));

        Assert.Single(results, result => result.IsApplied);
        Assert.Single(
            results,
            result => result.Status ==
                ProductWorkspaceWindowCompositeStatus.Superseded);
    }

    [Fact]
    public void UndoRestoresBothOriginalSnapshotsAndConsumesTokenOnce()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Events.Clear();

        ProductWorkspaceWindowCompositeUndoResult undone =
            harness.Coordinator.Undo(token, userConfirmed: true);
        ProductWorkspaceWindowCompositeUndoResult repeated =
            harness.Coordinator.Undo(token, userConfirmed: true);

        Assert.True(undone.IsUndone);
        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.Unavailable,
            repeated.Status);
        Assert.Equal(fixture.Token.Undo, harness.Configuration.Binding);
        Assert.False(harness.Windows.Applied);
        Assert.Null(harness.Coordinator.CurrentUndoToken);
        Assert.Contains("configuration:restore", harness.Events);
        Assert.Contains("windows:restore", harness.Events);
    }

    [Fact]
    public void UndoRequiresExactTokenConfirmationAndAppliedBinding()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.TokenMismatch,
            harness.Coordinator.Undo(
                token with { OperationId = Guid.NewGuid() },
                true).Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.ConfirmationRequired,
            harness.Coordinator.Undo(token, false).Status);
        harness.Configuration.Binding = token.Undo;
        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.Superseded,
            harness.Coordinator.Undo(token, true).Status);
        Assert.Equal(token, harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void UndoInputCloseFailureLeavesAppliedStateAndTokenUntouched()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Input.CloseResult = false;

        ProductWorkspaceWindowCompositeUndoResult result =
            harness.Coordinator.Undo(token, true);

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.InputGateFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InputCloseFailed,
            result.Failure);
        Assert.Equal(token.Applied, harness.Configuration.Binding);
        Assert.Equal(token, harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void UndoWindowCaptureFailureReopensInputAndKeepsToken()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Windows.CaptureResult = false;

        ProductWorkspaceWindowCompositeUndoResult result =
            harness.Coordinator.Undo(token, true);

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.CaptureFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.WindowCaptureFailed,
            result.Failure);
        Assert.False(result.InputClosed);
        Assert.Equal(token, harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void UndoInputReopenFailureHidesRestoredHostsAndConsumesToken()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Input.ReopenResult = false;

        ProductWorkspaceWindowCompositeUndoResult result =
            harness.Coordinator.Undo(token, true);

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.RecoveryFailed,
            result.Status);
        Assert.Equal(
            ProductWorkspaceWindowCompositeFailure.InputReopenFailed,
            result.Failure);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.Equal(fixture.Token.Undo, harness.Configuration.Binding);
        Assert.Null(harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void FailedUndoRollsForwardAndRemainsRetryable()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Configuration.FailRestoreVerificationOnCall = 1;

        ProductWorkspaceWindowCompositeUndoResult result =
            harness.Coordinator.Undo(token, true);

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.RolledForward,
            result.Status);
        Assert.Equal(token.Applied, harness.Configuration.Binding);
        Assert.True(harness.Windows.Applied);
        Assert.Equal(token, harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void FailedUndoRecoveryHidesHostsAndInvalidatesUnsafeToken()
    {
        Fixture fixture = CreateFixture();
        using Harness harness = Harness.Create(fixture);
        ProductWorkspaceWindowCompositeUndoToken token =
            harness.Coordinator.Execute(fixture.Request).UndoToken!;
        harness.Configuration.FailRestoreVerificationOnCall = 1;
        harness.Windows.RestoreResult = false;

        ProductWorkspaceWindowCompositeUndoResult result =
            harness.Coordinator.Undo(token, true);

        Assert.Equal(
            ProductWorkspaceWindowCompositeUndoStatus.RecoveryFailed,
            result.Status);
        Assert.True(result.InputClosed);
        Assert.True(result.HostsHidden);
        Assert.Null(harness.Coordinator.CurrentUndoToken);
    }

    [Fact]
    public void TokenPreparationRejectsUnsafeEvidence()
    {
        Fixture fixture = CreateFixture();

        Assert.Null(ProductWorkspaceWindowCompositeTransactionCoordinator.PrepareToken(
            fixture.Before,
            fixture.Recovered,
            fixture.Plan,
            ["container-1"],
            windowOwnershipAttested: false,
            5,
            7,
            11,
            fixture.Token.Before.DesktopHostInstanceId,
            13,
            reviewApproved: true,
            OperationId));
        Assert.Null(ProductWorkspaceWindowCompositeTransactionCoordinator.PrepareToken(
            fixture.Before,
            fixture.Before,
            fixture.Plan,
            ["container-1"],
            true,
            5,
            7,
            11,
            fixture.Token.Before.DesktopHostInstanceId,
            13,
            true,
            OperationId));
        Assert.Null(ProductWorkspaceWindowCompositeTransactionCoordinator.PrepareToken(
            fixture.Before,
            fixture.Recovered,
            fixture.Plan,
            ["container-1"],
            true,
            5,
            long.MaxValue,
            11,
            fixture.Token.Before.DesktopHostInstanceId,
            13,
            true,
            OperationId));
    }

    private static Fixture CreateFixture()
    {
        ProductWorkspaceState before = new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Work",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-saved",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                },
            ],
        };
        ProductWorkspaceState recovered = before with
        {
            Containers =
            [
                before.Containers[0] with
                {
                    Placement = before.Containers[0].Placement with
                    {
                        DisplayKey = "display-current",
                        XDip = 40,
                    },
                },
            ],
        };
        LayoutRecoveryPlan plan = new(
            LayoutRecoveryStatus.ReviewRequired,
            [
                new DisplayRecoveryMapping(
                    "display-saved",
                    "display-current",
                    DisplayMatchKind.SimilarGeometry),
            ],
            Array.Empty<string>(),
            [
                new ContainerRecoveryPlacement(
                    "container-1",
                    "display-saved",
                    "display-current",
                    new(32, 48, 360, 240),
                    new(40, 48, 360, 240),
                    WasVisibilityCorrected: false),
            ]);
        Guid host = Guid.Parse("ec9a9080-f56d-41c6-b625-1ad4b5440b10");
        ProductWorkspaceWindowCompositeToken token =
            ProductWorkspaceWindowCompositeTransactionCoordinator.PrepareToken(
                before,
                recovered,
                plan,
                ["container-1"],
                true,
                5,
                7,
                11,
                host,
                13,
                true,
                OperationId)!;
        return new(
            before,
            recovered,
            plan,
            token,
            new(
                before,
                recovered,
                plan,
                ["container-1"],
                true,
                token,
                UserConfirmed: true));
    }

    private sealed record Fixture(
        ProductWorkspaceState Before,
        ProductWorkspaceState Recovered,
        LayoutRecoveryPlan Plan,
        ProductWorkspaceWindowCompositeToken Token,
        ProductWorkspaceWindowCompositeRequest Request);

    private sealed class Harness : IDisposable
    {
        private Harness(Fixture fixture)
        {
            Events = new();
            Configuration = new(fixture.Before, fixture.Token.Before, Events);
            Windows = new(Events);
            Input = new(Events);
            Coordinator = new(
                () => Configuration.Binding,
                Configuration,
                Windows,
                Input);
        }

        internal List<string> Events { get; }

        internal FakeConfigurationLayer Configuration { get; }

        internal FakeWindowLayer Windows { get; }

        internal FakeInputGate Input { get; }

        internal ProductWorkspaceWindowCompositeTransactionCoordinator Coordinator
        {
            get;
        }

        internal static Harness Create(Fixture fixture) => new(fixture);

        public void Dispose() => Coordinator.Dispose();
    }

    private sealed record Snapshot(
        string Kind,
        ProductWorkspaceState? State,
        ProductWorkspaceWindowCompositeBinding? Binding,
        bool WindowApplied) : IProductWorkspaceWindowCompositeSnapshot
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeConfigurationLayer(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding binding,
        List<string> events)
        : IProductWorkspaceCompositeConfigurationLayer
    {
        internal ProductWorkspaceState State { get; private set; } = state;

        internal ProductWorkspaceWindowCompositeBinding Binding { get; set; } =
            binding;

        internal bool ApplyResult { get; set; } = true;

        internal bool CaptureResult { get; set; } = true;

        internal bool VerificationResult { get; set; } = true;

        internal bool RestoreResult { get; set; } = true;

        internal bool RestoreVerificationResult { get; set; } = true;

        internal int? FailRestoreVerificationOnCall { get; set; }

        private int restoreVerificationCalls;

        public ProductWorkspaceWindowCompositeCapture Capture()
        {
            events.Add("configuration:capture");
            return CaptureResult
                ? new(true, new Snapshot(
                    "configuration",
                    State,
                    Binding,
                    false))
                : ProductWorkspaceWindowCompositeCapture.Failed;
        }

        public bool Apply(
            ProductWorkspaceState next,
            ProductWorkspaceWindowCompositeBinding expectedBinding)
        {
            events.Add("configuration:apply");
            State = next;
            Binding = expectedBinding;
            return ApplyResult;
        }

        public bool Verify(
            ProductWorkspaceState expected,
            ProductWorkspaceWindowCompositeBinding expectedBinding)
        {
            events.Add("configuration:verify");
            return VerificationResult
                && State == expected
                && Binding == expectedBinding;
        }

        public bool Restore(
            IProductWorkspaceWindowCompositeSnapshot snapshot,
            ProductWorkspaceWindowCompositeBinding expectedBinding)
        {
            events.Add("configuration:restore");
            if (snapshot is Snapshot captured && captured.State is not null)
            {
                State = captured.State;
                Binding = expectedBinding;
            }

            return RestoreResult;
        }

        public bool VerifyRestored(
            IProductWorkspaceWindowCompositeSnapshot snapshot,
            ProductWorkspaceWindowCompositeBinding expectedBinding)
        {
            events.Add("configuration:verify-restored");
            restoreVerificationCalls++;
            return RestoreVerificationResult
                && restoreVerificationCalls != FailRestoreVerificationOnCall
                && snapshot is Snapshot captured
                && captured.State == State
                && Binding == expectedBinding;
        }
    }

    private sealed class FakeWindowLayer(List<string> events)
        : IProductWorkspaceCompositeWindowLayer
    {
        internal bool Applied { get; private set; }

        internal bool CaptureResult { get; set; } = true;

        internal bool ApplyResult { get; set; } = true;

        internal bool RestoreResult { get; set; } = true;

        internal bool RestoreVerificationResult { get; set; } = true;

        internal int? FailVerificationOnCall { get; set; }

        internal int VerificationCalls { get; private set; }

        internal Action? AfterVerify { get; set; }

        internal Action? AfterCapture { get; set; }

        public ProductWorkspaceWindowCompositeCapture Capture(
            IReadOnlyList<string> containerIds,
            long registryGeneration)
        {
            events.Add("windows:capture");
            AfterCapture?.Invoke();
            return CaptureResult
                ? new(true, new Snapshot("windows", null, null, Applied))
                : ProductWorkspaceWindowCompositeCapture.Failed;
        }

        public bool Apply(
            IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
            long registryGeneration)
        {
            events.Add("windows:apply");
            Applied = true;
            return ApplyResult;
        }

        public bool Verify(
            IReadOnlyList<LayoutRecoveryWindowPlacement> placements,
            long registryGeneration)
        {
            events.Add("windows:verify");
            VerificationCalls++;
            AfterVerify?.Invoke();
            return Applied && VerificationCalls != FailVerificationOnCall;
        }

        public bool Restore(
            IProductWorkspaceWindowCompositeSnapshot snapshot,
            long registryGeneration)
        {
            events.Add("windows:restore");
            if (snapshot is Snapshot captured)
            {
                Applied = captured.WindowApplied;
            }

            return RestoreResult;
        }

        public bool VerifyRestored(
            IProductWorkspaceWindowCompositeSnapshot snapshot,
            long registryGeneration)
        {
            events.Add("windows:verify-restored");
            return RestoreVerificationResult
                && snapshot is Snapshot captured
                && Applied == captured.WindowApplied;
        }
    }

    private sealed class FakeInputGate(List<string> events)
        : IProductWorkspaceCompositeInputGate
    {
        internal bool CloseResult { get; set; } = true;

        internal bool ReopenResult { get; set; } = true;

        internal bool HideResult { get; set; } = true;

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
}
