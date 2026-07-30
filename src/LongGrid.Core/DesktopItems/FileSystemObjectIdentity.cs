namespace LongGrid.Core.DesktopItems;

public sealed record FileSystemObjectIdentity
{
    private const int FileIdByteLength = 16;

    private FileSystemObjectIdentity(
        ulong volumeSerialNumber,
        string fileId)
    {
        VolumeSerialNumber = volumeSerialNumber;
        FileId = fileId;
    }

    public ulong VolumeSerialNumber { get; }

    public string FileId { get; }

    public string StableKey => $"{VolumeSerialNumber:X16}:{FileId}";

    public static FileSystemObjectIdentity Create(
        ulong volumeSerialNumber,
        ReadOnlySpan<byte> fileId)
    {
        if (fileId.Length != FileIdByteLength)
        {
            throw new ArgumentException(
                $"A Windows file ID must contain {FileIdByteLength} bytes.",
                nameof(fileId));
        }

        return new FileSystemObjectIdentity(
            volumeSerialNumber,
            Convert.ToHexString(fileId));
    }
}
