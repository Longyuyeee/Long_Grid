using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopHost;
using LongGrid.ThumbnailWorker;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class ProductDesktopThumbnailRequestControllerTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task DisabledPolicyCreatesZeroWorkerAndZeroRequest()
    {
        int factoryCalls = 0;
        using var controller = new ProductDesktopThumbnailRequestController(() =>
        {
            factoryCalls++;
            throw new InvalidOperationException("Must remain lazy.");
        });

        ProductDesktopThumbnailRefreshResult actual = await controller.RefreshAsync(
            enabled: false,
            [new("item:1", "C:\\private\\photo.bmp")],
            pixelSize: 64,
            themeKey: "light");

        Assert.False(actual.Enabled);
        Assert.Equal(0, actual.WorkerRequestCount);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task QueueBoundsTwentyRealFilesToTwelveRequests()
    {
        using var sandbox = new TemporaryThumbnailSandbox();
        ProductDesktopThumbnailCandidate[] candidates = Enumerable.Range(1, 20)
            .Select(index => new ProductDesktopThumbnailCandidate(
                $"item:{index}",
                sandbox.WriteBitmap($"image-{index}.bmp", index + 1, 2)))
            .ToArray();
        var runtime = new RecordingRuntime();
        using var controller = new ProductDesktopThumbnailRequestController(
            () => runtime);

        ProductDesktopThumbnailRefreshResult actual = await controller.RefreshAsync(
            enabled: true,
            candidates,
            pixelSize: 64,
            themeKey: "dark");

        Assert.Equal(12, actual.CandidateCount);
        Assert.Equal(12, actual.WorkerRequestCount);
        Assert.Equal(12, runtime.ExtractCalls);
        Assert.All(actual.Results, result => Assert.Equal(
            ProductDesktopThumbnailStatus.ReadyThumbnail,
            result.Status));
    }

    [Fact]
    public async Task RealFileVersionAndThemeInvalidateBoundedCache()
    {
        using var sandbox = new TemporaryThumbnailSandbox();
        string path = sandbox.WriteBitmap("cache.bmp", 2, 2);
        var runtime = new RecordingRuntime();
        using var controller = new ProductDesktopThumbnailRequestController(
            () => runtime);
        ProductDesktopThumbnailCandidate[] candidate = [new("item:1", path)];

        ProductDesktopThumbnailRefreshResult first = await controller.RefreshAsync(
            true, candidate, 64, "light");
        ProductDesktopThumbnailRefreshResult cached = await controller.RefreshAsync(
            true, candidate, 64, "light");
        _ = sandbox.WriteBitmap("cache.bmp", 3, 2);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
        ProductDesktopThumbnailRefreshResult modified = await controller.RefreshAsync(
            true, candidate, 64, "light");
        ProductDesktopThumbnailRefreshResult themed = await controller.RefreshAsync(
            true, candidate, 64, "dark");

        Assert.Equal(1, first.WorkerRequestCount);
        Assert.Equal(1, cached.CacheHitCount);
        Assert.Equal(0, cached.WorkerRequestCount);
        Assert.Equal(1, modified.WorkerRequestCount);
        Assert.Equal(1, themed.WorkerRequestCount);
        Assert.Equal(3, runtime.ExtractCalls);
    }

    [Fact]
    public async Task RealRestrictedWorkerReturnsPixelsOrFiniteFallbackAndRecoversFaults()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            return;
        }

        using var sandbox = new TemporaryThumbnailSandbox();
        string path = sandbox.WriteBitmap("real-worker.bmp", 8, 8);
        RestrictedThumbnailWorkerRuntime runtime =
            RestrictedThumbnailWorkerRuntime.Start();
        RestrictedThumbnailExtractionResult extracted;
        RestrictedThumbnailExtractionResult timeout;
        RestrictedThumbnailExtractionResult exited;
        try
        {
            extracted = await runtime.ExtractAsync(
                path,
                pixelSize: 64,
                TimeSpan.FromMilliseconds(250));
            timeout = await runtime.ExecuteEvidenceFaultAsync(
                RestrictedThumbnailEvidenceFault.Hang,
                TimeSpan.FromMilliseconds(100));
            exited = await runtime.ExecuteEvidenceFaultAsync(
                RestrictedThumbnailEvidenceFault.Exit,
                TimeSpan.FromMilliseconds(250));

            Assert.True(
                extracted.Success && extracted.Frame is not null
                    || !extracted.Success && extracted.Frame is null);
            if (extracted.Frame is { } frame)
            {
                Assert.Equal(frame.Stride * frame.Height, frame.Bgra32Pixels.Length);
            }
            Assert.True(timeout.TimedOut);
            Assert.True(timeout.WorkerExited);
            Assert.True(exited.WorkerExited);
        }
        finally
        {
            runtime.Dispose();
        }

        Assert.True(runtime.OwnedProfileDeletionConfirmed);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf005b1RealRestrictedWorkerEvidence",
            Expected = new
            {
                Extraction = "ReadyPixelsOrFiniteFallback",
                TimeoutMillisecondsAtMost = 500,
                TimeoutKillsWorker = true,
                ExplicitExitObserved = true,
                OwnedProfileDeleted = true,
            },
            Actual = new
            {
                Extraction = extracted.Success
                    ? "ReadyPixels"
                    : $"FiniteFallback:0x{extracted.HResult:X8}",
                extractionPixels = extracted.Frame?.Bgra32Pixels.Length ?? 0,
                extracted.TimedOut,
                extracted.WorkerExited,
                extracted.ProtocolError,
                ExtractionRoundTripMilliseconds =
                    extracted.RoundTripMilliseconds,
                TimeoutRoundTripMilliseconds =
                    timeout.RoundTripMilliseconds,
                TimeoutKillsWorker = timeout.WorkerExited,
                ExplicitExitObserved = exited.WorkerExited,
                OwnedProfileDeleted = runtime.OwnedProfileDeletionConfirmed,
            },
            Difference = timeout.RoundTripMilliseconds <= 500
                ? "None"
                : "TimeoutExceededBudget",
        }));
        Assert.True(timeout.RoundTripMilliseconds <= 500);
    }

    [Fact]
    public async Task RealProductQueueRequestsOwnedBitmapThenDisablesAndCleansProfile()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            return;
        }

        using var sandbox = new TemporaryThumbnailSandbox();
        string path = sandbox.WriteBitmap("product-queue.bmp", 8, 8);
        using var controller = new ProductDesktopThumbnailRequestController();

        ProductDesktopThumbnailRefreshResult enabled =
            await controller.RefreshAsync(
                true,
                [new("item:1", path)],
                64,
                "light");
        ProductDesktopThumbnailRefreshResult disabled =
            await controller.RefreshAsync(
                false,
                [new("item:1", path)],
                64,
                "light");

        ProductDesktopThumbnailResult result = Assert.Single(enabled.Results);
        Assert.Contains(
            result.Status,
            new[]
            {
                ProductDesktopThumbnailStatus.ReadyThumbnail,
                ProductDesktopThumbnailStatus.FailedFallback,
            });
        Assert.Equal(1, enabled.WorkerRequestCount);
        Assert.Equal(0, disabled.WorkerRequestCount);
        Assert.False(disabled.WorkerStarted);
        Assert.True(controller.OwnedProfileDeletionConfirmed);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf005b1RealProductQueueEvidence",
            Expected = new
            {
                VisibleRequests = 1,
                TerminalState = "ReadyThumbnailOrFailedFallback",
                DisabledRequests = 0,
                OwnedProfileDeleted = true,
            },
            Actual = new
            {
                VisibleRequests = enabled.WorkerRequestCount,
                TerminalState = result.Status.ToString(),
                DisabledRequests = disabled.WorkerRequestCount,
                OwnedProfileDeleted =
                    controller.OwnedProfileDeletionConfirmed,
            },
            Difference = "None",
        }));
    }

    [Fact]
    public void AuthoritativeWorkspaceCandidatesAreImageOnlyBoundedAndPathAnonymous()
    {
        string root = Path.Combine(Path.GetTempPath(), "LongGrid.Pf005b2");
        ProductItemReferenceState[] items = Enumerable.Range(1, 14)
            .Select(index => ProductItemReferenceState.CreateResolved(
                $"persisted-{index}",
                new DesktopCatalogEntry(
                    new DesktopItemIdentity(
                        "filesystem",
                        Path.Combine(root, $"private-{index}.png")),
                    "user-desktop",
                    $"图片 {index}",
                    DesktopItemKind.File)))
            .Append(ProductItemReferenceState.CreateResolved(
                "persisted-text",
                new DesktopCatalogEntry(
                    new DesktopItemIdentity(
                        "filesystem",
                        Path.Combine(root, "private.txt")),
                    "user-desktop",
                    "文本",
                    DesktopItemKind.File)))
            .ToArray();
        ProductWorkspaceState state = new()
        {
            ProfileId = "profile",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-a",
                    Name = "图片",
                    Appearance = new() { Color = "#2457D6" },
                    Placement = new() { DisplayKey = "display-primary" },
                    Items = items,
                },
            ],
        };

        IReadOnlyList<ProductDesktopThumbnailCandidate> actual =
            ProductDesktopThumbnailCandidateBuilder.Build(state);

        Assert.Equal(12, actual.Count);
        Assert.All(actual, candidate =>
        {
            Assert.EndsWith(".png", candidate.TargetPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("thumbnail:", candidate.AnonymousItemKey,
                StringComparison.Ordinal);
            Assert.DoesNotContain("private", candidate.AnonymousItemKey,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(2, 1, 7, 7, 9, 9, true, true)]
    [InlineData(1, 1, 8, 7, 9, 9, true, true)]
    [InlineData(1, 1, 7, 7, 10, 9, true, true)]
    [InlineData(1, 1, 7, 7, 9, 9, true, false)]
    public void StaleRefreshFactsCannotPublish(
        long requestedGeneration,
        long currentGeneration,
        long requestedRevision,
        long currentRevision,
        long requestedTopology,
        long currentTopology,
        bool requestedEnabled,
        bool currentEnabled)
    {
        Assert.False(ProductDesktopThumbnailRefreshAdmission.CanPublish(
            requestedGeneration,
            currentGeneration,
            requestedRevision,
            currentRevision,
            requestedTopology,
            currentTopology,
            requestedEnabled,
            currentEnabled));
        Assert.True(ProductDesktopThumbnailRefreshAdmission.CanPublish(
            1, 1, 7, 7, 9, 9, true, true));
    }

    private sealed class RecordingRuntime : IProductRestrictedThumbnailRuntime
    {
        public int ExtractCalls { get; private set; }

        public RestrictedThumbnailWorkerRuntimeSnapshot Snapshot { get; } = new(
            IsStarted: true,
            WorkerProcessCount: 1,
            ActiveOwnedProfileCount: 1,
            IsZeroCapabilityAppContainer: true,
            UsesKillOnJobClose: true);

        public bool OwnedProfileDeletionConfirmed { get; private set; }

        public Task<RestrictedThumbnailExtractionResult> ExtractAsync(
            string path,
            int pixelSize,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ExtractCalls++;
            byte[] pixels = new byte[16];
            return Task.FromResult(new RestrictedThumbnailExtractionResult(
                Success: true,
                TimedOut: false,
                WorkerExited: false,
                ProtocolError: false,
                HResult: 0,
                new RestrictedThumbnailPixelFrame(2, 2, 8, pixels),
                RoundTripMilliseconds: 1));
        }

        public void Dispose() => OwnedProfileDeletionConfirmed = true;
    }

    private sealed class TemporaryThumbnailSandbox : IDisposable
    {
        private readonly string root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"LongGrid-PF005B1-{Guid.NewGuid():N}")).FullName;

        internal string WriteBitmap(string name, int width, int height)
        {
            string path = Path.Combine(root, name);
            int stride = checked(((width * 3) + 3) & ~3);
            int pixelBytes = checked(stride * height);
            const int pixelOffset = 54;
            using FileStream stream = new(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0x4D42);
            writer.Write(pixelOffset + pixelBytes);
            writer.Write(0);
            writer.Write(pixelOffset);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height);
            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write(0);
            writer.Write(pixelBytes);
            writer.Write(2_835);
            writer.Write(2_835);
            writer.Write(0);
            writer.Write(0);
            byte[] row = new byte[stride];
            for (int x = 0; x < width; x++)
            {
                row[x * 3] = (byte)(x % 251);
                row[(x * 3) + 1] = (byte)((x * 3) % 251);
                row[(x * 3) + 2] = (byte)((x * 7) % 251);
            }
            for (int y = 0; y < height; y++)
            {
                writer.Write(row);
            }
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
