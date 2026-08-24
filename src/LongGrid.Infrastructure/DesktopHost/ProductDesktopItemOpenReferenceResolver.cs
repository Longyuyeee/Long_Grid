using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopItemOpenReferenceResolution(
    ProductDesktopItemOpenStatus Status,
    string? Target = null,
    string? Parameters = null)
{
    internal bool IsResolved => Status == ProductDesktopItemOpenStatus.LaunchAccepted
        && !string.IsNullOrWhiteSpace(Target);
}

internal interface IProductDesktopItemOpenReferenceResolver
{
    ProductDesktopItemOpenReferenceResolution Resolve(
        ConfigurationItemKind kind,
        string referencePath);
}

internal sealed class WindowsProductDesktopItemOpenReferenceResolver
    : IProductDesktopItemOpenReferenceResolver
{
    private const long MaximumShortcutBytes = 1024 * 1024;
    private const long MaximumInternetShortcutBytes = 64 * 1024;
    private const int MaximumArgumentsLength = 4096;
    private const int MaximumUrlLength = 8192;
    private const int MaximumUrlLines = 128;

    public ProductDesktopItemOpenReferenceResolution Resolve(
        ConfigurationItemKind kind,
        string referencePath)
    {
        if (!OperatingSystem.IsWindows()
            || !Path.IsPathFullyQualified(referencePath)
            || kind is not (ConfigurationItemKind.Shortcut
                or ConfigurationItemKind.Url))
        {
            return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
        }

        FileInfo file;
        long originalLength;
        DateTime originalLastWriteUtc;
        byte[] originalHash;
        try
        {
            file = new(referencePath);
            if (!file.Exists
                || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Failed(ProductDesktopItemOpenStatus.TargetUnavailable);
            }
            long maximum = kind == ConfigurationItemKind.Shortcut
                ? MaximumShortcutBytes
                : MaximumInternetShortcutBytes;
            if (file.Length <= 0)
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }
            if (file.Length > maximum)
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceTooLarge);
            }
            originalLength = file.Length;
            originalLastWriteUtc = file.LastWriteTimeUtc;
            originalHash = SHA256.HashData(File.ReadAllBytes(referencePath));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed(ProductDesktopItemOpenStatus.TargetUnavailable);
        }

        ProductDesktopItemOpenReferenceResolution resolved =
            kind == ConfigurationItemKind.Shortcut
            ? ResolveShortcut(referencePath)
            : ResolveInternetShortcut(referencePath);
        try
        {
            file.Refresh();
            if (!file.Exists
                || file.Length != originalLength
                || file.LastWriteTimeUtc != originalLastWriteUtc
                || (file.Attributes & FileAttributes.ReparsePoint) != 0
                || !CryptographicOperations.FixedTimeEquals(
                    originalHash,
                    SHA256.HashData(File.ReadAllBytes(referencePath))))
            {
                return Failed(ProductDesktopItemOpenStatus.TargetUnavailable);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed(ProductDesktopItemOpenStatus.TargetUnavailable);
        }
        return resolved;
    }

    private static ProductDesktopItemOpenReferenceResolution ResolveShortcut(
        string referencePath)
    {
        if (!string.Equals(
            Path.GetExtension(referencePath),
            ".lnk",
            StringComparison.OrdinalIgnoreCase))
        {
            return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
        }

        object? shellLinkObject = null;
        try
        {
            Type? shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId);
            shellLinkObject = shellLinkType is null
                ? null
                : Activator.CreateInstance(shellLinkType);
            if (shellLinkObject is not IShellLinkW shellLink
                || shellLinkObject is not IPersistFile persistFile)
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }

            persistFile.Load(referencePath, (int)StorageMode.Read);
            var target = new StringBuilder(32768);
            int pathResult = shellLink.GetPath(
                target,
                target.Capacity,
                nint.Zero,
                ShellLinkGetPathFlags.RawPath);
            var arguments = new StringBuilder(32768);
            int argumentsResult = shellLink.GetArguments(
                arguments,
                arguments.Capacity);
            if (pathResult < 0 || argumentsResult < 0)
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }

            string resolvedTarget = Environment.ExpandEnvironmentVariables(
                target.ToString().Trim());
            string resolvedArguments = arguments.ToString();
            if (!Path.IsPathFullyQualified(resolvedTarget)
                || resolvedArguments.Length > MaximumArgumentsLength
                || resolvedArguments.Any(char.IsControl))
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }

            resolvedTarget = Path.GetFullPath(resolvedTarget);
            bool file = File.Exists(resolvedTarget);
            bool directory = Directory.Exists(resolvedTarget);
            if (!file && !directory)
            {
                return Failed(
                    ProductDesktopItemOpenStatus.ShortcutTargetUnavailable);
            }
            if (directory && !string.IsNullOrWhiteSpace(resolvedArguments))
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }
            if ((File.GetAttributes(resolvedTarget)
                    & FileAttributes.ReparsePoint) != 0)
            {
                return Failed(
                    ProductDesktopItemOpenStatus.ShortcutTargetUnsafe);
            }
            string extension = Path.GetExtension(resolvedTarget);
            if (file
                && (string.Equals(extension, ".lnk",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".url",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return Failed(
                    ProductDesktopItemOpenStatus.ShortcutTargetUnsafe);
            }
            return new(
                ProductDesktopItemOpenStatus.LaunchAccepted,
                resolvedTarget,
                string.IsNullOrWhiteSpace(resolvedArguments)
                    ? null
                    : resolvedArguments);
        }
        catch (Exception exception) when (
            exception is COMException
                or ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
        }
        finally
        {
            if (shellLinkObject is not null
                && Marshal.IsComObject(shellLinkObject))
            {
                _ = Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }

    private static ProductDesktopItemOpenReferenceResolution
        ResolveInternetShortcut(string referencePath)
    {
        if (!string.Equals(
            Path.GetExtension(referencePath),
            ".url",
            StringComparison.OrdinalIgnoreCase))
        {
            return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(referencePath);
            if (bytes.Length <= 0
                || bytes.Length > MaximumInternetShortcutBytes)
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceTooLarge);
            }
            string text = DecodeInternetShortcut(bytes);
            string? url = null;
            bool inSection = false;
            int lines = 0;
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } rawLine)
            {
                if (++lines > MaximumUrlLines)
                {
                    return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
                }
                string line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    inSection = string.Equals(
                        line,
                        "[InternetShortcut]",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection
                    || !line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (url is not null)
                {
                    return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
                }
                url = line[4..].Trim();
            }

            if (string.IsNullOrWhiteSpace(url)
                || url.Length > MaximumUrlLength
                || url.Any(char.IsControl)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failed(ProductDesktopItemOpenStatus.ProtocolRejected);
            }
            if (string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
            }
            return new(
                ProductDesktopItemOpenStatus.LaunchAccepted,
                uri.AbsoluteUri);
        }
        catch (Exception exception) when (
            exception is DecoderFallbackException
                or IOException
                or UnauthorizedAccessException)
        {
            return Failed(ProductDesktopItemOpenStatus.ReferenceMalformed);
        }
    }

    private static string DecodeInternetShortcut(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true,
                throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        }
        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);
    }

    private static ProductDesktopItemOpenReferenceResolution Failed(
        ProductDesktopItemOpenStatus status) => new(status);

    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    [Flags]
    private enum StorageMode
    {
        Read = 0,
    }

    [Flags]
    private enum ShellLinkGetPathFlags : uint
    {
        RawPath = 0x00000004,
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumPath,
            nint findData,
            ShellLinkGetPathFlags flags);
        [PreserveSig] int GetIdList(out nint idList);
        [PreserveSig] int SetIdList(nint idList);
        [PreserveSig]
        int GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maximumName);
        [PreserveSig]
        int SetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig]
        int GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int maximumDirectory);
        [PreserveSig]
        int SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig]
        int GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int maximumArguments);
        [PreserveSig]
        int SetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCommand(out int showCommand);
        [PreserveSig] int SetShowCommand(int showCommand);
        [PreserveSig]
        int GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int maximumIconPath,
            out int iconIndex);
        [PreserveSig]
        int SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
            int iconIndex);
        [PreserveSig]
        int SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            uint reserved);
        [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig]
        int SetPath(
            [MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
