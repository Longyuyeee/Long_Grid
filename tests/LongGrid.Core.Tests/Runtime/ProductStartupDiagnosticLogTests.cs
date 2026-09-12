using System.Text.Json;
using LongGrid.App;

namespace LongGrid.Core.Tests.Runtime;

public sealed class ProductStartupDiagnosticLogTests
{
    [Fact]
    public void FailureContainsBoundedPhaseAndCodeButNoExceptionContent()
    {
        using var sandbox = new Sandbox();
        var log = new ProductStartupDiagnosticLog(sandbox.Path);
        log.RecordStage(ProductStartupStage.WindowCreating);
        var error = new IOException("private-file C:\\Users\\Secret\\财务.xlsx");
        error.Data["secret"] = "private-data";
        log.RecordFailure(ProductStartupFailureOrigin.XamlUnhandled, error);
        string text = File.ReadAllText(System.IO.Path.Combine(sandbox.Path, "latest-failure.json"));
        using var document = JsonDocument.Parse(text);
        Assert.Equal("WindowCreating", document.RootElement.GetProperty("Stage").GetString());
        Assert.Equal("IO", document.RootElement.GetProperty("Category").GetString());
        Assert.Equal(error.HResult, document.RootElement.GetProperty("HResult").GetInt32());
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("Secret", text);
        Assert.DoesNotContain("财务", text);
        Assert.True(new FileInfo(System.IO.Path.Combine(sandbox.Path, "latest-failure.json")).Length <= 4096);
    }

    [Fact]
    public void SubsequentStartupPreservesFailureAndRecordsStayBounded()
    {
        using var sandbox = new Sandbox();
        var first = new ProductStartupDiagnosticLog(sandbox.Path);
        first.RecordFailure(ProductStartupFailureOrigin.EntryPoint, new InvalidOperationException());
        string failure = File.ReadAllText(System.IO.Path.Combine(sandbox.Path, "latest-failure.json"));
        var next = new ProductStartupDiagnosticLog(sandbox.Path);
        for (int index = 0; index < 100; index++) next.RecordStage(ProductStartupStage.ProcessStarting);
        Assert.Equal(failure, File.ReadAllText(System.IO.Path.Combine(sandbox.Path, "latest-failure.json")));
        Assert.Equal(2, Directory.GetFiles(sandbox.Path).Length);
        using var previous = JsonDocument.Parse(failure);
        using var current = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(sandbox.Path, "latest-startup.json")));
        Assert.NotEqual(previous.RootElement.GetProperty("RunId").GetGuid(),
            current.RootElement.GetProperty("RunId").GetGuid());
    }

    [Fact]
    public void UnwritableDestinationDoesNotReplaceOriginalOutcome()
    {
        using var sandbox = new Sandbox();
        string file = System.IO.Path.Combine(sandbox.Path, "not-a-directory");
        File.WriteAllText(file, "keep");
        var log = new ProductStartupDiagnosticLog(file);
        log.RecordStage(ProductStartupStage.ProcessStarting);
        log.RecordFailure(ProductStartupFailureOrigin.EntryPoint, new IOException());
        Assert.Equal("keep", File.ReadAllText(file));
    }

    private sealed class Sandbox : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "LongGrid.StartupDiagnostics.Tests", Guid.NewGuid().ToString("N"));
        public Sandbox() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
