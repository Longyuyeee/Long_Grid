using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReferenceOrderIntegrationTests
{
    [Fact]
    public async Task RealUnicodeReferencesReloadInTheRequestedCustomOrderWithoutFileChanges()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ReferenceOrder.Integration",
            Guid.NewGuid().ToString("N"));
        string desktopDirectory = Path.Combine(sandbox, "真实桌面");
        string storeDirectory = Path.Combine(sandbox, "配置");
        Directory.CreateDirectory(desktopDirectory);
        string[] paths =
        [
            Path.Combine(desktopDirectory, "一号-计划.txt"),
            Path.Combine(desktopDirectory, "二号-资料.txt"),
            Path.Combine(desktopDirectory, "三号-结果.txt"),
        ];
        await File.WriteAllTextAsync(paths[0], "alpha-原始内容");
        await File.WriteAllTextAsync(paths[1], "beta-原始内容");
        await File.WriteAllTextAsync(paths[2], "gamma-原始内容");
        Dictionary<string, string> before = Inventory(paths);

        try
        {
            ProductWorkspaceState state = CreateState(paths);
            ProductConfigurationDocument initial =
                ProductWorkspaceConfigurationProjector.Project(state).Document!;
            var store = new ProductConfigurationStore(storeDirectory);
            await store.SaveAsync(initial);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();

            ProductWorkspaceContainerCommitResult committed =
                coordinator.CommitContainer(
                    state,
                    new(
                        ProductWorkspaceContainerCommitAction.MoveReferenceEarlier,
                        revision,
                        1,
                        string.Empty,
                        Confirmed: true,
                        ItemOrdinal: 3));
            ProductWorkspaceSaveCompletionResult completion =
                await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(committed.IsAccepted);
            Assert.Equal(
                ProductWorkspaceContainerEditUndoKind.ReferenceOrder,
                committed.EditUndoToken!.Kind);
            Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, completion.Status);
            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            Assert.Equal(
                ["item-1", "item-3", "item-2"],
                reloaded.Document!.Containers[0].Items.Select(item => item.Id));
            Assert.Equal(before, Inventory(paths));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RealWriteLeaseFailureCompensatesOrderAndRetryPersistsOriginal()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ReferenceOrder.RealLeaseFailure",
            Guid.NewGuid().ToString("N"));
        string desktopDirectory = Path.Combine(sandbox, "真实桌面");
        Directory.CreateDirectory(desktopDirectory);
        string[] paths =
        [
            Path.Combine(desktopDirectory, "甲-保持.txt"),
            Path.Combine(desktopDirectory, "乙-保持.txt"),
            Path.Combine(desktopDirectory, "丙-保持.txt"),
        ];
        await File.WriteAllTextAsync(paths[0], "lease-alpha");
        await File.WriteAllTextAsync(paths[1], "lease-beta");
        await File.WriteAllTextAsync(paths[2], "lease-gamma");
        Dictionary<string, string> before = Inventory(paths);

        try
        {
            var store = new ProductConfigurationStore(
                Path.Combine(sandbox, "配置"),
                writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
                writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
            ProductWorkspaceState original = CreateState(paths);
            await store.SaveAsync(
                ProductWorkspaceConfigurationProjector.Project(original).Document!);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            long revision = coordinator.AdvanceExternalRevision();

            await using FileStream lease = AcquireLease(store.WriteLeasePath);
            ProductWorkspaceContainerCommitResult moved = coordinator.CommitContainer(
                original,
                new(
                    ProductWorkspaceContainerCommitAction.MoveReferenceLater,
                    revision,
                    1,
                    string.Empty,
                    Confirmed: true,
                    ItemOrdinal: 1));
            long failedRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                failedRevision);
            ProductWorkspaceSaveFailure observedFailure = saves.Snapshot.Failure;
            ProductConfigurationLoadResult diskAfterFailure = await store.LoadAsync();
            ProductWorkspaceContainerEditUndoCommitResult compensation =
                coordinator.CommitContainerEditUndo(
                    moved.State!,
                    moved.EditUndoToken!,
                    confirmed: true);
            long compensationRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                compensationRevision);
            await lease.DisposeAsync();

            ProductWorkspaceSaveRetryResult retry = saves.Retry();
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                compensationRevision);
            ProductWorkspaceSaveCompletionResult completion =
                await saves.CompleteAsync();
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();

            Assert.True(moved.IsAccepted);
            Assert.Equal(
                ProductWorkspaceSaveFailure.WriteLeaseUnavailable,
                observedFailure);
            Assert.Equal(
                ["item-1", "item-2", "item-3"],
                diskAfterFailure.Document!.Containers[0].Items.Select(item => item.Id));
            Assert.True(compensation.IsAccepted);
            Assert.Equal(ProductWorkspaceSaveRetryStatus.Accepted, retry.Status);
            Assert.Equal(ProductWorkspaceSaveCompletionStatus.Completed, completion.Status);
            Assert.Equal(
                ["item-1", "item-2", "item-3"],
                reloaded.Document!.Containers[0].Items.Select(item => item.Id));
            Assert.Equal(before, Inventory(paths));
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

    private static ProductWorkspaceState CreateState(IReadOnlyList<string> paths) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Unicode 引用",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = paths.Select((path, index) =>
                        ProductItemReferenceState.CreateResolved(
                            $"item-{index + 1}",
                            new DesktopCatalogEntry(
                                new DesktopItemIdentity("filesystem", path),
                                "user-desktop",
                                Path.GetFileName(path),
                                DesktopItemKind.File))).ToArray(),
                },
            ],
        };

    private static Dictionary<string, string> Inventory(
        IEnumerable<string> paths) =>
        paths.OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => path,
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static FileStream AcquireLease(string path) =>
        new(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private static async Task WaitForStatusAsync(
        ProductWorkspaceSaveController saves,
        ProductWorkspaceSaveStatus status,
        long revision)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            ProductWorkspaceSaveSnapshot snapshot = saves.Snapshot;
            if (snapshot.Status == status && snapshot.CurrentRevision == revision)
            {
                return;
            }

            await Task.Delay(5);
        }

        Assert.Equal(status, saves.Snapshot.Status);
        Assert.Equal(revision, saves.Snapshot.CurrentRevision);
    }
}
