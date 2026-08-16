using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerCreationDefaultsTests
{
    private static readonly PixelRect WorkArea = new(100, 200, 1920, 1040);

    [Fact]
    public void EmptyWorkspaceUsesBaseNameAndBoundedPrimaryPlacement()
    {
        ProductWorkspaceContainerCreationDefaultsDecision decision = Evaluate();

        Assert.True(decision.CanCreate);
        Assert.Equal("新方格", decision.Name);
        Assert.Equal("display-primary", decision.Placement!.DisplayKey);
        Assert.Equal(32, decision.Placement.XDip);
        Assert.Equal(48, decision.Placement.YDip);
        Assert.Equal(360, decision.Placement.WidthDip);
        Assert.Equal(240, decision.Placement.HeightDip);
    }

    [Fact]
    public void DefaultNameFillsFirstGapWithoutCaseSensitiveDuplicates()
    {
        ProductContainerState[] existing =
        [
            Container("one", "新方格", 32, 48),
            Container("three", "新方格 3", 56, 48),
            Container("upper", "新方格 4", 80, 48),
        ];

        ProductWorkspaceContainerCreationDefaultsDecision decision =
            Evaluate(existing);

        Assert.Equal("新方格 2", decision.Name);
    }

    [Theory]
    [InlineData(" Work ", "Work")]
    [InlineData("新建 🗂️", "新建 🗂️")]
    public void RequestedUnicodeNameIsTrimmedDeterministically(
        string requested,
        string expected)
    {
        ProductWorkspaceContainerCreationDefaultsDecision decision =
            Evaluate(requestedName: requested);

        Assert.True(decision.CanCreate);
        Assert.Equal(expected, decision.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void InvalidRequestedNameFailsClosed(string requestedName)
    {
        ProductWorkspaceContainerCreationDefaultsDecision decision =
            Evaluate(requestedName: requestedName);

        Assert.Equal(
            ProductWorkspaceContainerCreationDefaultsStatus.Invalid,
            decision.Status);
        Assert.False(decision.CanCreate);
    }

    [Fact]
    public void RequestedDuplicateNameIsRejectedIgnoringCase()
    {
        ProductWorkspaceContainerCreationDefaultsDecision decision = Evaluate(
            [Container("one", "Work", 32, 48)],
            requestedName: " work ");

        Assert.Equal(
            ProductWorkspaceContainerCreationDefaultsStatus.DuplicateName,
            decision.Status);
    }

    [Fact]
    public void TwentySequentialPlansHaveUniqueNamesIdsAndPlacements()
    {
        ProductWorkspaceState state = new()
        {
            ProfileId = "profile",
            Containers = Array.Empty<ProductContainerState>(),
        };

        for (int index = 0; index < 20; index++)
        {
            ProductWorkspaceContainerCreationDefaultsDecision decision =
                Evaluate(state.Containers);
            Assert.True(decision.CanCreate);
            ProductContainerState container = new()
            {
                Id = $"container-{index + 1}",
                Name = decision.Name!,
                Appearance = new()
                {
                    Color = "#2563EB",
                    Opacity = 0.88,
                    Collapsed = false,
                },
                Placement = decision.Placement!,
                Items = Array.Empty<ProductItemReferenceState>(),
            };
            ProductWorkspaceEditResult result =
                ProductWorkspaceReducer.CreateContainer(state, container);
            Assert.True(result.IsSuccess);
            state = result.State!;
        }

        Assert.Equal(20, state.Containers.Count);
        Assert.Equal(20, state.Containers.Select(container => container.Id)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, state.Containers.Select(container => container.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("新方格", state.Containers[0].Name);
        Assert.Equal("新方格 20", state.Containers[^1].Name);
        Assert.Equal(20, state.Containers.Select(container => (
                container.Placement.XDip,
                container.Placement.YDip,
                container.Placement.WidthDip,
                container.Placement.HeightDip))
            .Distinct().Count());
        Assert.All(state.Containers, container =>
        {
            Assert.InRange(container.Placement.XDip, 0, 1560);
            Assert.InRange(container.Placement.YDip, 0, 800);
        });
    }

    [Fact]
    public void LimitAndMalformedInputsFailWithoutPlan()
    {
        ProductContainerState[] full = Enumerable.Range(
                0,
                ProductConfigurationLimits.MaximumContainers)
            .Select(index => Container(
                $"container-{index}",
                $"Box {index}",
                index,
                index))
            .ToArray();

        Assert.Equal(
            ProductWorkspaceContainerCreationDefaultsStatus.LimitReached,
            Evaluate(full).Status);
        Assert.Equal(
            ProductWorkspaceContainerCreationDefaultsStatus.Invalid,
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                null,
                null,
                "display-primary",
                WorkArea,
                96).Status);
        Assert.Equal(
            ProductWorkspaceContainerCreationDefaultsStatus.Invalid,
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                [],
                null,
                string.Empty,
                WorkArea,
                96).Status);
    }

    private static ProductWorkspaceContainerCreationDefaultsDecision Evaluate(
        IReadOnlyList<ProductContainerState>? existing = null,
        string? requestedName = null) =>
        ProductWorkspaceContainerCreationDefaults.Evaluate(
            existing ?? Array.Empty<ProductContainerState>(),
            requestedName,
            "display-primary",
            WorkArea,
            96);

    private static ProductContainerState Container(
        string id,
        string name,
        double x,
        double y) => new()
        {
            Id = id,
            Name = name,
            Appearance = new()
            {
                Color = "#2563EB",
                Opacity = 0.88,
                Collapsed = false,
            },
            Placement = new()
            {
                DisplayKey = "display-primary",
                XDip = x,
                YDip = y,
                WidthDip = 360,
                HeightDip = 240,
            },
            Items = Array.Empty<ProductItemReferenceState>(),
        };
}
