using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

internal static class ProductWorkspaceIdentityPolicy
{
    private const string FileSystemProvider = "filesystem";

    public static bool IsSupportedProvider(string? provider) =>
        string.Equals(
            provider,
            FileSystemProvider,
            StringComparison.OrdinalIgnoreCase);

    public static bool HasConsistentOptionalFileIdentity(
        DesktopItemIdentity identity) =>
        string.IsNullOrWhiteSpace(identity.VolumeId)
            == string.IsNullOrWhiteSpace(identity.FileId);

    public static bool TryNormalizeCanonicalTarget(
        string? target,
        out string? canonicalTarget)
    {
        canonicalTarget = null;
        if (string.IsNullOrWhiteSpace(target)
            || !Path.IsPathFullyQualified(target))
        {
            return false;
        }

        try
        {
            canonicalTarget = Path.GetFullPath(target);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    public static ConfigurationItemKind MapKind(DesktopItemKind kind) =>
        kind switch
        {
            DesktopItemKind.File => ConfigurationItemKind.File,
            DesktopItemKind.Directory => ConfigurationItemKind.Folder,
            DesktopItemKind.Shortcut => ConfigurationItemKind.Shortcut,
            DesktopItemKind.InternetShortcut => ConfigurationItemKind.Url,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
