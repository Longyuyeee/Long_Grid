using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

internal static class RestrictedThumbnailTokenProbe
{
    private const uint DisableMaxPrivilege = 0x1;
    private const uint SeGroupIntegrity = 0x20;
    private const uint TokenAdjustDefault = 0x0080;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenImpersonate = 0x0004;
    private const uint TokenQuery = 0x0008;
    private const int SecurityImpersonation = 2;
    private const int TokenImpersonation = 2;
    private const int TokenIntegrityLevel = 25;
    internal const int LowIntegrityRid = 0x1000;

    internal static RestrictedThumbnailTokenResult Run(
        string ownedInputPath,
        string sandboxRoot)
    {
        bool restrictedTokenCreated = false;
        bool lowIntegrityObserved = false;
        bool ownedInputReadSucceeded = false;
        bool mediumSandboxWriteBlocked = false;
        bool parentWriteControlSucceeded = false;
        string deniedWritePath = Path.Combine(sandboxRoot, "low-write.tmp");
        string controlWritePath = Path.Combine(sandboxRoot, "parent-write.tmp");

        try
        {
            using SafeAccessTokenHandle restrictedToken =
                CreateLowIntegrityPrimaryToken();
            restrictedTokenCreated = true;
            using SafeAccessTokenHandle impersonationToken = DuplicateForImpersonation(
                restrictedToken);
            lowIntegrityObserved = GetIntegrityRid(impersonationToken)
                == LowIntegrityRid;

            WindowsIdentity.RunImpersonated(impersonationToken, () =>
            {
                ownedInputReadSucceeded = File.ReadAllBytes(ownedInputPath).Length > 0;
                try
                {
                    File.WriteAllText(deniedWritePath, "must-not-write");
                }
                catch (UnauthorizedAccessException)
                {
                    mediumSandboxWriteBlocked = true;
                }
            });
        }
        finally
        {
            TryDeleteFile(deniedWritePath);
            try
            {
                File.WriteAllText(controlWritePath, "control");
                parentWriteControlSucceeded = File.Exists(controlWritePath);
            }
            finally
            {
                TryDeleteFile(controlWritePath);
            }
        }

        return new RestrictedThumbnailTokenResult(
            restrictedTokenCreated,
            lowIntegrityObserved,
            ownedInputReadSucceeded,
            mediumSandboxWriteBlocked,
            parentWriteControlSucceeded,
            RestrictedTokenFlags: "DISABLE_MAX_PRIVILEGE");
    }

    internal static SafeAccessTokenHandle CreateLowIntegrityPrimaryToken()
    {
        if (!OpenProcessToken(
            GetCurrentProcess(),
            TokenAssignPrimary | TokenDuplicate | TokenQuery | TokenAdjustDefault,
            out SafeAccessTokenHandle processToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (processToken)
        {
            if (!CreateRestrictedToken(
                processToken,
                DisableMaxPrivilege,
                0,
                nint.Zero,
                0,
                nint.Zero,
                0,
                nint.Zero,
                out SafeAccessTokenHandle restrictedToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                SetLowIntegrity(restrictedToken);
                return restrictedToken;
            }
            catch
            {
                restrictedToken.Dispose();
                throw;
            }
        }
    }

    private static void SetLowIntegrity(SafeAccessTokenHandle token)
    {
        if (!ConvertStringSidToSid("S-1-16-4096", out nint integritySid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var label = new TokenMandatoryLabel
            {
                Label = new SidAndAttributes
                {
                    Sid = integritySid,
                    Attributes = SeGroupIntegrity,
                },
            };
            int informationLength = checked(
                Marshal.SizeOf<TokenMandatoryLabel>() + GetLengthSid(integritySid));
            if (!SetTokenInformation(
                token,
                TokenIntegrityLevel,
                ref label,
                informationLength))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            _ = LocalFree(integritySid);
        }
    }

    private static SafeAccessTokenHandle DuplicateForImpersonation(
        SafeAccessTokenHandle restrictedToken)
    {
        if (!DuplicateTokenEx(
            restrictedToken,
            TokenQuery | TokenImpersonate,
            nint.Zero,
            SecurityImpersonation,
            TokenImpersonation,
            out SafeAccessTokenHandle impersonationToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return impersonationToken;
    }

    internal static int GetIntegrityRid(SafeAccessTokenHandle token)
    {
        _ = GetTokenInformation(
            token,
            TokenIntegrityLevel,
            nint.Zero,
            0,
            out int requiredLength);
        if (requiredLength <= 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        nint buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            if (!GetTokenInformation(
                token,
                TokenIntegrityLevel,
                buffer,
                requiredLength,
                out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            TokenMandatoryLabel label =
                Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            byte subAuthorityCount = Marshal.ReadByte(
                GetSidSubAuthorityCount(label.Label.Sid));
            if (subAuthorityCount == 0)
            {
                throw new InvalidOperationException(
                    "The token integrity SID has no sub-authority.");
            }

            nint integrityRid = GetSidSubAuthority(
                label.Label.Sid,
                (uint)(subAuthorityCount - 1));
            return Marshal.ReadInt32(integrityRid);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static int GetProcessIntegrityRid(nint processHandle)
    {
        if (!OpenProcessToken(
            processHandle,
            TokenQuery,
            out SafeAccessTokenHandle processToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (processToken)
        {
            return GetIntegrityRid(processToken);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        internal nint Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        internal SidAndAttributes Label;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existingTokenHandle,
        uint flags,
        uint disableSidCount,
        nint sidsToDisable,
        uint deletePrivilegeCount,
        nint privilegesToDelete,
        uint restrictedSidCount,
        nint sidsToRestrict,
        out SafeAccessTokenHandle newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        ref TokenMandatoryLabel tokenInformation,
        int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle existingTokenHandle,
        uint desiredAccess,
        nint tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeAccessTokenHandle newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSid(
        string stringSid,
        out nint sid);

    [DllImport("advapi32.dll")]
    private static extern int GetLengthSid(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthority);
}

internal sealed record RestrictedThumbnailTokenResult(
    bool RestrictedTokenCreated,
    bool LowIntegrityObserved,
    bool OwnedInputReadSucceeded,
    bool MediumSandboxWriteBlocked,
    bool ParentWriteControlSucceeded,
    string RestrictedTokenFlags);
