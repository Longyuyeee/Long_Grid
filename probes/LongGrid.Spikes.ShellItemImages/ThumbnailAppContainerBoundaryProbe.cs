using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class ThumbnailAppContainerBoundaryProbe
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint TokenQuery = 0x0008;
    private const int TokenIsAppContainer = 29;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint ProbeTimeoutMilliseconds = 10_000;
    private static readonly nuint ProcThreadAttributeSecurityCapabilities =
        0x00020009;
    private static readonly nuint ProcThreadAttributeHandleList = 0x00020002;

    internal static ThumbnailAppContainerBoundaryResult Run(
        string unbrokeredReadPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unbrokeredReadPath);
        string profileName = $"LongGridP003b{Guid.NewGuid():N}";
        nint appContainerSid = nint.Zero;
        bool profileCreated = false;
        bool profileDeleted = false;
        ThumbnailAppContainerBoundaryResult? result = null;

        try
        {
            ThrowIfFailed(CreateAppContainerProfile(
                profileName,
                "Long Grid P0-03b probe",
                "Ephemeral no-capability thumbnail isolation probe",
                capabilities: nint.Zero,
                capabilityCount: 0,
                out appContainerSid));
            profileCreated = true;

            var appContainerIdentity = new SecurityIdentifier(appContainerSid);
            string sandboxPath = Path.GetDirectoryName(unbrokeredReadPath)
                ?? throw new InvalidOperationException(
                    "The AppContainer sandbox path is unavailable.");
            GrantDirectoryAccess(
                sandboxPath,
                appContainerIdentity,
                FileSystemRights.Traverse,
                InheritanceFlags.None);
            string brokerControlPath = Directory.CreateDirectory(
                Path.Combine(sandboxPath, "appcontainer-broker-control")).FullName;
            GrantDirectoryAccess(
                brokerControlPath,
                appContainerIdentity,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            string allowedReadPath = Path.Combine(
                brokerControlPath,
                "allowed-control.txt");
            File.WriteAllText(allowedReadPath, "exit /b 0");

            using ThumbnailWorkerJob job = ThumbnailWorkerJob.Create();
            AppContainerReadAttempt noOp = LaunchCommand(
                "exit /b 0",
                appContainerSid,
                job,
                executeCommand: true);
            AppContainerReadAttempt allowed = LaunchReadAttempt(
                allowedReadPath,
                appContainerSid,
                job);
            AppContainerReadAttempt denied = LaunchReadAttempt(
                unbrokeredReadPath,
                appContainerSid,
                job);

            result = new ThumbnailAppContainerBoundaryResult(
                ProfileCreated: true,
                ZeroCapabilities: true,
                NoOpSucceeded: noOp.IsAppContainer && noOp.ExitCode == 0,
                ControlReadSucceeded:
                    allowed.IsAppContainer && allowed.ExitCode == 0,
                UnbrokeredReadBlocked:
                    denied.IsAppContainer && denied.ExitCode != 0,
                AllProcessesAppContainer:
                    noOp.IsAppContainer
                    && allowed.IsAppContainer
                    && denied.IsAppContainer,
                ProcessesAssignedBeforeResume: true,
                ProfileDeleted: false,
                NoOpExitCode: noOp.ExitCode,
                ControlExitCode: allowed.ExitCode,
                UnbrokeredExitCode: denied.ExitCode);
        }
        finally
        {
            if (appContainerSid != nint.Zero)
            {
                _ = FreeSid(appContainerSid);
            }

            if (profileCreated)
            {
                profileDeleted = DeleteAppContainerProfile(profileName) >= 0;
            }

        }

        return (result
            ?? throw new InvalidOperationException(
                "The AppContainer boundary result was not created.")) with
        {
            ProfileDeleted = profileDeleted,
        };
    }

    private static AppContainerReadAttempt LaunchReadAttempt(
        string path,
        nint appContainerSid,
        ThumbnailWorkerJob job) =>
        LaunchCommand(
            $"type {QuoteArgument(path)}",
            appContainerSid,
            job,
            executeCommand: true);

    private static AppContainerReadAttempt LaunchCommand(
        string command,
        nint appContainerSid,
        ThumbnailWorkerJob job,
        bool executeCommand)
    {
        string applicationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        nint attributeList = nint.Zero;
        nint securityCapabilitiesPointer = nint.Zero;
        nint inheritedHandleList = nint.Zero;
        nint commandLine = nint.Zero;
        SafeFileHandle? childStandardInput = null;
        SafeFileHandle? childStandardOutput = null;
        ProcessInformation processInformation = default;
        bool processCreated = false;

        try
        {
            childStandardInput = OpenInheritedNull(GenericRead);
            childStandardOutput = OpenInheritedNull(GenericWrite);
            attributeList = CreateAttributeList(
                appContainerSid,
                childStandardInput,
                childStandardOutput,
                out securityCapabilitiesPointer,
                out inheritedHandleList);
            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardInput = childStandardInput.DangerousGetHandle(),
                    StandardOutput = childStandardOutput.DangerousGetHandle(),
                    StandardError = childStandardOutput.DangerousGetHandle(),
                },
                AttributeList = attributeList,
            };
            commandLine = Marshal.StringToHGlobalUni(
                $"{QuoteArgument(applicationPath)} /d /q "
                + $"{(executeCommand ? "/c " : string.Empty)}{command}");
            if (!CreateProcess(
                applicationPath,
                commandLine,
                processAttributes: nint.Zero,
                threadAttributes: nint.Zero,
                inheritHandles: true,
                CreateNoWindow | CreateSuspended | ExtendedStartupInfoPresent,
                environment: nint.Zero,
                currentDirectory: Path.GetDirectoryName(applicationPath),
                ref startupInfo,
                out processInformation))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The AppContainer boundary process did not start.");
            }

            processCreated = true;
            job.Assign(processInformation.Process);
            bool isAppContainer = IsAppContainerProcess(
                processInformation.Process);
            if (ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The AppContainer boundary process did not resume.");
            }

            uint waitResult = WaitForSingleObject(
                processInformation.Process,
                ProbeTimeoutMilliseconds);
            if (waitResult == WaitTimeout)
            {
                _ = TerminateProcess(processInformation.Process, 1);
                throw new TimeoutException(
                    "The AppContainer boundary process did not exit.");
            }

            if (waitResult != WaitObject0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Waiting for the AppContainer boundary process failed.");
            }

            if (!GetExitCodeProcess(
                processInformation.Process,
                out uint exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new AppContainerReadAttempt(isAppContainer, exitCode);
        }
        catch
        {
            if (processCreated)
            {
                _ = TerminateProcess(processInformation.Process, 1);
            }

            throw;
        }
        finally
        {
            if (processInformation.Thread != nint.Zero)
            {
                _ = CloseHandle(processInformation.Thread);
            }

            if (processInformation.Process != nint.Zero)
            {
                _ = CloseHandle(processInformation.Process);
            }

            if (attributeList != nint.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (securityCapabilitiesPointer != nint.Zero)
            {
                Marshal.FreeHGlobal(securityCapabilitiesPointer);
            }

            if (inheritedHandleList != nint.Zero)
            {
                Marshal.FreeHGlobal(inheritedHandleList);
            }

            childStandardInput?.Dispose();
            childStandardOutput?.Dispose();

            if (commandLine != nint.Zero)
            {
                Marshal.FreeHGlobal(commandLine);
            }
        }
    }

    private static nint CreateAttributeList(
        nint appContainerSid,
        SafeFileHandle standardInput,
        SafeFileHandle standardOutput,
        out nint securityCapabilitiesPointer,
        out nint inheritedHandleList)
    {
        nuint attributeListSize = 0;
        _ = InitializeProcThreadAttributeList(
            nint.Zero,
            attributeCount: 2,
            flags: 0,
            ref attributeListSize);
        nint attributeList = Marshal.AllocHGlobal(
            checked((int)attributeListSize));
        securityCapabilitiesPointer = nint.Zero;
        inheritedHandleList = nint.Zero;

        try
        {
            if (!InitializeProcThreadAttributeList(
                attributeList,
                attributeCount: 2,
                flags: 0,
                ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var securityCapabilities = new SecurityCapabilities
            {
                AppContainerSid = appContainerSid,
                Capabilities = nint.Zero,
                CapabilityCount = 0,
                Reserved = 0,
            };
            securityCapabilitiesPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(
                securityCapabilities,
                securityCapabilitiesPointer,
                fDeleteOld: false);
            if (!UpdateProcThreadAttribute(
                attributeList,
                flags: 0,
                ProcThreadAttributeSecurityCapabilities,
                securityCapabilitiesPointer,
                (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                previousValue: nint.Zero,
                returnSize: nint.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            nint[] handles =
            [
                standardInput.DangerousGetHandle(),
                standardOutput.DangerousGetHandle(),
            ];
            inheritedHandleList = Marshal.AllocHGlobal(
                checked(nint.Size * handles.Length));
            Marshal.Copy(handles, 0, inheritedHandleList, handles.Length);
            if (!UpdateProcThreadAttribute(
                attributeList,
                flags: 0,
                ProcThreadAttributeHandleList,
                inheritedHandleList,
                (nuint)(nint.Size * handles.Length),
                previousValue: nint.Zero,
                returnSize: nint.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return attributeList;
        }
        catch
        {
            Marshal.FreeHGlobal(attributeList);
            if (securityCapabilitiesPointer != nint.Zero)
            {
                Marshal.FreeHGlobal(securityCapabilitiesPointer);
                securityCapabilitiesPointer = nint.Zero;
            }

            if (inheritedHandleList != nint.Zero)
            {
                Marshal.FreeHGlobal(inheritedHandleList);
                inheritedHandleList = nint.Zero;
            }

            throw;
        }
    }

    private static SafeFileHandle OpenInheritedNull(uint desiredAccess)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        SafeFileHandle handle = CreateFile(
            "NUL",
            desiredAccess,
            FileShareRead | FileShareWrite,
            ref attributes,
            OpenExisting,
            flagsAndAttributes: 0,
            templateFile: nint.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }

    private static bool IsAppContainerProcess(nint processHandle)
    {
        if (!OpenProcessToken(
            processHandle,
            TokenQuery,
            out SafeAccessTokenHandle token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (token)
        {
            if (!GetTokenInformation(
                token,
                TokenIsAppContainer,
                out int isAppContainer,
                sizeof(int),
                out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return isAppContainer != 0;
        }
    }

    private static void GrantDirectoryAccess(
        string directoryPath,
        SecurityIdentifier appContainerIdentity,
        FileSystemRights rights,
        InheritanceFlags inheritanceFlags)
    {
        var directory = new DirectoryInfo(directoryPath);
        DirectorySecurity security = FileSystemAclExtensions.GetAccessControl(
            directory);
        security.AddAccessRule(new FileSystemAccessRule(
            appContainerIdentity,
            rights,
            inheritanceFlags,
            PropagationFlags.None,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(directory, security);
    }

    private static string QuoteArgument(string argument)
    {
        var quoted = new StringBuilder(argument.Length + 2);
        quoted.Append('"');
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                quoted.Append('\\', (backslashes * 2) + 1);
                quoted.Append(character);
                backslashes = 0;
                continue;
            }

            quoted.Append('\\', backslashes);
            backslashes = 0;
            quoted.Append(character);
        }

        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private static void ThrowIfFailed(int hResult)
    {
        if (hResult < 0)
        {
            Marshal.ThrowExceptionForHR(hResult);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        internal nint AppContainerSid;
        internal nint Capabilities;
        internal uint CapabilityCount;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal nint SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Cb;
        internal nint Reserved;
        internal nint Desktop;
        internal nint Title;
        internal uint X;
        internal uint Y;
        internal uint XSize;
        internal uint YSize;
        internal uint XCountChars;
        internal uint YCountChars;
        internal uint FillAttribute;
        internal uint Flags;
        internal ushort ShowWindow;
        internal ushort Reserved2;
        internal nint Reserved2Pointer;
        internal nint StandardInput;
        internal nint StandardOutput;
        internal nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal nint Process;
        internal nint Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    private sealed record AppContainerReadAttempt(
        bool IsAppContainer,
        uint ExitCode);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(
        string appContainerName,
        string displayName,
        string description,
        nint capabilities,
        uint capabilityCount,
        out nint appContainerSid);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeleteAppContainerProfile(string appContainerName);

    [DllImport("advapi32.dll")]
    private static extern nint FreeSid(nint sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SecurityAttributes securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        nint attributeList,
        int attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nuint attribute,
        nint value,
        nuint size,
        nint previousValue,
        nint returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(
        nint process,
        out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

internal sealed record ThumbnailAppContainerBoundaryResult(
    bool ProfileCreated,
    bool ZeroCapabilities,
    bool NoOpSucceeded,
    bool ControlReadSucceeded,
    bool UnbrokeredReadBlocked,
    bool AllProcessesAppContainer,
    bool ProcessesAssignedBeforeResume,
    bool ProfileDeleted,
    uint NoOpExitCode,
    uint ControlExitCode,
    uint UnbrokeredExitCode);
