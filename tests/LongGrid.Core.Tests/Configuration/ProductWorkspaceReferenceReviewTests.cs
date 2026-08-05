using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceReferenceReviewTests
{
    [Fact]
    public void ReviewContainsOnlyUnresolvedReferencesInStableAnonymousOrder()
    {
        ProductWorkspaceState state = MissingState(locked: false);
        ProductItemReferenceState second = MissingItem("item-2", "Second");
        state = state with
        {
            Containers =
            [
                state.Containers[0] with
                {
                    Items = [ResolvedItem("resolved", "Visible"), state.Containers[0].Items[0]],
                },
                state.Containers[0] with
                {
                    Id = "container-2",
                    Name = "Second",
                    Items = [second],
                },
            ],
        };

        ProductWorkspaceReferenceReviewResult result =
            ProductWorkspaceReferenceReview.Create(state, 7, 3);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Snapshot!.Items,
            item =>
            {
                Assert.Equal(1, item.Ordinal);
                Assert.Equal(ProductItemReferenceResolution.Missing, item.Resolution);
                Assert.Equal("item-1", item.Token.ItemId);
            },
            item =>
            {
                Assert.Equal(2, item.Ordinal);
                Assert.Equal(ProductItemReferenceResolution.Missing, item.Resolution);
                Assert.Equal("item-2", item.Token.ItemId);
            });
        Assert.Equal(7, result.Snapshot.CatalogGeneration);
        Assert.Equal(3, result.Snapshot.EditRevision);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    public void ReviewRejectsInvalidVersions(long generation, long revision)
    {
        ProductWorkspaceReferenceReviewResult result =
            ProductWorkspaceReferenceReview.Create(
                MissingState(locked: false),
                generation,
                revision);

        Assert.Equal(ProductWorkspaceReferenceReviewError.InvalidState, result.Error);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void ReviewReturnsFiniteFailureForInvalidState()
    {
        ProductWorkspaceReferenceReviewResult result =
            ProductWorkspaceReferenceReview.Create(
                MissingState(locked: false) with { Containers = null! },
                1,
                0);

        Assert.Equal(ProductWorkspaceReferenceReviewError.InvalidState, result.Error);
        Assert.Equal(
            ProductWorkspaceProjectionError.InvalidState,
            result.ProjectionError);
    }

    [Fact]
    public void GateRejectsStaleCatalogGenerationBeforeActing()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token with { CatalogGeneration = 1 },
            currentGeneration: 2,
            currentRevision: 0,
            ProductWorkspaceReferenceAction.Keep);

        Assert.Equal(
            ProductWorkspaceReferenceGateError.StaleCatalogGeneration,
            result.Error);
    }

    [Fact]
    public void GateRejectsStaleEditRevisionBeforeActing()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token,
            currentGeneration: 1,
            currentRevision: 1,
            ProductWorkspaceReferenceAction.Keep);

        Assert.Equal(
            ProductWorkspaceReferenceGateError.StaleEditRevision,
            result.Error);
    }

    [Fact]
    public void GateRejectsAReferenceWhoseResolutionChanged()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();
        state = state with
        {
            Containers =
            [
                state.Containers[0] with
                {
                    Items =
                    [
                        ResolvedItem("item-1", "Project"),
                    ],
                },
            ],
        };

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token,
            1,
            0,
            ProductWorkspaceReferenceAction.Keep);

        Assert.Equal(ProductWorkspaceReferenceGateError.ItemChanged, result.Error);
    }

    [Fact]
    public void GateRejectsLockedContainer()
    {
        ProductWorkspaceState state = MissingState(locked: true);
        ProductWorkspaceReferenceReviewToken token =
            ProductWorkspaceReferenceReview.Create(state, 1, 0)
                .Snapshot!.Items[0].Token;

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token,
            1,
            0,
            ProductWorkspaceReferenceAction.Keep);

        Assert.Equal(
            ProductWorkspaceReferenceGateError.ContainerLocked,
            result.Error);
    }

    [Fact]
    public void KeepSucceedsWithoutProducingAnEdit()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token,
            1,
            0,
            ProductWorkspaceReferenceAction.Keep);

        Assert.True(result.IsSuccess);
        Assert.False(result.WouldChange);
        Assert.Null(result.Preview);
    }

    [Fact]
    public void RemoveRequiresExplicitConfirmation()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result =
            ProductWorkspaceReferenceGate.Evaluate(
                state,
                1,
                0,
                [],
                new(
                    token,
                    ProductWorkspaceReferenceAction.Remove,
                    Confirmed: false));

        Assert.Equal(
            ProductWorkspaceReferenceGateError.ConfirmationRequired,
            result.Error);
    }

    [Fact]
    public void ConfirmedRemoveProducesDetachedPreviewOnly()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result =
            ProductWorkspaceReferenceGate.Evaluate(
                state,
                1,
                0,
                [],
                new(
                    token,
                    ProductWorkspaceReferenceAction.Remove,
                    Confirmed: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.WouldChange);
        Assert.Empty(result.Preview!.State!.Containers[0].Items);
        Assert.Single(state.Containers[0].Items);
    }

    [Fact]
    public void ReplaceRequiresAnExplicitCandidate()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();

        ProductWorkspaceReferenceGateResult result = Evaluate(
            state,
            token,
            1,
            0,
            ProductWorkspaceReferenceAction.Replace);

        Assert.Equal(
            ProductWorkspaceReferenceGateError.ReplacementRequired,
            result.Error);
    }

    [Fact]
    public void ReplaceRejectsMissingAndAmbiguousCandidates()
    {
        (ProductWorkspaceState state, ProductWorkspaceReferenceReviewToken token) =
            ReviewToken();
        DesktopCatalogEntry candidate = CatalogEntry("Replacement");

        ProductWorkspaceReferenceGateResult missing =
            ProductWorkspaceReferenceGate.Evaluate(
                state,
                1,
                0,
                [],
                new(token, ProductWorkspaceReferenceAction.Replace, Replacement: candidate));
        ProductWorkspaceReferenceGateResult ambiguous =
            ProductWorkspaceReferenceGate.Evaluate(
                state,
                1,
                0,
                [candidate, candidate with { SourceId = "public-desktop" }],
                new(token, ProductWorkspaceReferenceAction.Replace, Replacement: candidate));

        Assert.Equal(
            ProductWorkspaceReferenceGateError.ReplacementNotFound,
            missing.Error);
        Assert.Equal(
            ProductWorkspaceReferenceGateError.ReplacementAmbiguous,
            ambiguous.Error);
    }

    [Fact]
    public void ReplacePreservesItemIdAndExtensionsInDetachedPreview()
    {
        ProductWorkspaceState state = MissingState(locked: false);
        ProductItemReferenceState source = state.Containers[0].Items[0];
        ProductConfigurationDocument persisted =
            ProductWorkspaceConfigurationProjector.Project(state).Document!;
        persisted = persisted with
        {
            Containers =
            [
                persisted.Containers[0] with
                {
                    Items =
                    [
                        persisted.Containers[0].Items[0] with
                        {
                            ExtensionData = new Dictionary<string, JsonElement>
                            {
                                ["future"] = JsonSerializer.SerializeToElement(9),
                            },
                        },
                    ],
                },
            ],
        };
        state = ProductWorkspaceConfigurationResolver.Resolve(persisted, []).State!;
        ProductWorkspaceReferenceReviewToken token =
            ProductWorkspaceReferenceReview.Create(state, 1, 0)
                .Snapshot!.Items[0].Token;
        DesktopCatalogEntry candidate = CatalogEntry("Replacement");

        ProductWorkspaceReferenceGateResult result =
            ProductWorkspaceReferenceGate.Evaluate(
                state,
                1,
                0,
                [candidate],
                new(token, ProductWorkspaceReferenceAction.Replace, Replacement: candidate));

        ProductItemReferenceState preview =
            result.Preview!.State!.Containers[0].Items[0];
        Assert.True(result.IsSuccess);
        Assert.True(result.WouldChange);
        Assert.Equal("item-1", preview.Id);
        Assert.Equal(ProductItemReferenceResolution.Resolved, preview.Resolution);
        Assert.Equal(9, preview.ExtensionData!["future"].GetInt32());
        Assert.Equal(ProductItemReferenceResolution.Missing, source.Resolution);
    }

    private static ProductWorkspaceReferenceGateResult Evaluate(
        ProductWorkspaceState state,
        ProductWorkspaceReferenceReviewToken token,
        long currentGeneration,
        long currentRevision,
        ProductWorkspaceReferenceAction action) =>
        ProductWorkspaceReferenceGate.Evaluate(
            state,
            currentGeneration,
            currentRevision,
            [],
            new(token, action));

    private static (
        ProductWorkspaceState State,
        ProductWorkspaceReferenceReviewToken Token) ReviewToken()
    {
        ProductWorkspaceState state = MissingState(locked: false);
        ProductWorkspaceReferenceReviewToken token =
            ProductWorkspaceReferenceReview.Create(state, 1, 0)
                .Snapshot!.Items[0].Token;
        return (state, token);
    }

    private static ProductWorkspaceState MissingState(bool locked) =>
        new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = locked,
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
                    Items =
                    [
                        MissingItem("item-1", "Project"),
                    ],
                },
            ],
        };

    private static ProductItemReferenceState MissingItem(string id, string name)
    {
        ProductWorkspaceState resolved = new()
        {
            ProfileId = "default",
            Containers =
            [
                new ProductContainerState
                {
                    Id = "container-helper",
                    Name = "Helper",
                    Appearance = new ProductContainerAppearanceState
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                    },
                    Placement = new ProductContainerPlacementState
                    {
                        DisplayKey = "display-a",
                        WidthDip = 320,
                        HeightDip = 240,
                    },
                    Items = [ResolvedItem(id, name)],
                },
            ],
        };
        ProductConfigurationDocument document =
            ProductWorkspaceConfigurationProjector.Project(resolved).Document!;
        return ProductWorkspaceConfigurationResolver.Resolve(document, [])
            .State!.Containers[0].Items[0];
    }

    private static ProductItemReferenceState ResolvedItem(string id, string name) =>
        ProductItemReferenceState.CreateResolved(id, CatalogEntry(name));

    private static DesktopCatalogEntry CatalogEntry(string name) =>
        new(
            new DesktopItemIdentity(
                "filesystem",
                Path.Combine(
                    Path.GetTempPath(),
                    "LongGrid.ReferenceReview.Tests",
                    name)),
            "user-desktop",
            name,
            DesktopItemKind.Directory);
}
