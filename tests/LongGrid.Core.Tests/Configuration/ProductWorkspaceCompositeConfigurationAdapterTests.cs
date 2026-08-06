using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceCompositeConfigurationAdapterTests
{
    [Fact]
    public void ProductionBindingStateExchangesOnlyTheExactCurrentBinding()
    {
        ProductWorkspaceWindowCompositeBinding before = Binding(State("before"));
        ProductWorkspaceWindowCompositeBinding after = Binding(
            State("after"),
            revision: 2);
        var state = new ProductWorkspaceCompositeBindingState(before);

        Assert.True(state.Matches(before));
        Assert.False(state.TryExchange(after, before));
        Assert.True(state.TryExchange(before, after));
        Assert.Equal(after, state.Current);
        Assert.False(state.TryExchange(before, after));
    }

    [Fact]
    public void ProductionBindingStateRejectsInvalidBindings()
    {
        ProductWorkspaceWindowCompositeBinding valid = Binding(State("before"));

        Assert.Throws<ArgumentException>(() =>
            new ProductWorkspaceCompositeBindingState(
                valid with { DesktopHostInstanceId = Guid.Empty }));
        var state = new ProductWorkspaceCompositeBindingState(valid);
        Assert.False(state.TryExchange(
            valid,
            valid with { ConfigurationFingerprint = "invalid" }));
        Assert.Equal(valid, state.Current);
    }

    [Fact]
    public async Task CaptureRequiresValidatedPrimaryAndCreatesRestorableSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState state = State("before");
        await store.SaveAsync(Document(state));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, state);

        ProductWorkspaceWindowCompositeCapture capture = adapter.Capture();

        Assert.True(capture.Succeeded);
        Assert.NotNull(capture.Snapshot);
        Assert.True(adapter.VerifyRestored(capture.Snapshot, Binding(state)));
        capture.Snapshot.Dispose();
    }

    [Fact]
    public async Task CaptureRejectsMissingRecoveredAndSafeModeStorage()
    {
        using var missingDirectory = new TemporaryDirectory(create: false);
        ProductWorkspaceCompositeConfigurationAdapter missing = CreateAdapter(
            new ProductConfigurationStore(missingDirectory.Path),
            State("missing"));
        Assert.False(missing.Capture().Succeeded);

        using var recoveredDirectory = new TemporaryDirectory();
        var recoveredStore = new ProductConfigurationStore(recoveredDirectory.Path);
        await recoveredStore.SaveAsync(Document(State("backup")));
        await recoveredStore.SaveAsync(Document(State("primary")));
        await File.WriteAllTextAsync(recoveredStore.PrimaryPath, "{ damaged");
        ProductWorkspaceCompositeConfigurationAdapter recovered =
            CreateAdapter(recoveredStore, State("primary"));
        Assert.False(recovered.Capture().Succeeded);

        File.Delete(recoveredStore.BackupPath);
        ProductWorkspaceCompositeConfigurationAdapter safeMode =
            CreateAdapter(recoveredStore, State("primary"));
        Assert.False(safeMode.Capture().Succeeded);
    }

    [Fact]
    public async Task ApplyUsesFingerprintCompareExchangeAndRereadVerification()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        ProductWorkspaceState after = State("after");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;

        bool applied = adapter.Apply(after, Binding(after, revision: 2));

        Assert.True(applied);
        Assert.True(adapter.Verify(after, Binding(after, revision: 2)));
        Assert.Equal(
            "after",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task ApplyRejectsBindingFingerprintMismatchWithoutPublishing()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        ProductWorkspaceState after = State("after");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;

        bool applied = adapter.Apply(after, Binding(before, revision: 2));

        Assert.False(applied);
        Assert.Equal(
            "before",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task ApplyRejectsExternalWriteBetweenCaptureAndPublish()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;
        await store.SaveAsync(Document(State("external")));

        bool applied = adapter.Apply(
            State("after"),
            Binding(State("after"), revision: 2));

        Assert.False(applied);
        Assert.Equal(
            "external",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task ApplyRejectsInvalidStateAndDisposedCapture()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        ProductWorkspaceWindowCompositeCapture capture = adapter.Capture();
        capture.Snapshot!.Dispose();
        ProductWorkspaceState invalid = State("invalid") with
        {
            Containers = null!,
        };

        Assert.False(adapter.Apply(
            State("after"),
            Binding(State("after"), revision: 2)));
        Assert.False(adapter.Apply(
            invalid,
            Binding(State("after"), revision: 2)));
        Assert.Equal(
            "before",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task RestoreReplacesOnlyAdapterPublishedVersion()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        ProductWorkspaceState after = State("after");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;
        Assert.True(adapter.Apply(after, Binding(after, revision: 2)));

        bool restored = adapter.Restore(snapshot, Binding(before));

        Assert.True(restored);
        Assert.True(adapter.VerifyRestored(snapshot, Binding(before)));
        Assert.Equal(
            "before",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task RestoreDoesNotOverwriteExternalChange()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        ProductWorkspaceState after = State("after");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;
        Assert.True(adapter.Apply(after, Binding(after, revision: 2)));
        await store.SaveAsync(Document(State("external")));

        bool restored = adapter.Restore(snapshot, Binding(before));

        Assert.False(restored);
        Assert.Equal(
            "external",
            (await store.LoadAsync()).Document?.ProfileId);
    }

    [Fact]
    public async Task RestoreRejectsForeignSnapshotDisposedSnapshotAndWrongBinding()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(directory.Path);
        ProductWorkspaceState before = State("before");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter first =
            CreateAdapter(store, before);
        ProductWorkspaceCompositeConfigurationAdapter second =
            CreateAdapter(store, before);
        ProductWorkspaceWindowCompositeCapture firstCapture = first.Capture();
        ProductWorkspaceWindowCompositeCapture secondCapture = second.Capture();

        Assert.False(first.Restore(secondCapture.Snapshot!, Binding(before)));
        Assert.False(first.Restore(firstCapture.Snapshot!, Binding(State("other"))));
        firstCapture.Snapshot!.Dispose();
        Assert.False(first.Restore(firstCapture.Snapshot, Binding(before)));

        secondCapture.Snapshot!.Dispose();
    }

    [Fact]
    public async Task WriteLeaseContentionIsFiniteAndDoesNotPublish()
    {
        using var directory = new TemporaryDirectory();
        var store = new ProductConfigurationStore(
            directory.Path,
            writeLeaseTimeout: TimeSpan.FromMilliseconds(50),
            writeLeaseRetryDelay: TimeSpan.FromMilliseconds(5));
        ProductWorkspaceState before = State("before");
        await store.SaveAsync(Document(before));
        ProductWorkspaceCompositeConfigurationAdapter adapter =
            CreateAdapter(store, before);
        using IProductWorkspaceWindowCompositeSnapshot snapshot =
            adapter.Capture().Snapshot!;
        await using FileStream lease = AcquireLease(store.WriteLeasePath);

        bool applied = adapter.Apply(
            State("after"),
            Binding(State("after"), revision: 2));

        Assert.False(applied);
        Assert.Equal(
            "before",
            (await new ProductConfigurationStore(directory.Path).LoadAsync())
                .Document?.ProfileId);
    }

    [Fact]
    public async Task ConcurrentCompareExchangeAllowsOnlyOneWinner()
    {
        using var directory = new TemporaryDirectory();
        var first = new ProductConfigurationStore(directory.Path);
        var second = new ProductConfigurationStore(directory.Path);
        ProductConfigurationDocument before = Document(State("before"));
        await first.SaveAsync(before);
        string expected = ProductWorkspaceConfigurationFingerprint.Compute(before);

        ProductConfigurationCompareExchangeStatus[] results = await Task.WhenAll(
            first.CompareExchangePrimaryAsync(Document(State("first")), expected),
            second.CompareExchangePrimaryAsync(Document(State("second")), expected));

        Assert.Single(
            results,
            result => result == ProductConfigurationCompareExchangeStatus.Saved);
        Assert.Single(
            results,
            result => result == ProductConfigurationCompareExchangeStatus.Conflict);
        Assert.True(
            (await first.LoadAsync()).Document?.ProfileId
                is "first" or "second");
    }

    [Fact]
    public async Task CompareExchangeRejectsUnavailablePrimaryAndMalformedFingerprint()
    {
        using var directory = new TemporaryDirectory(create: false);
        var store = new ProductConfigurationStore(directory.Path);
        ProductConfigurationDocument replacement = Document(State("after"));

        ProductConfigurationCompareExchangeStatus unavailable =
            await store.CompareExchangePrimaryAsync(
                replacement,
                new string('A', 64));

        Assert.Equal(
            ProductConfigurationCompareExchangeStatus.PrimaryUnavailable,
            unavailable);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CompareExchangePrimaryAsync(replacement, "not-a-fingerprint"));
    }

    private static ProductWorkspaceState State(string profileId) =>
        new()
        {
            ProfileId = profileId,
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = [],
                },
            ],
        };

    private static ProductConfigurationDocument Document(
        ProductWorkspaceState state)
    {
        ProductWorkspaceProjectionResult result =
            ProductWorkspaceConfigurationProjector.Project(state);
        Assert.True(result.IsSuccess);
        return result.Document!;
    }

    private static ProductWorkspaceCompositeConfigurationAdapter CreateAdapter(
        ProductConfigurationStore store,
        ProductWorkspaceState state) =>
        new(store, new BindingExchange(Binding(state)));

    private static ProductWorkspaceWindowCompositeBinding Binding(
        ProductWorkspaceState state,
        long revision = 1) =>
        new(
            TopologyGeneration: 1,
            EditRevision: revision,
            WindowRegistryGeneration: 1,
            DesktopHostInstanceId: Guid.Parse(
                "65e8144f-28c3-45f5-a691-0df949fa1b25"),
            DesktopHostGeneration: 1,
            ConfigurationFingerprint:
                ProductWorkspaceConfigurationFingerprint.Compute(
                    Document(state)));

    private sealed class BindingExchange(
        ProductWorkspaceWindowCompositeBinding current)
        : IProductWorkspaceCompositeBindingExchange
    {
        private readonly object sync = new();
        private ProductWorkspaceWindowCompositeBinding current = current;

        public bool Matches(ProductWorkspaceWindowCompositeBinding expected)
        {
            lock (sync)
            {
                return current == expected;
            }
        }

        public bool TryExchange(
            ProductWorkspaceWindowCompositeBinding expected,
            ProductWorkspaceWindowCompositeBinding replacement)
        {
            lock (sync)
            {
                if (current != expected)
                {
                    return false;
                }

                current = replacement;
                return true;
            }
        }
    }

    private static FileStream AcquireLease(string path) =>
        new(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "LongGrid.CompositeConfiguration.Tests",
                Guid.NewGuid().ToString("N"));
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
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
