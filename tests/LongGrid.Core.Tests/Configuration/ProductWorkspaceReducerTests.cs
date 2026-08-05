using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReducerTests
{
    [Fact]
    public void RenameProducesDetachedImmutableState()
    {
        var extensions = Extension("future", 1);
        ProductWorkspaceState source = CreateState() with
        {
            ExtensionData = extensions,
        };

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.RenameContainer(source, "container-1", "Renamed");
        extensions["future"] = JsonSerializer.SerializeToElement(2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Changed);
        Assert.Equal("Renamed", result.State!.Containers[0].Name);
        Assert.Equal("Current project", source.Containers[0].Name);
        Assert.Equal(1, result.State.ExtensionData!["future"].GetInt32());
    }

    [Fact]
    public void NoOpRenameSucceedsWithoutDirtyingState()
    {
        ProductWorkspaceState state = CreateState();

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.RenameContainer(state, "container-1", "Current project");

        Assert.True(result.IsSuccess);
        Assert.False(result.Changed);
        Assert.NotSame(state, result.State);
    }

    [Fact]
    public void ContainerCreationAndRemovalUseValidatedSnapshots()
    {
        ProductContainerState second = CreateState().Containers[0] with
        {
            Id = "container-2",
            Name = "Second",
            Items = [],
        };

        ProductWorkspaceEditResult created =
            ProductWorkspaceReducer.CreateContainer(CreateState(), second);
        ProductWorkspaceEditResult removed =
            ProductWorkspaceReducer.RemoveContainer(created.State!, "container-2");

        Assert.True(created.IsSuccess);
        Assert.Equal(2, created.State!.Containers.Count);
        Assert.True(removed.IsSuccess);
        Assert.Single(removed.State!.Containers);
    }

    [Fact]
    public void DuplicateDomainIdIsRejectedByFormalConfigurationContract()
    {
        ProductContainerState duplicate = CreateState().Containers[0];

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.CreateContainer(CreateState(), duplicate);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProductWorkspaceEditError.ConfigurationRejected, result.Error);
        Assert.Equal(
            ProductWorkspaceProjectionError.ConfigurationRejected,
            result.ProjectionError);
        Assert.Equal(
            ProductConfigurationError.DuplicateObjectId,
            result.ConfigurationError);
        Assert.Null(result.State);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("appearance")]
    [InlineData("placement")]
    [InlineData("add")]
    [InlineData("remove-item")]
    [InlineData("remove-container")]
    public void LockedContainerRejectsContentAndLayoutMutations(string action)
    {
        ProductWorkspaceState state = CreateState(isLocked: true);

        ProductWorkspaceEditResult result = action switch
        {
            "rename" => ProductWorkspaceReducer.RenameContainer(
                state,
                "container-1",
                "Renamed"),
            "appearance" => ProductWorkspaceReducer.UpdateAppearance(
                state,
                "container-1",
                state.Containers[0].Appearance with { Opacity = 0.5 }),
            "placement" => ProductWorkspaceReducer.UpdatePlacement(
                state,
                "container-1",
                state.Containers[0].Placement with { XDip = 80 }),
            "add" => ProductWorkspaceReducer.AddResolvedReference(
                state,
                "container-1",
                CreateItem("item-2", "Second")),
            "remove-item" => ProductWorkspaceReducer.RemoveReference(
                state,
                "container-1",
                "item-1"),
            "remove-container" => ProductWorkspaceReducer.RemoveContainer(
                state,
                "container-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        Assert.Equal(ProductWorkspaceEditError.ContainerLocked, result.Error);
        Assert.Null(result.State);
    }

    [Fact]
    public void LockCanAlwaysBeExplicitlyChanged()
    {
        ProductWorkspaceState state = CreateState(isLocked: true);

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.SetContainerLocked(state, "container-1", false);

        Assert.True(result.IsSuccess);
        Assert.False(result.State!.Containers[0].IsLocked);
    }

    [Fact]
    public void MissingReferenceIsPreservedUnlessRemovalIsConfirmed()
    {
        ProductWorkspaceState state = CreateMissingState();

        ProductWorkspaceEditResult blocked =
            ProductWorkspaceReducer.RemoveReference(
                state,
                "container-1",
                "item-1");
        ProductWorkspaceEditResult confirmed =
            ProductWorkspaceReducer.RemoveReference(
                state,
                "container-1",
                "item-1",
                confirmUnresolvedReference: true);

        Assert.Equal(
            ProductWorkspaceEditError.UnresolvedReferenceRequiresConfirmation,
            blocked.Error);
        Assert.True(confirmed.IsSuccess);
        Assert.Empty(confirmed.State!.Containers[0].Items);
    }

    [Fact]
    public void ContainerRemovalAlsoRequiresConfirmationForUnresolvedReferences()
    {
        ProductWorkspaceState state = CreateMissingState();

        ProductWorkspaceEditResult blocked =
            ProductWorkspaceReducer.RemoveContainer(state, "container-1");
        ProductWorkspaceEditResult confirmed =
            ProductWorkspaceReducer.RemoveContainer(
                state,
                "container-1",
                confirmUnresolvedReferences: true);

        Assert.Equal(
            ProductWorkspaceEditError.UnresolvedReferenceRequiresConfirmation,
            blocked.Error);
        Assert.True(confirmed.IsSuccess);
        Assert.Empty(confirmed.State!.Containers);
    }

    [Fact]
    public void ExplicitReplacementRebindsAnUnresolvedReferenceToCatalogIdentity()
    {
        ProductWorkspaceState state = WithItemExtension(
            CreateMissingState(),
            "future",
            5);
        ProductItemReferenceState replacement = CreateItem("ignored", "Replacement");

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.ReplaceReference(
                state,
                "container-1",
                "item-1",
                replacement.CatalogEntry!);

        Assert.True(result.IsSuccess);
        ProductItemReferenceState item = result.State!.Containers[0].Items[0];
        Assert.Equal("item-1", item.Id);
        Assert.Equal(ProductItemReferenceResolution.Resolved, item.Resolution);
        Assert.Equal(
            replacement.CatalogEntry!.Identity.CanonicalTarget,
            item.CatalogEntry!.Identity.CanonicalTarget);
        Assert.Equal(5, item.ExtensionData!["future"].GetInt32());
    }

    [Fact]
    public void MissingTargetsReturnFiniteNotFoundErrors()
    {
        ProductWorkspaceState state = CreateState();

        ProductWorkspaceEditResult container =
            ProductWorkspaceReducer.RenameContainer(state, "absent", "Name");
        ProductWorkspaceEditResult item =
            ProductWorkspaceReducer.RemoveReference(
                state,
                "container-1",
                "absent");

        Assert.Equal(ProductWorkspaceEditError.ContainerNotFound, container.Error);
        Assert.Equal(ProductWorkspaceEditError.ItemNotFound, item.Error);
    }

    [Fact]
    public void InvalidInputStateFailsBeforeApplyingAnEdit()
    {
        ProductWorkspaceState state = CreateState() with { Containers = null! };

        ProductWorkspaceEditResult result =
            ProductWorkspaceReducer.RenameContainer(state, "container-1", "Name");

        Assert.Equal(ProductWorkspaceEditError.InvalidState, result.Error);
        Assert.Null(result.State);
    }

    private static ProductWorkspaceState CreateState(bool isLocked = false) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = isLocked,
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = false,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items = [CreateItem("item-1", "Project")],
                },
            ],
        };

    private static ProductWorkspaceState CreateMissingState()
    {
        ProductWorkspaceState template = CreateState();
        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(template).Document!;
        return ProductWorkspaceConfigurationResolver.Resolve(
            document,
            Array.Empty<DesktopCatalogEntry>()).State!;
    }

    private static ProductWorkspaceState WithItemExtension(
        ProductWorkspaceState state,
        string name,
        int value)
    {
        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(state).Document!;
        DesktopItemReferenceConfiguration persisted =
            document.Containers[0].Items[0];
        document = document with
        {
            Containers =
            [
                document.Containers[0] with
                {
                    Items =
                    [
                        persisted with { ExtensionData = Extension(name, value) },
                    ],
                },
            ],
        };
        return ProductWorkspaceConfigurationResolver.Resolve(
            document,
            Array.Empty<DesktopCatalogEntry>()).State!;
    }

    private static ProductItemReferenceState CreateItem(string id, string name) =>
        ProductItemReferenceState.CreateResolved(
            id,
            new DesktopCatalogEntry(
                new DesktopItemIdentity(
                    "filesystem",
                    Path.Combine(
                        Path.GetTempPath(),
                        "LongGrid.Reducer.Tests",
                        name)),
                "user-desktop",
                name,
                DesktopItemKind.Directory));

    private static Dictionary<string, JsonElement> Extension(
        string name,
        int value) =>
        new()
        {
            [name] = JsonSerializer.SerializeToElement(value),
        };
}
