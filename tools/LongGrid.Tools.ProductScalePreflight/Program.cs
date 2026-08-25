using System.Text.Json;
using LongGrid.Infrastructure.Configuration;

try
{
    if (args.Length != 0)
    {
        WriteError("InvalidArguments");
        return 3;
    }

    ProductWorkspaceScalePreflightResult result =
        await ProductWorkspaceScalePreflight.RunAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4aProductWorkspaceScalePreflight",
        outcome = result.Outcome.ToString(),
        containerCount = result.ContainerCount,
        itemCount = result.ItemCount,
        iterations = result.Iterations,
        layoutPreviewIterations = result.LayoutPreviewIterations,
        resolvedItemCount = result.ResolvedItemCount,
        projectedItemCount = result.ProjectedItemCount,
        selectionActionCount = result.SelectionActionCount,
        searchMatchCount = result.SearchMatchCount,
        sortedContainerCount = result.SortedContainerCount,
        readyContainerCount = result.ReadyContainerCount,
        metrics = result.Metrics.Select(metric => new
        {
            name = metric.Name,
            p95Milliseconds = metric.P95Milliseconds,
            regressionLimitMilliseconds = metric.RegressionLimitMilliseconds,
            passed = metric.Passed,
        }),
        temporarySandboxCleaned = result.TemporarySandboxCleaned,
        readsRealDesktop = result.ReadsRealDesktop,
        realFileOperationsAllowed = result.RealFileOperationsAllowed,
    }));
    return result.Outcome == ProductWorkspaceScalePreflightOutcome.Passed
        ? 0
        : 2;
}
catch (OperationCanceledException)
{
    WriteError("Cancelled");
    return 4;
}
catch (Exception exception) when (
    exception is InvalidOperationException
        or IOException
        or UnauthorizedAccessException)
{
    WriteError(exception.GetType().Name);
    return 4;
}

static void WriteError(string error) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4aProductWorkspaceScalePreflight",
        outcome = "Failed",
        error,
        readsRealDesktop = false,
        realFileOperationsAllowed = false,
    }));
