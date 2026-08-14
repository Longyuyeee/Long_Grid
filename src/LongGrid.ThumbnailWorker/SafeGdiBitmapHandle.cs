using Microsoft.Win32.SafeHandles;

namespace LongGrid.ThumbnailWorker;

internal sealed class SafeGdiBitmapHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private static int _releasedCount;

    internal SafeGdiBitmapHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    internal static int ReleasedCount => Volatile.Read(ref _releasedCount);

    protected override bool ReleaseHandle()
    {
        bool deleted = NativeMethods.DeleteObject(handle);
        if (deleted)
        {
            Interlocked.Increment(ref _releasedCount);
        }

        return deleted;
    }
}
