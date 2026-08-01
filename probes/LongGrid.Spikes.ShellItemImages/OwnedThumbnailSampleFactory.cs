using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

internal static class OwnedThumbnailSampleFactory
{
    private static readonly byte[] Gif1X1 = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    private static readonly byte[] Jpeg2X2 = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkS"
        + "Ew8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJ"
        + "CQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy"
        + "MjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEA"
        + "AAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIh"
        + "MUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6"
        + "Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZ"
        + "mqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx"
        + "8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREA"
        + "AgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAV"
        + "YnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hp"
        + "anN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPE"
        + "xcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD1"
        + "Dwj4c0O58F6FPPo2nSzS6dbvJJJaozOxjUkkkZJJ70UUVwVfjfqeXW/iS9Wf/9k=");
    private static readonly byte[] TiffLzw2X2 = Convert.FromBase64String(
        "SUkqABgAAACAP8AAB/gFAQOBv+FQIAQEEAD+AAQAAQAAAAAAAAAAAQQAAQAAAAIA"
        + "AAABAQQAAQAAAAIAAAACAQMABAAAAN4AAAADAQMAAQAAAAUAAAAGAQMAAQAAAAIA"
        + "AAARAQQAAQAAAAgAAAAVAQMAAQAAAAQAAAAWAQQAAQAAAAIAAAAXAQQAAQAAABAA"
        + "AAAaAQUAAQAAAOYAAAAbAQUAAQAAAO4AAAAcAQMAAQAAAAEAAAAoAQMAAQAAAAIA"
        + "AAA9AQMAAQAAAAIAAABSAQMAAQAAAAIAAAAAAAAACAAIAAgACAAAdwEA6AMAAAB3"
        + "AQDoAwAA");
    private static readonly byte[] Heic2X2 = Convert.FromBase64String(
        "AAAAHGZ0eXBoZWljAAAAAG1pZjFoZWljbWlhZgAAAWltZXRhAAAAAAAAACFoZGxy"
        + "AAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAAA5waXRtAAAAAAABAAAAImlsb2MA"
        + "AAAAREAAAQABAAAAAAGNAAEAAAAAAAAA+gAAACNpaW5mAAAAAAABAAAAFWluZmUC"
        + "AAAAAAEAAGh2YzEAAAAA6WlwcnAAAADKaXBjbwAAAHZodmNDAQNwAAAAAAAAAAAA"
        + "HvAA/P34+AAADwNgAAEAGEABDAH//wNwAAADAJAAAAMAAAMAHroCQGEAAQAqQgEB"
        + "A3AAAAMAkAAAAwAAAwAeoCCBBZbqrprm4CGgwIAAAAMAgAAAAwCEYgABAAZEAcFz"
        + "wYkAAAAUaXNwZQAAAAAAAABAAAAAQAAAAChjbGFwAAAAAgAAAAEAAAACAAAAAf//"
        + "/8IAAAAC////wgAAAAIAAAAQcGl4aQAAAAADCAgIAAAAF2lwbWEAAAAAAAAAAQAB"
        + "BIECBIMAAAECbWRhdAAAAPYoAa8GMhuPe4DxccGAizg99ZSGIJHpLjfCuNfkmpqG"
        + "gZWYTPljAUzwK1Ga/OKEp/fa6t/zMgc5MhRX+Tls5l1jQ+hDjgFLlQFasO9VCGr"
        + "cUZTFt+tpfHWc+6FiVgEYzIV7MyP3jKPx5FD8U0WL9YDJPfaQXZrju06FsrYdBS"
        + "W20C7QDVqK3Khx6nmiudjRcal9GkrKcjjhB0PzYYglYmhwLILPNBPc8Ur5EWMRj"
        + "y6nsDI6YH0HB9KWjs3lgCi8LhzEY7wtYT8uT+vEYicNQpVFa1QNEG9CwzHK4lAd"
        + "J+mOJTgJ3ByU2EFZgRxSoRMNdlo/EHoLjfY=");

    internal static void WritePng(string path)
    {
        const int width = 2;
        const int height = 2;
        byte[] scanlines =
        [
            0, 255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 0, 255, 255, 255, 255, 255, 255,
        ];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
            compressed,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        using FileStream stream = File.Create(path);
        stream.Write(
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        ]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    internal static void WriteGif(string path) => File.WriteAllBytes(path, Gif1X1);

    internal static void WriteJpeg(string path) => File.WriteAllBytes(path, Jpeg2X2);

    internal static void WriteTiff(string path)
    {
        const int ifdOffset = 8;
        const ushort entryCount = 10;
        const int bitsPerSampleOffset = ifdOffset + 2 + (entryCount * 12) + 4;
        const int pixelOffset = bitsPerSampleOffset + 6;
        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(ifdOffset);
        writer.Write(entryCount);
        WriteTiffEntry(writer, tag: 256, type: 4, count: 1, value: 2);
        WriteTiffEntry(writer, tag: 257, type: 4, count: 1, value: 2);
        WriteTiffEntry(
            writer,
            tag: 258,
            type: 3,
            count: 3,
            value: bitsPerSampleOffset);
        WriteTiffEntry(writer, tag: 259, type: 3, count: 1, value: 1);
        WriteTiffEntry(writer, tag: 262, type: 3, count: 1, value: 2);
        WriteTiffEntry(
            writer,
            tag: 273,
            type: 4,
            count: 1,
            value: pixelOffset);
        WriteTiffEntry(writer, tag: 277, type: 3, count: 1, value: 3);
        WriteTiffEntry(writer, tag: 278, type: 4, count: 1, value: 2);
        WriteTiffEntry(writer, tag: 279, type: 4, count: 1, value: 12);
        WriteTiffEntry(writer, tag: 284, type: 3, count: 1, value: 1);
        writer.Write(0);
        writer.Write((ushort)8);
        writer.Write((ushort)8);
        writer.Write((ushort)8);
        writer.Write(
        [
            (byte)255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 255,
        ]);
    }

    internal static void WriteTiffLzw(string path) =>
        File.WriteAllBytes(path, TiffLzw2X2);

    internal static void WriteHeic(string path) => File.WriteAllBytes(path, Heic2X2);

    private static void WriteTiffEntry(
        BinaryWriter writer,
        ushort tag,
        ushort type,
        uint count,
        uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }

    private static void WriteChunk(
        Stream stream,
        string type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ~crc);
        stream.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0
                    ? crc >> 1
                    : (crc >> 1) ^ 0xEDB88320;
            }
        }

        return crc;
    }
}
