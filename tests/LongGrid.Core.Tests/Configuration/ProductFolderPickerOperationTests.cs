using System.Runtime.InteropServices;
using LongGrid.App;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductFolderPickerOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Inject the exact Shell/WinRT failure at the picker boundary without opening a system dialog.")]
    public async Task ShellFailureIsFiniteAndAllowsRetry(bool asynchronous)
    {
        var operation = new ProductFolderPickerOperation();
        var exception = new COMException("private-path-must-not-leak", unchecked((int)0x80004005));
        var failed = await operation.PickAsync(() => asynchronous
            ? Task.FromException<string?>(exception)
            : throw exception);
        Assert.Equal(ProductFolderPickerStatus.Unavailable, failed.Status);
        Assert.Null(failed.Path);
        Assert.DoesNotContain("private-path", failed.ToString());
        Assert.False(operation.IsActive);
        var retry = await operation.PickAsync(() => Task.FromResult<string?>("C:\\工作"));
        Assert.Equal(ProductFolderPickerStatus.Selected, retry.Status);
        Assert.Equal("C:\\工作", retry.Path);
    }

    [Fact]
    public async Task AccessDeniedAndCancellationDoNotSelectAnything()
    {
        var operation = new ProductFolderPickerOperation();
        var denied = await operation.PickAsync(() => throw new UnauthorizedAccessException());
        var cancelled = await operation.PickAsync(() => throw new OperationCanceledException());
        var dismissed = await operation.PickAsync(() => Task.FromResult<string?>(null));
        Assert.Equal(ProductFolderPickerStatus.Unavailable, denied.Status);
        Assert.Equal(ProductFolderPickerStatus.Cancelled, cancelled.Status);
        Assert.Equal(ProductFolderPickerStatus.Cancelled, dismissed.Status);
        Assert.All(new[] { denied, cancelled, dismissed }, result => Assert.Null(result.Path));
        Assert.False(operation.IsActive);
    }

    [Fact]
    public async Task RepeatedClickDoesNotLaunchSecondPicker()
    {
        var operation = new ProductFolderPickerOperation();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = operation.PickAsync(() => completion.Task);
        int secondCalls = 0;
        Assert.True(operation.IsActive);
        var second = await operation.PickAsync(() =>
        {
            secondCalls++;
            return Task.FromResult<string?>("unexpected");
        });
        Assert.Equal(ProductFolderPickerStatus.Busy, second.Status);
        Assert.Equal(0, secondCalls);
        completion.SetResult(null);
        Assert.Equal(ProductFolderPickerStatus.Cancelled, (await first).Status);
        Assert.False(operation.IsActive);
    }

    [Fact]
    public async Task UnexpectedFaultIsNotHiddenAndStillReleasesBusyState()
    {
        var operation = new ProductFolderPickerOperation();
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation.PickAsync(
            () => throw new InvalidOperationException("programming error")));
        Assert.False(operation.IsActive);
    }
}
