using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

internal static class ShellShortcutReader
{
    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    public static ShortcutTargetReadResult TryReadTarget(string shortcutPath)
    {
        object? shellLinkObject = null;

        try
        {
            Type shellLinkType = Type.GetTypeFromCLSID(
                ShellLinkClassId,
                throwOnError: true)!;
            shellLinkObject = Activator.CreateInstance(shellLinkType);

            if (shellLinkObject is not IShellLinkW shellLink
                || shellLinkObject is not IPersistFile persistFile)
            {
                return new ShortcutTargetReadResult(false, null);
            }

            persistFile.Load(shortcutPath, 0);
            var targetPath = new StringBuilder(32768);
            int result = shellLink.GetPath(
                targetPath,
                targetPath.Capacity,
                nint.Zero,
                0);

            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            string expandedTarget = Environment.ExpandEnvironmentVariables(
                targetPath.ToString());

            return new ShortcutTargetReadResult(
                Loaded: true,
                TargetPath: string.IsNullOrWhiteSpace(expandedTarget)
                    ? null
                    : expandedTarget);
        }
        catch (Exception exception) when (
            exception is COMException
            or IOException
            or UnauthorizedAccessException)
        {
            return new ShortcutTargetReadResult(false, null);
        }
        finally
        {
            if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
            {
                _ = Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }
}

internal sealed record ShortcutTargetReadResult(
    bool Loaded,
    string? TargetPath);

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig]
    int GetPath(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
        int characterCount,
        nint findData,
        uint flags);

    [PreserveSig]
    int GetIDList(out nint itemIdList);

    [PreserveSig]
    int SetIDList(nint itemIdList);

    [PreserveSig]
    int GetDescription(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,
        int characterCount);

    [PreserveSig]
    int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

    [PreserveSig]
    int GetWorkingDirectory(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
        int characterCount);

    [PreserveSig]
    int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

    [PreserveSig]
    int GetArguments(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
        int characterCount);

    [PreserveSig]
    int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

    [PreserveSig]
    int GetHotkey(out short hotkey);

    [PreserveSig]
    int SetHotkey(short hotkey);

    [PreserveSig]
    int GetShowCommand(out int showCommand);

    [PreserveSig]
    int SetShowCommand(int showCommand);

    [PreserveSig]
    int GetIconLocation(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
        int characterCount,
        out int iconIndex);

    [PreserveSig]
    int SetIconLocation(
        [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
        int iconIndex);

    [PreserveSig]
    int SetRelativePath(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        uint reserved);

    [PreserveSig]
    int Resolve(nint owner, uint flags);

    [PreserveSig]
    int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}
