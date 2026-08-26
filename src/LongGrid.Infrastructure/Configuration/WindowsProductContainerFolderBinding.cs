using System.Runtime.InteropServices;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using Microsoft.Win32.SafeHandles;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductContainerFolderBindingProbeError
{
    None,
    InvalidTarget,
    Missing,
    AccessDenied,
    NotDirectory,
    Unavailable,
}

public sealed record ProductContainerFolderBindingProbeResult(
    ProductContainerFolderBindingProbeError Error,
    string? CanonicalTarget,
    FileSystemObjectIdentity? Identity)
{
    public bool IsSuccess =>
        Error == ProductContainerFolderBindingProbeError.None
        && CanonicalTarget is not null
        && Identity is not null;
}

public static class WindowsProductContainerFolderBinding
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;

    public static ProductContainerFolderBindingProbeResult Probe(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || !Path.IsPathFullyQualified(target))
        {
            return Failure(ProductContainerFolderBindingProbeError.InvalidTarget);
        }

        string canonicalTarget;
        try
        {
            canonicalTarget = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(target));
            if (!Directory.Exists(canonicalTarget))
            {
                return File.Exists(canonicalTarget)
                    ? Failure(ProductContainerFolderBindingProbeError.NotDirectory)
                    : Failure(ProductContainerFolderBindingProbeError.Missing);
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(ProductContainerFolderBindingProbeError.InvalidTarget);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(ProductContainerFolderBindingProbeError.AccessDenied);
        }

        using SafeFileHandle handle = CreateFileW(
            canonicalTarget,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            nint.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            nint.Zero);
        if (handle.IsInvalid)
        {
            return Failure(MapWin32Error(Marshal.GetLastWin32Error()));
        }

        if (!GetFileInformationByHandleEx(
            handle,
            FileIdInfoClass,
            out FileIdInfo information,
            (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            return Failure(MapWin32Error(Marshal.GetLastWin32Error()));
        }

        Span<byte> fileId = stackalloc byte[16];
        BitConverter.TryWriteBytes(fileId, information.FileIdLow);
        BitConverter.TryWriteBytes(fileId[8..], information.FileIdHigh);
        return new(
            ProductContainerFolderBindingProbeError.None,
            canonicalTarget,
            FileSystemObjectIdentity.Create(
                information.VolumeSerialNumber,
                fileId));
    }

    public static ProductContainerFolderBindingState CreateResolved(
        ProductContainerFolderBindingProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!probe.IsSuccess)
        {
            throw new ArgumentException(
                "A successful folder probe is required.",
                nameof(probe));
        }

        return new()
        {
            PersistedTarget = probe.CanonicalTarget!,
            VolumeSerialNumber = probe.Identity!.VolumeSerialNumber,
            FileId = probe.Identity.FileId,
            Resolution = ProductContainerFolderBindingResolution.Resolved,
            ResolvedTarget = probe.CanonicalTarget,
        };
    }

    public static ProductContainerFolderBindingState Resolve(
        ProductContainerFolderBindingState binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ProductContainerFolderBindingProbeResult probe = Probe(
            binding.PersistedTarget);
        ProductContainerFolderBindingResolution resolution = probe.Error switch
        {
            ProductContainerFolderBindingProbeError.None
                when probe.Identity!.VolumeSerialNumber != binding.VolumeSerialNumber
                    || !string.Equals(
                        probe.Identity.FileId,
                        binding.FileId,
                        StringComparison.OrdinalIgnoreCase) =>
                ProductContainerFolderBindingResolution.Replaced,
            ProductContainerFolderBindingProbeError.None =>
                ProductContainerFolderBindingResolution.Resolved,
            ProductContainerFolderBindingProbeError.Missing =>
                ProductContainerFolderBindingResolution.Missing,
            ProductContainerFolderBindingProbeError.AccessDenied =>
                ProductContainerFolderBindingResolution.AccessDenied,
            ProductContainerFolderBindingProbeError.InvalidTarget
                or ProductContainerFolderBindingProbeError.NotDirectory =>
                ProductContainerFolderBindingResolution.InvalidTarget,
            _ => ProductContainerFolderBindingResolution.Unavailable,
        };
        return binding with
        {
            Resolution = resolution,
            ResolvedTarget = resolution ==
                ProductContainerFolderBindingResolution.Resolved
                    ? probe.CanonicalTarget
                    : null,
        };
    }

    public static ProductWorkspaceState ResolveWorkspace(
        ProductWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with
        {
            Containers = state.Containers
                .Select(container => container with
                {
                    FolderBinding = container.FolderBinding is null
                        ? null
                        : Resolve(container.FolderBinding),
                })
                .ToArray(),
        };
    }

    private static ProductContainerFolderBindingProbeResult Failure(
        ProductContainerFolderBindingProbeError error) =>
        new(error, null, null);

    private static ProductContainerFolderBindingProbeError MapWin32Error(int error) =>
        error switch
        {
            ErrorFileNotFound or ErrorPathNotFound =>
                ProductContainerFolderBindingProbeError.Missing,
            ErrorAccessDenied => ProductContainerFolderBindingProbeError.AccessDenied,
            _ => ProductContainerFolderBindingProbeError.Unavailable,
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);
}
