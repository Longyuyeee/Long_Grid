using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class ProductDesktopExplorerReferenceDropTests(
    ITestOutputHelper output)
{
    [Fact]
    public async Task RealStaHwndRegistersOleDropTargetAndCommitsLinkOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "真实拖入.txt");
            File.WriteAllText(file, "LongGrid PF-007A2 native OLE evidence");
            string beforePath = Path.GetFullPath(file);
            byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(file));
            IReadOnlyList<DesktopCatalogEntry> catalog = DesktopCatalog.Build(
                [new("test-catalog", file, IsDirectory: false)]);
            ProductWorkspaceState state = State();
            var workflow = new FakeWorkflow();
            await using var saves = new ProductWorkspaceSaveController(
                workflow,
                new ImmediateScheduler(),
                TimeSpan.FromMilliseconds(1));
            var coordinator = new ProductWorkspaceCommitCoordinator(saves);
            var completion = new TaskCompletionSource<NativeDropEvidence>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    ProductDesktopHostDisplayProjection display =
                        ProductDesktopHostDisplayProjection.Create(
                            "display-primary",
                            new(100, 100, 800, 600),
                            96,
                            [ProductDesktopHostReadOnlyProjection.Create(
                                "container-1",
                                "工作",
                                [],
                                "#2457D6",
                                0.82,
                                isCollapsed: false,
                                xDip: 20,
                                yDip: 20,
                                widthDip: 360,
                                heightDip: 240)]);
                    using WindowsProductDesktopHostReadOnlySurface surface =
                        WindowsProductDesktopHostReadOnlySurface.Create(
                            display,
                            new nint(7007));
                    ProductWorkspaceResolvedReferenceBatchCommitResult? committed =
                        null;
                    int callbackCount = 0;
                    surface.BindExplorerReferenceDrop((dataObject, containerId) =>
                    {
                        callbackCount++;
                        ProductDesktopExplorerReferenceDropPreparation prepared =
                            ProductDesktopExplorerReferenceDropAdapter.Prepare(
                                dataObject,
                                state,
                                expectedEditRevision: 0,
                                catalogGeneration: 11,
                                catalog,
                                containerId);
                        if (!prepared.IsAccepted)
                        {
                            return false;
                        }

                        committed = coordinator.CommitResolvedReferenceBatch(
                            state,
                            11,
                            catalog,
                            prepared.CommitRequest!);
                        return committed.IsAccepted;
                    });
                    var data = new System.Windows.DataObject(
                        System.Windows.DataFormats.FileDrop,
                        new[] { file });
                    var unsupported = new System.Windows.DataObject(
                        System.Windows.DataFormats.UnicodeText,
                        "not-a-file-drop");
                    uint unsupportedEffect =
                        surface.DispatchExplorerDragEnterForEvidence(
                            unsupported,
                            screenX: 130,
                            screenY: 130,
                            allowedEffects: 7);
                    uint copyOnlyEffect =
                        surface.DispatchExplorerDragEnterForEvidence(
                            data,
                            screenX: 130,
                            screenY: 130,
                            allowedEffects: 1);
                    Assert.True(surface.ApplyHidden());
                    uint hiddenEffect =
                        surface.DispatchExplorerDragEnterForEvidence(
                            data,
                            screenX: 130,
                            screenY: 130,
                            allowedEffects: 7);
                    Assert.True(surface.ApplyPassive());
                    uint enterEffect = surface.DispatchExplorerDragEnterForEvidence(
                        data,
                        screenX: 130,
                        screenY: 130,
                        allowedEffects: 7);
                    int callbacksAfterEnter = callbackCount;
                    uint dropEffect = surface.DispatchExplorerDropForEvidence(
                        data,
                        screenX: 130,
                        screenY: 130,
                        allowedEffects: 7);
                    completion.TrySetResult(new(
                        surface.Handle != nint.Zero,
                        surface.ExplorerDropTargetRegistered,
                        unsupportedEffect,
                        copyOnlyEffect,
                        hiddenEffect,
                        enterEffect,
                        dropEffect,
                        callbacksAfterEnter,
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
                Name = "LongGrid.PF007A2.RealOleSta",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            NativeDropEvidence actual = await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            _ = await saves.CompleteAsync();

            Assert.True(actual.RealWindow);
            Assert.True(actual.DropTargetRegistered);
            Assert.Equal(WindowsProductDesktopHostDropTarget.EffectNone,
                actual.UnsupportedEffect);
            Assert.Equal(WindowsProductDesktopHostDropTarget.EffectNone,
                actual.CopyOnlyEffect);
            Assert.Equal(WindowsProductDesktopHostDropTarget.EffectNone,
                actual.HiddenEffect);
            Assert.Equal(WindowsProductDesktopHostDropTarget.EffectLink,
                actual.EnterEffect);
            Assert.Equal(WindowsProductDesktopHostDropTarget.EffectLink,
                actual.DropEffect);
            Assert.Equal(0, actual.CallbacksAfterEnter);
            Assert.Equal(1, actual.CallbackCount);
            Assert.True(actual.Commit?.IsAccepted);
            Assert.Single(actual.Commit!.State!.Containers[0].Items);
            Assert.Equal(1, workflow.SaveCalls);
            Assert.Equal(beforePath, Path.GetFullPath(file));
            Assert.Equal(beforeHash, SHA256.HashData(File.ReadAllBytes(file)));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Purpose = "Pf007A2RealStaHwndOleDropTarget",
                Expected = new
                {
                    Registered = true,
                    HoverEffect = "Link",
                    DropEffect = "Link",
                    UnsupportedEffect = "None",
                    CopyOnlyEffect = "None",
                    HiddenEffect = "None",
                    CallbackCount = 1,
                    SaveCalls = 1,
                    SourceFilesChanged = false,
                },
                Actual = new
                {
                    actual.DropTargetRegistered,
                    actual.UnsupportedEffect,
                    actual.CopyOnlyEffect,
                    actual.HiddenEffect,
                    HoverEffect = actual.EnterEffect,
                    DropEffect = actual.DropEffect,
                    actual.CallbackCount,
                    SaveCalls = workflow.SaveCalls,
                    SourceFilesChanged = !string.Equals(
                        beforePath,
                        Path.GetFullPath(file),
                        StringComparison.OrdinalIgnoreCase)
                        || !beforeHash.SequenceEqual(
                            SHA256.HashData(File.ReadAllBytes(file))),
                },
                Difference = "None",
                Outcome = "Pass",
                EvidenceBoundary =
                    "Real STA, real HWND, native RegisterDragDrop, real CF_HDROP HGLOBAL and formal atomic reference commit; physical Explorer pointer drag remains M1 evidence.",
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    private sealed record NativeDropEvidence(
        bool RealWindow,
        bool DropTargetRegistered,
        uint UnsupportedEffect,
        uint CopyOnlyEffect,
        uint HiddenEffect,
        uint EnterEffect,
        uint DropEffect,
        int CallbacksAfterEnter,
        int CallbackCount,
        ProductWorkspaceResolvedReferenceBatchCommitResult? Commit);

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
