internal static class ThumbnailProviderMatrixProbe
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    internal static async Task<ThumbnailProviderMatrixResult> RunAsync(
        IReadOnlyList<ThumbnailOwnedProviderSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException(
                "At least one owned provider sample is required.",
                nameof(samples));
        }

        ThumbnailProviderStrategyMatrix controlledCopy =
            await RunStrategyAsync(samples, ThumbnailInputStrategy.ControlledCopy);
        ThumbnailProviderStrategyMatrix minimumPathAcl =
            await RunStrategyAsync(samples, ThumbnailInputStrategy.MinimumPathAcl);
        bool sameSampleSet = controlledCopy.Samples
            .Select(sample => sample.Format)
            .SequenceEqual(minimumPathAcl.Samples.Select(sample => sample.Format));
        bool allSamplesSafelyClassified = sameSampleSet
            && StrategyPassed(controlledCopy, requireAclRestoration: false)
            && StrategyPassed(minimumPathAcl, requireAclRestoration: true);
        return new ThumbnailProviderMatrixResult(
            controlledCopy,
            minimumPathAcl,
            SameSampleSet: sameSampleSet,
            AllSamplesSafelyClassified: allSamplesSafelyClassified);
    }

    private static async Task<ThumbnailProviderStrategyMatrix> RunStrategyAsync(
        IReadOnlyList<ThumbnailOwnedProviderSample> samples,
        ThumbnailInputStrategy strategy)
    {
        using var client = new ThumbnailWorkerClient(
            maximumRequestsPerProcess: 20,
            inputStrategy: strategy);
        var results = new List<ThumbnailProviderSampleResult>(samples.Count);
        for (int index = 0; index < samples.Count; index++)
        {
            ThumbnailOwnedProviderSample sample = samples[index];
            ThumbnailWorkerCallResult read = await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    $"provider-{strategy}-{index}-read",
                    ThumbnailWorkerRequestKind.ReadBoundaryProbe,
                    sample.Path,
                    Size: 0,
                    Flags: 0,
                    InputTransport: GetInputTransport(strategy)),
                RequestTimeout);
            ThumbnailWorkerCallResult extraction = await client.ExecuteAsync(
                new ThumbnailWorkerRequest(
                    ThumbnailWorkerServer.CurrentProtocolVersion,
                    $"provider-{strategy}-{index}-extract",
                    ThumbnailWorkerRequestKind.Extract,
                    sample.Path,
                    Size: 128,
                    ShellItemImageFactoryFlags.ThumbnailOnly
                        | ShellItemImageFactoryFlags.BiggerSizeOk),
                RequestTimeout);
            bool inputReadable = read.Completed
                && read.Response is { Success: true };
            bool extractionSucceeded = extraction.Completed
                && extraction.Response is { Success: true };
            int hResult = extraction.Response?.HResult ?? 0;
            bool accessDeniedSafely = extraction.Completed
                && extraction.Response is { Success: false }
                && hResult == AccessDeniedHResult;
            results.Add(new ThumbnailProviderSampleResult(
                sample.Format,
                inputReadable,
                extractionSucceeded,
                accessDeniedSafely,
                hResult));
        }

        bool allWorkersAppContainer = client.AllWorkersAppContainer;
        bool inputAuthorizationUsed = strategy switch
        {
            ThumbnailInputStrategy.ControlledCopy =>
                client.UsesControlledInputCopies,
            ThumbnailInputStrategy.MinimumPathAcl => client.UsesMinimumPathAcl,
            _ => false,
        };
        bool allAclChangesRestored = strategy
            != ThumbnailInputStrategy.MinimumPathAcl
            || client.AllPathAclLeasesRestored;
        client.Dispose();
        return new ThumbnailProviderStrategyMatrix(
            strategy.ToString(),
            results,
            allWorkersAppContainer,
            inputAuthorizationUsed,
            allAclChangesRestored,
            client.AppContainerProfileDeleted);
    }

    private static bool StrategyPassed(
        ThumbnailProviderStrategyMatrix strategy,
        bool requireAclRestoration) =>
        strategy.AllWorkersAppContainer
        && strategy.InputAuthorizationUsed
        && (!requireAclRestoration || strategy.AllAclChangesRestored)
        && strategy.ProfileDeleted
        && strategy.Samples.All(sample =>
            sample.InputReadable
            && (sample.ExtractionSucceeded || sample.AccessDeniedSafely));

    private static ThumbnailInputTransport GetInputTransport(
        ThumbnailInputStrategy strategy) => strategy switch
        {
            ThumbnailInputStrategy.ControlledCopy =>
                ThumbnailInputTransport.ControlledCopy,
            ThumbnailInputStrategy.MinimumPathAcl =>
                ThumbnailInputTransport.MinimumPathAcl,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };
}

internal sealed record ThumbnailOwnedProviderSample(string Format, string Path);

internal sealed record ThumbnailProviderMatrixResult(
    ThumbnailProviderStrategyMatrix ControlledCopy,
    ThumbnailProviderStrategyMatrix MinimumPathAcl,
    bool SameSampleSet,
    bool AllSamplesSafelyClassified);

internal sealed record ThumbnailProviderStrategyMatrix(
    string Strategy,
    IReadOnlyList<ThumbnailProviderSampleResult> Samples,
    bool AllWorkersAppContainer,
    bool InputAuthorizationUsed,
    bool AllAclChangesRestored,
    bool ProfileDeleted);

internal sealed record ThumbnailProviderSampleResult(
    string Format,
    bool InputReadable,
    bool ExtractionSucceeded,
    bool AccessDeniedSafely,
    int HResult);
