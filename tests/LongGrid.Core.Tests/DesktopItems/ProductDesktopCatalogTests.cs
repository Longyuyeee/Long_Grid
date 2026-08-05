using System.Collections.Concurrent;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Core.Tests.DesktopItems;

public sealed class ProductDesktopCatalogTests
{
    [Fact]
    public async Task ReaderPublishesAuthoritativeNonRecursivePhysicalCatalog()
    {
        using var directories = new TemporaryDesktopDirectories();
        Directory.CreateDirectory(Path.Combine(directories.User, "Folder"));
        File.WriteAllText(Path.Combine(directories.User, "Notes.txt"), "notes");
        File.WriteAllText(
            Path.Combine(directories.User, "Folder", "Nested.txt"),
            "nested");
        File.WriteAllText(Path.Combine(directories.Public, "Portal.url"), "url");
        var reader = new ProductDesktopCatalogReader(
            directories.User,
            directories.Public);

        ProductDesktopCatalogReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDesktopCatalogReadStatus.Ready, result.Status);
        Assert.True(result.IsAuthoritative);
        Assert.Equal(3, result.Entries.Count);
        Assert.DoesNotContain(
            result.Entries,
            entry => entry.DisplayName == "Nested.txt");
        Assert.All(
            result.Sources,
            source => Assert.Equal(
                ProductDesktopCatalogSourceStatus.Ready,
                source.Status));
    }

    [Fact]
    public async Task MissingSourceMakesCollectedEntriesNonAuthoritative()
    {
        using var directories = new TemporaryDesktopDirectories();
        File.WriteAllText(Path.Combine(directories.User, "Notes.txt"), "notes");
        Directory.Delete(directories.Public);
        var reader = new ProductDesktopCatalogReader(
            directories.User,
            directories.Public);

        ProductDesktopCatalogReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDesktopCatalogReadStatus.Partial, result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.Single(result.Entries);
        Assert.Contains(
            result.Sources,
            source => source.Source == ProductDesktopCatalogSourceKind.PublicDesktop
                && source.Status == ProductDesktopCatalogSourceStatus.Missing);
    }

    [Fact]
    public async Task TwoExistingEmptySourcesAreAuthoritativeEmptyCatalog()
    {
        using var directories = new TemporaryDesktopDirectories();
        var reader = new ProductDesktopCatalogReader(
            directories.User,
            directories.Public);

        ProductDesktopCatalogReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDesktopCatalogReadStatus.Ready, result.Status);
        Assert.True(result.IsAuthoritative);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task TwoMissingSourcesAreUnavailableInsteadOfAuthoritativeEmpty()
    {
        using var directories = new TemporaryDesktopDirectories();
        Directory.Delete(directories.User);
        Directory.Delete(directories.Public);
        var reader = new ProductDesktopCatalogReader(
            directories.User,
            directories.Public);

        ProductDesktopCatalogReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDesktopCatalogReadStatus.Unavailable, result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void CurrentUserReaderFactoryCreatesReadOnlyAdapter()
    {
        ProductDesktopCatalogReader reader =
            ProductDesktopCatalogReader.CreateForCurrentUser();

        Assert.NotNull(reader);
    }

    [Fact]
    public async Task ControllerPublishesGenerationAndAuthoritativeSnapshot()
    {
        var reader = new QueuedReader();
        reader.Enqueue(Result(ProductDesktopCatalogReadStatus.Ready));
        await using var controller = new ProductDesktopCatalogController(reader);
        var observed = new List<ProductDesktopCatalogStatus>();
        controller.SnapshotChanged += (_, snapshot) => observed.Add(snapshot.Status);

        ProductDesktopCatalogRefreshResult refresh = await controller.RefreshAsync();

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Published, refresh.Status);
        Assert.Equal(1, refresh.Generation);
        Assert.Equal(ProductDesktopCatalogStatus.Ready, controller.Snapshot.Status);
        Assert.True(controller.Snapshot.IsAuthoritative);
        Assert.Equal(
            [ProductDesktopCatalogStatus.Refreshing, ProductDesktopCatalogStatus.Ready],
            observed);
    }

    [Fact]
    public async Task LaterGenerationWinsWhenEarlierReadCompletesLast()
    {
        var reader = new QueuedReader();
        TaskCompletionSource<ProductDesktopCatalogReadResult> first = reader.EnqueuePending();
        TaskCompletionSource<ProductDesktopCatalogReadResult> second = reader.EnqueuePending();
        await using var controller = new ProductDesktopCatalogController(reader);

        Task<ProductDesktopCatalogRefreshResult> firstRefresh = controller.RefreshAsync();
        Task<ProductDesktopCatalogRefreshResult> secondRefresh = controller.RefreshAsync();
        second.SetResult(Result(ProductDesktopCatalogReadStatus.Ready));
        ProductDesktopCatalogRefreshResult secondResult = await secondRefresh;
        first.SetResult(Result(ProductDesktopCatalogReadStatus.Partial));
        ProductDesktopCatalogRefreshResult firstResult = await firstRefresh;

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Published, secondResult.Status);
        Assert.Equal(ProductDesktopCatalogRefreshStatus.Stale, firstResult.Status);
        Assert.Equal(2, controller.Snapshot.Generation);
        Assert.Equal(ProductDesktopCatalogStatus.Ready, controller.Snapshot.Status);
    }

    [Theory]
    [InlineData(
        ProductDesktopCatalogReadStatus.Partial,
        ProductDesktopCatalogStatus.Partial)]
    [InlineData(
        ProductDesktopCatalogReadStatus.Unavailable,
        ProductDesktopCatalogStatus.Unavailable)]
    [InlineData(
        ProductDesktopCatalogReadStatus.Failed,
        ProductDesktopCatalogStatus.Failed)]
    public async Task ControllerMapsEveryFiniteReaderStatus(
        ProductDesktopCatalogReadStatus readStatus,
        ProductDesktopCatalogStatus expectedStatus)
    {
        var reader = new QueuedReader();
        reader.Enqueue(Result(readStatus));
        await using var controller = new ProductDesktopCatalogController(reader);

        ProductDesktopCatalogRefreshResult result = await controller.RefreshAsync();

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Published, result.Status);
        Assert.Equal(expectedStatus, result.Snapshot.Status);
        Assert.False(result.Snapshot.IsAuthoritative);
    }

    [Fact]
    public async Task ReaderIoFailurePublishesFiniteFailedState()
    {
        var reader = new QueuedReader();
        TaskCompletionSource<ProductDesktopCatalogReadResult> pending =
            reader.EnqueuePending();
        pending.SetException(new IOException("finite test failure"));
        await using var controller = new ProductDesktopCatalogController(reader);

        ProductDesktopCatalogRefreshResult result = await controller.RefreshAsync();

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Published, result.Status);
        Assert.Equal(ProductDesktopCatalogStatus.Failed, result.Snapshot.Status);
    }

    [Fact]
    public async Task CancelledEarlierGenerationCannotReplaceLaterResult()
    {
        var reader = new QueuedReader();
        _ = reader.EnqueuePending();
        TaskCompletionSource<ProductDesktopCatalogReadResult> second =
            reader.EnqueuePending();
        await using var controller = new ProductDesktopCatalogController(reader);
        using var firstCancellation = new CancellationTokenSource();

        Task<ProductDesktopCatalogRefreshResult> first =
            controller.RefreshAsync(firstCancellation.Token);
        Task<ProductDesktopCatalogRefreshResult> later = controller.RefreshAsync();
        second.SetResult(Result(ProductDesktopCatalogReadStatus.Ready));
        _ = await later;
        firstCancellation.Cancel();

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Stale, (await first).Status);
        Assert.Equal(ProductDesktopCatalogStatus.Ready, controller.Snapshot.Status);
    }

    [Fact]
    public async Task FailedEarlierGenerationCannotReplaceLaterResult()
    {
        var reader = new QueuedReader();
        TaskCompletionSource<ProductDesktopCatalogReadResult> firstPending =
            reader.EnqueuePending();
        TaskCompletionSource<ProductDesktopCatalogReadResult> secondPending =
            reader.EnqueuePending();
        await using var controller = new ProductDesktopCatalogController(reader);

        Task<ProductDesktopCatalogRefreshResult> first = controller.RefreshAsync();
        Task<ProductDesktopCatalogRefreshResult> later = controller.RefreshAsync();
        secondPending.SetResult(Result(ProductDesktopCatalogReadStatus.Ready));
        _ = await later;
        firstPending.SetException(new IOException("stale test failure"));

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Stale, (await first).Status);
        Assert.Equal(ProductDesktopCatalogStatus.Ready, controller.Snapshot.Status);
    }

    [Fact]
    public async Task CallerCancellationPublishesFiniteCancelledState()
    {
        var reader = new QueuedReader();
        _ = reader.EnqueuePending();
        await using var controller = new ProductDesktopCatalogController(reader);
        using var cancellation = new CancellationTokenSource();

        Task<ProductDesktopCatalogRefreshResult> refresh =
            controller.RefreshAsync(cancellation.Token);
        cancellation.Cancel();
        ProductDesktopCatalogRefreshResult result = await refresh;

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Cancelled, result.Status);
        Assert.Equal(ProductDesktopCatalogStatus.Cancelled, controller.Snapshot.Status);
        Assert.False(controller.Snapshot.IsAuthoritative);
    }

    [Fact]
    public async Task DisposalCancelsAndDrainsAcceptedRefresh()
    {
        var reader = new QueuedReader();
        _ = reader.EnqueuePending();
        var controller = new ProductDesktopCatalogController(reader);

        Task<ProductDesktopCatalogRefreshResult> refresh = controller.RefreshAsync();
        await controller.DisposeAsync();
        ProductDesktopCatalogRefreshResult result = await refresh;

        Assert.Equal(ProductDesktopCatalogRefreshStatus.Cancelled, result.Status);
        await controller.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => controller.RefreshAsync());
    }

    private static ProductDesktopCatalogReadResult Result(
        ProductDesktopCatalogReadStatus status)
    {
        IReadOnlyList<DesktopCatalogEntry> entries = status is
            ProductDesktopCatalogReadStatus.Ready or
            ProductDesktopCatalogReadStatus.Partial
            ? new[]
            {
                new DesktopCatalogEntry(
                    new DesktopItemIdentity("filesystem", Target()),
                    "user-desktop",
                    "Anonymous item",
                    DesktopItemKind.File),
            }
            : Array.Empty<DesktopCatalogEntry>();
        return new(
            status,
            entries,
            Array.Empty<ProductDesktopCatalogSourceSnapshot>());
    }

    private static string Target() =>
        Path.Combine(Path.GetTempPath(), "LongGrid.Catalog.Controller", "item.txt");

    private sealed class QueuedReader : IProductDesktopCatalogReader
    {
        private readonly ConcurrentQueue<
            TaskCompletionSource<ProductDesktopCatalogReadResult>> results = new();

        public void Enqueue(ProductDesktopCatalogReadResult result)
        {
            TaskCompletionSource<ProductDesktopCatalogReadResult> pending =
                EnqueuePending();
            pending.SetResult(result);
        }

        public TaskCompletionSource<ProductDesktopCatalogReadResult> EnqueuePending()
        {
            var source = new TaskCompletionSource<ProductDesktopCatalogReadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            results.Enqueue(source);
            return source;
        }

        public async Task<ProductDesktopCatalogReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Assert.True(results.TryDequeue(out TaskCompletionSource<
                ProductDesktopCatalogReadResult>? source));
            return await source.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TemporaryDesktopDirectories : IDisposable
    {
        public TemporaryDesktopDirectories()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "LongGrid.ProductDesktopCatalog.Tests",
                Guid.NewGuid().ToString("N"));
            User = Path.Combine(Root, "User");
            Public = Path.Combine(Root, "Public");
            Directory.CreateDirectory(User);
            Directory.CreateDirectory(Public);
        }

        public string Root { get; }

        public string User { get; }

        public string Public { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
