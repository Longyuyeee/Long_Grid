using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal sealed class ThumbnailSharedMemoryTransfer : IDisposable
{
    private const uint FileMapRead = 0x0004;
    private const uint FileMapWrite = 0x0002;
    private const uint PageReadWrite = 0x04;
    private readonly SafeFileHandle _mappingHandle;

    private ThumbnailSharedMemoryTransfer(
        SafeFileHandle mappingHandle,
        long workerHandle)
    {
        _mappingHandle = mappingHandle;
        WorkerHandle = workerHandle;
    }

    internal long WorkerHandle { get; }

    internal static ThumbnailSharedMemoryTransfer Create(Process workerProcess)
    {
        ArgumentNullException.ThrowIfNull(workerProcess);
        SafeFileHandle mappingHandle = CreateFileMapping(
            new nint(-1),
            nint.Zero,
            PageReadWrite,
            maximumSizeHigh: 0,
            ThumbnailWorkerServer.MaximumPixelBytes,
            name: null);
        if (mappingHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!DuplicateHandle(
                GetCurrentProcess(),
                mappingHandle,
                workerProcess.Handle,
                out nint workerHandle,
                desiredAccess: FileMapWrite,
                inheritHandle: false,
                options: 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new ThumbnailSharedMemoryTransfer(
                mappingHandle,
                workerHandle.ToInt64());
        }
        catch
        {
            mappingHandle.Dispose();
            throw;
        }
    }

    internal byte[] Read(int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            byteLength,
            ThumbnailWorkerServer.MaximumPixelBytes);
        nint view = MapViewOfFile(
            _mappingHandle,
            FileMapRead,
            fileOffsetHigh: 0,
            fileOffsetLow: 0,
            (nuint)byteLength);
        if (view == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var bytes = new byte[byteLength];
            Marshal.Copy(view, bytes, startIndex: 0, byteLength);
            return bytes;
        }
        finally
        {
            _ = UnmapViewOfFile(view);
        }
    }

    internal static void Write(
        SafeFileHandle mappingHandle,
        byte[] bytes,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(mappingHandle);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            capacity,
            ThumbnailWorkerServer.MaximumPixelBytes);
        nint view = MapViewOfFile(
            mappingHandle,
            FileMapWrite,
            fileOffsetHigh: 0,
            fileOffsetLow: 0,
            (nuint)capacity);
        if (view == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            Marshal.Copy(bytes, startIndex: 0, view, bytes.Length);
        }
        finally
        {
            _ = UnmapViewOfFile(view);
        }
    }

    public void Dispose() => _mappingHandle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileMapping(
        nint fileHandle,
        nint attributes,
        uint protect,
        uint maximumSizeHigh,
        uint maximumSizeLow,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        nint sourceProcessHandle,
        SafeFileHandle sourceHandle,
        nint targetProcessHandle,
        out nint targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint MapViewOfFile(
        SafeFileHandle fileMappingObject,
        uint desiredAccess,
        uint fileOffsetHigh,
        uint fileOffsetLow,
        nuint numberOfBytesToMap);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnmapViewOfFile(nint baseAddress);
}
