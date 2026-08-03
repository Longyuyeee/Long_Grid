using LongGrid.Core.Runtime;

namespace LongGrid.Core.Tests.Runtime;

public sealed class RuntimeStatusSnapshotTests
{
    [Fact]
    public void DevelopmentSnapshotKeepsEveryExternalCapabilitySafe()
    {
        RuntimeStatusSnapshot snapshot =
            RuntimeStatusSnapshot.CreateDevelopmentReadOnly();

        Assert.True(snapshot.IsDevelopmentReadOnly);
        Assert.Equal(RuntimeMode.DevelopmentReadOnly, snapshot.Mode);
        Assert.Equal(
            RuntimeCapabilityState.Disconnected,
            snapshot.DesktopCatalog);
        Assert.Equal(
            RuntimeCapabilityState.DisabledBySafetyPolicy,
            snapshot.FileOperations);
        Assert.Equal(
            RuntimeCapabilityState.Disconnected,
            snapshot.DesktopHost);
        Assert.False(snapshot.HasExternalConnection);
        Assert.False(snapshot.AllowsFileOperations);
    }

    [Fact]
    public void DevelopmentSnapshotsAreValueEquivalent()
    {
        RuntimeStatusSnapshot first =
            RuntimeStatusSnapshot.CreateDevelopmentReadOnly();
        RuntimeStatusSnapshot second =
            RuntimeStatusSnapshot.CreateDevelopmentReadOnly();

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }
}
