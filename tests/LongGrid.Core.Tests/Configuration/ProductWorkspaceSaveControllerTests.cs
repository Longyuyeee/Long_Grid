using System.Collections.Concurrent;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSaveControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void DebounceDelayMustStayInsideBoundedRange(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProductWorkspaceSaveController(
                new FakeWorkflow(),
                new ManualScheduler(),
                TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void RejectedAndUnchangedEditsNeverScheduleOrDirty()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        ProductWorkspaceEditResult rejected =
            ProductWorkspaceReducer.RenameContainer(
                CreateState(),
                "absent",
                "Name");
        ProductWorkspaceEditResult unchanged =
            ProductWorkspaceReducer.RenameContainer(
                CreateState(),
                "container-1",
                "Current project");

        ProductWorkspaceSaveSubmissionResult rejectedResult =
            controller.Submit(rejected);
        ProductWorkspaceSaveSubmissionResult unchangedResult =
            controller.Submit(unchanged);

        Assert.Equal(
            ProductWorkspaceSaveSubmissionStatus.RejectedEdit,
            rejectedResult.Status);
        Assert.Equal(
            ProductWorkspaceEditError.ContainerNotFound,
            rejectedResult.EditError);
        Assert.Equal(
            ProductWorkspaceSaveSubmissionStatus.NoChange,
            unchangedResult.Status);
        Assert.Equal(ProductWorkspaceSaveStatus.Clean, controller.Snapshot.Status);
        Assert.Equal(0, scheduler.Count);
        Assert.Empty(workflow.SavedDocuments);
    }

    [Fact]
    public void EditMutatedAfterReductionIsRejectedBeforeScheduling()
    {
        var scheduler = new ManualScheduler();
        var controller = new ProductWorkspaceSaveController(
            new FakeWorkflow(),
            scheduler);
        ProductWorkspaceEditResult edit = CreateEdit("Valid") with
        {
            State = CreateEdit("Valid").State! with { Containers = null! },
        };

        ProductWorkspaceSaveSubmissionResult result = controller.Submit(edit);

        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.InvalidState, result.Status);
        Assert.Equal(ProductWorkspaceProjectionError.InvalidState, result.ProjectionError);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task AcceptedEditIsCapturedBeforeDebounce()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        ProductWorkspaceEditResult edit = CreateEdit("Captured", extensionValue: 1);

        ProductWorkspaceSaveSubmissionResult submission = controller.Submit(edit);
        edit.State!.ExtensionData!["future"] =
            JsonSerializer.SerializeToElement(2);
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);

        Assert.True(submission.IsAccepted);
        Assert.Single(workflow.SavedDocuments);
        Assert.Equal(
            1,
            workflow.SavedDocuments.Single().ExtensionData!["future"].GetInt32());
    }

    [Fact]
    public async Task NewEditCancelsOlderDebounceAndOnlyLatestRevisionSaves()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        ProductWorkspaceEditResult first = CreateEdit("First");
        ProductWorkspaceEditResult second =
            ProductWorkspaceReducer.RenameContainer(
                first.State!,
                "container-1",
                "Second");

        controller.Submit(first);
        await scheduler.WaitForCountAsync(1);
        controller.Submit(second);
        await scheduler.WaitForCountAsync(2);
        scheduler.Release(1);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);

        Assert.True(scheduler.IsCanceled(0));
        Assert.Single(workflow.SavedDocuments);
        Assert.Equal(
            "Second",
            workflow.SavedDocuments.Single().Containers[0].Name);
        Assert.Equal(2, controller.Snapshot.SavedRevision);
    }

    [Fact]
    public async Task OlderSaveCompletionCannotOverwriteNewerWaitingEdit()
    {
        var scheduler = new ManualScheduler();
        var firstSave = new TaskCompletionSource<ProductConfigurationSaveAttemptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, call) => call == 1
                ? firstSave.Task
                : Task.FromResult(Saved()),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        ProductWorkspaceEditResult first = CreateEdit("First");
        ProductWorkspaceEditResult second =
            ProductWorkspaceReducer.RenameContainer(
                first.State!,
                "container-1",
                "Second");

        controller.Submit(first);
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saving);
        await workflow.WaitForSaveCallCountAsync(1);
        controller.Submit(second);
        await scheduler.WaitForCountAsync(2);
        firstSave.SetResult(Failed(ProductConfigurationSaveError.IoFailure));
        await WaitForStatusAsync(
            controller,
            ProductWorkspaceSaveStatus.WaitingForDebounce);

        Assert.Equal(ProductWorkspaceSaveFailure.None, controller.Snapshot.Failure);
        Assert.False(controller.Snapshot.CanRetry);
        scheduler.Release(1);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);
        Assert.Equal(2, controller.Snapshot.SavedRevision);
    }

    [Fact]
    public async Task StaleRevisionResumingAfterLatestSaveNeverReachesWorkflow()
    {
        var scheduler = new ManualScheduler(manualSaveYields: true);
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        ProductWorkspaceEditResult first = CreateEdit("First");
        ProductWorkspaceEditResult second =
            ProductWorkspaceReducer.RenameContainer(
                first.State!,
                "container-1",
                "Second");

        controller.Submit(first);
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await scheduler.WaitForSaveYieldCountAsync(1);

        controller.Submit(second);
        await scheduler.WaitForCountAsync(2);
        scheduler.Release(1);
        await scheduler.WaitForSaveYieldCountAsync(2);

        scheduler.ReleaseSaveYield(1);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);
        scheduler.ReleaseSaveYield(0);
        await controller.CompleteAsync();

        ProductConfigurationDocument saved = Assert.Single(workflow.SavedDocuments);
        Assert.Equal("Second", saved.Containers[0].Name);
        Assert.Equal(2, controller.Snapshot.SavedRevision);
    }

    [Theory]
    [InlineData(
        ProductConfigurationSaveError.InvalidConfiguration,
        ProductWorkspaceSaveFailure.InvalidConfiguration,
        false)]
    [InlineData(
        ProductConfigurationSaveError.DamagedEvidence,
        ProductWorkspaceSaveFailure.DamagedEvidence,
        true)]
    [InlineData(
        ProductConfigurationSaveError.WriteLeaseUnavailable,
        ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
        true)]
    [InlineData(
        ProductConfigurationSaveError.IoFailure,
        ProductWorkspaceSaveFailure.IoFailure,
        true)]
    public async Task WorkflowFailuresMapToFiniteProductState(
        ProductConfigurationSaveError error,
        ProductWorkspaceSaveFailure expectedFailure,
        bool canRetry)
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(Failed(error)),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Failure"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);

        Assert.Equal(expectedFailure, controller.Snapshot.Failure);
        Assert.Equal(canRetry, controller.Snapshot.CanRetry);
    }

    [Fact]
    public async Task ExplicitRetryUsesWorkflowSnapshotAndCanRecover()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(
                Failed(ProductConfigurationSaveError.DamagedEvidence)),
            RetryHandler = () => Task.FromResult(Saved()),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Retry"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);
        ProductWorkspaceSaveRetryResult retry = controller.Retry();
        Assert.Equal(ProductWorkspaceSaveActivity.Retry, retry.Snapshot.Activity);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);

        Assert.Equal(ProductWorkspaceSaveRetryStatus.Accepted, retry.Status);
        Assert.Equal(1, workflow.RetryCalls);
        Assert.Equal(1, controller.Snapshot.SavedRevision);
    }

    [Fact]
    public async Task ExternalBaselineDiscardsFailedRetryBeforeRecoveryCanContinue()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(
                Failed(ProductConfigurationSaveError.IoFailure)),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Stale before recovery"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);

        bool discarded = controller.DiscardFailedRetryForExternalBaseline();
        ProductWorkspaceSaveRetryResult retry = controller.Retry();

        Assert.True(discarded);
        Assert.Equal(1, workflow.DiscardRetryCalls);
        Assert.Equal(ProductWorkspaceSaveStatus.Clean, controller.Snapshot.Status);
        Assert.False(controller.Snapshot.CanRetry);
        Assert.Equal(ProductWorkspaceSaveRetryStatus.NotAvailable, retry.Status);
        Assert.Equal(0, workflow.RetryCalls);
    }

    [Fact]
    public void ExternalBaselineDoesNotInterruptPendingSave()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        controller.Submit(CreateEdit("Still pending"));

        bool discarded = controller.DiscardFailedRetryForExternalBaseline();

        Assert.False(discarded);
        Assert.Equal(0, workflow.DiscardRetryCalls);
        Assert.Equal(
            ProductWorkspaceSaveStatus.WaitingForDebounce,
            controller.Snapshot.Status);
    }

    [Fact]
    public async Task MissingWorkflowRetryBecomesNonRetryableFiniteFailure()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(
                Failed(ProductConfigurationSaveError.IoFailure)),
            RetryHandler = () => Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                    null,
                    CanRetry: false)),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Retry unavailable"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);
        controller.Retry();
        await WaitForFailureAsync(
            controller,
            ProductWorkspaceSaveFailure.RetryUnavailable);

        Assert.False(controller.Snapshot.CanRetry);
    }

    [Fact]
    public async Task CloseForcesLatestPendingEditWithoutWaitingForDebounce()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Close"));
        await scheduler.WaitForCountAsync(1);
        ProductWorkspaceSaveCompletionResult result =
            await controller.CompleteAsync();

        Assert.True(scheduler.IsCanceled(0));
        Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, result.Status);
        Assert.Single(workflow.SavedDocuments);
        Assert.Equal(1, workflow.CompleteCalls);
        Assert.Equal(ProductWorkspaceSaveStatus.Saved, controller.Snapshot.Status);
    }

    [Fact]
    public async Task LatestFailureBlocksCloseUntilExplicitRetrySucceeds()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(
                Failed(ProductConfigurationSaveError.IoFailure)),
            RetryHandler = () => Task.FromResult(Saved()),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Blocked"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);
        ProductWorkspaceSaveCompletionResult blocked =
            await controller.CompleteAsync();
        Assert.Equal(0, workflow.CompleteCalls);
        controller.Retry();
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);
        ProductWorkspaceSaveCompletionResult completed =
            await controller.CompleteAsync();

        Assert.Equal(
            ProductWorkspaceSaveCompletionStatus.BlockedByFailure,
            blocked.Status);
        Assert.Equal(1, workflow.CompleteCalls);
        Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task CloseTimeoutDoesNotCancelAcceptedSaveAndReopensSubmissions()
    {
        var scheduler = new ManualScheduler();
        var firstSave = new TaskCompletionSource<ProductConfigurationSaveAttemptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, call) => call == 1
                ? firstSave.Task
                : Task.FromResult(Saved()),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        controller.Submit(CreateEdit("First"));
        await scheduler.WaitForCountAsync(1);
        using var cancellation = new CancellationTokenSource();
        Task<ProductWorkspaceSaveCompletionResult> closing =
            controller.CompleteAsync(cancellation.Token);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saving);
        await workflow.WaitForSaveCallCountAsync(1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
        ProductWorkspaceEditResult second =
            ProductWorkspaceReducer.RenameContainer(
                CreateEdit("First").State!,
                "container-1",
                "Second");
        ProductWorkspaceSaveSubmissionResult accepted = controller.Submit(second);
        firstSave.SetResult(Saved());
        await scheduler.WaitForCountAsync(2);
        scheduler.Release(1);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);
        await controller.CompleteAsync();

        Assert.Equal(ProductWorkspaceSaveSubmissionStatus.Accepted, accepted.Status);
        Assert.Equal(2, controller.Snapshot.SavedRevision);
    }

    [Fact]
    public async Task CompletedControllerRejectsFurtherSaveAndRetryRequests()
    {
        var controller = new ProductWorkspaceSaveController(
            new FakeWorkflow(),
            new ManualScheduler());

        await controller.CompleteAsync();
        ProductWorkspaceSaveSubmissionResult submission =
            controller.Submit(CreateEdit("After complete"));
        ProductWorkspaceSaveRetryResult retry = controller.Retry();

        Assert.Equal(
            ProductWorkspaceSaveSubmissionStatus.Completed,
            submission.Status);
        Assert.Equal(ProductWorkspaceSaveRetryStatus.Completed, retry.Status);
    }

    [Fact]
    public async Task ObserverFailureCannotBreakPersistenceOrdering()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow();
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);
        controller.SnapshotChanged += (_, _) => throw new InvalidOperationException();

        controller.Submit(CreateEdit("Observed"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);

        Assert.Single(workflow.SavedDocuments);
    }

    [Fact]
    public async Task RealWorkflowPersistsControllerSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var scheduler = new ManualScheduler();
        var store = new ProductConfigurationStore(directory.Path);
        var workflow = new ProductConfigurationSaveWorkflow(
            new ProductConfigurationSaveCoordinator(store));
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Persisted"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Saved);
        await controller.CompleteAsync();

        ProductConfigurationLoadResult loaded = await store.LoadAsync();
        Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, loaded.Status);
        Assert.Equal("Persisted", loaded.Document!.Containers[0].Name);
    }

    [Fact]
    public async Task AsyncDisposalRefusesToDiscardLatestFailure()
    {
        var scheduler = new ManualScheduler();
        var workflow = new FakeWorkflow
        {
            SaveHandler = (_, _) => Task.FromResult(
                Failed(ProductConfigurationSaveError.InvalidConfiguration)),
        };
        var controller = new ProductWorkspaceSaveController(workflow, scheduler);

        controller.Submit(CreateEdit("Invalid"));
        await scheduler.WaitForCountAsync(1);
        scheduler.Release(0);
        await WaitForStatusAsync(controller, ProductWorkspaceSaveStatus.Failed);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.DisposeAsync());
        Assert.Equal(0, workflow.CompleteCalls);
    }

    private static ProductWorkspaceEditResult CreateEdit(
        string name,
        int? extensionValue = null)
    {
        ProductWorkspaceState state = CreateState();
        if (extensionValue is int value)
        {
            state = state with
            {
                ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["future"] = JsonSerializer.SerializeToElement(value),
                },
            };
        }

        return ProductWorkspaceReducer.RenameContainer(
            state,
            "container-1",
            name);
    }

    private static ProductWorkspaceState CreateState() =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = false,
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = false,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        ProductItemReferenceState.CreateResolved(
                            "item-1",
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity(
                                    "filesystem",
                                    Path.Combine(
                                        Path.GetTempPath(),
                                        "LongGrid.SaveController.Tests",
                                        "Project")),
                                "user-desktop",
                                "Project",
                                DesktopItemKind.Directory)),
                    ],
                },
            ],
        };

    private static ProductConfigurationSaveAttemptResult Saved() =>
        new(
            ProductConfigurationSaveAttemptStatus.Saved,
            null,
            CanRetry: false);

    private static ProductConfigurationSaveAttemptResult Failed(
        ProductConfigurationSaveError error) =>
        new(
            ProductConfigurationSaveAttemptStatus.Failed,
            error,
            CanRetry: error != ProductConfigurationSaveError.InvalidConfiguration);

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController controller,
        ProductWorkspaceSaveStatus status)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (controller.Snapshot.Status == status)
            {
                return;
            }

            await Task.Delay(5);
        }

        Assert.Equal(status, controller.Snapshot.Status);
    }

    private static async Task WaitForFailureAsync(
        ProductWorkspaceSaveController controller,
        ProductWorkspaceSaveFailure failure)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (controller.Snapshot.Failure == failure)
            {
                return;
            }

            await Task.Delay(5);
        }

        Assert.Equal(failure, controller.Snapshot.Failure);
    }

    private sealed class ManualScheduler(bool manualSaveYields = false)
        : IProductWorkspaceSaveScheduler
    {
        private readonly object gate = new();
        private readonly List<DelayRequest> requests = [];
        private readonly List<DelayRequest> saveYields = [];

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return requests.Count;
                }
            }
        }

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            var request = new DelayRequest(delay, cancellationToken);
            lock (gate)
            {
                requests.Add(request);
            }

            return request.WaitAsync();
        }

        public async Task YieldAsync(CancellationToken cancellationToken)
        {
            if (!manualSaveYields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                return;
            }

            var request = new DelayRequest(TimeSpan.Zero, cancellationToken);
            lock (gate)
            {
                saveYields.Add(request);
            }

            await request.WaitAsync();
        }

        public void Release(int index)
        {
            lock (gate)
            {
                requests[index].Release();
            }
        }

        public bool IsCanceled(int index)
        {
            lock (gate)
            {
                return requests[index].IsCanceled;
            }
        }

        public void ReleaseSaveYield(int index)
        {
            lock (gate)
            {
                saveYields[index].Release();
            }
        }

        public async Task WaitForCountAsync(int count)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (Count >= count)
                {
                    return;
                }

                await Task.Delay(5);
            }

            Assert.True(Count >= count, $"Expected {count} scheduled delays.");
        }

        public async Task WaitForSaveYieldCountAsync(int count)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                lock (gate)
                {
                    if (saveYields.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(5);
            }

            lock (gate)
            {
                Assert.True(
                    saveYields.Count >= count,
                    $"Expected {count} scheduled save yields.");
            }
        }

        private sealed class DelayRequest(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            private readonly TaskCompletionSource completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TimeSpan Delay { get; } = delay;

            public bool IsCanceled => completion.Task.IsCanceled;

            public void Release() => completion.TrySetResult();

            public async Task WaitAsync()
            {
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() =>
                        completion.TrySetCanceled(cancellationToken));
                await completion.Task;
            }
        }
    }

    private sealed class FakeWorkflow : IProductConfigurationSaveWorkflow
    {
        private int saveCalls;

        public int SaveCalls => Volatile.Read(ref saveCalls);

        public ConcurrentQueue<ProductConfigurationDocument> SavedDocuments { get; } = [];

        public Func<ProductConfigurationDocument, int,
            Task<ProductConfigurationSaveAttemptResult>> SaveHandler
        { get; init; } =
            (_, _) => Task.FromResult(Saved());

        public Func<Task<ProductConfigurationSaveAttemptResult>> RetryHandler { get; init; } =
            () => Task.FromResult(
                new ProductConfigurationSaveAttemptResult(
                    ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                    null,
                    CanRetry: false));

        public int RetryCalls { get; private set; }

        public int CompleteCalls { get; private set; }

        public int DiscardRetryCalls { get; private set; }

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            SavedDocuments.Enqueue(document);
            int call = Interlocked.Increment(ref saveCalls);
            return SaveHandler(document, call);
        }

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default)
        {
            RetryCalls++;
            return RetryHandler();
        }

        public void DiscardRetry()
        {
            DiscardRetryCalls++;
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.CompletedTask;
        }

        public async Task WaitForSaveCallCountAsync(int count)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (SaveCalls >= count)
                {
                    return;
                }

                await Task.Delay(5);
            }

            Assert.True(
                SaveCalls >= count,
                $"Expected {count} workflow save calls.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LongGrid.SaveController.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
