[CmdletBinding()]
param(
    [string] $EvidencePath,

    [string] $ExpectedCommit,

    [string] $ReviewOutputPath,

    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'

$expectedPurpose = 'M4c2FormalApp24HourResourceStabilitySession'
$expectedSlice = 'M4c2b2'
$expectedDurationHours = 24
$expectedSampleSeconds = 60
$expectedSampleCount = 1441
$minimumSampleCoveragePercent = 98
$maximumSampleGapSeconds = 180
$warmupMinutes = 30
$comparisonWindowMinutes = 60
$privateBytesMedianDeltaLimit = 64MB
$privateBytesSlopeLimitPerHour = 2MB
$handleMedianDeltaLimit = 32
$handleSlopeLimitPerHour = 1
$threadMedianDeltaLimit = 4
$threadSlopeLimitPerHour = 0.25
$windowMedianDeltaLimit = 0
$windowTransientIncreaseLimit = 2
$maximumConsecutiveUiaMisses = 2

function Assert-Condition {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Get-Median {
    param([double[]] $Values)

    Assert-Condition ($Values.Count -gt 0) `
        'A median requires at least one value.'
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) {
        return [double]$ordered[$middle]
    }

    return ([double]$ordered[$middle - 1] +
        [double]$ordered[$middle]) / 2
}

function Get-LinearSlopePerHour {
    param([object[]] $Samples, [string] $PropertyName)

    if ($Samples.Count -lt 2) {
        return 0.0
    }

    [double]$sumX = 0
    [double]$sumY = 0
    [double]$sumXY = 0
    [double]$sumXX = 0
    foreach ($sample in $Samples) {
        [double]$x = [double]$sample.elapsedSeconds / 3600
        [double]$y = [double]$sample.$PropertyName
        $sumX += $x
        $sumY += $y
        $sumXY += $x * $y
        $sumXX += $x * $x
    }

    [double]$count = $Samples.Count
    [double]$denominator = ($count * $sumXX) - ($sumX * $sumX)
    if ([Math]::Abs($denominator) -lt 0.000001) {
        return 0.0
    }

    return (($count * $sumXY) - ($sumX * $sumY)) / $denominator
}

function Get-MaximumSampleGap {
    param([object[]] $Samples)

    [double]$maximum = 0
    for ($index = 1; $index -lt $Samples.Count; $index++) {
        [double]$gap = [double]$Samples[$index].elapsedSeconds -
            [double]$Samples[$index - 1].elapsedSeconds
        if ($gap -gt $maximum) {
            $maximum = $gap
        }
    }

    return $maximum
}

function Get-MaximumConsecutiveUiaMisses {
    param([object[]] $Samples)

    [int]$current = 0
    [int]$maximum = 0
    foreach ($sample in $Samples) {
        if ($sample.uiAutomationRootAvailable) {
            $current = 0
        } else {
            $current++
            $maximum = [Math]::Max($maximum, $current)
        }
    }

    return $maximum
}

function Get-StateRevisionKey {
    param([object] $Telemetry)

    return [ordered]@{
        workspaceSaveStatus = $Telemetry.WorkspaceSaveStatus
        workspaceCurrentRevision = $Telemetry.WorkspaceCurrentRevision
        workspaceSavedRevision = $Telemetry.WorkspaceSavedRevision
        catalogStatus = $Telemetry.CatalogStatus
        catalogGeneration = $Telemetry.CatalogGeneration
        catalogEntryCount = $Telemetry.CatalogEntryCount
        topologyStatus = $Telemetry.TopologyStatus
        topologyGeneration = $Telemetry.TopologyGeneration
        topologyDisplayCount = $Telemetry.TopologyDisplayCount
        desktopHostStatus = $Telemetry.DesktopHostStatus
        desktopHostGeneration = $Telemetry.DesktopHostGeneration
        desktopHostOwnedWindowCount = $Telemetry.DesktopHostOwnedWindowCount
        desktopHostWorkspaceRevision = $Telemetry.DesktopHostWorkspaceRevision
        desktopHostTopologyGeneration = $Telemetry.DesktopHostTopologyGeneration
        desktopHostRenderedContainerCount = `
            $Telemetry.DesktopHostRenderedContainerCount
        explicitInteractionActive = $Telemetry.ExplicitInteractionActive
        selectionRevision = $Telemetry.SelectionRevision
        interactionStatus = $Telemetry.InteractionStatus
        interactionRevision = $Telemetry.InteractionRevision
    } | ConvertTo-Json -Compress
}

function Test-CommitMatches {
    param([string] $Actual, [string] $Expected)

    if ([string]::IsNullOrWhiteSpace($Actual) -or
        [string]::IsNullOrWhiteSpace($Expected)) {
        return $false
    }

    return $Actual.StartsWith($Expected,
            [StringComparison]::OrdinalIgnoreCase) -or
        $Expected.StartsWith($Actual,
            [StringComparison]::OrdinalIgnoreCase)
}

function Test-Evidence {
    param([object] $Evidence, [string] $Commit)

    $failures = [System.Collections.Generic.List[string]]::new()
    function Add-Check {
        param([bool] $Condition, [string] $Code)
        if (-not $Condition) {
            $failures.Add($Code)
        }
    }

    Add-Check ($Evidence.schemaVersion -eq 1) 'SchemaVersionMismatch'
    Add-Check ($Evidence.purpose -eq $expectedPurpose) 'PurposeMismatch'
    Add-Check ($Evidence.slice -eq $expectedSlice) 'SliceMismatch'
    Add-Check (Test-CommitMatches $Evidence.commit $Commit) `
        'CommitMismatch'
    Add-Check ($Evidence.operatorId -match '^O[1-9]$') `
        'AnonymousOperatorInvalid'
    Add-Check ($Evidence.operatorIdentifierPolicy -eq 'AnonymousLabelsOnly') `
        'OperatorPolicyInvalid'
    Add-Check ($Evidence.resultStatus -eq `
            'PendingReal24HourEvidenceReview') 'InputStatusInvalid'
    Add-Check (-not $Evidence.canProduceM4cPass) `
        'InputMustNotSelfApprove'
    Add-Check (-not $Evidence.containsPathsNamesContentHandlesOrProcessIds) `
        'SensitiveEvidenceFlagPresent'
    Add-Check (@($Evidence.blockers).Count -eq 1 -and
        @($Evidence.blockers)[0] -eq 'Real24HourEvidenceNotReviewed') `
        'InputBlockerInvalid'
    $budgets = $Evidence.budgets
    Add-Check ($null -ne $budgets -and
        $budgets.privateBytesFinalMedianDeltaBytes -eq
            $privateBytesMedianDeltaLimit -and
        $budgets.privateBytesMaximumSlopeBytesPerHour -eq
            $privateBytesSlopeLimitPerHour -and
        $budgets.handleFinalMedianDelta -eq $handleMedianDeltaLimit -and
        $budgets.handleMaximumSlopePerHour -eq $handleSlopeLimitPerHour -and
        $budgets.threadFinalMedianDelta -eq $threadMedianDeltaLimit -and
        $budgets.threadMaximumSlopePerHour -eq $threadSlopeLimitPerHour -and
        $budgets.workerPrivateBytesFinalMedianDeltaBytes -eq
            $privateBytesMedianDeltaLimit -and
        $budgets.workerPrivateBytesMaximumSlopeBytesPerHour -eq
            $privateBytesSlopeLimitPerHour -and
        $budgets.workerHandleFinalMedianDelta -eq $handleMedianDeltaLimit -and
        $budgets.workerHandleMaximumSlopePerHour -eq
            $handleSlopeLimitPerHour -and
        $budgets.workerThreadFinalMedianDelta -eq $threadMedianDeltaLimit -and
        $budgets.workerThreadMaximumSlopePerHour -eq
            $threadSlopeLimitPerHour -and
        $budgets.topLevelWindowFinalMedianDelta -eq
            $windowMedianDeltaLimit -and
        $budgets.topLevelWindowMaximumTransientIncrease -eq
            $windowTransientIncreaseLimit -and
        $budgets.maximumConsecutiveUiaMisses -eq
            $maximumConsecutiveUiaMisses -and
        $budgets.processExitOrRestartCount -eq 0 -and
        $budgets.orphanWorkerCountAtEnd -eq 0 -and
        $budgets.ownedProfileCountAtEnd -eq 0 -and
        $budgets.unexpectedStateRevisionDriftCount -eq 0) `
        'BudgetContractMismatch'

    [DateTimeOffset]$started = [DateTimeOffset]::MinValue
    [DateTimeOffset]$ended = [DateTimeOffset]::MinValue
    $datesValid = [DateTimeOffset]::TryParse(
        [string]$Evidence.startedUtc, [ref]$started) -and
        [DateTimeOffset]::TryParse([string]$Evidence.endedUtc, [ref]$ended)
    Add-Check $datesValid 'TimestampInvalid'
    if ($datesValid) {
        Add-Check ($ended -gt $started -and
            ($ended - $started).TotalHours -ge $expectedDurationHours) `
            'DurationBelow24Hours'
        Add-Check ([Math]::Abs(
                ($ended - $started).TotalHours -
                [double]$Evidence.elapsedHours) -le 0.001) `
            'ElapsedHoursMismatch'
    }

    $samples = @($Evidence.samples)
    Add-Check ($Evidence.sampleCount -eq $samples.Count) `
        'SampleCountMismatch'
    Add-Check ($samples.Count -gt 0) 'SamplesMissing'
    $coverage = [Math]::Min(100,
        100 * $samples.Count / $expectedSampleCount)
    Add-Check ($coverage -ge $minimumSampleCoveragePercent) `
        'SampleCoverageBelow98Percent'
    Add-Check ((Get-MaximumSampleGap $samples) -le $maximumSampleGapSeconds) `
        'SampleGapAbove180Seconds'

    [double]$previousElapsed = -1
    [long]$previousSequence = 0
    [string]$previousState = ''
    [int]$stateDriftCount = 0
    foreach ($sample in $samples) {
        [double]$elapsed = [double]$sample.elapsedSeconds
        Add-Check ($elapsed -gt $previousElapsed) 'SampleOrderInvalid'
        $previousElapsed = $elapsed
        Add-Check ($sample.privateBytes -ge 0 -and
            $sample.handleCount -ge 0 -and
            $sample.threadCount -ge 0 -and
            $sample.workerPrivateBytes -ge 0 -and
            $sample.workerHandleCount -ge 0 -and
            $sample.workerThreadCount -ge 0 -and
            $sample.topLevelWindowCount -ge 0) 'NegativeResourceValue'
        Add-Check $sample.mainWindowPresent 'MainWindowMissing'

        $telemetry = $sample.telemetry
        Add-Check ($null -ne $telemetry) 'TelemetryMissing'
        if ($null -ne $telemetry) {
            Add-Check ($telemetry.SchemaVersion -eq 1) `
                'TelemetrySchemaInvalid'
            Add-Check ($telemetry.Sequence -eq ($previousSequence + 1)) `
                'TelemetrySequenceInvalid'
            $previousSequence = [long]$telemetry.Sequence
            Add-Check (-not $telemetry.ContainsPathsNamesContentHandlesOrProcessIds) `
                'TelemetrySensitiveFlagPresent'
            Add-Check ($telemetry.FormalThumbnailWorkerIntegrated -and
                $telemetry.WorkerProcessCount -eq 1 -and
                $telemetry.ActiveOwnedProfileCount -eq 1 -and
                -not $telemetry.OwnedProfileDeletionConfirmed) `
                'RestrictedWorkerContractInvalid'

            if ($elapsed -ge ($warmupMinutes * 60)) {
                $state = Get-StateRevisionKey $telemetry
                if (-not [string]::IsNullOrEmpty($previousState) -and
                    $previousState -ne $state) {
                    $stateDriftCount++
                }
                $previousState = $state
            }
        }
    }
    Add-Check ($stateDriftCount -eq 0) 'UnexpectedStateRevisionDrift'
    $cleanup = $Evidence.cleanupTelemetry
    Add-Check ($null -ne $cleanup -and
        $cleanup.SchemaVersion -eq 1 -and
        $cleanup.Sequence -eq ($previousSequence + 1) -and
        -not $cleanup.ContainsPathsNamesContentHandlesOrProcessIds -and
        -not $cleanup.FormalThumbnailWorkerIntegrated -and
        $cleanup.WorkerProcessCount -eq 0 -and
        $cleanup.ActiveOwnedProfileCount -eq 0 -and
        $cleanup.OwnedProfileDeletionConfirmed) `
        'CleanupTelemetryInvalid'

    $eligible = @($samples | Where-Object elapsedSeconds -ge `
            ($warmupMinutes * 60))
    $windowCount = [int]($comparisonWindowMinutes * 60 /
        $expectedSampleSeconds)
    $first = @($eligible | Select-Object -First $windowCount)
    $last = @($eligible | Select-Object -Last $windowCount)
    Add-Check ($first.Count -eq $windowCount -and
        $last.Count -eq $windowCount) 'ComparisonWindowsIncomplete'

    $recomputed = $null
    if ($first.Count -eq $windowCount -and $last.Count -eq $windowCount) {
        $firstWindowMedian = Get-Median @($first.topLevelWindowCount)
        $maximumWindowCount = @(
            $eligible.topLevelWindowCount | Measure-Object -Maximum
        )[0].Maximum
        $recomputed = [ordered]@{
            sampleCoveragePercent = [Math]::Round($coverage, 3)
            maximumSampleGapSeconds = Get-MaximumSampleGap $samples
            privateBytesFinalMedianDelta =
                (Get-Median @($last.privateBytes)) -
                (Get-Median @($first.privateBytes))
            privateBytesSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'privateBytes'
            handleFinalMedianDelta =
                (Get-Median @($last.handleCount)) -
                (Get-Median @($first.handleCount))
            handleSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'handleCount'
            threadFinalMedianDelta =
                (Get-Median @($last.threadCount)) -
                (Get-Median @($first.threadCount))
            threadSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'threadCount'
            workerPrivateBytesFinalMedianDelta =
                (Get-Median @($last.workerPrivateBytes)) -
                (Get-Median @($first.workerPrivateBytes))
            workerPrivateBytesSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'workerPrivateBytes'
            workerHandleFinalMedianDelta =
                (Get-Median @($last.workerHandleCount)) -
                (Get-Median @($first.workerHandleCount))
            workerHandleSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'workerHandleCount'
            workerThreadFinalMedianDelta =
                (Get-Median @($last.workerThreadCount)) -
                (Get-Median @($first.workerThreadCount))
            workerThreadSlopePerHour = Get-LinearSlopePerHour `
                $eligible 'workerThreadCount'
            topLevelWindowFinalMedianDelta =
                (Get-Median @($last.topLevelWindowCount)) -
                $firstWindowMedian
            topLevelWindowTransientIncrease =
                $maximumWindowCount - $firstWindowMedian
            maximumConsecutiveUiaMisses =
                Get-MaximumConsecutiveUiaMisses $eligible
            unexpectedStateRevisionDriftCount = $stateDriftCount
        }

        Add-Check ($recomputed.privateBytesFinalMedianDelta -le
            $privateBytesMedianDeltaLimit -and
            $recomputed.privateBytesSlopePerHour -le
            $privateBytesSlopeLimitPerHour -and
            $recomputed.handleFinalMedianDelta -le
            $handleMedianDeltaLimit -and
            $recomputed.handleSlopePerHour -le
            $handleSlopeLimitPerHour -and
            $recomputed.threadFinalMedianDelta -le
            $threadMedianDeltaLimit -and
            $recomputed.threadSlopePerHour -le
            $threadSlopeLimitPerHour -and
            $recomputed.workerPrivateBytesFinalMedianDelta -le
            $privateBytesMedianDeltaLimit -and
            $recomputed.workerPrivateBytesSlopePerHour -le
            $privateBytesSlopeLimitPerHour -and
            $recomputed.workerHandleFinalMedianDelta -le
            $handleMedianDeltaLimit -and
            $recomputed.workerHandleSlopePerHour -le
            $handleSlopeLimitPerHour -and
            $recomputed.workerThreadFinalMedianDelta -le
            $threadMedianDeltaLimit -and
            $recomputed.workerThreadSlopePerHour -le
            $threadSlopeLimitPerHour -and
            $recomputed.topLevelWindowFinalMedianDelta -le
            $windowMedianDeltaLimit -and
            $recomputed.topLevelWindowTransientIncrease -le
            $windowTransientIncreaseLimit -and
            $recomputed.maximumConsecutiveUiaMisses -le
            $maximumConsecutiveUiaMisses) 'ResourceBudgetExceeded'

        Add-Check ($Evidence.summary.workerExitOrRestartCount -eq 0 -and
            $Evidence.summary.orphanWorkerCountAtEnd -eq 0 -and
            $Evidence.summary.ownedProfileCountAtEnd -eq 0 -and
            $Evidence.summary.unexpectedStateRevisionDriftCount -eq 0) `
            'LifecycleBudgetExceeded'
        Add-Check ($Evidence.summary.formalStateRevisionTelemetryAvailable -and
            $Evidence.summary.workerActivityObserved -and
            $Evidence.summary.partialProcessBudgetsWithinLimits) `
            'FormalObservationMissing'
    }

    $uniqueFailures = @($failures | Sort-Object -Unique)
    return [pscustomobject]@{
        eligible = $uniqueFailures.Count -eq 0
        failures = $uniqueFailures
        recomputed = $recomputed
    }
}

