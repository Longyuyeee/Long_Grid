using LongGrid.Core.Taskbar;

namespace LongGrid.TaskbarWorker;

internal static class TaskbarNativeAdapterCatalog
{
    // R2A2c deliberately ships an empty catalog. A native adapter may only be
    // registered after its exact Windows build passes the disposable R4 matrix.
    internal static ITaskbarAppearanceNativeAdapter? Resolve(int windowsBuild)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowsBuild);
        return null;
    }
}
