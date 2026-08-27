using System.Text.Json;
using LongGrid.Core.Taskbar;

namespace LongGrid.TaskbarWorker;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length != 1
            || !string.Equals(args[0], "--compatibility-probe", StringComparison.Ordinal))
        {
            return 64;
        }

        TaskbarCompatibilityReport report = TaskbarCompatibilityProbe.Capture();
        Console.WriteLine(JsonSerializer.Serialize(report, TaskbarJsonContext.Default.TaskbarCompatibilityReport));
        return report.ProbeOutcome == TaskbarProbeOutcome.Pass ? 0 : 1;
    }
}