function New-SyntheticEvidence {
    $telemetry = [pscustomobject]@{
        SchemaVersion = 1; Sequence = 0
        ContainsPathsNamesContentHandlesOrProcessIds = $false
        FormalThumbnailWorkerIntegrated = $true
        WorkerProcessCount = 1; ActiveOwnedProfileCount = 1
        OwnedProfileDeletionConfirmed = $false
        WorkspaceSaveStatus = 'Saved'; WorkspaceCurrentRevision = 1
        WorkspaceSavedRevision = 1; CatalogStatus = 'Ready'
        CatalogGeneration = 1; CatalogEntryCount = 1
        TopologyStatus = 'Ready'; TopologyGeneration = 1
        TopologyDisplayCount = 1; DesktopHostStatus = 'ReadyReadOnly'
        DesktopHostGeneration = 1; DesktopHostOwnedWindowCount = 1
        DesktopHostWorkspaceRevision = 1
        DesktopHostTopologyGeneration = 1
        DesktopHostRenderedContainerCount = 1
        ExplicitInteractionActive = $false; SelectionRevision = 0
        InteractionStatus = 'EmergencyDisabled'; InteractionRevision = 0
    }
    $samples = for ($index = 0; $index -lt $expectedSampleCount; $index++) {
        $sampleTelemetry = $telemetry.PSObject.Copy()
        $sampleTelemetry.Sequence = $index + 1
        [pscustomobject]@{
            elapsedSeconds = $index * $expectedSampleSeconds
            privateBytes = 100MB; handleCount = 100; threadCount = 10
            workerPrivateBytes = 20MB; workerHandleCount = 20
            workerThreadCount = 2; topLevelWindowCount = 1
            mainWindowPresent = $true; uiAutomationRootAvailable = $true
            telemetry = $sampleTelemetry
        }
    }
    $started = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
    return [pscustomobject]@{
        schemaVersion = 1; purpose = $expectedPurpose; slice = $expectedSlice
        commit = '0123456789ab'; operatorId = 'O1'
        operatorIdentifierPolicy = 'AnonymousLabelsOnly'
        startedUtc = $started.ToString('O')
        endedUtc = $started.AddHours(24).ToString('O'); elapsedHours = 24
        resultStatus = 'PendingReal24HourEvidenceReview'
        budgets = [pscustomobject]@{
            privateBytesFinalMedianDeltaBytes = 64MB
            privateBytesMaximumSlopeBytesPerHour = 2MB
            handleFinalMedianDelta = 32; handleMaximumSlopePerHour = 1
            threadFinalMedianDelta = 4; threadMaximumSlopePerHour = 0.25
            workerPrivateBytesFinalMedianDeltaBytes = 64MB
            workerPrivateBytesMaximumSlopeBytesPerHour = 2MB
            workerHandleFinalMedianDelta = 32
            workerHandleMaximumSlopePerHour = 1
            workerThreadFinalMedianDelta = 4
            workerThreadMaximumSlopePerHour = 0.25
            topLevelWindowFinalMedianDelta = 0
            topLevelWindowMaximumTransientIncrease = 2
            maximumConsecutiveUiaMisses = 2
            processExitOrRestartCount = 0; orphanWorkerCountAtEnd = 0
            ownedProfileCountAtEnd = 0
            unexpectedStateRevisionDriftCount = 0
        }
        sampleCount = $samples.Count; samples = $samples
        summary = [pscustomobject]@{
            workerExitOrRestartCount = 0; orphanWorkerCountAtEnd = 0
            ownedProfileCountAtEnd = 0
            unexpectedStateRevisionDriftCount = 0
            formalStateRevisionTelemetryAvailable = $true
            workerActivityObserved = $true
            partialProcessBudgetsWithinLimits = $true
        }
        cleanupTelemetry = [pscustomobject]@{
            SchemaVersion = 1; Sequence = $expectedSampleCount + 1
            ContainsPathsNamesContentHandlesOrProcessIds = $false
            FormalThumbnailWorkerIntegrated = $false
            WorkerProcessCount = 0; ActiveOwnedProfileCount = 0
            OwnedProfileDeletionConfirmed = $true
        }
        blockers = @('Real24HourEvidenceNotReviewed')
        canProduceM4cPass = $false
        containsPathsNamesContentHandlesOrProcessIds = $false
    }
}

