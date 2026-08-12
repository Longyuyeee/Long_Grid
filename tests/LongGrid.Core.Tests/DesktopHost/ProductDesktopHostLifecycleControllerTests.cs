using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostLifecycleControllerTests
{
    [Fact]
    public void DefaultPolicyCreatesNoNativeHostOrOwnedWindows()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate(null));

        ProductDesktopHostLifecycleSnapshot snapshot = controller.Snapshot;

        Assert.Equal(
            ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy,
            snapshot.Status);
        Assert.False(snapshot.FeatureEnabled);
        Assert.False(snapshot.NativeHostConnected);
        Assert.Equal(0, snapshot.OwnedWindowCount);
        Assert.Equal(0, snapshot.Generation);
    }

    [Fact]
    public void ExplicitOptInOnlyWaitsForFutureHost()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"));

        ProductDesktopHostLifecycleSnapshot snapshot = controller.Snapshot;

        Assert.Equal(ProductDesktopHostLifecycleStatus.AwaitingHost, snapshot.Status);
        Assert.True(snapshot.FeatureEnabled);
        Assert.False(snapshot.NativeHostConnected);
        Assert.Equal(0, snapshot.OwnedWindowCount);
    }

    [Fact]
    public async Task DisposalIsIdempotentAndPublishesAnonymousCompletion()
    {
        var controller = new ProductDesktopHostLifecycleController(
            ProductDesktopHostFeaturePolicy.Evaluate("1"));
        var published = new List<ProductDesktopHostLifecycleSnapshot>();
        controller.SnapshotChanged += (_, snapshot) => published.Add(snapshot);

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        ProductDesktopHostLifecycleSnapshot completed = Assert.Single(published);
        Assert.Equal(ProductDesktopHostLifecycleStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Generation);
        Assert.False(completed.FeatureEnabled);
        Assert.False(completed.NativeHostConnected);
        Assert.Equal(0, completed.OwnedWindowCount);
        Assert.Equal(completed, controller.Snapshot);
    }
}
