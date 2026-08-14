using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSaveStateMachineTests
{
    [Fact]
    public void EditSchedulesDebounceAndClearsPreviousFailure()
    {
        ProductWorkspaceSaveSnapshot failed =
            ProductWorkspaceSaveSnapshot.Initial with
            {
                Status = ProductWorkspaceSaveStatus.Failed,
                Failure = ProductWorkspaceSaveFailure.IoFailure,
                CanRetry = true,
            };

        ProductWorkspaceSaveTransition transition =
            ProductWorkspaceSaveStateMachine.AcceptEdit(failed);

        Assert.Equal(ProductWorkspaceSaveStatus.WaitingForDebounce, transition.Snapshot.Status);
        Assert.Equal(1, transition.Snapshot.CurrentRevision);
        Assert.Equal(ProductWorkspaceSaveFailure.None, transition.Snapshot.Failure);
        Assert.False(transition.Snapshot.CanRetry);
        Assert.Equal(ProductWorkspaceSaveCommandKind.ScheduleDebounce, transition.Command.Kind);
        Assert.Equal(1, transition.Command.Revision);
    }

    [Fact]
    public void OnlyLatestDebounceCanStartASave()
    {
        ProductWorkspaceSaveTransition first =
            ProductWorkspaceSaveStateMachine.AcceptEdit(
                ProductWorkspaceSaveSnapshot.Initial);
        ProductWorkspaceSaveTransition second =
            ProductWorkspaceSaveStateMachine.AcceptEdit(first.Snapshot);

        ProductWorkspaceSaveTransition stale =
            ProductWorkspaceSaveStateMachine.DebounceElapsed(second.Snapshot, 1);
        ProductWorkspaceSaveTransition latest =
            ProductWorkspaceSaveStateMachine.DebounceElapsed(stale.Snapshot, 2);

        Assert.Equal(ProductWorkspaceSaveCommandKind.None, stale.Command.Kind);
        Assert.Equal(ProductWorkspaceSaveStatus.WaitingForDebounce, stale.Snapshot.Status);
        Assert.Equal(ProductWorkspaceSaveCommandKind.Save, latest.Command.Kind);
        Assert.Equal(ProductWorkspaceSaveStatus.Saving, latest.Snapshot.Status);
        Assert.Equal(2, latest.Snapshot.ActiveSaveRevision);
        Assert.Equal(ProductWorkspaceSaveActivity.Save, latest.Snapshot.Activity);
    }

    [Fact]
    public void OnlyCurrentActiveSaveRevisionCanReachWorkflow()
    {
        ProductWorkspaceSaveSnapshot firstSave = StartSave();
        ProductWorkspaceSaveTransition newerEdit =
            ProductWorkspaceSaveStateMachine.AcceptEdit(firstSave);
        ProductWorkspaceSaveTransition latestSave =
            ProductWorkspaceSaveStateMachine.DebounceElapsed(
                newerEdit.Snapshot,
                2);

        Assert.False(
            ProductWorkspaceSaveStateMachine.CanSubmitSave(
                newerEdit.Snapshot,
                1));
        Assert.False(
            ProductWorkspaceSaveStateMachine.CanSubmitSave(
                latestSave.Snapshot,
                1));
        Assert.True(
            ProductWorkspaceSaveStateMachine.CanSubmitSave(
                latestSave.Snapshot,
                2));
    }

    [Fact]
    public void RetryActivityCannotUseOrdinarySaveAdmission()
    {
        ProductWorkspaceSaveTransition failed =
            ProductWorkspaceSaveStateMachine.SaveCompleted(
                StartSave(),
                1,
                ProductWorkspaceSaveFailure.IoFailure);
        ProductWorkspaceSaveSnapshot retrying =
            ProductWorkspaceSaveStateMachine.RetryRequested(
                failed.Snapshot).Snapshot;

        Assert.False(
            ProductWorkspaceSaveStateMachine.CanSubmitSave(retrying, 1));
    }

    [Fact]
    public void SuccessfulLatestSaveBecomesSaved()
    {
        ProductWorkspaceSaveSnapshot saving = StartSave();

        ProductWorkspaceSaveTransition completed =
            ProductWorkspaceSaveStateMachine.SaveCompleted(saving, 1);

        Assert.Equal(ProductWorkspaceSaveStatus.Saved, completed.Snapshot.Status);
        Assert.Equal(1, completed.Snapshot.SavedRevision);
        Assert.Null(completed.Snapshot.ActiveSaveRevision);
        Assert.Equal(ProductWorkspaceSaveActivity.None, completed.Snapshot.Activity);
        Assert.Equal(ProductWorkspaceSaveCommandKind.None, completed.Command.Kind);
    }

    [Fact]
    public void RetryableFailureProducesExplicitRetryCommand()
    {
        ProductWorkspaceSaveTransition failed =
            ProductWorkspaceSaveStateMachine.SaveCompleted(
                StartSave(),
                1,
                ProductWorkspaceSaveFailure.WriteLeaseUnavailable);

        ProductWorkspaceSaveTransition retry =
            ProductWorkspaceSaveStateMachine.RetryRequested(failed.Snapshot);

        Assert.Equal(ProductWorkspaceSaveStatus.Failed, failed.Snapshot.Status);
        Assert.Equal(ProductWorkspaceSaveActivity.None, failed.Snapshot.Activity);
        Assert.True(failed.Snapshot.CanRetry);
        Assert.Equal(ProductWorkspaceSaveCommandKind.Retry, retry.Command.Kind);
        Assert.Equal(1, retry.Command.Revision);
        Assert.Equal(ProductWorkspaceSaveStatus.Saving, retry.Snapshot.Status);
        Assert.Equal(ProductWorkspaceSaveActivity.Retry, retry.Snapshot.Activity);
        Assert.False(retry.Snapshot.CanRetry);
    }

    [Fact]
    public void NonRetryableFailureCannotIssueRetry()
    {
        ProductWorkspaceSaveTransition failed =
            ProductWorkspaceSaveStateMachine.SaveCompleted(
                StartSave(),
                1,
                ProductWorkspaceSaveFailure.InvalidConfiguration);

        ProductWorkspaceSaveTransition retry =
            ProductWorkspaceSaveStateMachine.RetryRequested(failed.Snapshot);

        Assert.Equal(ProductWorkspaceSaveCommandKind.None, retry.Command.Kind);
        Assert.Equal(failed.Snapshot, retry.Snapshot);
    }

    [Fact]
    public void ExternalBaselineReplacementClearsOnlyFiniteFailure()
    {
        ProductWorkspaceSaveSnapshot failed = ProductWorkspaceSaveStateMachine
            .SaveCompleted(
                ProductWorkspaceSaveStateMachine.DebounceElapsed(
                    ProductWorkspaceSaveStateMachine.AcceptEdit(
                        ProductWorkspaceSaveSnapshot.Initial).Snapshot,
                    revision: 1).Snapshot,
                revision: 1,
                ProductWorkspaceSaveFailure.IoFailure).Snapshot;

        ProductWorkspaceSaveTransition reset =
            ProductWorkspaceSaveStateMachine.ExternalBaselineReplaced(failed);
        ProductWorkspaceSaveTransition unchanged =
            ProductWorkspaceSaveStateMachine.ExternalBaselineReplaced(
                ProductWorkspaceSaveSnapshot.Initial);

        Assert.Equal(ProductWorkspaceSaveStatus.Clean, reset.Snapshot.Status);
        Assert.Equal(1, reset.Snapshot.SavedRevision);
        Assert.False(reset.Snapshot.CanRetry);
        Assert.Equal(ProductWorkspaceSaveFailure.None, reset.Snapshot.Failure);
        Assert.Same(ProductWorkspaceSaveSnapshot.Initial, unchanged.Snapshot);
    }

    [Theory]
    [InlineData(ProductWorkspaceSaveFailure.DamagedEvidence, true)]
    [InlineData(ProductWorkspaceSaveFailure.WriteLeaseUnavailable, true)]
    [InlineData(ProductWorkspaceSaveFailure.IoFailure, true)]
    [InlineData(ProductWorkspaceSaveFailure.InvalidConfiguration, false)]
    public void RetryAvailabilityIsDerivedFromFiniteFailure(
        ProductWorkspaceSaveFailure failure,
        bool expected)
    {
        ProductWorkspaceSaveTransition result =
            ProductWorkspaceSaveStateMachine.SaveCompleted(
                StartSave(),
                1,
                failure);

        Assert.Equal(expected, result.Snapshot.CanRetry);
    }

    [Fact]
    public void CompletionFromOlderSaveCannotOverwriteNewerEdit()
    {
        ProductWorkspaceSaveSnapshot firstSave = StartSave();
        ProductWorkspaceSaveTransition newerEdit =
            ProductWorkspaceSaveStateMachine.AcceptEdit(firstSave);

        ProductWorkspaceSaveTransition staleCompletion =
            ProductWorkspaceSaveStateMachine.SaveCompleted(
                newerEdit.Snapshot,
                1,
                ProductWorkspaceSaveFailure.IoFailure);

        Assert.Equal(
            ProductWorkspaceSaveStatus.WaitingForDebounce,
            staleCompletion.Snapshot.Status);
        Assert.Equal(2, staleCompletion.Snapshot.CurrentRevision);
        Assert.Equal(ProductWorkspaceSaveFailure.None, staleCompletion.Snapshot.Failure);
        Assert.False(staleCompletion.Snapshot.CanRetry);
        Assert.Null(staleCompletion.Snapshot.ActiveSaveRevision);
    }

    [Fact]
    public void CompletionForUnknownRevisionIsIgnored()
    {
        ProductWorkspaceSaveSnapshot saving = StartSave();

        ProductWorkspaceSaveTransition result =
            ProductWorkspaceSaveStateMachine.SaveCompleted(saving, 99);

        Assert.Equal(saving, result.Snapshot);
        Assert.Equal(ProductWorkspaceSaveCommandKind.None, result.Command.Kind);
    }

    [Fact]
    public void RetryRequestOutsideRetryableFailureIsIgnored()
    {
        ProductWorkspaceSaveTransition result =
            ProductWorkspaceSaveStateMachine.RetryRequested(
                ProductWorkspaceSaveSnapshot.Initial);

        Assert.Equal(ProductWorkspaceSaveSnapshot.Initial, result.Snapshot);
        Assert.Equal(ProductWorkspaceSaveCommandKind.None, result.Command.Kind);
    }

    private static ProductWorkspaceSaveSnapshot StartSave()
    {
        ProductWorkspaceSaveTransition edit =
            ProductWorkspaceSaveStateMachine.AcceptEdit(
                ProductWorkspaceSaveSnapshot.Initial);
        return ProductWorkspaceSaveStateMachine.DebounceElapsed(
            edit.Snapshot,
            edit.Command.Revision).Snapshot;
    }
}
