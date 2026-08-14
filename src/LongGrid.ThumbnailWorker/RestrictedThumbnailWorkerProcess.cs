using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LongGrid.ThumbnailWorker;

internal sealed class AppContainerThumbnailWorkerProcess : IDisposable
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint TokenQuery = 0x0008;
    private const int TokenIsAppContainer = 29;
    private static readonly nuint ProcThreadAttributeHandleList = 0x00020002;
    private static readonly nuint ProcThreadAttributeSecurityCapabilities =
        0x00020009;

    private AppContainerThumbnailWorkerProcess(
        Process process,
        StreamWriter standardInput,
        StreamReader standardOutput,
        bool isAppContainer)
    {
        Process = process;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        IsAppContainer = isAppContainer;
    }

    internal Process Process { get; }

    internal StreamWriter StandardInput { get; }

    internal StreamReader StandardOutput { get; }

    internal bool IsAppContainer { get; }

    internal static AppContainerThumbnailWorkerProcess Start(
        ThumbnailWorkerJob workerJob,
        ThumbnailAppContainerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(workerJob);
        ArgumentNullException.ThrowIfNull(profile);
        SafeFileHandle? childStandardInput = null;
        SafeFileHandle? parentStandardInput = null;
        SafeFileHandle? childStandardOutput = null;
        SafeFileHandle? parentStandardOutput = null;
        SafeFileHandle? childStandardError = null;
        FileStream? inputStream = null;
        FileStream? outputStream = null;
        Process? process = null;
        nint attributeList = nint.Zero;
        nint securityCapabilitiesPointer = nint.Zero;
        nint inheritedHandleList = nint.Zero;
        nint commandLineBuffer = nint.Zero;
        ProcessInformation processInformation = default;
        bool processCreated = false;
        try
        {
            childStandardInput = CreatePipePair(
                parentReads: false,
                out parentStandardInput);
            childStandardOutput = CreatePipePair(
                parentReads: true,
                out parentStandardOutput);
            childStandardError = OpenInheritedNullOutput();
            attributeList = CreateAttributeList(
                profile.AppContainerSid,
                childStandardInput,
                childStandardOutput,
                childStandardError,
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
                    StandardError = childStandardError.DangerousGetHandle(),
                },
                AttributeList = attributeList,
            };
            string applicationPath = profile.WorkerExecutablePath;
            commandLineBuffer = Marshal.StringToHGlobalUni(
                $"{QuoteArgument(applicationPath)} --thumbnail-worker --job-only");
            if (!CreateProcess(
                applicationPath,
                commandLineBuffer,
                nint.Zero,
                nint.Zero,
                inheritHandles: true,
                CreateNoWindow | CreateSuspended | ExtendedStartupInfoPresent,
                nint.Zero,
                Path.GetDirectoryName(applicationPath),
                ref startupInfo,
                out processInformation))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The AppContainer thumbnail worker did not start.");
            }

            processCreated = true;
            workerJob.Assign(processInformation.Process);
            bool isAppContainer = QueryIsAppContainer(processInformation.Process);
            if (ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The AppContainer thumbnail worker did not resume.");
            }

            process = Process.GetProcessById(
                unchecked((int)processInformation.ProcessId));
            inputStream = new FileStream(
                parentStandardInput,
                FileAccess.Write,
                bufferSize: 4_096,
                isAsync: false);
            parentStandardInput = null;
            outputStream = new FileStream(
                parentStandardOutput,
                FileAccess.Read,
                bufferSize: 4_096,
                isAsync: false);
            parentStandardOutput = null;
            var input = new StreamWriter(
                inputStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var output = new StreamReader(
                outputStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return new AppContainerThumbnailWorkerProcess(
                process,
                input,
                output,
                isAppContainer);
        }
        catch
        {
            if (processCreated)
            {
                _ = TerminateProcess(processInformation.Process, 1);
            }

            inputStream?.Dispose();
            outputStream?.Dispose();
            process?.Dispose();
            throw;
        }
        finally
        {
            childStandardInput?.Dispose();
            parentStandardInput?.Dispose();
            childStandardOutput?.Dispose();
            parentStandardOutput?.Dispose();
            childStandardError?.Dispose();
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

            if (commandLineBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(commandLineBuffer);
            }

            if (processInformation.Thread != nint.Zero)
            {
                _ = CloseHandle(processInformation.Thread);
            }

            if (processInformation.Process != nint.Zero)
            {
                _ = CloseHandle(processInformation.Process);
            }
        }
    }

    public void Dispose()
    {
        StandardInput.Dispose();
        StandardOutput.Dispose();
        Process.Dispose();
    }

    private static bool QueryIsAppContainer(nint processHandle)
    {
        if (!OpenProcessToken(processHandle, TokenQuery, out SafeAccessTokenHandle token))
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

    private static SafeFileHandle CreatePipePair(
        bool parentReads,
        out SafeFileHandle parentHandle)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        if (!CreatePipe(
            out SafeFileHandle readHandle,
            out SafeFileHandle writeHandle,
            ref attributes,
            0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        SafeFileHandle childHandle = parentReads ? writeHandle : readHandle;
        parentHandle = parentReads ? readHandle : writeHandle;
        if (!SetHandleInformation(parentHandle, HandleFlagInherit, flags: 0))
        {
            int error = Marshal.GetLastWin32Error();
            childHandle.Dispose();
            parentHandle.Dispose();
            throw new Win32Exception(error);
        }

        return childHandle;
    }

    private static SafeFileHandle OpenInheritedNullOutput()
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        SafeFileHandle handle = CreateFile(
            "NUL",
            GenericWrite,
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

    private static nint CreateAttributeList(
        nint appContainerSid,
        SafeFileHandle standardInput,
        SafeFileHandle standardOutput,
        SafeFileHandle standardError,
        out nint securityCapabilitiesPointer,
        out nint inheritedHandleList)
    {
        nuint attributeListSize = 0;
        _ = InitializeProcThreadAttributeList(
            nint.Zero,
            attributeCount: 2,
            flags: 0,
            ref attributeListSize);
        nint attributeList = Marshal.AllocHGlobal(checked((int)attributeListSize));
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

            var capabilities = new SecurityCapabilities
            {
                AppContainerSid = appContainerSid,
                Capabilities = nint.Zero,
                CapabilityCount = 0,
                Reserved = 0,
            };
            securityCapabilitiesPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(
                capabilities,
                securityCapabilitiesPointer,
                fDeleteOld: false);
            if (!UpdateProcThreadAttribute(
                attributeList,
                flags: 0,
                ProcThreadAttributeSecurityCapabilities,
                securityCapabilitiesPointer,
                (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                nint.Zero,
                nint.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            nint[] handles =
            [
                standardInput.DangerousGetHandle(),
                standardOutput.DangerousGetHandle(),
                standardError.DangerousGetHandle(),
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
                nint.Zero,
                nint.Zero))
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

    private static string QuoteArgument(string argument) =>
        $"\"{argument.Replace("\"", "\\\"")}\"";

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal nint SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        internal nint AppContainerSid;
        internal nint Capabilities;
        internal uint CapabilityCount;
        internal uint Reserved;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
