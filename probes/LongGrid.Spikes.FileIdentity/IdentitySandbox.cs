using LongGrid.Core.DesktopItems;

internal static class IdentitySandbox
{
    public static SandboxIdentityResult Run()
    {
        string ownedRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "LongGrid-P0-01c"));
        string sandbox = Path.Combine(ownedRoot, Guid.NewGuid().ToString("N"));
        bool fileRenamePreservedIdentity = false;
        bool directoryRenamePreservedIdentity = false;
        bool copyCreatedNewIdentity = false;
        bool cleanupSucceeded = false;

        try
        {
            Directory.CreateDirectory(sandbox);

            string originalFile = Path.Combine(sandbox, "before.txt");
            string renamedFile = Path.Combine(sandbox, "after.txt");
            string copiedFile = Path.Combine(sandbox, "copy.txt");
            File.WriteAllText(originalFile, "Long Grid P0-01c identity sandbox.");

            FileSystemObjectIdentity fileBefore = ReadRequired(originalFile);
            File.Move(originalFile, renamedFile);
            FileSystemObjectIdentity fileAfter = ReadRequired(renamedFile);
            File.Copy(renamedFile, copiedFile);
            FileSystemObjectIdentity copy = ReadRequired(copiedFile);

            string originalDirectory = Path.Combine(sandbox, "directory-before");
            string renamedDirectory = Path.Combine(sandbox, "directory-after");
            Directory.CreateDirectory(originalDirectory);
            FileSystemObjectIdentity directoryBefore = ReadRequired(originalDirectory);
            Directory.Move(originalDirectory, renamedDirectory);
            FileSystemObjectIdentity directoryAfter = ReadRequired(renamedDirectory);

            fileRenamePreservedIdentity = fileBefore == fileAfter;
            directoryRenamePreservedIdentity = directoryBefore == directoryAfter;
            copyCreatedNewIdentity = fileAfter != copy;
        }
        finally
        {
            string canonicalSandbox = Path.GetFullPath(sandbox);
            string requiredPrefix = ownedRoot.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (canonicalSandbox.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(canonicalSandbox))
            {
                try
                {
                    Directory.Delete(canonicalSandbox, recursive: true);
                    cleanupSucceeded = true;
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException)
                {
                    cleanupSucceeded = false;
                }
            }

            if (!cleanupSucceeded && Directory.Exists(canonicalSandbox))
            {
                Console.Error.WriteLine("P0-01c temporary sandbox cleanup failed.");
            }
        }

        return new SandboxIdentityResult(
            fileRenamePreservedIdentity,
            directoryRenamePreservedIdentity,
            copyCreatedNewIdentity,
            cleanupSucceeded);
    }

    private static FileSystemObjectIdentity ReadRequired(string path)
    {
        FileIdentityReadResult result = WindowsFileIdentityReader.TryRead(path);
        return result.Identity
            ?? throw new IOException(
                $"File identity read failed with Win32 error {result.Win32Error}.");
    }
}

internal sealed record SandboxIdentityResult(
    bool FileRenamePreservedIdentity,
    bool DirectoryRenamePreservedIdentity,
    bool CopyCreatedNewIdentity,
    bool CleanupSucceeded);