if ($ValidateOnly) {
    $valid = New-SyntheticEvidence
    $validResult = Test-Evidence $valid '0123456789ab'
    Assert-Condition $validResult.eligible `
        "Valid synthetic evidence was rejected: $($validResult.failures -join ', ')"
    $invalid = New-SyntheticEvidence
    $invalid.elapsedHours = 23
    $invalid.endedUtc = '2026-01-01T23:00:00Z'
    $invalid.samples[10].telemetry.WorkerProcessCount = 0
    $invalidResult = Test-Evidence $invalid '0123456789ab'
    Assert-Condition (-not $invalidResult.eligible -and
        $invalidResult.failures -contains 'DurationBelow24Hours' -and
        $invalidResult.failures -contains 'RestrictedWorkerContractInvalid') `
        'Invalid synthetic evidence was not rejected deterministically.'

    [ordered]@{
        schemaVersion = 1
        purpose = 'M4c2cResourceStabilityEvidenceReview'
        resultStatus = 'ReviewContractValidated'
        readsEvidenceOnly = $true
        startsProductProcesses = $false
        changesSystemOrDesktopFiles = $false
        canProduceM4cPass = $false
        liveEvidenceStillPending = $true
    } | ConvertTo-Json
    exit 0
}

Assert-Condition (-not [string]::IsNullOrWhiteSpace($EvidencePath)) `
    'EvidencePath is required.'
Assert-Condition (Test-Path -LiteralPath $EvidencePath -PathType Leaf) `
    'EvidencePath must identify an existing JSON file.'
