using Microsoft.Win32;

internal static class ThumbnailProviderMatrixProbe
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int ModuleNotFoundHResult = unchecked((int)0x8007007E);
    private const int FailedExtractionHResult = unchecked((int)0x8004B200);
    private const string ThumbnailHandlerShellExtension =
        "{E357FCCD-A995-4576-B01F-234630154E96}";
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

        IReadOnlyList<ThumbnailParentProviderSampleResult> parentProcess =
            RunParentProcessBaseline(samples);
        ThumbnailProviderStrategyMatrix controlledCopy =
            await RunStrategyAsync(samples, ThumbnailInputStrategy.ControlledCopy);
        ThumbnailProviderStrategyMatrix minimumPathAcl =
            await RunStrategyAsync(samples, ThumbnailInputStrategy.MinimumPathAcl);
        bool sameParentSampleSet = parentProcess
            .Select(sample => sample.Format)
            .SequenceEqual(controlledCopy.Samples.Select(sample => sample.Format));
        bool sameSampleSet = sameParentSampleSet
            && controlledCopy.Samples
            .Select(sample => sample.Format)
            .SequenceEqual(minimumPathAcl.Samples.Select(sample => sample.Format));
        bool parentSamplesSafelyClassified = sameParentSampleSet
            && parentProcess.All(sample =>
                (sample.ExtractionSucceeded
                    && sample.Width > 0
                    && sample.Height > 0)
                || sample.ProviderUnavailableSafely
                || sample.ShellExtractionUnavailableSafely);
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
                    && copy.ShellExtractionUnavailableSafely
                        == acl.ShellExtractionUnavailableSafely
                    && copy.HResult == acl.HResult)
                .All(agree => agree);
        bool workersMatchParentPerFormat = sameSampleSet
            && parentProcess.Zip(
                controlledCopy.Samples,
                (parent, worker) =>
                    parent.ExtractionSucceeded == worker.ExtractionSucceeded
                    && parent.ProviderUnavailableSafely
                        == worker.ProviderUnavailableSafely
                    && parent.ShellExtractionUnavailableSafely
                        == worker.ShellExtractionUnavailableSafely
                    && parent.HResult == worker.HResult)
                .All(agree => agree)
            && parentProcess.Zip(
                minimumPathAcl.Samples,
                (parent, worker) =>
                    parent.ExtractionSucceeded == worker.ExtractionSucceeded
                    && parent.ProviderUnavailableSafely
                        == worker.ProviderUnavailableSafely
                    && parent.ShellExtractionUnavailableSafely
                        == worker.ShellExtractionUnavailableSafely
                    && parent.HResult == worker.HResult)
                .All(agree => agree);
        bool allSamplesSafelyClassified = sameSampleSet
            && parentSamplesSafelyClassified
            && strategiesAgreePerFormat
            && StrategyPassed(controlledCopy, requireAclRestoration: false)
            && StrategyPassed(minimumPathAcl, requireAclRestoration: true);
        return new ThumbnailProviderMatrixResult(
            parentProcess,
            controlledCopy,
            minimumPathAcl,
            SameSampleSet: sameSampleSet,
            ParentSamplesSafelyClassified: parentSamplesSafelyClassified,
            UniformOutcomeAcrossFormats: uniformOutcomeAcrossFormats,
            StrategiesAgreePerFormat: strategiesAgreePerFormat,
            WorkersMatchParentPerFormat: workersMatchParentPerFormat,
            AllSamplesSafelyClassified: allSamplesSafelyClassified);
    }

    private static List<ThumbnailParentProviderSampleResult>
        RunParentProcessBaseline(
            IReadOnlyList<ThumbnailOwnedProviderSample> samples)
    {
        var results = new List<ThumbnailParentProviderSampleResult>(samples.Count);
        foreach (ThumbnailOwnedProviderSample sample in samples)
        {
            ThumbnailHandlerRegistration registration =
                InspectSpecificThumbnailHandler(sample.Path);
            ImageExtractionResult extraction = ShellImageExtractor.Extract(
                sample.Path,
                size: 128,
                flags: ShellItemImageFactoryFlags.ThumbnailOnly
                    | ShellItemImageFactoryFlags.BiggerSizeOk);
            results.Add(new ThumbnailParentProviderSampleResult(
                sample.Format,
                extraction.Success,
                !extraction.Success
                    && extraction.HResult == ModuleNotFoundHResult,
                IsKnownShellExtractionUnavailable(sample, extraction.HResult),
                registration.Registered,
                registration.ModulePresent,
                registration.Registered && !registration.ModulePresent,
                extraction.HResult,
                extraction.Width,
                extraction.Height));
        }

        return results;
    }

    private static ThumbnailHandlerRegistration InspectSpecificThumbnailHandler(
        string path)
    {
        string extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return new(false, false);
        }

        try
        {
            using RegistryKey? handlerKey = Registry.ClassesRoot.OpenSubKey(
                $@"{extension}\ShellEx\{ThumbnailHandlerShellExtension}");
            string? classId = handlerKey?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(classId)
                || !Guid.TryParse(classId, out Guid parsedClassId))
            {
                return new(false, false);
            }

            using RegistryKey? serverKey = Registry.ClassesRoot.OpenSubKey(
                $@"CLSID\{{{parsedClassId:D}}}\InprocServer32");
            string? module = serverKey?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(module))
            {
                return new(true, false);
            }

            string expandedModule = Environment.ExpandEnvironmentVariables(
                module.Trim().Trim('"'));
            return new(true, File.Exists(expandedModule));
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, false);
        }
        catch (System.Security.SecurityException)
        {
            return new(false, false);
        }
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
            bool shellExtractionUnavailableSafely = extraction.Completed
                && extraction.Response is { Success: false }
                && IsKnownShellExtractionUnavailable(sample, hResult);
            results.Add(new ThumbnailProviderSampleResult(
                sample.Format,
                inputReadable,
                extractionSucceeded,
                accessDeniedSafely,
                providerUnavailableSafely,
                shellExtractionUnavailableSafely,
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
                || sample.ProviderUnavailableSafely
                || sample.ShellExtractionUnavailableSafely));

    private static bool IsKnownShellExtractionUnavailable(
        ThumbnailOwnedProviderSample sample,
        int hResult) =>
        string.Equals(sample.Format, "HEIC", StringComparison.Ordinal)
        && hResult == FailedExtractionHResult;

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
            && sample.ShellExtractionUnavailableSafely
                == samples[0].ShellExtractionUnavailableSafely
            && sample.HResult == samples[0].HResult);
}

internal sealed record ThumbnailOwnedProviderSample(string Format, string Path);

internal sealed record ThumbnailProviderMatrixResult(
    IReadOnlyList<ThumbnailParentProviderSampleResult> ParentProcess,
    ThumbnailProviderStrategyMatrix ControlledCopy,
    ThumbnailProviderStrategyMatrix MinimumPathAcl,
    bool SameSampleSet,
    bool ParentSamplesSafelyClassified,
    bool UniformOutcomeAcrossFormats,
    bool StrategiesAgreePerFormat,
    bool WorkersMatchParentPerFormat,
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
    bool ShellExtractionUnavailableSafely,
    int HResult);

internal sealed record ThumbnailParentProviderSampleResult(
    string Format,
    bool ExtractionSucceeded,
    bool ProviderUnavailableSafely,
    bool ShellExtractionUnavailableSafely,
    bool SpecificHandlerRegistered,
    bool SpecificHandlerModulePresent,
    bool StaleSpecificHandlerRegistration,
    int HResult,
    int Width,
    int Height);

internal sealed record ThumbnailHandlerRegistration(
    bool Registered,
    bool ModulePresent);
