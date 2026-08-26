using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class ProductDesktopReferenceReassignmentTests(
    ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(
        2026, 8, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SelectedItemDragFreezesAuthorityAndFindsOnlySafeOtherContainer()
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();

        ProductDesktopReferenceReassignmentSession session = Assert.IsType<
            ProductDesktopReferenceReassignmentSession>(
                ProductDesktopReferenceReassignmentAdapter.TryStart(
                    projection,
                    transaction,
                    x: 70,
                    y: 100));
        ProductDesktopReferenceReassignmentSession belowThreshold = Assert.IsType<
            ProductDesktopReferenceReassignmentSession>(
                ProductDesktopReferenceReassignmentAdapter.TryUpdate(
                    projection,
                    transaction,
                    session,
                    x: 73,
                    y: 103));
        ProductDesktopReferenceReassignmentSession overTarget = Assert.IsType<
            ProductDesktopReferenceReassignmentSession>(
                ProductDesktopReferenceReassignmentAdapter.TryUpdate(
                    projection,
                    transaction,
                    belowThreshold,
                    x: 450,
                    y: 100));

        Assert.False(belowThreshold.DragThresholdReached);
        Assert.Null(belowThreshold.HoveredTargetContainerId);
        Assert.True(overTarget.DragThresholdReached);
        Assert.Equal("container-2", overTarget.HoveredTargetContainerId);
        ProductDesktopReferenceReassignmentSurfaceInput completed = Assert.IsType<
            ProductDesktopReferenceReassignmentSurfaceInput>(
                ProductDesktopReferenceReassignmentAdapter.TryComplete(
                    projection,
                    transaction,
                    overTarget,
                    x: 450,
                    y: 100));
        Assert.Equal("container-1", completed.SourceContainerId);
        Assert.Equal(["item:1"], completed.ItemIds);
        Assert.Equal(450, completed.PointerScreenX);
        Assert.Equal(100, completed.PointerScreenY);

        Assert.Null(ProductDesktopReferenceReassignmentAdapter.TryUpdate(
            projection,
            transaction with
            {
                Selection = transaction.Selection! with
                {
                    SelectionRevision = transaction.Selection!.SelectionRevision + 1,
                },
            },
            session,
            x: 450,
            y: 100));
    }

    [Fact]
    public void AdmissionRejectsStaleLockedTemporaryUnresolvedAndDuplicateOrdinal()
    {
        DesktopCatalogEntry catalog = Assert.Single(DesktopCatalog.Build(
            [new("test-catalog", @"C:\Evidence\item.txt", IsDirectory: false)]));
        ProductWorkspaceState state = State(catalog);
        var request = new ProductDesktopReferenceReassignmentRequest(
            "container-1",
            ["item:1"],
            "container-2",
            "display-primary",
            WorkspaceRevision: 4,
            TopologyGeneration: 6,
            SourceAttested: true,
            IsInjected: false);

        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.Accepted,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 4, 6, request).Status);
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.StaleAuthority,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 5, 6, request).Status);
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.TemporaryFolderItem,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 4, 6, request with { ItemIds = ["folder:1:1"] }).Status);

        ProductWorkspaceState locked = state with
        {
            Containers =
            [
                state.Containers[0],
                state.Containers[1] with { IsLocked = true },
            ],
        };
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.ContainerLocked,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                locked, 4, 6, request).Status);

        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(state).Document!;
        ProductWorkspaceState unavailable =
            ProductWorkspaceConfigurationResolver.Resolve(
                document,
                Array.Empty<DesktopCatalogEntry>()).State!;
        unavailable = unavailable with
        {
            Containers =
            [
                unavailable.Containers[0],
                unavailable.Containers[1],
            ],
        };
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.ReferenceUnavailable,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                unavailable, 4, 6, request).Status);
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.InvalidRequest,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state,
                4,
                6,
                request with { ItemIds = ["item:1", "item:01"] }).Status);

        ProductDesktopReferenceReassignmentRequest[] invalidRequests =
        [
            request with { SourceAttested = false },
            request with { IsInjected = true },
            request with { ItemIds = [] },
            request with { ItemIds = [" "] },
            request with { ItemIds = ["item:1", "item:1"] },
            request with
            {
                ItemIds = Enumerable.Range(1, 257)
                    .Select(index => $"item:{index}")
                    .ToArray(),
            },
        ];
        Assert.All(invalidRequests, invalid => Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.InvalidRequest,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 4, 6, invalid).Status));
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.StaleAuthority,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state,
                4,
                7,
                request).Status);

        ProductDesktopReferenceReassignmentRequest[] unavailableContainers =
        [
            request with { SourceContainerId = "missing" },
            request with { TargetContainerId = "missing" },
            request with { TargetContainerId = "container-1" },
            request with { DisplayId = "display-secondary" },
        ];
        Assert.All(unavailableContainers, unavailableRequest => Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus
                .ContainerUnavailable,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 4, 6, unavailableRequest).Status));
        Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.ContainerLocked,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state with
                {
                    Containers =
                    [
                        state.Containers[0] with { IsLocked = true },
                        state.Containers[1],
                    ],
                },
                4,
                6,
                request).Status);

        ProductDesktopReferenceReassignmentRequest[] unavailableReferences =
        [
            request with { ItemIds = ["item:abc"] },
            request with { ItemIds = ["item:0"] },
            request with { ItemIds = ["item:2"] },
        ];
        Assert.All(unavailableReferences, unavailableRequest => Assert.Equal(
            ProductDesktopReferenceReassignmentAdmissionStatus.ReferenceUnavailable,
            ProductDesktopReferenceReassignmentAdmissionAdapter.Prepare(
                state, 4, 6, unavailableRequest).Status));
    }

    [Fact]
    public void GestureFailsClosedBeforeThresholdOrWithoutFrozenAuthority()
    {
        (ProductDesktopHostDisplayProjection projection,
            ProductDesktopInteractionSurfaceTransactionSnapshot transaction) =
            Context();
        ProductDesktopReferenceReassignmentSession session = Assert.IsType<
            ProductDesktopReferenceReassignmentSession>(
                ProductDesktopReferenceReassignmentAdapter.TryStart(
                    projection,
                    transaction,
                    70,
                    100));

        Assert.Null(ProductDesktopReferenceReassignmentAdapter.TryComplete(
            projection,
            transaction,
            session,
            72,
            102));
        Assert.Null(ProductDesktopReferenceReassignmentAdapter.TryStart(
            projection,
            transaction with
            {
                Status = ProductDesktopInteractionSurfaceTransactionStatus.Passive,
            },
            70,
            100));
        Assert.Null(ProductDesktopReferenceReassignmentAdapter.TryStart(
            projection,
            transaction,
            450,
            100));
        ProductDesktopReferenceReassignmentSession sameSource = Assert.IsType<
            ProductDesktopReferenceReassignmentSession>(
                ProductDesktopReferenceReassignmentAdapter.TryUpdate(
                    projection,
                    transaction,
                    session,
                    120,
                    100));
        Assert.True(sameSource.DragThresholdReached);
        Assert.Null(sameSource.HoveredTargetContainerId);
    }

    [Fact]
    public async Task RealHwndGestureCommitsAtomicReassignmentOnceAndLeavesFileUnchanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.PF007B.RealHwnd",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "真实改归属.txt");
            File.WriteAllText(file, "LongGrid PF-007B real HWND evidence");
            string beforePath = Path.GetFullPath(file);
            byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(file));
            DesktopCatalogEntry catalog = Assert.Single(DesktopCatalog.Build(
                [new("test-catalog", file, IsDirectory: false)]));
            ProductWorkspaceState state = State(catalog);
            var workflow = new FakeWorkflow();
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            var completion = new TaskCompletionSource<RealGestureEvidence>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    (ProductDesktopHostDisplayProjection projection,
                        ProductDesktopInteractionSurfaceTransactionSnapshot
                            transaction) = Context();
                    using WindowsProductDesktopHostReadOnlySurface surface =
                        WindowsProductDesktopHostReadOnlySurface.Create(
                            projection,
                            new nint(7008));
                    Assert.True(surface.ApplyExplicit());
                    int clickSelectionCount = 0;
                    int singleClickOpenCount = 0;
                    surface.BindSelection(() => transaction, (_, _) =>
                    {
                        clickSelectionCount++;
                        return true;
                    });
                    surface.BindItemOpen(_ =>
                    {
                        singleClickOpenCount++;
                        return true;
                    });
                    int callbackCount = 0;
                    ProductWorkspaceResolvedReferenceReassignmentCommitResult?
                        committed = null;
                    surface.BindReferenceReassignment(input =>
                    {
                        callbackCount++;
                        Assert.Equal("container-1", input.SourceContainerId);
                        Assert.Equal(["item:1"], input.ItemIds);
                        Assert.Equal(450, input.PointerScreenX);
                        Assert.Equal(100, input.PointerScreenY);
                        committed = coordinator.CommitResolvedReferenceReassignment(
                            state,
                            new(
                                ExpectedEditRevision: 0,
                                SourceContainerOrdinal: 1,
                                ItemOrdinals: [1],
                                TargetContainerOrdinal: 2));
                        return committed.IsAccepted;
                    });

                    bool injectedStart =
                        surface.BeginReferenceReassignmentForEvidence(
                            70,
                            100,
                            sourceAttested: true,
                            isInjected: true);
                    Assert.True(surface.ApplyItemOpenPolicy(true));
                    bool clickStart = surface.BeginReferenceReassignmentForEvidence(
                        70,
                        100);
                    bool clickComplete =
                        surface.CompleteReferenceReassignmentForEvidence(
                            72,
                            102);
                    Assert.True(surface.ApplyItemOpenPolicy(false));
                    bool start = surface.BeginReferenceReassignmentForEvidence(
                        70,
                        100);
                    bool update = surface.UpdateReferenceReassignmentForEvidence(
                        450,
                        100);
                    bool complete =
                        surface.CompleteReferenceReassignmentForEvidence(
                            450,
                            100);
                    completion.TrySetResult(new(
                        surface.Handle != nint.Zero,
                        injectedStart,
                        clickStart,
                        clickComplete,
                        clickSelectionCount,
                        singleClickOpenCount,
                        start,
                        update,
                        complete,
                        callbackCount,
                        committed));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "LongGrid.PF007B.RealHwndGesture",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            RealGestureEvidence actual = await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            _ = await saves.CompleteAsync();

            Assert.True(actual.RealWindow);
            Assert.False(actual.InjectedStart);
            Assert.True(actual.ClickStart);
            Assert.True(actual.ClickComplete);
            Assert.Equal(1, actual.ClickSelectionCount);
            Assert.Equal(1, actual.SingleClickOpenCount);
            Assert.True(actual.Start);
            Assert.True(actual.Update);
            Assert.True(actual.Complete);
            Assert.Equal(1, actual.CallbackCount);
            Assert.True(actual.Commit?.IsAccepted);
            Assert.Empty(actual.Commit!.State!.Containers[0].Items);
            Assert.Single(actual.Commit.State.Containers[1].Items);
            Assert.NotNull(actual.Commit.UndoToken);
            Assert.Equal(1, workflow.SaveCalls);
            Assert.Equal(beforePath, Path.GetFullPath(file));
            Assert.Equal(beforeHash, SHA256.HashData(File.ReadAllBytes(file)));

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Purpose = "PF007BRealHwndReferenceReassignment",
                Expected = new
                {
                    RealHwnd = true,
                    InjectedInputAccepted = false,
                    BelowThresholdClickRestored = true,
                    SingleClickOpenCount = 1,
                    CallbackCount = 1,
                    SaveCalls = 1,
                    SourceCount = 0,
                    TargetCount = 1,
                    UndoAvailable = true,
                    DesktopFilesChanged = false,
                },
                Actual = new
                {
                    RealHwnd = actual.RealWindow,
                    InjectedInputAccepted = actual.InjectedStart,
                    BelowThresholdClickRestored = actual.ClickComplete,
                    actual.SingleClickOpenCount,
                    actual.CallbackCount,
                    SaveCalls = workflow.SaveCalls,
                    SourceCount = actual.Commit.State.Containers[0].Items.Count,
                    TargetCount = actual.Commit.State.Containers[1].Items.Count,
                    UndoAvailable = actual.Commit.UndoToken is not null,
                    DesktopFilesChanged = !string.Equals(
                        beforePath,
                        Path.GetFullPath(file),
                        StringComparison.OrdinalIgnoreCase)
                        || !beforeHash.SequenceEqual(
                            SHA256.HashData(File.ReadAllBytes(file))),
                },
                Difference = "None",
                Outcome = "Pass",
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (
        ProductDesktopHostDisplayProjection Projection,
        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction)
        Context()
    {
        ProductDesktopHostDisplayProjection projection =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(0, 0, 1000, 700),
                96,
                [
                    ProductDesktopHostReadOnlyProjection.Create(
                        "container-1",
                        "工作",
                        ["真实改归属.txt"],
                        "#2457D6",
                        0.82,
                        false,
                        24,
                        36,
                        300,
                        240,
                        itemIds: ["item:1"]),
                    ProductDesktopHostReadOnlyProjection.Create(
                        "container-2",
                        "归档",
                        [],
                        "#2D7D46",
                        0.82,
                        false,
                        400,
                        36,
                        300,
                        240),
                ]);
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(),
            "container-1",
            WorkspaceRevision: 1,
            TopologyGeneration: 2,
            WindowRegistryGeneration: 3,
            ExpiresAtUtc: Now.AddSeconds(10));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                ["item:1"],
                Now).Controller!;
        ProductDesktopSelectionSnapshot snapshot = selection.Apply(
            lease,
            ["item:1"],
            new(
                ProductDesktopSelectionAction.SelectItem,
                ItemId: "item:1"),
            Now);
        return (
            projection,
            new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    3,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                snapshot,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(snapshot),
                1));
    }

    private static ProductWorkspaceState State(DesktopCatalogEntry entry) => new()
    {
        ProfileId = "default",
        Containers =
        [
            Container(
                "container-1",
                "工作",
                [ProductItemReferenceState.CreateResolved("reference-1", entry)],
                x: 24),
            Container("container-2", "归档", [], x: 400),
        ],
    };

    private static ProductContainerState Container(
        string id,
        string name,
        IReadOnlyList<ProductItemReferenceState> items,
        double x) => new()
        {
            Id = id,
            Name = name,
            Appearance = new()
            {
                Color = "#2457D6",
                Opacity = 0.82,
            },
            Placement = new()
            {
                DisplayKey = "display-primary",
                XDip = x,
                YDip = 36,
                WidthDip = 300,
                HeightDip = 240,
            },
            Items = items,
        };

    private sealed record RealGestureEvidence(
        bool RealWindow,
        bool InjectedStart,
        bool ClickStart,
        bool ClickComplete,
        int ClickSelectionCount,
        int SingleClickOpenCount,
        bool Start,
        bool Update,
        bool Complete,
        int CallbackCount,
        ProductWorkspaceResolvedReferenceReassignmentCommitResult? Commit);

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken token) =>
            Task.CompletedTask;

        public Task YieldAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class FakeWorkflow : IProductConfigurationSaveWorkflow
    {
        public int SaveCalls { get; private set; }

        public Task<ProductConfigurationSaveAttemptResult> SaveAsync(
            ProductConfigurationDocument document,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.Saved,
                null,
                CanRetry: false));
        }

        public Task<ProductConfigurationSaveAttemptResult> RetryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductConfigurationSaveAttemptResult(
                ProductConfigurationSaveAttemptStatus.NoRetryAvailable,
                null,
                CanRetry: false));

        public void DiscardRetry()
        {
        }

        public Task CompleteAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