Assert-Condition (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) `
    'ExpectedCommit is required and must identify the reviewed build.'

$evidence = Get-Content -LiteralPath $EvidencePath -Raw |
    ConvertFrom-Json
$result = Test-Evidence $evidence $ExpectedCommit
$review = [ordered]@{
    schemaVersion = 1
    purpose = 'M4c2cResourceStabilityEvidenceReview'
    reviewedCommit = $Evidence.commit
    expectedCommit = $ExpectedCommit
    resultStatus = if ($result.eligible) {
        'EligibleForM4cDecision'
    } else {
        'RejectedEvidence'
    }
    failures = @($result.failures)
    recomputed = $result.recomputed
    sourceEvidenceUnmodified = $true
    canProduceM4cPass = $false
    requiresHumanAuditDecision = $result.eligible
}

$reviewJson = $review | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($ReviewOutputPath)) {
    $parent = Split-Path -Parent $ReviewOutputPath
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($parent) -and
        (Test-Path -LiteralPath $parent -PathType Container)) `
        'ReviewOutputPath parent directory must already exist.'
    Assert-Condition (-not (Test-Path -LiteralPath $ReviewOutputPath)) `
        'ReviewOutputPath must not already exist.'
    [System.IO.File]::WriteAllText($ReviewOutputPath, $reviewJson,
        [System.Text.UTF8Encoding]::new($false))
}

$reviewJson
if ($result.eligible) {
    exit 0
}

exit 4
