using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class WindowsProductContainerFolderBindingTests
{
    [Fact]
    public void RealDirectoryBindingSurvivesMoveByIdentityAndRequiresExplicitReconnect()
    {
        using RealFolderSandbox sandbox = RealFolderSandbox.Create();
        string original = sandbox.CreateDirectory("source");
        File.WriteAllText(Path.Combine(original, "sentinel.txt"), "LongGrid-folder-r1-a");
        string contentHash = HashFile(Path.Combine(original, "sentinel.txt"));

        ProductContainerFolderBindingProbeResult initial =
            WindowsProductContainerFolderBinding.Probe(original);
        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.CreateResolved(initial);

        string moved = Path.Combine(sandbox.Root, "moved");
        Directory.Move(original, moved);
        ProductContainerFolderBindingState stale =
            WindowsProductContainerFolderBinding.Resolve(binding);
        ProductContainerFolderBindingProbeResult movedProbe =
            WindowsProductContainerFolderBinding.Probe(moved);
        ProductContainerFolderBindingState reconnected =
            WindowsProductContainerFolderBinding.CreateResolved(movedProbe);

        Assert.True(initial.IsSuccess);
        Assert.Equal(ProductContainerFolderBindingResolution.Missing, stale.Resolution);
        Assert.True(movedProbe.IsSuccess);
        Assert.Equal(binding.VolumeSerialNumber, reconnected.VolumeSerialNumber);
        Assert.Equal(binding.FileId, reconnected.FileId);
        Assert.Equal(Path.GetFullPath(moved), reconnected.PersistedTarget);
        Assert.Equal(contentHash, HashFile(Path.Combine(moved, "sentinel.txt")));
    }

    [Fact]
    public void RecreatedPathIsReportedAsReplacedAndUserContentIsUntouched()
    {
        using RealFolderSandbox sandbox = RealFolderSandbox.Create();
        string target = sandbox.CreateDirectory("bound");
        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.CreateResolved(
                WindowsProductContainerFolderBinding.Probe(target)) with
            {
                SortMode = ProductContainerFolderSortMode.NameDescending,
            };

        Directory.Delete(target);
        Directory.CreateDirectory(target);
        string replacementFile = Path.Combine(target, "replacement.txt");
        File.WriteAllText(replacementFile, "keep-me");

        ProductContainerFolderBindingState resolved =
            WindowsProductContainerFolderBinding.Resolve(binding);

        Assert.Equal(ProductContainerFolderBindingResolution.Replaced, resolved.Resolution);
        Assert.Null(resolved.ResolvedTarget);
        Assert.Equal("keep-me", File.ReadAllText(replacementFile));
    }

    [Fact]
    public void FolderBindingRoundTripsAndUnbindChangesOnlyConfigurationState()
    {
        using RealFolderSandbox sandbox = RealFolderSandbox.Create();
        string target = sandbox.CreateDirectory("bound");
        string userFile = Path.Combine(target, "user.txt");
        File.WriteAllText(userFile, "unchanged");
        string beforeHash = HashFile(userFile);
        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.CreateResolved(
                WindowsProductContainerFolderBinding.Probe(target)) with
            {
                SortMode = ProductContainerFolderSortMode.NameDescending,
            };
        ProductWorkspaceState state = CreateWorkspace();

        ProductWorkspaceEditResult bound = ProductWorkspaceReducer.SetFolderBinding(
            state,
            "container-1",
            binding);
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(bound.State!);
        ProductConfigurationDocument roundTrip = ProductConfigurationJson.Deserialize(
            ProductConfigurationJson.SerializeToUtf8Bytes(projection.Document!));
        ProductWorkspaceSessionSnapshot loaded = ProductWorkspaceSessionLoader.Load(
            new(
                ProductConfigurationLoadStatus.LoadedPrimary,
                roundTrip,
                ProductConfigurationStorageFailure.None,
                ProductConfigurationStorageFailure.None,
                ProductConfigurationError.None,
                ProductConfigurationError.None),
            ProductWorkspaceCatalogSnapshot.Available([]));
        ProductWorkspaceEditResult unbound = ProductWorkspaceReducer.SetFolderBinding(
            bound.State!,
            "container-1",
            null);

        Assert.True(bound.IsSuccess);
        Assert.True(bound.Changed);
        Assert.True(projection.IsSuccess);
        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion,
            roundTrip.SchemaVersion);
        Assert.Equal(
            ProductContainerFolderSortMode.NameDescending,
            roundTrip.Containers[0].FolderBinding!.SortMode);
        Assert.Equal(Path.GetFullPath(target), roundTrip.Containers[0].FolderBinding!.Target);
        Assert.Equal(
            ProductContainerFolderBindingResolution.Resolved,
            loaded.State!.Containers[0].FolderBinding!.Resolution);
        Assert.Equal(
            ProductContainerFolderSortMode.NameDescending,
            loaded.State.Containers[0].FolderBinding!.SortMode);
        Assert.True(unbound.IsSuccess);
        Assert.True(unbound.Changed);
        Assert.Null(unbound.State!.Containers[0].FolderBinding);
        Assert.True(File.Exists(userFile));
        Assert.Equal(beforeHash, HashFile(userFile));
    }

    [Fact]
    public void InvalidOrFileTargetsAreRejectedWithoutFilesystemWrites()
    {
        using RealFolderSandbox sandbox = RealFolderSandbox.Create();
        string file = Path.Combine(sandbox.Root, "not-a-folder.txt");
        File.WriteAllText(file, "unchanged");
        string beforeHash = HashFile(file);

        ProductContainerFolderBindingProbeResult relative =
            WindowsProductContainerFolderBinding.Probe("relative-folder");
        ProductContainerFolderBindingProbeResult notDirectory =
            WindowsProductContainerFolderBinding.Probe(file);

        Assert.Equal(ProductContainerFolderBindingProbeError.InvalidTarget, relative.Error);
        Assert.Equal(ProductContainerFolderBindingProbeError.NotDirectory, notDirectory.Error);
        Assert.Equal(beforeHash, HashFile(file));
    }

    private static ProductWorkspaceState CreateWorkspace() => new()
    {
        ProfileId = "default",
        Containers =
        [
            new ProductContainerState
            {
                Id = "container-1",
                Name = "工作",
                Appearance = new()
                {
                    Color = "#2563EB",
                    Opacity = 0.88,
                },
                Placement = new()
                {
                    DisplayKey = "display-1",
                    WidthDip = 360,
                    HeightDip = 240,
                },
                Items = Array.Empty<ProductItemReferenceState>(),
            },
        ],
    };

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class RealFolderSandbox : IDisposable
    {
        private const string Prefix = "LongGrid.FolderR1A.";

        private RealFolderSandbox(string root) => Root = root;

        public string Root { get; }

        public static RealFolderSandbox Create()
        {
            string root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new(root);
        }

        public string CreateDirectory(string name)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            string fullRoot = Path.GetFullPath(Root);
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!fullRoot.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected test path.");
            }

            if (Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}
