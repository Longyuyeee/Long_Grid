using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopExplorerReferenceDropTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task RealHDropCommitsOnceAndLeavesDroppedItemsUnchanged()
    {
        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "计划.txt");
            string folder = Path.Combine(root, "资料");
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, "LongGrid PF-007 real HDROP evidence");
            byte[] before = SHA256.HashData(File.ReadAllBytes(file));
            IReadOnlyList<DesktopCatalogEntry> catalog = DesktopCatalog.Build(
            [
                new("test-catalog", file, IsDirectory: false),
                new("test-catalog", folder, IsDirectory: true),
            ]);
            var data = new System.Windows.DataObject(
                System.Windows.DataFormats.FileDrop,
                new[] { file, folder });
            var workflow = new FakeWorkflow();
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            ProductWorkspaceState state = State();

            ProductDesktopExplorerReferenceDropPreparation actual =
                ProductDesktopExplorerReferenceDropAdapter.Prepare(
                    data,
                    state,
                    expectedEditRevision: 0,
                    catalogGeneration: 11,
                    catalog,
                    "container-1");

            Assert.True(actual.IsAccepted);
            Assert.Equal(ProductDesktopExplorerReferenceDropStatus.Accepted,
                actual.Status);
            Assert.Equal(1, actual.CommitRequest!.ContainerOrdinal);
            Assert.Equal(0, actual.CommitRequest.ExpectedEditRevision);
            Assert.Equal(11, actual.CommitRequest.ExpectedCatalogGeneration);
            Assert.Equal([0, 1], actual.CommitRequest.CatalogIndexes);
            ProductWorkspaceResolvedReferenceBatchCommitResult committed =
                coordinator.CommitResolvedReferenceBatch(
                    state,
                    11,
                    catalog,
                    actual.CommitRequest);
            Assert.True(committed.IsAccepted);
            _ = await saves.CompleteAsync();
            Assert.Equal(1, workflow.SaveCalls);
            Assert.Equal(2, committed.State!.Containers[0].Items.Count);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(file)));
            Assert.True(Directory.Exists(folder));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Purpose = "Pf007A1RealOleHDropAtomicReferenceCommit",
                Expected = new
                {
                    DropStatus = "Accepted",
                    ItemCount = 2,
                    SaveCalls = 1,
                    DesktopFilesChanged = false,
                },
                Actual = new
                {
                    DropStatus = actual.Status.ToString(),
                    ItemCount = committed.State.Containers[0].Items.Count,
                    SaveCalls = workflow.SaveCalls,
                    DesktopFilesChanged = !before.SequenceEqual(
                        SHA256.HashData(File.ReadAllBytes(file)))
                        || !Directory.Exists(folder),
                },
                Difference = "None",
                Outcome = "Pass",
                EvidenceBoundary =
                    "Real CF_HDROP HGLOBAL and real sandbox files; native DesktopHost IDropTarget registration remains PF-007A2.",
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HDropModifiersCannotChangeReferenceOnlyCommandShape()
    {
        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "shortcut.lnk");
            File.WriteAllBytes(file, [0x4c, 0x47]);
            IReadOnlyList<DesktopCatalogEntry> catalog = DesktopCatalog.Build(
                [new("test-catalog", file, IsDirectory: false)]);
            var data = new System.Windows.DataObject(
                System.Windows.DataFormats.FileDrop,
                new[] { file });

            ProductDesktopExplorerReferenceDropPreparation actual =
                ProductDesktopExplorerReferenceDropAdapter.Prepare(
                    data, State(), 3, 5, catalog, "container-1");

            Assert.True(actual.IsAccepted);
            Assert.IsType<ProductWorkspaceResolvedReferenceBatchCommitRequest>(
                actual.CommitRequest);
            Assert.DoesNotContain(
                typeof(ProductDesktopExplorerReferenceDropPreparation)
                    .GetProperties(),
                property => property.Name.Contains("Move",
                    StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Copy",
                        StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("unknown-target")]
    [InlineData("not-catalog")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    public void UnsafeOrStaleDropsFailClosed(string fault)
    {
        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "item.txt");
            File.WriteAllText(file, "unchanged");
            string[] paths = fault switch
            {
                "missing" => [Path.Combine(root, "missing.txt")],
                "duplicate" => [file, file],
                "too-many" => Enumerable.Repeat(file, 257).ToArray(),
                _ => [file],
            };
            IReadOnlyList<DesktopCatalogEntry> catalog = fault == "not-catalog"
                ? []
                : DesktopCatalog.Build(
                    [new("test-catalog", file, IsDirectory: false)]);
            ProductWorkspaceState state = State(
                locked: fault == "locked");
            var data = new System.Windows.DataObject(
                System.Windows.DataFormats.FileDrop,
                paths);

            ProductDesktopExplorerReferenceDropPreparation actual =
                ProductDesktopExplorerReferenceDropAdapter.Prepare(
                    data,
                    state,
                    3,
                    5,
                    catalog,
                    fault == "unknown-target" ? "other" : "container-1");

            Assert.False(actual.IsAccepted);
            Assert.Null(actual.CommitRequest);
            Assert.Equal("unchanged", File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.PF007.HDrop",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProductWorkspaceState State(bool locked = false) => new()
    {
        ProfileId = "default",
        Containers =
        [
            new ProductContainerState
            {
                Id = "container-1",
                Name = "工作",
                IsLocked = locked,
                Appearance = new ProductContainerAppearanceState
                {
                    Color = "#2457D6",
                    Opacity = 0.82,
                },
                Placement = new ProductContainerPlacementState
                {
                    DisplayKey = "display-primary",
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = [],
            },
        ],
    };

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
