using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class WindowsProductContainerFolderContentReaderTests
{
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
        Assert.Empty(actual.Items);
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

    private static ProductWorkspaceState CreateWorkspace(string bound)
    {
        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.CreateResolved(
                WindowsProductContainerFolderBinding.Probe(bound));
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
