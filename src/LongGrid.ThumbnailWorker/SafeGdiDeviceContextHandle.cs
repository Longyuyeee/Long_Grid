using Microsoft.Win32.SafeHandles;

namespace LongGrid.ThumbnailWorker;

internal sealed class SafeGdiDeviceContextHandle :
    SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeGdiDeviceContextHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.DeleteDC(handle);
}
