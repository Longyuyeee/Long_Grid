using System.Runtime.InteropServices;
using LongGrid.Core.DesktopItems;

internal sealed class ShellChangeListener : IDisposable
{
    private const uint ChangeMessage = 0x8001;
    private const uint CloseMessage = 0x0010;
    private const uint DestroyMessage = 0x0002;
    private const int DesktopFolderId = 0;

    private static readonly WindowProcedure WindowProcedureDelegate = WindowProcedure;
    private static ShellChangeListener? activeListener;

    private readonly object sync = new();
    private readonly string sandbox;
    private readonly string sandboxPrefix;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly ManualResetEventSlim stopped = new(false);
    private readonly ChangeReconciliationGate reconciliationGate = new(
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromSeconds(3));
    private readonly Dictionary<ShellChangeEvent, int> sandboxEventCounts = [];

    private Exception? startupException;
    private nint window;
    private int totalNotificationCount;
    private int sandboxNotificationCount;
    private uint desktopRegistration;
    private uint sandboxRegistration;

    public ShellChangeListener(string sandbox)
    {
        this.sandbox = Path.GetFullPath(sandbox);
        sandboxPrefix = this.sandbox.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "LongGrid.P0-02.ShellChangeListener",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Shell listener startup timed out.");
        }

        if (startupException is not null)
        {
            throw new InvalidOperationException(
                "Shell listener startup failed.",
                startupException);
        }
    }

    public bool DesktopRegistrationSucceeded => desktopRegistration != 0;

    public bool SandboxRegistrationSucceeded => sandboxRegistration != 0;

    public bool TryBeginReconciliation(DateTimeOffset now)
    {
        lock (sync)
        {
            if (!reconciliationGate.ShouldReconcile(now))
            {
                return false;
            }

            reconciliationGate.CompleteReconciliation();
            return true;
        }
    }

    public NotificationSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return new NotificationSnapshot(
                totalNotificationCount,
                sandboxNotificationCount,
                CountEvents(ShellChangeEvent.Create | ShellChangeEvent.MakeDirectory),
                CountEvents(ShellChangeEvent.Delete | ShellChangeEvent.RemoveDirectory),
                CountEvents(ShellChangeEvent.RenameItem | ShellChangeEvent.RenameFolder),
                CountEvents(ShellChangeEvent.UpdateItem | ShellChangeEvent.UpdateDirectory));
        }
    }

    public void Dispose()
    {
        if (window != nint.Zero)
        {
            _ = NativeWindowMethods.PostMessage(window, CloseMessage, nint.Zero, nint.Zero);
        }

        if (!stopped.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("Shell listener shutdown timed out.");
        }

        ready.Dispose();
        stopped.Dispose();
    }

    private void ThreadMain()
    {
        string className = $"LongGrid.P0_02.{Guid.NewGuid():N}";
        nint module = NativeWindowMethods.GetModuleHandle(null);
        ushort classAtom = 0;
        nint desktopPidl = nint.Zero;
        nint sandboxPidl = nint.Zero;
        bool comInitialized = false;

        try
        {
            int initializeResult = NativeWindowMethods.CoInitializeEx(
                nint.Zero,
                ComInitialization.ApartmentThreaded);
            comInitialized = initializeResult >= 0;
            Marshal.ThrowExceptionForHR(initializeResult);

            activeListener = this;
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Instance = module,
                ClassName = className,
                WindowProcedure = WindowProcedureDelegate,
            };

            classAtom = NativeWindowMethods.RegisterClassEx(ref windowClass);
            if (classAtom == 0)
            {
                throw new InvalidOperationException("Window class registration failed.");
            }

            window = NativeWindowMethods.CreateWindowEx(
                0,
                className,
                className,
                0,
                0,
                0,
                0,
                0,
                new nint(-3),
                nint.Zero,
                module,
                nint.Zero);

            if (window == nint.Zero)
            {
                throw new InvalidOperationException("Message-only window creation failed.");
            }

            Marshal.ThrowExceptionForHR(
                NativeWindowMethods.SHGetSpecialFolderLocation(
                    nint.Zero,
                    DesktopFolderId,
                    out desktopPidl));
            Marshal.ThrowExceptionForHR(
                NativeWindowMethods.SHParseDisplayName(
                    sandbox,
                    nint.Zero,
                    out sandboxPidl,
                    0,
                    out _));

            desktopRegistration = Register(desktopPidl, recursive: true);
            sandboxRegistration = Register(sandboxPidl, recursive: true);

            if (desktopRegistration == 0 || sandboxRegistration == 0)
            {
                throw new InvalidOperationException("Shell notification registration failed.");
            }

            ready.Set();

            while (NativeWindowMethods.GetMessage(
                out WindowMessage message,
                nint.Zero,
                0,
                0) > 0)
            {
                _ = NativeWindowMethods.TranslateMessage(ref message);
                _ = NativeWindowMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            startupException = exception;
            ready.Set();
        }
        finally
        {
            if (desktopRegistration != 0)
            {
                _ = NativeWindowMethods.SHChangeNotifyDeregister(desktopRegistration);
            }

            if (sandboxRegistration != 0)
            {
                _ = NativeWindowMethods.SHChangeNotifyDeregister(sandboxRegistration);
            }

            if (desktopPidl != nint.Zero)
            {
                Marshal.FreeCoTaskMem(desktopPidl);
            }

            if (sandboxPidl != nint.Zero)
            {
                Marshal.FreeCoTaskMem(sandboxPidl);
            }

            if (window != nint.Zero)
            {
                _ = NativeWindowMethods.DestroyWindow(window);
                window = nint.Zero;
            }

            if (classAtom != 0)
            {
                _ = NativeWindowMethods.UnregisterClass(className, module);
            }

            activeListener = null;
            if (comInitialized)
            {
                NativeWindowMethods.CoUninitialize();
            }

            stopped.Set();
        }
    }

    private uint Register(nint itemIdList, bool recursive)
    {
        var entry = new ShellChangeNotifyEntry
        {
            ItemIdList = itemIdList,
            Recursive = recursive,
        };

        return NativeWindowMethods.SHChangeNotifyRegister(
            window,
            ShellChangeRegistrationFlags.InterruptLevel
                | ShellChangeRegistrationFlags.ShellLevel
                | ShellChangeRegistrationFlags.NewDelivery,
            ShellChangeEvent.RenameItem
                | ShellChangeEvent.Create
                | ShellChangeEvent.Delete
                | ShellChangeEvent.MakeDirectory
                | ShellChangeEvent.RemoveDirectory
                | ShellChangeEvent.UpdateDirectory
                | ShellChangeEvent.UpdateItem
                | ShellChangeEvent.RenameFolder,
            ChangeMessage,
            1,
            ref entry);
    }

    private void OnChange(nint changeHandle, nint processId)
    {
        nint notificationLock = NativeWindowMethods.SHChangeNotificationLock(
            changeHandle,
            unchecked((uint)processId.ToInt64()),
            out nint itemIdListArray,
            out ShellChangeEvent changeEvent);

        if (notificationLock == nint.Zero)
        {
            return;
        }

        try
        {
            bool isSandboxEvent = IsSandboxEvent(itemIdListArray);

            lock (sync)
            {
                totalNotificationCount++;

                if (isSandboxEvent)
                {
                    sandboxNotificationCount++;
                    IncrementSandboxEventCounts(changeEvent);
                    reconciliationGate.RecordChange(DateTimeOffset.UtcNow);
                }
            }
        }
        finally
        {
            _ = NativeWindowMethods.SHChangeNotificationUnlock(notificationLock);
        }
    }

    private bool IsSandboxEvent(nint itemIdListArray)
    {
        if (itemIdListArray == nint.Zero)
        {
            return false;
        }

        nint first = Marshal.ReadIntPtr(itemIdListArray);
        nint second = Marshal.ReadIntPtr(itemIdListArray, nint.Size);

        return IsInsideSandbox(first) || IsInsideSandbox(second);
    }

    private bool IsInsideSandbox(nint itemIdList)
    {
        if (itemIdList == nint.Zero)
        {
            return false;
        }

        var path = new char[260];
        if (!NativeWindowMethods.SHGetPathFromIDList(itemIdList, path))
        {
            return false;
        }

        try
        {
            int terminator = Array.IndexOf(path, '\0');
            string parsedPath = new(
                path,
                0,
                terminator < 0 ? path.Length : terminator);
            string canonical = Path.GetFullPath(parsedPath);
            return canonical.Equals(sandbox, StringComparison.OrdinalIgnoreCase)
                || canonical.StartsWith(
                    sandboxPrefix,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private void IncrementSandboxEventCounts(ShellChangeEvent changeEvent)
    {
        foreach (ShellChangeEvent value in Enum.GetValues<ShellChangeEvent>())
        {
            if (value != ShellChangeEvent.None && changeEvent.HasFlag(value))
            {
                sandboxEventCounts[value] =
                    sandboxEventCounts.GetValueOrDefault(value) + 1;
            }
        }
    }

    private int CountEvents(ShellChangeEvent events)
    {
        int count = 0;

        foreach ((ShellChangeEvent changeEvent, int eventCount) in sandboxEventCounts)
        {
            if (events.HasFlag(changeEvent))
            {
                count += eventCount;
            }
        }

        return count;
    }

    private static nint WindowProcedure(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        if (message == ChangeMessage)
        {
            activeListener?.OnChange(wordParameter, longParameter);
            return nint.Zero;
        }

        if (message == CloseMessage)
        {
            _ = NativeWindowMethods.DestroyWindow(window);
            return nint.Zero;
        }

        if (message == DestroyMessage)
        {
            NativeWindowMethods.PostQuitMessage(0);
            return nint.Zero;
        }

        return NativeWindowMethods.DefWindowProc(
            window,
            message,
            wordParameter,
            longParameter);
    }
}

internal sealed record NotificationSnapshot(
    int TotalNotificationCount,
    int SandboxNotificationCount,
    int CreateLikeCount,
    int DeleteLikeCount,
    int RenameLikeCount,
    int UpdateLikeCount);
