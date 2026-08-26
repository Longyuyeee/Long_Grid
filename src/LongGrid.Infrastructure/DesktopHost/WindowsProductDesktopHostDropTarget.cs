using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace LongGrid.Infrastructure.DesktopHost;

[ComVisible(true)]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsProductDesktopHostDropTarget
{
    [PreserveSig]
    int DragEnter(
        IDataObject dataObject,
        uint keyState,
        NativeDropPoint point,
        ref uint effect);

    [PreserveSig]
    int DragOver(uint keyState, NativeDropPoint point, ref uint effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop(
        IDataObject dataObject,
        uint keyState,
        NativeDropPoint point,
        ref uint effect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDropPoint(int x, int y)
{
    internal int X = x;

    internal int Y = y;
}

internal sealed class WindowsProductDesktopHostDropTarget
    : IWindowsProductDesktopHostDropTarget, IDisposable
{
    internal const uint EffectNone = 0;
    internal const uint EffectLink = 4;
    private const int Success = 0;
    private const short ClipboardFormatHDrop = 15;
    private readonly nint window;
    private readonly Func<int, int, string?> resolveTarget;
    private readonly Func<object, string, bool> submit;
    private readonly Action<string?> publishHover;
    private string? currentTarget;
    private bool dragDataSupported;
    private bool oleInitialized;
    private bool registered;
    private bool disposed;

    private WindowsProductDesktopHostDropTarget(
        nint window,
        Func<int, int, string?> resolveTarget,
        Func<object, string, bool> submit,
        Action<string?> publishHover)
    {
        this.window = window;
        this.resolveTarget = resolveTarget;
        this.submit = submit;
        this.publishHover = publishHover;
    }

    internal bool IsRegistered => registered && !disposed;

    internal static WindowsProductDesktopHostDropTarget? TryRegister(
        nint window,
        Func<int, int, string?> resolveTarget,
        Func<object, string, bool> submit,
        Action<string?> publishHover)
    {
        if (!OperatingSystem.IsWindows()
            || window == nint.Zero
            || resolveTarget is null
            || submit is null
            || publishHover is null)
        {
            return null;
        }

        var target = new WindowsProductDesktopHostDropTarget(
            window,
            resolveTarget,
            submit,
            publishHover);
        int oleResult = NativeMethods.OleInitialize(nint.Zero);
        if (oleResult < 0)
        {
            return null;
        }

        target.oleInitialized = true;
        int registration = NativeMethods.RegisterDragDrop(window, target);
        if (registration < 0)
        {
            target.Dispose();
            return null;
        }

        target.registered = true;
        return target;
    }

    public int DragEnter(
        IDataObject dataObject,
        uint keyState,
        NativeDropPoint point,
        ref uint effect)
    {
        _ = keyState;
        dragDataSupported = SupportsHDrop(dataObject);
        currentTarget = dragDataSupported ? ResolveClientTarget(point) : null;
        bool canLink = CanLink(effect, currentTarget);
        effect = canLink ? EffectLink : EffectNone;
        PublishHover(canLink ? currentTarget : null);
        return Success;
    }

    public int DragOver(uint keyState, NativeDropPoint point, ref uint effect)
    {
        _ = keyState;
        currentTarget = dragDataSupported ? ResolveClientTarget(point) : null;
        bool canLink = CanLink(effect, currentTarget);
        effect = canLink ? EffectLink : EffectNone;
        PublishHover(canLink ? currentTarget : null);
        return Success;
    }

    public int DragLeave()
    {
        currentTarget = null;
        dragDataSupported = false;
        PublishHover(null);
        return Success;
    }

    public int Drop(
        IDataObject dataObject,
        uint keyState,
        NativeDropPoint point,
        ref uint effect)
    {
        _ = keyState;
        string? target = SupportsHDrop(dataObject)
            ? ResolveClientTarget(point)
            : null;
        bool accepted = false;
        if (target is not null && (effect & EffectLink) != 0)
        {
            try
            {
                accepted = submit(dataObject, target);
            }
            catch (Exception)
            {
                accepted = false;
            }
        }
        effect = accepted ? EffectLink : EffectNone;
        currentTarget = null;
        dragDataSupported = false;
        PublishHover(null);
        return Success;
    }

    internal uint DispatchDragEnterForEvidence(
        object dataObject,
        int screenX,
        int screenY,
        uint allowedEffects)
    {
        uint effect = allowedEffects;
        _ = DragEnter(
            (IDataObject)dataObject,
            0,
            new(screenX, screenY),
            ref effect);
        return effect;
    }

    internal uint DispatchDropForEvidence(
        object dataObject,
        int screenX,
        int screenY,
        uint allowedEffects)
    {
        uint effect = allowedEffects;
        _ = Drop(
            (IDataObject)dataObject,
            0,
            new(screenX, screenY),
            ref effect);
        return effect;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        currentTarget = null;
        dragDataSupported = false;
        PublishHover(null);
        if (registered)
        {
            _ = NativeMethods.RevokeDragDrop(window);
            registered = false;
        }
        if (oleInitialized)
        {
            NativeMethods.OleUninitialize();
            oleInitialized = false;
        }
    }

    private string? ResolveClientTarget(NativeDropPoint point)
    {
        var client = new NativeDropPoint(point.X, point.Y);
        return NativeMethods.ScreenToClient(window, ref client)
            ? resolveTarget(client.X, client.Y)
            : null;
    }

    private void PublishHover(string? target)
    {
        if (!disposed)
        {
            publishHover(target);
        }
    }

    private static bool CanLink(uint allowedEffects, string? target) =>
        target is not null && (allowedEffects & EffectLink) != 0;

    private static bool SupportsHDrop(IDataObject dataObject)
    {
        try
        {
            var format = new FORMATETC
            {
                cfFormat = ClipboardFormatHDrop,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL,
            };
            return dataObject.QueryGetData(ref format) >= 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll")]
        internal static extern int OleInitialize(nint reserved);

        [DllImport("ole32.dll")]
        internal static extern void OleUninitialize();

        [DllImport("ole32.dll")]
        internal static extern int RegisterDragDrop(
            nint window,
            IWindowsProductDesktopHostDropTarget dropTarget);

        [DllImport("ole32.dll")]
        internal static extern int RevokeDragDrop(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(
            nint window,
            ref NativeDropPoint point);
    }
}
