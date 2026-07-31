using System.Runtime.InteropServices;

internal static class ShellFileOperation
{
    private const int Success = 0;

    private const FileOperationFlags ProbeFlags =
        FileOperationFlags.Silent
        | FileOperationFlags.NoConfirmation
        | FileOperationFlags.NoErrorUi
        | FileOperationFlags.NoConnectedElements
        | FileOperationFlags.EarlyFailure;

    internal static ShellMoveResult Move(
        IEnumerable<ShellMoveRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        ShellMoveRequest[] requestArray = requests.ToArray();
        if (requestArray.Length == 0)
        {
            throw new ArgumentException(
                "At least one move request is required.",
                nameof(requests));
        }

        var operation = (IFileOperation)new FileOperationComObject();
        var shellItems = new List<object>();

        try
        {
            ThrowIfFailed(operation.SetOperationFlags(ProbeFlags));

            foreach (ShellMoveRequest request in requestArray)
            {
                IShellItem source = CreateShellItem(request.SourcePath);
                IShellItem destination = CreateShellItem(request.DestinationDirectory);
                shellItems.Add(source);
                shellItems.Add(destination);

                ThrowIfFailed(operation.MoveItem(
                    source,
                    destination,
                    null,
                    request.ProgressSink));
            }

            int performResult = operation.PerformOperations();
            int abortedResult = operation.GetAnyOperationsAborted(out bool aborted);
            ThrowIfFailed(abortedResult);
            return new ShellMoveResult(performResult, aborted);
        }
        finally
        {
            foreach (object shellItem in shellItems)
            {
                ReleaseComObject(shellItem);
            }

            ReleaseComObject(operation);
            GC.KeepAlive(requestArray);
        }
    }

    private static IShellItem CreateShellItem(string path)
    {
        int result = NativeMethods.SHCreateItemFromParsingName(
            path,
            0,
            NativeMethods.ShellItemInterfaceId,
            out IShellItem shellItem);
        ThrowIfFailed(result);
        return shellItem;
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < Success)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static void ReleaseComObject(object value)
    {
        if (Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}

internal sealed record ShellMoveRequest(
    string SourcePath,
    string DestinationDirectory,
    IFileOperationProgressSink? ProgressSink = null);

internal sealed record ShellMoveResult(int Result, bool Aborted)
{
    private const int Cancelled = unchecked((int)0x800704C7);

    internal bool CancellationSignaled => Aborted || Result == Cancelled;
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class CancelMoveProgressSink : IFileOperationProgressSink
{
    private const int Success = 0;
    private const int Cancelled = unchecked((int)0x800704C7);

    internal int PreMoveCount { get; private set; }

    internal int PostMoveCount { get; private set; }

    public int StartOperations() => Success;

    public int FinishOperations(int result) => Success;

    public int PreRenameItem(uint flags, IShellItem item, string newName) => Success;

    public int PostRenameItem(
        uint flags,
        IShellItem item,
        string newName,
        int result,
        IShellItem? newItem) => Success;

    public int PreMoveItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        string? newName)
    {
        PreMoveCount++;
        return Cancelled;
    }

    public int PostMoveItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        string? newName,
        int result,
        IShellItem? newItem)
    {
        PostMoveCount++;
        return Success;
    }

    public int PreCopyItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        string? newName) => Success;

    public int PostCopyItem(
        uint flags,
        IShellItem item,
        IShellItem destinationFolder,
        string? newName,
        int result,
        IShellItem? newItem) => Success;

    public int PreDeleteItem(uint flags, IShellItem item) => Success;

    public int PostDeleteItem(
        uint flags,
        IShellItem item,
        int result,
        IShellItem? newItem) => Success;

    public int PreNewItem(
        uint flags,
        IShellItem destinationFolder,
        string newName) => Success;

    public int PostNewItem(
        uint flags,
        IShellItem destinationFolder,
        string newName,
        string? templateName,
        uint fileAttributes,
        int result,
        IShellItem? newItem) => Success;

    public int UpdateProgress(uint workTotal, uint workSoFar) => Success;

    public int ResetTimer() => Success;

    public int PauseTimer() => Success;

    public int ResumeTimer() => Success;
}
