using System.Runtime.InteropServices;

namespace LongGrid.ThumbnailWorker;

internal static class NativeMethods
{
    internal const uint CoInitMultithreaded = 0;
    internal const uint GrGdiObjects = 0;
    internal const int RpcEChangedMode = unchecked((int)0x80010106);
    internal const uint DibRgbColors = 0;

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint objectHandle);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    internal static extern int GetObject(
        nint objectHandle,
        int bufferSize,
        out NativeBitmap bitmap);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDIBits(
        nint deviceContext,
        nint bitmap,
        uint startScan,
        uint scanLines,
        [Out] byte[] bits,
        ref NativeBitmapInfoHeader bitmapInfo,
        uint usage);

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(nint process, uint flags);
}

[ComImport]
[Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(
        NativeSize size,
        ShellItemImageFactoryFlags flags,
        out nint bitmapHandle);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeSize(int Width, int Height);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBitmap
{
    internal int Type;
    internal int Width;
    internal int Height;
    internal int WidthBytes;
    internal ushort Planes;
    internal ushort BitsPixel;
    internal nint Bits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBitmapInfoHeader
{
    internal uint Size;
    internal int Width;
    internal int Height;
    internal ushort Planes;
    internal ushort BitCount;
    internal uint Compression;
    internal uint SizeImage;
    internal int XPelsPerMeter;
    internal int YPelsPerMeter;
    internal uint ColorsUsed;
    internal uint ColorsImportant;
}

[Flags]
internal enum ShellItemImageFactoryFlags : uint
{
    BiggerSizeOk = 0x00000001,
    IconOnly = 0x00000004,
    ThumbnailOnly = 0x00000008,
    InCacheOnly = 0x00000010,
}
