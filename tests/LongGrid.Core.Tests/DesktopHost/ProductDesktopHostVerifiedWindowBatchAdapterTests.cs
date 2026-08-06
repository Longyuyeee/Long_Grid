using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostVerifiedWindowBatchAdapterTests
{
    private static readonly ProductDesktopHostIdentity Host = new(
        Guid.Parse("523cb76f-f9d1-4419-901c-1d41ee14cd91"),
        11,
        120,
        240);

    [Fact]
    public void CaptureReturnsFreshBoundsForExactOwnedRegistry()
    {
        TestContext context = CreateContext();
        context.Inspector.Set(10, Observation(new(30, 40, 300, 200), marker: 50));

        ProductWorkspaceWindowCompositeCapture capture = context.Adapter.Capture(
            ["beta", "alpha"],
            context.Bridge.Snapshot.Generation);

        Assert.True(capture.Succeeded);
        Assert.NotNull(capture.Snapshot);
        Assert.True(context.Adapter.VerifyRestored(
            capture.Snapshot,
            context.Bridge.Snapshot.Generation));
        capture.Snapshot.Dispose();
    }

    [Fact]
    public void CaptureRejectsStaleGenerationSubsetAndDuplicateSet()
    {
        TestContext context = CreateContext();
        long generation = context.Bridge.Snapshot.Generation;

        Assert.False(context.Adapter.Capture(
            ["alpha", "beta"], generation - 1).Succeeded);
        Assert.False(context.Adapter.Capture(["alpha"], generation).Succeeded);
        Assert.False(context.Adapter.Capture(
            ["alpha", "alpha"], generation).Succeeded);
    }

    [Fact]
    public void CaptureRejectsOwnershipDriftWithoutMutatingWindows()
    {
        TestContext context = CreateContext();
        context.Inspector.Set(
            10,
            Observation(new(10, 20, 300, 200), processId: 999, marker: 50));

        ProductWorkspaceWindowCompositeCapture capture = context.Adapter.Capture(
            ["alpha", "beta"],
            context.Bridge.Snapshot.Generation);

        Assert.False(capture.Succeeded);
        Assert.Empty(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void ApplyUsesCanonicalOrderAndOnlyRegisteredHandles()
    {
        TestContext context = CreateContext();
        LayoutRecoveryWindowPlacement[] placements =
        [
            new("beta", new(500, 600, 220, 180)),
            new("alpha", new(100, 200, 240, 160)),
        ];

        bool applied = context.Adapter.Apply(
            placements,
            context.Bridge.Snapshot.Generation);

        Assert.True(applied);
        ProductDesktopHostWindowMutation[] batch = Assert.Single(
            context.Mutator.AppliedBatches);
        Assert.Equal(["alpha", "beta"], batch.Select(value => value.ContainerId));
        Assert.Equal([(nint)10, (nint)11], batch.Select(value => value.Handle));
        Assert.True(context.Adapter.Verify(
            placements,
            context.Bridge.Snapshot.Generation));
    }

    [Fact]
    public void ApplyRejectsInvalidPlacementWithoutCallingMutator()
    {
        TestContext context = CreateContext();

        bool applied = context.Adapter.Apply(
            [
                new("alpha", new(0, 0, 0, 100)),
                new("beta", new(0, 0, 100, 100)),
            ],
            context.Bridge.Snapshot.Generation);

        Assert.False(applied);
        Assert.Empty(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void ApplyPropagatesFiniteNativeBatchFailure()
    {
        TestContext context = CreateContext();
        context.Mutator.Succeeds = false;

        bool applied = context.Adapter.Apply(
            Placements(),
            context.Bridge.Snapshot.Generation);

        Assert.False(applied);
        Assert.Single(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void ApplyRejectsOwnershipChangeBeforeNativeBatch()
    {
        TestContext context = CreateContext();
        context.Inspector.Set(11, ProductDesktopHostWindowObservation.Missing);

        bool applied = context.Adapter.Apply(
            Placements(),
            context.Bridge.Snapshot.Generation);

        Assert.False(applied);
        Assert.Empty(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void VerifyRequiresExactObservedBounds()
    {
        TestContext context = CreateContext();

        bool verified = context.Adapter.Verify(
            Placements(),
            context.Bridge.Snapshot.Generation);

        Assert.False(verified);
        Assert.Empty(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void RestoreReappliesCapturedBoundsAndCanBeVerified()
    {
        TestContext context = CreateContext();
        long generation = context.Bridge.Snapshot.Generation;
        ProductWorkspaceWindowCompositeCapture capture = context.Adapter.Capture(
            ["alpha", "beta"],
            generation);
        Assert.True(capture.Succeeded);
        Assert.True(context.Adapter.Apply(Placements(), generation));

        bool restored = context.Adapter.Restore(capture.Snapshot!, generation);
        bool verified = context.Adapter.VerifyRestored(
            capture.Snapshot!,
            generation);

        Assert.True(restored);
        Assert.True(verified);
        Assert.Equal(2, context.Mutator.AppliedBatches.Count);
        Assert.Equal(
            new PixelRect(10, 20, 300, 200),
            context.Mutator.AppliedBatches[1]
                .Single(value => value.ContainerId == "alpha").Bounds);
        capture.Snapshot!.Dispose();
    }

    [Fact]
    public void RestoreRejectsDisposedSnapshotAndGenerationDrift()
    {
        TestContext context = CreateContext();
        long generation = context.Bridge.Snapshot.Generation;
        ProductWorkspaceWindowCompositeCapture capture = context.Adapter.Capture(
            ["alpha", "beta"],
            generation);
        capture.Snapshot!.Dispose();

        Assert.False(context.Adapter.Restore(capture.Snapshot, generation));
        Assert.False(context.Adapter.VerifyRestored(
            capture.Snapshot,
            generation + 1));
        Assert.Empty(context.Mutator.AppliedBatches);
    }

    [Fact]
    public void RestoreRejectsSnapshotFromAnotherLayer()
    {
        TestContext context = CreateContext();
        using var foreign = new ForeignSnapshot();

        Assert.False(context.Adapter.Restore(
            foreign,
            context.Bridge.Snapshot.Generation));
    }

    [Fact]
    public async Task BridgeSerializesRegistryMutationWithVerifiedBatch()
    {
        TestContext context = CreateContext();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        context.Mutator.BeforeApply = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };
        long generation = context.Bridge.Snapshot.Generation;

        Task<bool> apply = Task.Run(() => context.Adapter.Apply(
            Placements(),
            generation));
        Assert.True(await Task.Run(
            () => entered.Wait(TimeSpan.FromSeconds(5))));
        Task<bool> unregister = Task.Run(() => context.Bridge.Unregister("alpha", 1));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(unregister.IsCompleted);
        release.Set();

        Assert.True(await apply);
        Assert.True(await unregister);
    }

    [Fact]
    public void WindowsMutatorUsesDeferredNonActivatingNonZOrderBatch()
    {
        var api = new FakeDeferredWindowPositionApi();
        var mutator = new WindowsProductDesktopHostWindowBatchMutator(api);

        bool applied = mutator.Apply(
        [
            new("alpha", 10, new(1, 2, 300, 200)),
            new("beta", 11, new(3, 4, 320, 240)),
        ]);

        Assert.True(applied);
        Assert.Equal(2, api.BeginCount);
        Assert.Equal(2, api.Deferred.Count);
        Assert.All(api.Deferred, operation => Assert.Equal(0x0614u, operation.Flags));
        Assert.Equal((nint)102, api.EndedHandle);
    }

    [Fact]
    public void WindowsMutatorRejectsUnsupportedInvalidAndBeginFailure()
    {
        var unsupported = new FakeDeferredWindowPositionApi
        {
            IsSupported = false,
        };
        var invalid = new WindowsProductDesktopHostWindowBatchMutator(unsupported);
        Assert.False(invalid.Apply(
            [new("alpha", 10, new(1, 2, 300, 200))]));

        var beginFailure = new FakeDeferredWindowPositionApi
        {
            BeginResult = nint.Zero,
        };
        var mutator = new WindowsProductDesktopHostWindowBatchMutator(beginFailure);
        Assert.False(mutator.Apply(
            [new("alpha", 10, new(1, 2, 300, 200))]));
        Assert.Empty(beginFailure.Deferred);
        Assert.Equal(nint.Zero, beginFailure.EndedHandle);
    }

    [Fact]
    public void WindowsMutatorStopsWithoutEndWhenDeferFails()
    {
        var api = new FakeDeferredWindowPositionApi
        {
            FailDeferAt = 2,
        };
        var mutator = new WindowsProductDesktopHostWindowBatchMutator(api);

        bool applied = mutator.Apply(
        [
            new("alpha", 10, new(1, 2, 300, 200)),
            new("beta", 11, new(3, 4, 320, 240)),
        ]);

        Assert.False(applied);
        Assert.Equal(2, api.Deferred.Count);
        Assert.Equal(nint.Zero, api.EndedHandle);
    }

    [Fact]
    public void WindowsMutatorPropagatesEndFailure()
    {
        var api = new FakeDeferredWindowPositionApi
        {
            EndResult = false,
        };
        var mutator = new WindowsProductDesktopHostWindowBatchMutator(api);

        Assert.False(mutator.Apply(
            [new("alpha", 10, new(1, 2, 300, 200))]));
        Assert.NotEqual(nint.Zero, api.EndedHandle);
    }

    private static LayoutRecoveryWindowPlacement[] Placements() =>
    [
        new("alpha", new(100, 200, 240, 160)),
        new("beta", new(500, 600, 220, 180)),
    ];

    private static TestContext CreateContext()
    {
        var inspector = new FakeInspector();
        inspector.Set(10, Observation(new(10, 20, 300, 200), marker: 50));
        inspector.Set(11, Observation(new(40, 50, 320, 240), marker: 51));
        var bridge = new ProductDesktopHostWindowBridge(inspector);
        bridge.Connect(Host);
        Assert.True(bridge.Register(Claim("alpha", 10, 50)).IsRegistered);
        Assert.True(bridge.Register(Claim("beta", 11, 51)).IsRegistered);
        var mutator = new FakeMutator(inspector);
        return new(
            bridge,
            inspector,
            mutator,
            new ProductDesktopHostVerifiedWindowBatchAdapter(bridge, mutator));
    }

    private static ProductDesktopHostWindowClaim Claim(
        string containerId,
        int handle,
        int marker) =>
        new(containerId, Host, 1, handle, marker);

    private static ProductDesktopHostWindowObservation Observation(
        PixelRect bounds,
        uint processId = 120,
        uint threadId = 240,
        int marker = 50) =>
        new(true, processId, threadId, marker, bounds);

    private sealed record TestContext(
        ProductDesktopHostWindowBridge Bridge,
        FakeInspector Inspector,
        FakeMutator Mutator,
        ProductDesktopHostVerifiedWindowBatchAdapter Adapter);

    private sealed class FakeInspector : IProductDesktopHostWindowInspector
    {
        private readonly object sync = new();
        private readonly Dictionary<nint, ProductDesktopHostWindowObservation>
            observations = new();

        public ProductDesktopHostWindowObservation Inspect(nint handle)
        {
            lock (sync)
            {
                return observations.TryGetValue(handle, out var observation)
                    ? observation
                    : ProductDesktopHostWindowObservation.Missing;
            }
        }

        public void Set(
            nint handle,
            ProductDesktopHostWindowObservation observation)
        {
            lock (sync)
            {
                observations[handle] = observation;
            }
        }
    }

    private sealed class FakeMutator(FakeInspector inspector)
        : IProductDesktopHostWindowBatchMutator
    {
        public List<ProductDesktopHostWindowMutation[]> AppliedBatches { get; } = [];

        public bool Succeeds { get; set; } = true;

        public Action? BeforeApply { get; set; }

        public bool Apply(IReadOnlyList<ProductDesktopHostWindowMutation> windows)
        {
            ProductDesktopHostWindowMutation[] copy = windows.ToArray();
            AppliedBatches.Add(copy);
            BeforeApply?.Invoke();
            if (!Succeeds)
            {
                return false;
            }

            foreach (ProductDesktopHostWindowMutation window in copy)
            {
                inspector.Set(
                    window.Handle,
                    Observation(
                        window.Bounds,
                        marker: window.ContainerId == "alpha" ? 50 : 51));
            }

            return true;
        }
    }

    private sealed class ForeignSnapshot : IProductWorkspaceWindowCompositeSnapshot
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeDeferredWindowPositionApi
        : IWindowsDeferredWindowPositionApi
    {
        public bool IsSupported { get; set; } = true;

        public nint BeginResult { get; set; } = 100;

        public int BeginCount { get; private set; }

        public int FailDeferAt { get; set; }

        public bool EndResult { get; set; } = true;

        public nint EndedHandle { get; private set; }

        public List<(nint Window, PixelRect Bounds, uint Flags)> Deferred { get; } = [];

        public nint Begin(int windowCount)
        {
            BeginCount = windowCount;
            return BeginResult;
        }

        public nint Defer(
            nint deferredWindowPosition,
            nint window,
            PixelRect bounds,
            uint flags)
        {
            Deferred.Add((window, bounds, flags));
            return Deferred.Count == FailDeferAt
                ? nint.Zero
                : deferredWindowPosition + 1;
        }

        public bool End(nint deferredWindowPosition)
        {
            EndedHandle = deferredWindowPosition;
            return EndResult;
        }
    }
}
