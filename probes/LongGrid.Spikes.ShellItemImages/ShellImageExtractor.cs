using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ShellImageExtractor
{
    private static readonly Guid ImageFactoryInterfaceId =
        typeof(IShellItemImageFactory).GUID;

    internal static ImageExtractionResult Extract(
        string path,
        int size,
        ShellItemImageFactoryFlags flags)
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

            return objectResult == 0
                ? ImageExtractionResult.Failed(
                    Marshal.GetHRForLastWin32Error(),
                    stopwatch.Elapsed)
                : ImageExtractionResult.Succeeded(
                    metadata.Width,
                    metadata.Height,
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
}

internal sealed record ImageExtractionResult(
    bool Success,
    int HResult,
    int Width,
    int Height,
    TimeSpan Duration)
{
    internal static ImageExtractionResult Succeeded(
        int width,
        int height,
        TimeSpan duration) =>
        new(true, 0, width, height, duration);

    internal static ImageExtractionResult Failed(int hResult, TimeSpan duration) =>
        new(false, hResult, 0, 0, duration);
}
