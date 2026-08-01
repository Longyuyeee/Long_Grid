using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed class RestrictedThumbnailWorkerProcess : IDisposable
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
    private static readonly nuint ProcThreadAttributeHandleList = 0x00020002;

    private RestrictedThumbnailWorkerProcess(
        Process process,
        StreamWriter standardInput,
        StreamReader standardOutput,
        int integrityRid)
    {
        Process = process;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        IntegrityRid = integrityRid;
    }

    internal Process Process { get; }

    internal StreamWriter StandardInput { get; }

    internal StreamReader StandardOutput { get; }

    internal int IntegrityRid { get; }

    internal static RestrictedThumbnailWorkerProcess Start(
        ThumbnailWorkerJob workerJob)
    {
        ArgumentNullException.ThrowIfNull(workerJob);
        using SafeAccessTokenHandle token =
            RestrictedThumbnailTokenProbe.CreateLowIntegrityPrimaryToken();
        SafeFileHandle? childStandardInput = null;
        SafeFileHandle? parentStandardInput = null;
        SafeFileHandle? childStandardOutput = null;
        SafeFileHandle? parentStandardOutput = null;
        SafeFileHandle? childStandardError = null;
        FileStream? inputStream = null;
        FileStream? outputStream = null;
        Process? process = null;
        nint attributeList = nint.Zero;
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
                childStandardInput,
                childStandardOutput,
                childStandardError,
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
            string applicationPath = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "The process path is unavailable.");
            commandLineBuffer = Marshal.StringToHGlobalUni(
                BuildCommandLine(applicationPath));
            if (!CreateProcessAsUser(
                token,
                applicationPath,
                commandLineBuffer,
                nint.Zero,
                nint.Zero,
                inheritHandles: true,
                CreateNoWindow | CreateSuspended | ExtendedStartupInfoPresent,
                nint.Zero,
                Environment.CurrentDirectory,
                ref startupInfo,
                out processInformation))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The restricted thumbnail worker did not start.");
            }

            processCreated = true;
            workerJob.Assign(processInformation.Process);
            int integrityRid = RestrictedThumbnailTokenProbe.GetProcessIntegrityRid(
                processInformation.Process);
            if (ResumeThread(processInformation.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The restricted thumbnail worker did not resume.");
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
            return new RestrictedThumbnailWorkerProcess(
                process,
                input,
                output,
                integrityRid);
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
        if (!SetHandleInformation(
            parentHandle,
            HandleFlagInherit,
            flags: 0))
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
        SafeFileHandle standardInput,
        SafeFileHandle standardOutput,
        SafeFileHandle standardError,
        out nint inheritedHandleList)
    {
        nuint attributeListSize = 0;
        _ = InitializeProcThreadAttributeList(
            nint.Zero,
            attributeCount: 1,
            flags: 0,
            ref attributeListSize);
        nint attributeList = Marshal.AllocHGlobal(checked((int)attributeListSize));
        inheritedHandleList = nint.Zero;
        try
        {
            if (!InitializeProcThreadAttributeList(
                attributeList,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize))
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
            if (inheritedHandleList != nint.Zero)
            {
                Marshal.FreeHGlobal(inheritedHandleList);
                inheritedHandleList = nint.Zero;
            }

            throw;
        }
    }

    private static string BuildCommandLine(string applicationPath)
    {
        var arguments = new List<string> { applicationPath };
        if (string.Equals(
            Path.GetFileNameWithoutExtension(applicationPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add(Assembly.GetExecutingAssembly().Location);
        }

        arguments.Add("--thumbnail-worker");
        arguments.Add("--parent-pid");
        arguments.Add(Environment.ProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(' ', arguments.Select(QuoteArgument));
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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle token,
        string applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

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
