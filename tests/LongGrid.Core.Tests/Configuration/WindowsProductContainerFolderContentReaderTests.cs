using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Channels;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class WindowsProductContainerFolderContentReaderTests
{
    [Theory]
    [InlineData(ProductContainerFolderSortMode.FoldersFirstNameAscending,
        "m-folder,a-file.txt,z-file.txt")]
    [InlineData(ProductContainerFolderSortMode.NameAscending,
        "a-file.txt,m-folder,z-file.txt")]
    [InlineData(ProductContainerFolderSortMode.NameDescending,
        "z-file.txt,m-folder,a-file.txt")]
    public void RealDirectoryUsesPersistedBoundFolderSortWithoutChangingFiles(
        ProductContainerFolderSortMode sortMode,
        string expectedOrder)
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound-sort");
        Directory.CreateDirectory(Path.Combine(bound, "m-folder"));
        File.WriteAllText(Path.Combine(bound, "a-file.txt"), "a");
        File.WriteAllText(Path.Combine(bound, "z-file.txt"), "z");
        IReadOnlyDictionary<string, string> before = sandbox.CaptureFiles();

        ProductWorkspaceContainerFolderContent actual =
            WindowsProductContainerFolderContentReader.ReadWorkspace(
                CreateWorkspace(bound, sortMode),
                generation: 1).Find("container-1")!;

        Assert.Equal(
            expectedOrder.Split(','),
            actual.Items.Select(item => item.DisplayName));
        Assert.Equal(before, sandbox.CaptureFiles());
    }

    [Theory]
    [InlineData(ProductContainerFolderSortMode.TypeAscending,
        "甲文件夹,b-图片.png,a-报告.txt,c-笔记.txt")]
    [InlineData(ProductContainerFolderSortMode.TypeDescending,
        "甲文件夹,a-报告.txt,c-笔记.txt,b-图片.png")]
    public void RealUnicodeDirectoryGroupsFolderThenFileTypesWithStableNames(
        ProductContainerFolderSortMode sortMode,
        string expectedOrder)
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("类型排序");
        Directory.CreateDirectory(Path.Combine(bound, "甲文件夹"));
        File.WriteAllText(Path.Combine(bound, "a-报告.txt"), "same-type-a");
        File.WriteAllText(Path.Combine(bound, "b-图片.png"), "image");
        File.WriteAllText(Path.Combine(bound, "c-笔记.txt"), "same-type-c");
        IReadOnlyDictionary<string, string> before = sandbox.CaptureFiles();

        ProductWorkspaceContainerFolderContent actual =
            WindowsProductContainerFolderContentReader.ReadWorkspace(
                CreateWorkspace(
                    bound,
                    sortMode),
                generation: 21).Find("container-1")!;

        Assert.Equal(
            expectedOrder.Split(','),
            actual.Items.Select(item => item.DisplayName));
        Assert.Equal(before, sandbox.CaptureFiles());
    }

    [Theory]
    [InlineData(ProductContainerFolderSortMode.ModifiedNewestFirst,
        "甲文件夹,b-最新.txt,c-同刻.txt,a-最旧.txt")]
    [InlineData(ProductContainerFolderSortMode.ModifiedOldestFirst,
        "甲文件夹,a-最旧.txt,b-最新.txt,c-同刻.txt")]
    public void RealUnicodeDirectorySortsModificationTimeWithStableNameTieBreak(
        ProductContainerFolderSortMode sortMode,
        string expectedOrder)
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("时间排序");
        string folder = Directory.CreateDirectory(
            Path.Combine(bound, "甲文件夹")).FullName;
        string oldest = Path.Combine(bound, "a-最旧.txt");
        string newestA = Path.Combine(bound, "b-最新.txt");
        string newestB = Path.Combine(bound, "c-同刻.txt");
        File.WriteAllText(oldest, "oldest");
        File.WriteAllText(newestA, "newest-a");
        File.WriteAllText(newestB, "newest-b");
        DateTime oldUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime newUtc = oldUtc.AddDays(2);
        File.SetLastWriteTimeUtc(oldest, oldUtc);
        File.SetLastWriteTimeUtc(newestA, newUtc);
        File.SetLastWriteTimeUtc(newestB, newUtc);
        Directory.SetLastWriteTimeUtc(folder, oldUtc.AddDays(-1));
        IReadOnlyDictionary<string, string> before = sandbox.CaptureFiles();

        ProductWorkspaceContainerFolderContent actual =
            WindowsProductContainerFolderContentReader.ReadWorkspace(
                CreateWorkspace(
                    bound,
                    sortMode),
                generation: 22).Find("container-1")!;

        Assert.Equal(
            expectedOrder.Split(','),
            actual.Items.Select(item => item.DisplayName));
        Assert.Equal(before, sandbox.CaptureFiles());
        Assert.Equal(oldUtc, File.GetLastWriteTimeUtc(oldest));
        Assert.Equal(newUtc, File.GetLastWriteTimeUtc(newestA));
        Assert.Equal(newUtc, File.GetLastWriteTimeUtc(newestB));
    }

    [Fact]
    public void RealDirectoryProjectsOnlyDirectChildrenWithoutChangingFiles()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        string childFolder = Directory.CreateDirectory(
            Path.Combine(bound, "A-folder")).FullName;
        string file = Path.Combine(bound, "b-file.txt");
        string nested = Path.Combine(childFolder, "nested.txt");
        File.WriteAllText(file, "visible-file");
        File.WriteAllText(nested, "must-not-recurse");
        IReadOnlyDictionary<string, string> before = sandbox.CaptureFiles();
        ProductWorkspaceState state = CreateWorkspace(bound);

        ProductWorkspaceFolderContentSet contents =
            WindowsProductContainerFolderContentReader.ReadWorkspace(
                state,
                generation: 1);
        ProductWorkspaceContainerFolderContent actual =
            Assert.IsType<ProductWorkspaceContainerFolderContent>(
                contents.Find("container-1"));
        ProductWorkspaceReadSnapshot read =
            ProductWorkspaceReadModel.Create(state, contents).Snapshot!;
        ProductDesktopHostProjectionBatch desktop =
            ProductDesktopHostProjectionBuilder.Build(
                state,
                read,
                Topology(),
                workspaceRevision: 7)!;

        Assert.Equal(ProductWorkspaceFolderContentStatus.Ready, actual.Status);
        Assert.Equal(["A-folder", "b-file.txt"],
            actual.Items.Select(item => item.DisplayName));
        Assert.DoesNotContain(actual.Items, item => item.DisplayName == "nested.txt");
        Assert.Equal(2, read.Containers[0].FolderContentItemCount);
        Assert.Equal(2, read.ItemCount);
        Assert.Equal(
            ["folder:1:1", "folder:1:2"],
            desktop.Displays[0].Containers[0].ItemIds);
        Assert.Equal(before, sandbox.CaptureFiles());
    }

    [Fact]
    public void ExplicitRefreshPublishesNewSetAndPreservesExistingHashesAndLocations()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        string keep = Path.Combine(bound, "keep.txt");
        File.WriteAllText(keep, "keep-content");
        ProductWorkspaceState state = CreateWorkspace(bound);
        ProductWorkspaceFolderContentSet before =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 1);
        string beforeHash = HashFile(keep);

        string added = Path.Combine(bound, "added.txt");
        File.WriteAllText(added, "new-content");
        ProductWorkspaceFolderContentSet after =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 2);

        Assert.Equal(["keep.txt"],
            before.Find("container-1")!.Items.Select(item => item.DisplayName));
        Assert.Equal(["added.txt", "keep.txt"],
            after.Find("container-1")!.Items.Select(item => item.DisplayName));
        Assert.Equal(2, after.Generation);
        Assert.Equal(beforeHash, HashFile(keep));
        Assert.Equal("new-content", File.ReadAllText(added));
        Assert.Equal(Path.GetFullPath(bound), Path.GetDirectoryName(keep));
        Assert.Equal(Path.GetFullPath(bound), Path.GetDirectoryName(added));
    }

    [Fact]
    public void ReplacedBindingFailsClosedWithoutShowingReplacementContents()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        ProductWorkspaceState state = CreateWorkspace(bound);
        Directory.Delete(bound);
        Directory.CreateDirectory(bound);
        string replacement = Path.Combine(bound, "replacement.txt");
        File.WriteAllText(replacement, "do-not-project");

        ProductWorkspaceContainerFolderContent actual =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 1)
                .Find("container-1")!;

        Assert.Equal(
            ProductWorkspaceFolderContentStatus.BindingUnavailable,
            actual.Status);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Replaced,
            actual.BindingResolution);
        Assert.Empty(actual.Items);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Replaced,
            ProductWorkspaceReadModel.Create(state,
                WindowsProductContainerFolderContentReader.ReadWorkspace(state, 2))
                .Snapshot!.Containers[0].FolderBindingResolution);
        Assert.Equal("do-not-project", File.ReadAllText(replacement));
    }

    [Fact]
    public void LargeRealDirectoryIsBoundedAndReportsTruncation()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        for (int index = 0;
            index < WindowsProductContainerFolderContentReader.MaximumProjectedEntries + 4;
            index++)
        {
            File.WriteAllText(
                Path.Combine(bound, $"item-{index:D3}.txt"),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ProductWorkspaceContainerFolderContent actual =
            WindowsProductContainerFolderContentReader.ReadWorkspace(
                CreateWorkspace(bound),
                1).Find("container-1")!;

        Assert.Equal(ProductWorkspaceFolderContentStatus.Truncated, actual.Status);
        Assert.Equal(
            WindowsProductContainerFolderContentReader.MaximumProjectedEntries,
            actual.Items.Count);
        Assert.Equal(
            WindowsProductContainerFolderContentReader.MaximumProjectedEntries + 4,
            Directory.EnumerateFiles(bound).Count());
    }

    [Fact]
    public async Task RealDirectoryChangeInvalidatesAndCanBeReread()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        ProductWorkspaceState state = CreateWorkspace(bound);
        ProductWorkspaceFolderContentSet before =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 1);
        using var watcher = new ProductWorkspaceFolderContentWatcher();
        var invalidated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Invalidated += (_, _) => invalidated.TrySetResult();
        watcher.Configure(state);

        string added = Path.Combine(bound, "shell-change.txt");
        File.WriteAllText(added, "created-after-watch");
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ProductWorkspaceFolderContentSet after =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 2);

        Assert.Empty(before.Find("container-1")!.Items);
        Assert.Equal(
            ["shell-change.txt"],
            after.Find("container-1")!.Items.Select(item => item.DisplayName));
        Assert.Equal("created-after-watch", File.ReadAllText(added));
    }

    [Fact]
    public void MalformedEphemeralProjectionIsRejectedByReadModel()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        ProductWorkspaceState state = CreateWorkspace(bound);
        var malformed = new ProductWorkspaceFolderContentSet(
            8,
            new Dictionary<string, ProductWorkspaceContainerFolderContent>(
                StringComparer.Ordinal)
            {
                ["container-1"] = new(
                    "container-1",
                    8,
                    ProductWorkspaceFolderContentStatus.Ready,
                    [new("not-a-generation-id", bound, ConfigurationItemKind.File, bound)]),
            });

        ProductWorkspaceReadContainer actual =
            ProductWorkspaceReadModel.Create(state, malformed)
                .Snapshot!.Containers[0];

        Assert.Empty(actual.Items);
        Assert.Equal(0, actual.FolderContentItemCount);
        Assert.Equal(
            ProductWorkspaceFolderContentStatus.EnumerationFailed,
            actual.FolderContentStatus);
        Assert.Equal(ProductWorkspaceContainerHealth.NeedsReview, actual.Health);
    }

    [Fact]
    public async Task RealRootOfflineAndReturnAreObservedByParentWatcher()
    {
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        string keep = Path.Combine(bound, "keep.txt");
        File.WriteAllText(keep, "offline-recovery");
        string expectedHash = HashFile(keep);
        ProductWorkspaceState state = CreateWorkspace(bound);
        using var watcher = new ProductWorkspaceFolderContentWatcher();
        Channel<bool> invalidations = Channel.CreateUnbounded<bool>();
        watcher.Invalidated += (_, _) => invalidations.Writer.TryWrite(true);
        watcher.Configure(state);

        string offline = Path.Combine(sandbox.Root, "offline");
        Directory.Move(bound, offline);
        await WaitForInvalidationAsync(invalidations);
        ProductWorkspaceFolderContentSet missing =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 2);
        Drain(invalidations);
        await Task.Delay(350);

        Directory.Move(offline, bound);
        await WaitForInvalidationAsync(invalidations);
        ProductWorkspaceFolderContentSet recovered =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 3);

        Assert.Equal(
            ProductContainerFolderBindingResolution.Missing,
            missing.Find("container-1")!.BindingResolution);
        Assert.Empty(missing.Find("container-1")!.Items);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Resolved,
            recovered.Find("container-1")!.BindingResolution);
        Assert.Equal(
            ["keep.txt"],
            recovered.Find("container-1")!.Items.Select(item => item.DisplayName));
        Assert.Equal(expectedHash, HashFile(keep));
    }

    [Fact]
    public async Task RealAclDenialAndRecoveryProduceFiniteAuthoritativeStates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var sandbox = RealFolderContentSandbox.Create();
        string bound = sandbox.CreateDirectory("bound");
        string keep = Path.Combine(bound, "keep.txt");
        File.WriteAllText(keep, "acl-recovery");
        string expectedHash = HashFile(keep);
        ProductWorkspaceState state = CreateWorkspace(bound);
        string sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("A Windows user SID is required.");
        using var watcher = new ProductWorkspaceFolderContentWatcher();
        Channel<bool> invalidations = Channel.CreateUnbounded<bool>();
        watcher.Invalidated += (_, _) => invalidations.Writer.TryWrite(true);
        watcher.Configure(state);

        try
        {
            Assert.Equal(0, RunIcacls(bound, "/deny", $"*{sid}:(OI)(CI)F"));
            await WaitForInvalidationAsync(invalidations);
            ProductWorkspaceFolderContentSet denied =
                WindowsProductContainerFolderContentReader.ReadWorkspace(state, 4);

            Assert.Equal(
                ProductContainerFolderBindingResolution.AccessDenied,
                denied.Find("container-1")!.BindingResolution);
            Assert.Equal(
                ProductWorkspaceFolderContentStatus.AccessDenied,
                denied.Find("container-1")!.Status);
            Assert.Empty(denied.Find("container-1")!.Items);
            Assert.Equal(
                ProductContainerFolderBindingResolution.AccessDenied,
                ProductWorkspaceReadModel.Create(state, denied)
                    .Snapshot!.Containers[0].FolderBindingResolution);
            Drain(invalidations);
            await Task.Delay(350);
        }
        finally
        {
            Assert.Equal(0, RunIcacls(bound, "/remove:d", $"*{sid}"));
        }

        await WaitForInvalidationAsync(invalidations);
        ProductWorkspaceFolderContentSet recovered =
            WindowsProductContainerFolderContentReader.ReadWorkspace(state, 5);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Resolved,
            recovered.Find("container-1")!.BindingResolution);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Resolved,
            ProductWorkspaceReadModel.Create(state, recovered)
                .Snapshot!.Containers[0].FolderBindingResolution);
        Assert.Equal(expectedHash, HashFile(keep));
    }

    private static ProductWorkspaceState CreateWorkspace(
        string bound,
        ProductContainerFolderSortMode sortMode =
            ProductContainerFolderSortMode.FoldersFirstNameAscending)
    {
        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.CreateResolved(
                WindowsProductContainerFolderBinding.Probe(bound)) with
            {
                SortMode = sortMode,
            };
        return new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "绑定目录",
                    Appearance = new() { Color = "#2563EB", Opacity = 0.88 },
                    Placement = new()
                    {
                        DisplayKey = "display-1",
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = Array.Empty<ProductItemReferenceState>(),
                    FolderBinding = binding,
                },
            ],
        };
    }

    private static ProductDisplayTopologySnapshot Topology() => new(
        ProductDisplayTopologyStatus.Ready,
        3,
        [
            new DisplayTopologyNode(
                "display-1",
                new(0, 0, 1920, 1080),
                new(0, 0, 1920, 1040),
                96,
                DisplayRotation.Landscape,
                true),
        ],
        1,
        1,
        1);

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task WaitForInvalidationAsync(Channel<bool> channel) =>
        _ = await channel.Reader.ReadAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

    private static void Drain(Channel<bool> channel)
    {
        while (channel.Reader.TryRead(out _))
        {
        }
    }

    private static int RunIcacls(
        string path,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "icacls.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(path);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("icacls did not start.");
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("icacls did not finish in time.");
        }
        return process.ExitCode;
    }

    private sealed class RealFolderContentSandbox : IDisposable
    {
        private const string Prefix = "LongGrid.FolderR1C.";

        private RealFolderContentSandbox(string root) => Root = root;

        public string Root { get; }

        public static RealFolderContentSandbox Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                Prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new(root);
        }

        public string CreateDirectory(string name) =>
            Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

        public Dictionary<string, string> CaptureFiles() =>
            Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(Root, path),
                    HashFile,
                    StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            string fullRoot = Path.GetFullPath(Root);
            string temp = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            if (!fullRoot.StartsWith(
                    temp + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith(
                    Prefix,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to clean an unexpected test path.");
            }
            if (Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}
