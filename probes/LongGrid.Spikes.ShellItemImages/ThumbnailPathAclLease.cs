using System.Security.AccessControl;
using System.Security.Principal;

internal sealed class ThumbnailPathAclLease : IDisposable
{
    private readonly FileInfo _file;
    private readonly DirectoryInfo _directory;
    private readonly FileSystemAccessRule _fileRule;
    private readonly FileSystemAccessRule _directoryRule;
    private readonly SecurityIdentifier _appContainerIdentity;
    private readonly Action<bool> _onDisposed;
    private bool _disposed;

    private ThumbnailPathAclLease(
        FileInfo file,
        DirectoryInfo directory,
        FileSystemAccessRule fileRule,
        FileSystemAccessRule directoryRule,
        SecurityIdentifier appContainerIdentity,
        Action<bool> onDisposed)
    {
        _file = file;
        _directory = directory;
        _fileRule = fileRule;
        _directoryRule = directoryRule;
        _appContainerIdentity = appContainerIdentity;
        _onDisposed = onDisposed;
    }

    internal static ThumbnailPathAclLease Create(
        string sourcePath,
        SecurityIdentifier appContainerIdentity,
        Action<bool> onDisposed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(appContainerIdentity);
        ArgumentNullException.ThrowIfNull(onDisposed);
        string fullPath = Path.GetFullPath(sourcePath);
        var file = new FileInfo(fullPath);
        file.Refresh();
        if (!file.Exists
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || file.Length > ThumbnailAppContainerProfile.MaximumBrokeredInputBytes)
        {
            throw new IOException(
                "The minimum-ACL thumbnail input must be a regular file no "
                + $"larger than "
                + $"{ThumbnailAppContainerProfile.MaximumBrokeredInputBytes} bytes.");
        }

        var directory = file.Directory
            ?? throw new InvalidOperationException(
                "The thumbnail input directory is unavailable.");
        var directoryRule = new FileSystemAccessRule(
            appContainerIdentity,
            FileSystemRights.Traverse,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow);
        var fileRule = new FileSystemAccessRule(
            appContainerIdentity,
            FileSystemRights.Read,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow);

        bool directoryGranted = false;
        try
        {
            DirectorySecurity directorySecurity =
                FileSystemAclExtensions.GetAccessControl(directory);
            directorySecurity.AddAccessRule(directoryRule);
            FileSystemAclExtensions.SetAccessControl(directory, directorySecurity);
            directoryGranted = true;

            FileSecurity fileSecurity =
                FileSystemAclExtensions.GetAccessControl(file);
            fileSecurity.AddAccessRule(fileRule);
            FileSystemAclExtensions.SetAccessControl(file, fileSecurity);
            return new ThumbnailPathAclLease(
                file,
                directory,
                fileRule,
                directoryRule,
                appContainerIdentity,
                onDisposed);
        }
        catch
        {
            if (directoryGranted)
            {
                RemoveDirectoryRule(directory, directoryRule);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        bool restored = true;
        try
        {
            FileSecurity fileSecurity =
                FileSystemAclExtensions.GetAccessControl(_file);
            fileSecurity.RemoveAccessRuleSpecific(_fileRule);
            FileSystemAclExtensions.SetAccessControl(_file, fileSecurity);
            restored &= !HasExplicitRule(
                FileSystemAclExtensions.GetAccessControl(_file),
                _appContainerIdentity);
        }
        catch (IOException)
        {
            restored = false;
        }
        catch (UnauthorizedAccessException)
        {
            restored = false;
        }

        try
        {
            RemoveDirectoryRule(_directory, _directoryRule);
            restored &= !HasExplicitRule(
                FileSystemAclExtensions.GetAccessControl(_directory),
                _appContainerIdentity);
        }
        catch (IOException)
        {
            restored = false;
        }
        catch (UnauthorizedAccessException)
        {
            restored = false;
        }

        _disposed = true;
        _onDisposed(restored);
    }

    private static void RemoveDirectoryRule(
        DirectoryInfo directory,
        FileSystemAccessRule rule)
    {
        DirectorySecurity security =
            FileSystemAclExtensions.GetAccessControl(directory);
        security.RemoveAccessRuleSpecific(rule);
        FileSystemAclExtensions.SetAccessControl(directory, security);
    }

    private static bool HasExplicitRule(
        FileSystemSecurity security,
        SecurityIdentifier identity) => security
        .GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier))
        .Cast<FileSystemAccessRule>()
        .Any(rule => identity.Equals(rule.IdentityReference));
}
