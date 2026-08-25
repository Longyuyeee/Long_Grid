using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductDesktopWorkspaceCreatePublicationTests
{
    [Fact]
    public void MatchingFailureRequiresRollback()
    {
        var token = new ProductDesktopWorkspaceCreatePublicationToken(
            "container-created",
            WorkspaceRevision: 7,
            SaveRevision: 5);
        ProductWorkspaceSaveSnapshot failed = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = ProductWorkspaceSaveStatus.Failed,
            CurrentRevision = 5,
            Failure = ProductWorkspaceSaveFailure.IoFailure,
            CanRetry = true,
        };

        ProductDesktopWorkspaceCreatePublicationDecision decision =
            ProductDesktopWorkspaceCreatePublication.Evaluate(
                token,
                failed,
                currentWorkspaceRevision: 7,
                createdContainerStillPresent: true);

        Assert.Equal(
            ProductDesktopWorkspaceCreatePublicationDecision.RollbackRequired,
            decision);
    }

    [Theory]
    [InlineData(8, 5, true)]
    [InlineData(7, 6, true)]
    [InlineData(7, 5, false)]
    public void LaterEditSaveOrMissingContainerNeverRollsBack(
        long workspaceRevision,
        long saveRevision,
        bool containerPresent)
    {
        var token = new ProductDesktopWorkspaceCreatePublicationToken(
            "container-created",
            WorkspaceRevision: 7,
            SaveRevision: 5);
        ProductWorkspaceSaveSnapshot failed = ProductWorkspaceSaveSnapshot.Initial with
        {
            Status = ProductWorkspaceSaveStatus.Failed,
            CurrentRevision = saveRevision,
            Failure = ProductWorkspaceSaveFailure.IoFailure,
            CanRetry = true,
        };

        ProductDesktopWorkspaceCreatePublicationDecision decision =
            ProductDesktopWorkspaceCreatePublication.Evaluate(
                token,
                failed,
                workspaceRevision,
                containerPresent);

        Assert.Equal(
            ProductDesktopWorkspaceCreatePublicationDecision.Superseded,
            decision);
    }

    [Fact]
    public async Task RealIoFailureExposesGhostBeforeCompensationAndRemovesItAfterward()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.CreatePublication.RealFailure",
            Guid.NewGuid().ToString("N"));
        string blockedStoreDirectory = Path.Combine(sandbox, "store");
        Directory.CreateDirectory(sandbox);
        await File.WriteAllTextAsync(blockedStoreDirectory, "blocks-directory");
        try
        {
            var store = new ProductConfigurationStore(blockedStoreDirectory);
            var workflow = new ProductConfigurationSaveWorkflow(
                new ProductConfigurationSaveCoordinator(store));
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var commits = new ProductWorkspaceCommitCoordinator(saves);
            long baselineRevision = commits.AdvanceExternalRevision();
            ProductWorkspaceState empty = new()
            {
                ProfileId = "default",
                Containers = Array.Empty<ProductContainerState>(),
            };
            ProductContainerState created = Container("created", "Created");

            ProductWorkspaceContainerCommitResult create = commits.CommitContainer(
                empty,
                new(
                    ProductWorkspaceContainerCommitAction.Create,
                    baselineRevision,
                    ContainerOrdinal: 0,
                    Name: created.Name,
                    NewContainer: created));
            long createSaveRevision = saves.Snapshot.CurrentRevision;
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                createSaveRevision);

            Assert.True(create.IsAccepted);
            Assert.Single(create.State!.Containers);
            Assert.Equal(ProductWorkspaceSaveFailure.IoFailure, saves.Snapshot.Failure);
            var token = new ProductDesktopWorkspaceCreatePublicationToken(
                created.Id,
                create.EditRevision,
                createSaveRevision);
            Assert.Equal(
                ProductDesktopWorkspaceCreatePublicationDecision.RollbackRequired,
                ProductDesktopWorkspaceCreatePublication.Evaluate(
                    token,
                    saves.Snapshot,
                    commits.CurrentEditRevision,
                    createdContainerStillPresent: true));

            ProductWorkspaceContainerCommitResult rollback = commits.CommitContainer(
                create.State,
                new(
                    ProductWorkspaceContainerCommitAction.Remove,
                    create.EditRevision,
                    ContainerOrdinal: 1,
                    Name: string.Empty,
                    Confirmed: true));
            long rollbackSaveRevision = saves.Snapshot.CurrentRevision;

            Assert.True(rollback.IsAccepted);
            Assert.Empty(rollback.State!.Containers);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Failed,
                rollbackSaveRevision);

            File.Delete(blockedStoreDirectory);
            Assert.Equal(
                ProductWorkspaceSaveRetryStatus.Accepted,
                saves.Retry().Status);
            await WaitForStatusAsync(
                saves,
                ProductWorkspaceSaveStatus.Saved,
                rollbackSaveRevision);
            ProductConfigurationLoadResult reloaded = await store.LoadAsync();
            Assert.Equal(ProductConfigurationLoadStatus.LoadedPrimary, reloaded.Status);
            Assert.Empty(reloaded.Document!.Containers);
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
    }

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

    private static ProductContainerState Container(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Appearance = new()
            {
                Color = "#2563EB",
                Opacity = 0.88,
            },
            Placement = new()
            {
                DisplayKey = "display-unassigned",
                WidthDip = 360,
                HeightDip = 240,
            },
            Items = Array.Empty<ProductItemReferenceState>(),
        };

    private sealed class ImmediateScheduler : IProductWorkspaceSaveScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task YieldAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
