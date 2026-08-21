using System.Diagnostics;

namespace LongGrid.App;

internal static class ProductWinUiRuntimeSafety
{
    private const string KnownUnsafeRuntimeDirectoryPrefix =
        "Microsoft.WindowsAppRuntime.2_2.4.0.0_";
    internal static bool RequiresSingleWindowPreview()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                if (!string.Equals(
                        module.ModuleName,
                        "Microsoft.UI.Xaml.dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? runtimeDirectory = Path.GetFileName(
                    Path.GetDirectoryName(module.FileName));
                FileVersionInfo version = module.FileVersionInfo;
                return version.FileMajorPart == 3
                    && version.FileMinorPart == 2
                    && version.FileBuildPart == 3
                    && version.FilePrivatePart == 0
                    && runtimeDirectory?.StartsWith(
                        KnownUnsafeRuntimeDirectoryPrefix,
                        StringComparison.OrdinalIgnoreCase) == true;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            // An unknown runtime is not treated as the exact attested blocker.
        }

        return false;
    }
}
