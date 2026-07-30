using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.DesktopItems;

public sealed class FileSystemObjectIdentityTests
{
    [Fact]
    public void CreateBuildsAPathIndependentStableKey()
    {
        byte[] fileId = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();

        FileSystemObjectIdentity first = FileSystemObjectIdentity.Create(42, fileId);
        FileSystemObjectIdentity second = FileSystemObjectIdentity.Create(42, fileId);

        Assert.Equal(first, second);
        Assert.Equal(
            "000000000000002A:000102030405060708090A0B0C0D0E0F",
            first.StableKey);
    }

    [Fact]
    public void CreateRejectsFileIdsThatAreNot128Bits()
    {
        Assert.Throws<ArgumentException>(
            () => FileSystemObjectIdentity.Create(1, new byte[8]));
        Assert.Throws<ArgumentException>(
            () => FileSystemObjectIdentity.Create(1, new byte[17]));
    }
}
