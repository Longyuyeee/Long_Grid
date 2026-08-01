internal static class ThumbnailProviderMatrixProbe
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int ModuleNotFoundHResult = unchecked((int)0x8007007E);
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
        bool uniformOutcomeAcrossFormats =
            HasConsistentOutcome(controlledCopy.Samples)
            && HasConsistentOutcome(minimumPathAcl.Samples);
        bool strategiesAgreePerFormat = sameSampleSet
            && controlledCopy.Samples.Zip(
                minimumPathAcl.Samples,
                (copy, acl) =>
                    copy.ExtractionSucceeded == acl.ExtractionSucceeded
                    && copy.AccessDeniedSafely == acl.AccessDeniedSafely
                    && copy.ProviderUnavailableSafely
                        == acl.ProviderUnavailableSafely
                    && copy.HResult == acl.HResult)
                .All(agree => agree);
        bool allSamplesSafelyClassified = sameSampleSet
            && strategiesAgreePerFormat
            && StrategyPassed(controlledCopy, requireAclRestoration: false)
            && StrategyPassed(minimumPathAcl, requireAclRestoration: true);
        return new ThumbnailProviderMatrixResult(
            controlledCopy,
            minimumPathAcl,
            SameSampleSet: sameSampleSet,
            UniformOutcomeAcrossFormats: uniformOutcomeAcrossFormats,
            StrategiesAgreePerFormat: strategiesAgreePerFormat,
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
            bool providerUnavailableSafely = extraction.Completed
                && extraction.Response is { Success: false }
                && hResult == ModuleNotFoundHResult;
            results.Add(new ThumbnailProviderSampleResult(
                sample.Format,
                inputReadable,
                extractionSucceeded,
                accessDeniedSafely,
                providerUnavailableSafely,
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
            && (sample.ExtractionSucceeded
                || sample.AccessDeniedSafely
                || sample.ProviderUnavailableSafely));

    private static ThumbnailInputTransport GetInputTransport(
        ThumbnailInputStrategy strategy) => strategy switch
        {
            ThumbnailInputStrategy.ControlledCopy =>
                ThumbnailInputTransport.ControlledCopy,
            ThumbnailInputStrategy.MinimumPathAcl =>
                ThumbnailInputTransport.MinimumPathAcl,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };

    private static bool HasConsistentOutcome(
        IReadOnlyList<ThumbnailProviderSampleResult> samples) =>
        samples.Count > 0
        && samples.All(sample =>
            sample.ExtractionSucceeded == samples[0].ExtractionSucceeded
            && sample.AccessDeniedSafely == samples[0].AccessDeniedSafely
            && sample.ProviderUnavailableSafely
                == samples[0].ProviderUnavailableSafely
            && sample.HResult == samples[0].HResult);
}

internal sealed record ThumbnailOwnedProviderSample(string Format, string Path);

internal sealed record ThumbnailProviderMatrixResult(
    ThumbnailProviderStrategyMatrix ControlledCopy,
    ThumbnailProviderStrategyMatrix MinimumPathAcl,
    bool SameSampleSet,
    bool UniformOutcomeAcrossFormats,
    bool StrategiesAgreePerFormat,
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
    bool ProviderUnavailableSafely,
    int HResult);
