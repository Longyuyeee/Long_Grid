using System.Collections.Concurrent;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDisplayTopologyTests
{
    private static readonly DisplayTopologyNode Primary = new(
        "DISPLAY-A",
        new(0, 0, 1920, 1080),
        new(0, 0, 1920, 1040),
        96,
        DisplayRotation.Landscape,
        IsPrimary: true);

    [Fact]
    public async Task CompleteStrongSampleIsAuthoritative()
    {
        var reader = new ProductDisplayTopologyReader(
            new FixedSource(Sample()));

        ProductDisplayTopologyReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDisplayTopologyReadStatus.Ready, result.Status);
        Assert.True(result.IsAuthoritative);
        Assert.Single(result.Displays);
        Assert.Equal(1, result.ActivePathCount);
        Assert.Equal(1, result.StableIdentityCount);
        Assert.Equal(1, result.BufferAttempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task AnyIncompleteSignalMakesSampleNonAuthoritative(int defect)
    {
        ProductDisplayTopologySample sample = Sample();
        ProductDisplayTopologySampleMonitor monitor = sample.Monitors[0];
        sample = defect switch
        {
            1 => sample with
            {
                Monitors = [monitor with { HasStableTargetIdentity = false }],
            },
            2 => sample with
            {
                Monitors = [monitor with { MappedToActivePath = false }],
            },
            3 => sample with
            {
                Monitors = [monitor with { SourceBoundsMatch = false }],
            },
            4 => sample with
            {
                Monitors = [monitor with { TargetAvailable = false }],
            },
            5 => sample with
            {
                Monitors =
                [
                    monitor with
                    {
                        Display = monitor.Display with
                        {
                            Rotation = DisplayRotation.Unknown,
                        },
                    },
                ],
            },
            6 => sample with { ActivePathCount = 2 },
            7 => sample with
            {
                Monitors =
                [
                    monitor with
                    {
                        Display = monitor.Display with
                        {
                            WorkArea = new(-10, 0, 1920, 1040),
                        },
                    },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var reader = new ProductDisplayTopologyReader(new FixedSource(sample));

        ProductDisplayTopologyReadResult result = await reader.ReadAsync();

        Assert.Equal(ProductDisplayTopologyReadStatus.Degraded, result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.Single(result.Displays);
    }

    [Fact]
    public async Task EmptyAndInvalidSamplesReturnFiniteNonAuthoritativeStates()
    {
        var emptyReader = new ProductDisplayTopologyReader(
            new FixedSource(new(Array.Empty<ProductDisplayTopologySampleMonitor>(), 0, 1)));
        ProductDisplayTopologySample invalid = Sample() with
        {
            Monitors =
            [
                Sample().Monitors[0] with
                {
                    Display = Primary with { IsPrimary = false },
                },
            ],
        };
        var invalidReader = new ProductDisplayTopologyReader(
            new FixedSource(invalid));

        ProductDisplayTopologyReadResult empty = await emptyReader.ReadAsync();
        ProductDisplayTopologyReadResult failed = await invalidReader.ReadAsync();

        Assert.Equal(ProductDisplayTopologyReadStatus.Unavailable, empty.Status);
        Assert.Equal(ProductDisplayTopologyReadStatus.Failed, failed.Status);
        Assert.Empty(empty.Displays);
        Assert.Empty(failed.Displays);
    }

    [Fact]
    public async Task UnsupportedPlatformIsFiniteAndFactoryIsReadOnly()
    {
        var reader = new ProductDisplayTopologyReader(new UnsupportedSource());

        ProductDisplayTopologyReadResult result = await reader.ReadAsync();

        Assert.Equal(
            ProductDisplayTopologyReadStatus.UnsupportedPlatform,
            result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.NotNull(ProductDisplayTopologyReader.CreateForCurrentSession());
    }

    [Fact]
    public async Task CurrentWindowsSessionSamplingIsFiniteAndIdentitySafe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDisplayTopologyReader reader =
            ProductDisplayTopologyReader.CreateForCurrentSession();

        ProductDisplayTopologyReadResult result = await reader.ReadAsync();

        Assert.NotEqual(
            ProductDisplayTopologyReadStatus.UnsupportedPlatform,
            result.Status);
        Assert.InRange(result.BufferAttempts, 0, 8);
        Assert.All(
            result.Displays,
            display =>
            {
                Assert.Equal(64, display.StableId.Length);
                Assert.All(display.StableId, character =>
                    Assert.True(char.IsAsciiHexDigit(character)));
            });
    }

    [Fact]
    public async Task ControllerPublishesAuthoritativeGeneration()
    {
        var reader = new QueuedReader();
        reader.Enqueue(Result(ProductDisplayTopologyReadStatus.Ready));
        await using var controller = new ProductDisplayTopologyController(reader);
        var observed = new List<ProductDisplayTopologyStatus>();
        controller.SnapshotChanged += (_, snapshot) => observed.Add(snapshot.Status);

        ProductDisplayTopologyRefreshResult result = await controller.RefreshAsync();

        Assert.Equal(ProductDisplayTopologyRefreshStatus.Published, result.Status);
        Assert.Equal(1, result.Generation);
        Assert.True(result.Snapshot.IsAuthoritative);
        Assert.Equal(
            [ProductDisplayTopologyStatus.Refreshing, ProductDisplayTopologyStatus.Ready],
            observed);
    }

    [Fact]
    public async Task LaterGenerationWinsWhenEarlierSampleFinishesLast()
    {
        var reader = new QueuedReader();
        TaskCompletionSource<ProductDisplayTopologyReadResult> first =
            reader.EnqueuePending();
        TaskCompletionSource<ProductDisplayTopologyReadResult> second =
            reader.EnqueuePending();
        await using var controller = new ProductDisplayTopologyController(reader);

        Task<ProductDisplayTopologyRefreshResult> earlier = controller.RefreshAsync();
        Task<ProductDisplayTopologyRefreshResult> later = controller.RefreshAsync();
        second.SetResult(Result(ProductDisplayTopologyReadStatus.Ready));
        ProductDisplayTopologyRefreshResult published = await later;
        first.SetResult(Result(ProductDisplayTopologyReadStatus.Degraded));
        ProductDisplayTopologyRefreshResult stale = await earlier;

        Assert.Equal(ProductDisplayTopologyRefreshStatus.Published, published.Status);
        Assert.Equal(ProductDisplayTopologyRefreshStatus.Stale, stale.Status);
        Assert.Equal(2, controller.Snapshot.Generation);
        Assert.True(controller.Snapshot.IsAuthoritative);
    }

    [Fact]
    public async Task DisposalCancelsAndDrainsAcceptedRefresh()
    {
        var reader = new QueuedReader();
        _ = reader.EnqueuePending();
        var controller = new ProductDisplayTopologyController(reader);

        Task<ProductDisplayTopologyRefreshResult> refresh = controller.RefreshAsync();
        await controller.DisposeAsync();
        ProductDisplayTopologyRefreshResult result = await refresh;

        Assert.Equal(ProductDisplayTopologyRefreshStatus.Cancelled, result.Status);
        await controller.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => controller.RefreshAsync());
    }

    private static ProductDisplayTopologySample Sample() =>
        new(
            [new(Primary, true, true, true, true)],
            ActivePathCount: 1,
            BufferAttempts: 1);

    private static ProductDisplayTopologyReadResult Result(
        ProductDisplayTopologyReadStatus status) =>
        new(
            status,
            status is ProductDisplayTopologyReadStatus.Ready
                or ProductDisplayTopologyReadStatus.Degraded
                ? new[] { Primary }
                : Array.Empty<DisplayTopologyNode>(),
            status is ProductDisplayTopologyReadStatus.Ready
                or ProductDisplayTopologyReadStatus.Degraded
                ? 1
                : 0,
            status == ProductDisplayTopologyReadStatus.Ready ? 1 : 0,
            1);

    private sealed class FixedSource(ProductDisplayTopologySample sample)
        : IProductDisplayTopologySource
    {
        public ProductDisplayTopologySample Read(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sample;
        }
    }

    private sealed class UnsupportedSource : IProductDisplayTopologySource
    {
        public ProductDisplayTopologySample Read(
            CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException();
    }

    private sealed class QueuedReader : IProductDisplayTopologyReader
    {
        private readonly ConcurrentQueue<
            TaskCompletionSource<ProductDisplayTopologyReadResult>> results = new();

        public void Enqueue(ProductDisplayTopologyReadResult result)
        {
            TaskCompletionSource<ProductDisplayTopologyReadResult> pending =
                EnqueuePending();
            pending.SetResult(result);
        }

        public TaskCompletionSource<ProductDisplayTopologyReadResult>
            EnqueuePending()
        {
            var source = new TaskCompletionSource<ProductDisplayTopologyReadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            results.Enqueue(source);
            return source;
        }

        public async Task<ProductDisplayTopologyReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Assert.True(results.TryDequeue(out TaskCompletionSource<
                ProductDisplayTopologyReadResult>? source));
            return await source.Task.WaitAsync(cancellationToken);
        }
    }
}
