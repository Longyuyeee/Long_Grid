using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LongGrid.Core.DesktopItems;
using Microsoft.Win32.SafeHandles;

internal static class WindowsFileIdentityReader
{
    public static FileIdentityReadResult TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using SafeFileHandle handle = NativeFileMethods.CreateFile(
            path,
            FileAccessRights.ReadAttributes,
            FileShareMode.Read | FileShareMode.Write | FileShareMode.Delete,
            nint.Zero,
            FileCreationDisposition.OpenExisting,
            FileFlags.BackupSemantics | FileFlags.OpenReparsePoint,
            nint.Zero);

        if (handle.IsInvalid)
        {
            return FileIdentityReadResult.Failed(Marshal.GetLastWin32Error());
        }

        if (!NativeFileMethods.GetFileInformationByHandleEx(
            handle,
            FileInformationClass.FileIdInfo,
            out FileIdInformation information,
            (uint)Marshal.SizeOf<FileIdInformation>()))
        {
            return FileIdentityReadResult.Failed(Marshal.GetLastWin32Error());
        }

        Span<byte> fileId = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(fileId, information.FileId.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(fileId[8..], information.FileId.High);

        if (fileId.IndexOfAnyExcept((byte)0) < 0)
        {
            return FileIdentityReadResult.Failed(50);
        }

        return FileIdentityReadResult.Succeeded(
            FileSystemObjectIdentity.Create(
                information.VolumeSerialNumber,
                fileId));
    }
}

internal sealed record FileIdentityReadResult(
    FileSystemObjectIdentity? Identity,
    int? Win32Error)
{
    public static FileIdentityReadResult Succeeded(FileSystemObjectIdentity identity)
    {
        return new FileIdentityReadResult(identity, null);
    }

    public static FileIdentityReadResult Failed(int win32Error)
    {
        return new FileIdentityReadResult(null, win32Error);
    }
}

internal static class NativeFileMethods
{
    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        FileAccessRights desiredAccess,
        FileShareMode shareMode,
        nint securityAttributes,
        FileCreationDisposition creationDisposition,
        FileFlags flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInformationClass fileInformationClass,
        out FileIdInformation fileInformation,
        uint bufferSize);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileIdInformation
{
    public ulong VolumeSerialNumber;
    public FileId128 FileId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileId128
{
    public ulong Low;
    public ulong High;
}

[Flags]
internal enum FileAccessRights : uint
{
    ReadAttributes = 0x80,
}

[Flags]
internal enum FileShareMode : uint
{
    Read = 0x1,
    Write = 0x2,
    Delete = 0x4,
}

internal enum FileCreationDisposition : uint
{
    OpenExisting = 3,
}

[Flags]
internal enum FileFlags : uint
{
    OpenReparsePoint = 0x00200000,
    BackupSemantics = 0x02000000,
}

internal enum FileInformationClass
{
    FileIdInfo = 18,
}
