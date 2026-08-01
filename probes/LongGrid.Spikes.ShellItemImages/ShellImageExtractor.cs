using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ShellImageExtractor
{
    private static readonly Guid ImageFactoryInterfaceId =
        typeof(IShellItemImageFactory).GUID;

    internal static ImageExtractionResult Extract(
        string path,
        int size,
        ShellItemImageFactoryFlags flags,
        bool includePixels = false)
    {
        var stopwatch = Stopwatch.StartNew();
        int initializeResult = NativeMethods.CoInitializeEx(
            nint.Zero,
            NativeMethods.CoInitMultithreaded);
        bool uninitialize = initializeResult >= 0;

        if (initializeResult < 0 && initializeResult != NativeMethods.RpcEChangedMode)
        {
            return ImageExtractionResult.Failed(initializeResult, stopwatch.Elapsed);
        }

        IShellItemImageFactory? imageFactory = null;
        try
        {
            int createResult = NativeMethods.SHCreateItemFromParsingName(
                path,
                nint.Zero,
                in ImageFactoryInterfaceId,
                out imageFactory);
            if (createResult < 0)
            {
                return ImageExtractionResult.Failed(createResult, stopwatch.Elapsed);
            }

            int imageResult = imageFactory.GetImage(
                new NativeSize(size, size),
                flags,
                out nint bitmapHandle);
            if (bitmapHandle == nint.Zero)
            {
                return ImageExtractionResult.Failed(imageResult, stopwatch.Elapsed);
            }

            using var bitmap = new SafeGdiBitmapHandle(bitmapHandle);
            if (imageResult < 0)
            {
                return ImageExtractionResult.Failed(imageResult, stopwatch.Elapsed);
            }

            int objectResult = NativeMethods.GetObject(
                bitmap.DangerousGetHandle(),
                Marshal.SizeOf<NativeBitmap>(),
                out NativeBitmap metadata);

            if (objectResult == 0)
            {
                return ImageExtractionResult.Failed(
                    Marshal.GetHRForLastWin32Error(),
                    stopwatch.Elapsed);
            }

            if (metadata.Width <= 0
                || metadata.Height == 0
                || metadata.Height == int.MinValue)
            {
                return ImageExtractionResult.Failed(
                    unchecked((int)0x8007000D),
                    stopwatch.Elapsed);
            }

            BitmapPixelData? pixels = null;
            if (includePixels)
            {
                pixels = CopyPixels(bitmap, metadata, out int pixelError);
                if (pixels is null)
                {
                    return ImageExtractionResult.Failed(
                        HResultFromWin32(pixelError),
                        stopwatch.Elapsed);
                }
            }

            return ImageExtractionResult.Succeeded(
                metadata.Width,
                Math.Abs(metadata.Height),
                pixels,
                stopwatch.Elapsed);
        }
        catch (COMException exception)
        {
            return ImageExtractionResult.Failed(exception.HResult, stopwatch.Elapsed);
        }
        finally
        {
            if (imageFactory is not null)
            {
                Marshal.FinalReleaseComObject(imageFactory);
            }

            if (uninitialize)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    private static BitmapPixelData? CopyPixels(
        SafeGdiBitmapHandle bitmap,
        NativeBitmap metadata,
        out int error)
    {
        error = 0;
        int width = metadata.Width;
        int height = Math.Abs(metadata.Height);
        if (width is < 1 or > ThumbnailWorkerServer.MaximumPixelDimension
            || height is < 1 or > ThumbnailWorkerServer.MaximumPixelDimension)
        {
            error = 87;
            return null;
        }

        int stride = checked(width * 4);
        int byteLength = checked(stride * height);
        if (byteLength > ThumbnailWorkerServer.MaximumPixelBytes)
        {
            error = 87;
            return null;
        }

        using var deviceContext = new SafeGdiDeviceContextHandle(
            NativeMethods.CreateCompatibleDC(nint.Zero));
        if (deviceContext.IsInvalid)
        {
            error = Marshal.GetLastPInvokeError();
            return null;
        }

        var header = new NativeBitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<NativeBitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
            SizeImage = (uint)byteLength,
        };
        var bytes = new byte[byteLength];
        int copiedLines = NativeMethods.GetDIBits(
            deviceContext.DangerousGetHandle(),
            bitmap.DangerousGetHandle(),
            startScan: 0,
            scanLines: (uint)height,
            bytes,
            ref header,
            NativeMethods.DibRgbColors);
        if (copiedLines != height)
        {
            error = Marshal.GetLastPInvokeError();
            return null;
        }

        return new BitmapPixelData(width, height, stride, bytes);
    }

    private static int HResultFromWin32(int error) =>
        error <= 0
            ? unchecked((int)0x80004005)
            : unchecked((int)(0x80070000u | ((uint)error & 0xFFFFu)));
}

internal sealed record ImageExtractionResult(
    bool Success,
    int HResult,
    int Width,
    int Height,
    BitmapPixelData? Pixels,
    TimeSpan Duration)
{
    internal static ImageExtractionResult Succeeded(
        int width,
        int height,
        BitmapPixelData? pixels,
        TimeSpan duration) =>
        new(true, 0, width, height, pixels, duration);

    internal static ImageExtractionResult Failed(int hResult, TimeSpan duration) =>
        new(false, hResult, 0, 0, null, duration);
}

internal sealed record BitmapPixelData(
    int Width,
    int Height,
    int Stride,
    byte[] Bytes);
