using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopContainerMenuNavigationControllerTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task RealStoreNavigationTargetsExactContainerWithoutWriting()
    {
        string sandbox = Sandbox("RealNavigation");
        string storeDirectory = Path.Combine(sandbox, "store");
        try
        {
            var store = new ProductConfigurationStore(storeDirectory);
            ProductWorkspaceState state = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            string configurationPath = store.PrimaryPath;
            byte[] before = await File.ReadAllBytesAsync(configurationPath);
            DateTime beforeWrite = File.GetLastWriteTimeUtc(configurationPath);
            ProductWorkspaceSaveSnapshot clean =
                ProductWorkspaceSaveSnapshot.Initial;

            ProductDesktopContainerMenuAvailability availability =
                ProductDesktopContainerMenuNavigationController
                    .EvaluateAvailability(
                        state,
                        isReadOnly: false,
                        clean,
                        "container-1",
                        "display-1");
            ProductDesktopContainerMenuNavigationResult rename =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenRename),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            ProductDesktopContainerMenuNavigationResult appearance =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenAppearance),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            ProductDesktopContainerMenuNavigationResult sort =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction.OpenSort),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            ProductDesktopContainerMenuNavigationResult delete =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction
                        .DeleteContainerConfiguration),
                    state,
                    isReadOnly: false,
                    currentEditRevision: 1,
                    clean,
                    Topology());
            byte[] after = await File.ReadAllBytesAsync(configurationPath);
            DateTime afterWrite = File.GetLastWriteTimeUtc(configurationPath);

            Assert.Equal(new(true, true, true, true), availability);
            Assert.True(rename.IsAccepted);
            Assert.True(appearance.IsAccepted);
            Assert.True(sort.IsAccepted);
            Assert.True(delete.IsAccepted);
            Assert.All([rename, appearance, sort, delete], result =>
                Assert.Equal(1, result.ContainerOrdinal));
            Assert.Equal(before, after);
            Assert.Equal(beforeWrite, afterWrite);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Rename = "Accepted:Ordinal=1",
                    Appearance = "Accepted:Ordinal=1",
                    Sort = "Accepted:Ordinal=1",
                    DeleteConfirmation = "Accepted:Ordinal=1",
                    ConfigurationBytesChanged = false,
                    ConfigurationWriteTimeChanged = false,
                },
                Actual = new
                {
                    Rename = $"{rename.Status}:Ordinal={rename.ContainerOrdinal}",
                    Appearance =
                        $"{appearance.Status}:Ordinal={appearance.ContainerOrdinal}",
                    Sort = $"{sort.Status}:Ordinal={sort.ContainerOrdinal}",
                    DeleteConfirmation =
                        $"{delete.Status}:Ordinal={delete.ContainerOrdinal}",
                    ConfigurationBytesChanged = !before.SequenceEqual(after),
                    ConfigurationWriteTimeChanged = beforeWrite != afterWrite,
                },
                Difference = "None",
            }));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public void LockedReadOnlyAndUnsafeRequestsFailClosedBeforeNavigation()
    {
        ProductWorkspaceState locked = State(isLocked: true);
        ProductWorkspaceSaveSnapshot clean = ProductWorkspaceSaveSnapshot.Initial;

        Assert.Equal(
            new(false, false, true, false),
            ProductDesktopContainerMenuNavigationController.EvaluateAvailability(
                locked,
                isReadOnly: false,
                clean,
                "container-1",
                "display-1"));
        Assert.Equal(
            new(false, false, true, false),
            ProductDesktopContainerMenuNavigationController.EvaluateAvailability(
                State(),
                isReadOnly: true,
                clean,
                "container-1",
                "display-1"));
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenRename),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction
                .DeleteContainerConfiguration),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.True(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenSort),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request(ProductDesktopContainerMenuAction.OpenSort) with
            {
                IsInjected = true,
            },
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
        Assert.False(ProductDesktopContainerMenuNavigationController.Handle(
            Request((ProductDesktopContainerMenuAction)99),
            locked,
            false,
            1,
            clean,
            Topology()).IsAccepted);
    }

    [Fact]
    public async Task RealStoreDeleteConfirmationAndUnifiedUndoPreserveDesktopFile()
    {
        string sandbox = Sandbox("RealDeleteUndo");
        string storeDirectory = Path.Combine(sandbox, "store");
        string desktopDirectory = Path.Combine(sandbox, "desktop");
        string desktopFile = Path.Combine(desktopDirectory, "keep.txt");
        Directory.CreateDirectory(desktopDirectory);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            ProductItemReferenceState item =
                ProductItemReferenceState.CreateResolved(
                    "item-1",
                    new(
                        new DesktopItemIdentity("filesystem", desktopFile),
                        "user-desktop",
                        "keep.txt",
                        DesktopItemKind.File));
            ProductWorkspaceState baseState = State();
            ProductWorkspaceState state = baseState with
            {
                Containers =
                [
                    baseState.Containers[0] with
                    {
                        Items = [item],
                    },
                ],
            };
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            byte[] beforeCancel = await File.ReadAllBytesAsync(store.PrimaryPath);
            DateTime beforeCancelWrite =
                File.GetLastWriteTimeUtc(store.PrimaryPath);
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            var deletes = new ProductDesktopContainerDeleteController(
                commits,
                saves);
            long revision = commits.AdvanceExternalRevision();
            ProductDesktopContainerMenuRequest request = Request(
                ProductDesktopContainerMenuAction.DeleteContainerConfiguration);

            ProductDesktopContainerMenuNavigationResult prepared =
                ProductDesktopContainerMenuNavigationController.Handle(
                    request,
                    state,
                    isReadOnly: false,
                    revision,
                    saves.Snapshot,
                    Topology());
            byte[] afterCancel = await File.ReadAllBytesAsync(store.PrimaryPath);
            DateTime afterCancelWrite = File.GetLastWriteTimeUtc(store.PrimaryPath);

            ProductDesktopContainerMenuNavigationResult revalidated =
                ProductDesktopContainerMenuNavigationController.Handle(
                    request,
                    state,
                    isReadOnly: false,
                    revision,
                    saves.Snapshot,
                    Topology());
            ProductDesktopContainerDeleteResult removal =
                deletes.CommitConfirmed(revalidated, state);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                revision: 1);
            ProductDesktopContainerDeleteResult publication =
                deletes.ObserveSave(
                    removal.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);
            ProductWorkspaceContainerRemovalUndoToken token =
                Assert.IsType<ProductWorkspaceContainerRemovalUndoToken>(
                    removal.RemovalUndoToken);
            ProductWorkspaceLatestUndoSelection latest =
                ProductWorkspaceLatestUndoSelector.Select(
                    layoutRecovery: null,
                    containerRemoval: token,
                    referenceBatchAddition: null,
                    selectedReferenceContainer: null,
                    referenceRemoval: null,
                    referenceReassignment: null);
            ProductWorkspaceContainerRemovalUndoCommitResult undo =
                commits.CommitContainerRemovalUndo(
                    removal.State!,
                    token,
                    confirmed: true);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                revision: 2);
            ProductConfigurationLoadResult restored = await store.LoadAsync();

            Assert.True(prepared.IsAccepted);
            Assert.Equal(beforeCancel, afterCancel);
            Assert.Equal(beforeCancelWrite, afterCancelWrite);
            Assert.True(revalidated.IsAccepted);
            Assert.True(removal.IsAccepted);
            Assert.Empty(removal.State!.Containers);
            Assert.Equal(
                ProductDesktopContainerDeleteStatus.Published,
                publication.Status);
            Assert.Equal(ProductWorkspaceLatestUndoKind.ContainerRemoval, latest.Kind);
            Assert.True(undo.IsAccepted);
            Assert.Single(restored.Document!.Containers);
            Assert.Single(restored.Document.Containers[0].Items);
            Assert.True(File.Exists(desktopFile));
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    DefaultCancelConfigurationBytesChanged = false,
                    DefaultCancelWriteTimeChanged = false,
                    Revalidation = "Accepted:Ordinal=1",
                    RemovedContainerCount = 0,
                    LatestUndo = "ContainerRemoval",
                    RestoredContainerCount = 1,
                    RestoredReferenceCount = 1,
                    DesktopFileExists = true,
                    DesktopFileContent = "keep-original",
                },
                Actual = new
                {
                    DefaultCancelConfigurationBytesChanged =
                        !beforeCancel.SequenceEqual(afterCancel),
                    DefaultCancelWriteTimeChanged =
                        beforeCancelWrite != afterCancelWrite,
                    Revalidation =
                        $"{revalidated.Status}:Ordinal={revalidated.ContainerOrdinal}",
                    RemovedContainerCount = removal.State.Containers.Count,
                    LatestUndo = latest.Kind.ToString(),
                    RestoredContainerCount = restored.Document.Containers.Count,
                    RestoredReferenceCount = restored.Document.Containers[0].Items.Count,
                    DesktopFileExists = File.Exists(desktopFile),
                    DesktopFileContent = await File.ReadAllTextAsync(desktopFile),
                },
                Difference = "None",
            }));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public async Task RealWriteFailureDisablesEditingRoutesAndKeepsDiskOriginal()
    {
        string sandbox = Sandbox("RealSaveFailure");
        string storeDirectory = Path.Combine(sandbox, "store");
        try
        {
            var store = new ProductConfigurationStore(
                storeDirectory,
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            ProductWorkspaceState state = State();
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductWorkspaceEditResult edit = ProductWorkspaceReducer.RenameContainer(
                state,
                "container-1",
                "Renamed");
            Assert.True(saves.Submit(edit).IsAccepted);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                revision: 1);

            ProductDesktopContainerMenuAvailability availability =
                ProductDesktopContainerMenuNavigationController
                    .EvaluateAvailability(
                        state,
                        isReadOnly: false,
                        saves.Snapshot,
                        "container-1",
                        "display-1");
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                saves.Snapshot.Failure);
            Assert.Equal(new(false, false, true, false), availability);
            Assert.Equal("Work", reloaded.Document!.Containers[0].Name);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    Failure = "WriteLeaseUnavailable",
                    RenameEnabled = false,
                    AppearanceEnabled = false,
                    SortEnabled = true,
                    DeleteEnabled = false,
                    PersistedName = "Work",
                },
                Actual = new
                {
                    Failure = saves.Snapshot.Failure.ToString(),
                    RenameEnabled = availability.CanOpenRename,
                    AppearanceEnabled = availability.CanOpenAppearance,
                    SortEnabled = availability.CanOpenSort,
                    DeleteEnabled =
                        availability.CanDeleteContainerConfiguration,
                    PersistedName = reloaded.Document.Containers[0].Name,
                },
                Difference = "None",
            }));
            await lease.DisposeAsync();
            Assert.True(saves.DiscardFailedRetryForExternalBaseline());
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public async Task RealDeleteWriteFailureCompensatesAndPersistsOriginalContainer()
    {
        string sandbox = Sandbox("RealDeleteCompensation");
        string storeDirectory = Path.Combine(sandbox, "store");
        string desktopDirectory = Path.Combine(sandbox, "desktop");
        string desktopFile = Path.Combine(desktopDirectory, "keep.txt");
        Directory.CreateDirectory(desktopDirectory);
        await File.WriteAllTextAsync(desktopFile, "keep-original");
        try
        {
            ProductWorkspaceState seed = State();
            ProductWorkspaceState state = seed with
            {
                Containers =
                [
                    seed.Containers[0] with
                    {
                        Items =
                        [
                            ProductItemReferenceState.CreateResolved(
                                "item-1",
                                new(
                                    new DesktopItemIdentity(
                                        "filesystem",
                                        desktopFile),
                                    "user-desktop",
                                    "keep.txt",
                                    DesktopItemKind.File)),
                        ],
                    },
                ],
            };
            var store = new ProductConfigurationStore(
                storeDirectory,
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(state).Document!);
            await using ProductWorkspaceSaveController saves = Saves(store);
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            var deletes = new ProductDesktopContainerDeleteController(
                commits,
                saves);
            long revision = commits.AdvanceExternalRevision();
            ProductDesktopContainerMenuNavigationResult confirmation =
                ProductDesktopContainerMenuNavigationController.Handle(
                    Request(ProductDesktopContainerMenuAction
                        .DeleteContainerConfiguration),
                    state,
                    isReadOnly: false,
                    revision,
                    saves.Snapshot,
                    Topology());

            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductDesktopContainerDeleteResult removal =
                deletes.CommitConfirmed(confirmation, state);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                revision: 1);
            ProductDesktopContainerDeleteResult compensation =
                deletes.ObserveSave(
                    removal.State,
                    commits.CurrentEditRevision,
                    saves.Snapshot);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                revision: 2);
            ProductConfigurationLoadResult blockedReload = await store.LoadAsync();

            Assert.True(compensation.IsCompensated);
            Assert.Single(compensation.State!.Containers);
            Assert.Single(blockedReload.Document!.Containers);
            await lease.DisposeAsync();
            Assert.Equal(
                ProductWorkspaceSaveRetryStatus.Accepted,
                saves.Retry().Status);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                revision: 2);
            ProductConfigurationLoadResult restored = await store.LoadAsync();
            ProductWorkspaceLatestUndoSelection latest =
                ProductWorkspaceLatestUndoSelector.Select(
                    layoutRecovery: null,
                    containerRemoval: commits.CurrentContainerRemovalUndoToken,
                    referenceBatchAddition: null,
                    selectedReferenceContainer: null,
                    referenceRemoval: null,
                    referenceReassignment: null);

            Assert.Single(restored.Document!.Containers);
            Assert.Single(restored.Document.Containers[0].Items);
            Assert.Equal(ProductWorkspaceLatestUndoKind.Unavailable, latest.Kind);
            Assert.True(File.Exists(desktopFile));
            Assert.Equal("keep-original", await File.ReadAllTextAsync(desktopFile));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Expected = new
                {
                    SourceFailure = "WriteLeaseUnavailable",
                    Status = "Compensated",
                    InMemoryContainerCount = 1,
                    PersistedContainerCount = 1,
                    PersistedReferenceCount = 1,
                    LatestUndo = "Unavailable",
                    DesktopFileContent = "keep-original",
                },
                Actual = new
                {
                    SourceFailure = compensation.SourceFailure.ToString(),
                    Status = compensation.Status.ToString(),
                    InMemoryContainerCount = compensation.State.Containers.Count,
                    PersistedContainerCount = restored.Document.Containers.Count,
                    PersistedReferenceCount = restored.Document.Containers[0].Items.Count,
                    LatestUndo = latest.Kind.ToString(),
                    DesktopFileContent = await File.ReadAllTextAsync(desktopFile),
                },
                Difference = "None",
            }));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    private static ProductDesktopContainerMenuRequest Request(
        ProductDesktopContainerMenuAction action) => new(
            action,
            "container-1",
            "display-1",
            ExpectedWorkspaceRevision: 1,
            ExpectedTopologyGeneration: 7,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false);

    private static ProductWorkspaceState State(bool isLocked = false) => new()
    {
        ProfileId = "pf004c-menu-navigation",
        Containers =
        [
            new()
            {
                Id = "container-1",
                Name = "Work",
                IsLocked = isLocked,
                Appearance = new()
                {
                    Color = "#2457D6",
                    Opacity = 0.8,
                    Collapsed = false,
                },
                Placement = new()
                {
                    DisplayKey = "display-1",
                    XDip = 100,
                    YDip = 100,
                    WidthDip = 320,
                    HeightDip = 180,
                },
                Items = [],
            },
        ],
    };

    private static ProductDisplayTopologySnapshot Topology() => new(
        ProductDisplayTopologyStatus.Ready,
        Generation: 7,
        Displays:
        [
            new(
                "display-1",
                new(0, 0, 1920, 1080),
                new(0, 0, 1920, 1040),
                96,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ],
        ActivePathCount: 1,
        StableIdentityCount: 1,
        BufferAttempts: 1);

    private static ProductWorkspaceSaveController Saves(
        ProductConfigurationStore store) => new(
            new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store)),
            new ImmediateScheduler(),
            TimeSpan.FromMilliseconds(1));

    private static FileStream AcquireLease(string path) => new(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.None);

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus status,
        long revision)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (saves.Snapshot.Status != status
            || saves.Snapshot.CurrentRevision != revision)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string Sandbox(string scenario) => Path.Combine(
        Path.GetTempPath(),
        "LongGrid.PF004C",
        scenario,
        Guid.NewGuid().ToString("N"));

    private static void DeleteSandbox(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
