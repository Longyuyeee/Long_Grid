[CmdletBinding()]
param(
    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [string] $EvidenceDirectory,

    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [switch] $DedicatedTestAccountConfirmed,
    [switch] $PreparedAnonymousWorkspaceConfirmed,
    [switch] $RecoveryPlanConfirmed,
    [switch] $ContinuousPowerConfirmed,
    [switch] $NoAutomaticRestartConfirmed,
    [switch] $DesktopHostOptInConfirmed,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$environmentPreflightScript = Join-Path $PSScriptRoot `
    'Test-LongGridResourceStabilityEnvironment.ps1'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\m4c2-resource-stability-runbook.md'
$targetFramework = 'net8.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'

$durationHours = 24
$sampleSeconds = 60
$warmupMinutes = 30
$comparisonWindowMinutes = 60
$minimumSampleCoveragePercent = 98
$maximumSampleGapSeconds = 180
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
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-LongGridProcesses {
    @(
        Get-Process -Name @(
            'LongGrid.App',
            'LongGrid.ThumbnailWorker'
        ) -ErrorAction SilentlyContinue
    )
}

function Assert-CleanSession {
    param([string] $Checkpoint)

    $processes = @(Get-LongGridProcesses)
    try {
        Assert-Condition ($processes.Count -eq 0) `
            "$Checkpoint requires zero LongGrid.App or LongGrid.ThumbnailWorker processes; found PID(s): $($processes.Id -join ', '). This launcher never terminates processes it did not start."
    }
    finally {
        foreach ($process in $processes) {
            $process.Dispose()
        }
    }
}

function Get-Median {
    param([double[]] $Values)

    Assert-Condition ($Values.Count -gt 0) 'A median requires at least one value.'
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) {
        return [double]$ordered[$middle]
    }

    return ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2
}

function Get-LinearSlopePerHour {
    param(
        [object[]] $Samples,
        [string] $PropertyName
    )

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
            continue
        }

        $current++
        if ($current -gt $maximum) {
            $maximum = $current
        }
    }

    return $maximum
}

function Get-StateRevisionDriftCount {
    param([object[]] $Samples)

    [int]$driftCount = 0
    [string]$previous = ''
    foreach ($sample in $Samples) {
        $telemetry = $sample.telemetry
        [string]$current = [ordered]@{
            workspaceSaveStatus = $telemetry.WorkspaceSaveStatus
            workspaceCurrentRevision = $telemetry.WorkspaceCurrentRevision
            workspaceSavedRevision = $telemetry.WorkspaceSavedRevision
            catalogStatus = $telemetry.CatalogStatus
            catalogGeneration = $telemetry.CatalogGeneration
            catalogEntryCount = $telemetry.CatalogEntryCount
            topologyStatus = $telemetry.TopologyStatus
            topologyGeneration = $telemetry.TopologyGeneration
            topologyDisplayCount = $telemetry.TopologyDisplayCount
            desktopHostStatus = $telemetry.DesktopHostStatus
            desktopHostGeneration = $telemetry.DesktopHostGeneration
            desktopHostOwnedWindowCount = `
                $telemetry.DesktopHostOwnedWindowCount
            desktopHostWorkspaceRevision = `
                $telemetry.DesktopHostWorkspaceRevision
            desktopHostTopologyGeneration = `
                $telemetry.DesktopHostTopologyGeneration
            desktopHostRenderedContainerCount = `
                $telemetry.DesktopHostRenderedContainerCount
            explicitInteractionActive = $telemetry.ExplicitInteractionActive
            selectionRevision = $telemetry.SelectionRevision
            interactionStatus = $telemetry.InteractionStatus
            interactionRevision = $telemetry.InteractionRevision
        } | ConvertTo-Json -Compress
        if (-not [string]::IsNullOrEmpty($previous) -and
            -not [string]::Equals($previous, $current)) {
            $driftCount++
        }

        $previous = $current
    }

    return $driftCount
}

function Get-TopLevelWindowCount {
    param([int] $ProcessId)

    return [LongGridResourceWindowCounter]::CountForProcess(
        [uint32]$ProcessId)
}

function Test-UiaRootAvailable {
    param([IntPtr] $WindowHandle)

    if ($WindowHandle -eq [IntPtr]::Zero) {
        return $false
    }

    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $WindowHandle)
        return $null -ne $root
    }
    catch {
        return $false
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid resource-stability sessions can only run on Windows.'
}

foreach ($requiredPath in @(
    $projectPath,
    $startScript,
    $environmentPreflightScript,
    $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Resource-stability session dependency was not found: $requiredPath"
    }
}

$commit = 'unavailable'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = & git -C $projectRoot rev-parse --short=12 HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and
        -not [string]::IsNullOrWhiteSpace($candidate)) {
        $commit = $candidate.Trim()
    }
}

$sessionContract = [ordered]@{
    schemaVersion = 1
    purpose = 'M4c2FormalApp24HourResourceStabilitySession'
    slice = 'M4c2b2'
    mode = if ($ValidateOnly) { 'ValidateOnly' } else { 'LivePartialEvidence' }
    operatorId = if ($ValidateOnly) {
        'O1-O9-required-at-runtime'
    } else {
        $OperatorId
    }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    resultStatus = 'PendingLiveEvidence'
    durationHours = $durationHours
    sampleSeconds = $sampleSeconds
    warmupMinutes = $warmupMinutes
    comparisonWindowMinutes = $comparisonWindowMinutes
    minimumSampleCoveragePercent = $minimumSampleCoveragePercent
    maximumSampleGapSeconds = $maximumSampleGapSeconds
    budgets = [ordered]@{
        privateBytesFinalMedianDeltaBytes = $privateBytesMedianDeltaLimit
        privateBytesMaximumSlopeBytesPerHour = $privateBytesSlopeLimitPerHour
        handleFinalMedianDelta = $handleMedianDeltaLimit
        handleMaximumSlopePerHour = $handleSlopeLimitPerHour
        threadFinalMedianDelta = $threadMedianDeltaLimit
        threadMaximumSlopePerHour = $threadSlopeLimitPerHour
        workerPrivateBytesFinalMedianDeltaBytes =
            $privateBytesMedianDeltaLimit
        workerPrivateBytesMaximumSlopeBytesPerHour =
            $privateBytesSlopeLimitPerHour
        workerHandleFinalMedianDelta = $handleMedianDeltaLimit
        workerHandleMaximumSlopePerHour = $handleSlopeLimitPerHour
        workerThreadFinalMedianDelta = $threadMedianDeltaLimit
        workerThreadMaximumSlopePerHour = $threadSlopeLimitPerHour
        topLevelWindowFinalMedianDelta = $windowMedianDeltaLimit
        topLevelWindowMaximumTransientIncrease = $windowTransientIncreaseLimit
        maximumConsecutiveUiaMisses = $maximumConsecutiveUiaMisses
        processExitOrRestartCount = 0
        orphanWorkerCountAtEnd = 0
        ownedProfileCountAtEnd = 0
        unexpectedStateRevisionDriftCount = 0
    }
    startsFreshFormalApp = -not $ValidateOnly
    ownsStartedFormalApp = -not $ValidateOnly
    enablesDesktopHostDevelopmentOptIn = -not $ValidateOnly
    enablesExplicitInteraction = $false
    readsDesktopFirstLevelMetadata = -not $ValidateOnly
    readsDesktopFileContent = $false
    changesDesktopFiles = $false
    changesSystemSettings = $false
    recordsPathsNamesContentHandlesOrProcessIds = $false
    writesEvidenceOnlyToExplicitDirectory = -not $ValidateOnly
    requiresEnvironmentPreflight = $true
    formalThumbnailWorkerIntegrated = $true
    formalStateRevisionTelemetryAvailable = $true
    canProduceM4cPass = $false
    blockers = @(
        'Real24HourEvidenceNotCollected'
    )
}

Assert-Condition (
    $sessionContract.resultStatus -eq 'PendingLiveEvidence' -and
    $sessionContract.durationHours -eq 24 -and
    $sessionContract.sampleSeconds -eq 60 -and
    $sessionContract.warmupMinutes -eq 30 -and
    $sessionContract.comparisonWindowMinutes -eq 60 -and
    $sessionContract.minimumSampleCoveragePercent -eq 98 -and
    $sessionContract.maximumSampleGapSeconds -eq 180 -and
    $sessionContract.budgets.privateBytesFinalMedianDeltaBytes -eq 64MB -and
    $sessionContract.budgets.privateBytesMaximumSlopeBytesPerHour -eq 2MB -and
    $sessionContract.budgets.handleFinalMedianDelta -eq 32 -and
    $sessionContract.budgets.handleMaximumSlopePerHour -eq 1 -and
    $sessionContract.budgets.threadFinalMedianDelta -eq 4 -and
    $sessionContract.budgets.threadMaximumSlopePerHour -eq 0.25 -and
    $sessionContract.budgets.workerPrivateBytesFinalMedianDeltaBytes -eq 64MB -and
    $sessionContract.budgets.workerPrivateBytesMaximumSlopeBytesPerHour -eq 2MB -and
    $sessionContract.budgets.workerHandleFinalMedianDelta -eq 32 -and
    $sessionContract.budgets.workerHandleMaximumSlopePerHour -eq 1 -and
    $sessionContract.budgets.workerThreadFinalMedianDelta -eq 4 -and
    $sessionContract.budgets.workerThreadMaximumSlopePerHour -eq 0.25 -and
    $sessionContract.budgets.topLevelWindowFinalMedianDelta -eq 0 -and
    $sessionContract.budgets.topLevelWindowMaximumTransientIncrease -eq 2 -and
    -not $sessionContract.enablesExplicitInteraction -and
    -not $sessionContract.readsDesktopFileContent -and
    -not $sessionContract.changesDesktopFiles -and
    -not $sessionContract.changesSystemSettings -and
    -not $sessionContract.recordsPathsNamesContentHandlesOrProcessIds -and
    $sessionContract.requiresEnvironmentPreflight -and
    $sessionContract.formalThumbnailWorkerIntegrated -and
    $sessionContract.formalStateRevisionTelemetryAvailable -and
    -not $sessionContract.canProduceM4cPass -and
    $sessionContract.blockers.Count -eq 1
) 'The M4c2b2 duration, budget, privacy, blocker or evidence contract is invalid.'

$slopeSelfTestSamples = @(
    [pscustomobject]@{ elapsedSeconds = 0; value = 10 },
    [pscustomobject]@{ elapsedSeconds = 3600; value = 12 },
    [pscustomobject]@{ elapsedSeconds = 7200; value = 14 }
)
$uiaSelfTestSamples = @(
    [pscustomobject]@{ uiAutomationRootAvailable = $true },
    [pscustomobject]@{ uiAutomationRootAvailable = $false },
    [pscustomobject]@{ uiAutomationRootAvailable = $false },
    [pscustomobject]@{ uiAutomationRootAvailable = $true }
)
$telemetrySelfTest = [pscustomobject]@{
    WorkspaceSaveStatus = 'Saved'
    WorkspaceCurrentRevision = 2
    WorkspaceSavedRevision = 2
    CatalogStatus = 'Ready'
    CatalogGeneration = 3
    CatalogEntryCount = 4
    TopologyStatus = 'Ready'
    TopologyGeneration = 5
    TopologyDisplayCount = 1
    DesktopHostStatus = 'ReadyReadOnly'
    DesktopHostGeneration = 6
    DesktopHostOwnedWindowCount = 1
    DesktopHostWorkspaceRevision = 2
    DesktopHostTopologyGeneration = 5
    DesktopHostRenderedContainerCount = 1
    ExplicitInteractionActive = $false
    SelectionRevision = 0
    InteractionStatus = 'EmergencyDisabled'
    InteractionRevision = 0
}
$telemetrySelfTestSamples = @(
    [pscustomobject]@{ telemetry = $telemetrySelfTest },
    [pscustomobject]@{ telemetry = $telemetrySelfTest }
)
Assert-Condition (
    (Get-Median @(3, 1, 2)) -eq 2 -and
    (Get-Median @(4, 1, 3, 2)) -eq 2.5 -and
    [Math]::Abs((Get-LinearSlopePerHour $slopeSelfTestSamples 'value') - 2) `
        -lt 0.000001 -and
    (Get-MaximumSampleGap $slopeSelfTestSamples) -eq 3600 -and
    (Get-MaximumConsecutiveUiaMisses $uiaSelfTestSamples) -eq 2 -and
    (Get-StateRevisionDriftCount $telemetrySelfTestSamples) -eq 0
) 'The M4c2b2 deterministic trend helper self-test failed.'

$sessionContract | ConvertTo-Json -Depth 5

if ($ValidateOnly) {
    Write-Output 'M4c2b2 formal restricted worker contract validation passed; live 24-hour evidence and M4c remain pending.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($OperatorId)) {
    throw 'OperatorId is required and must be one anonymous label from O1 through O9.'
}

foreach ($confirmation in @(
    [pscustomobject]@{
        accepted = $DedicatedTestAccountConfirmed
        message = 'DedicatedTestAccountConfirmed is required.'
    },
    [pscustomobject]@{
        accepted = $PreparedAnonymousWorkspaceConfirmed
        message = 'PreparedAnonymousWorkspaceConfirmed is required.'
    },
    [pscustomobject]@{
        accepted = $RecoveryPlanConfirmed
        message = 'RecoveryPlanConfirmed is required.'
    },
    [pscustomobject]@{
        accepted = $ContinuousPowerConfirmed
        message = 'ContinuousPowerConfirmed is required.'
    },
    [pscustomobject]@{
        accepted = $NoAutomaticRestartConfirmed
        message = 'NoAutomaticRestartConfirmed is required.'
    },
    [pscustomobject]@{
        accepted = $DesktopHostOptInConfirmed
        message = 'DesktopHostOptInConfirmed is required.'
    })) {
    Assert-Condition $confirmation.accepted $confirmation.message
}

Assert-Condition (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) `
    'EvidenceDirectory is required for a live session.'
Assert-Condition (Test-Path -LiteralPath $EvidenceDirectory -PathType Container) `
    'EvidenceDirectory must be an existing dedicated directory.'
$existingEvidence = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Force)
Assert-Condition ($existingEvidence.Count -eq 0) `
    'EvidenceDirectory must be empty before the session starts.'

& $environmentPreflightScript `
    -EvidenceDirectory $EvidenceDirectory `
    -DedicatedTestAccountConfirmed:$DedicatedTestAccountConfirmed `
    -PreparedAnonymousWorkspaceConfirmed:$PreparedAnonymousWorkspaceConfirmed `
    -RecoveryPlanConfirmed:$RecoveryPlanConfirmed `
    -ContinuousPowerConfirmed:$ContinuousPowerConfirmed `
    -NoAutomaticRestartConfirmed:$NoAutomaticRestartConfirmed
Assert-Condition ($LASTEXITCODE -eq 0) `
    'M4c2c environment preflight rejected the live session.'

Assert-CleanSession 'M4c2b2 resource-stability preflight'

& $startScript `
    -Configuration $Configuration `
    -ValidateOnly `
    -NoRestore:$NoRestore `
    -NoBuild:$NoBuild
if ($LASTEXITCODE -ne 0) {
    throw "Long Grid startup validation failed with exit code $LASTEXITCODE."
}

$appPath = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier\LongGrid.App.exe"
Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) `
    "LongGrid.App executable was not found: $appPath"

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LongGridResourceWindowCounter
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

    public static int CountForProcess(uint processId)
    {
        int count = 0;
        EnumWindows((window, state) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId)
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);
}
'@
Add-Type -AssemblyName UIAutomationClient

$oldDesktopHostOptIn = $env:LONGGRID_ENABLE_DESKTOP_HOST
$oldInteractionEmergencyDisable = `
    $env:LONGGRID_DISABLE_DESKTOP_INTERACTION
$oldTelemetryPipe = $env:LONGGRID_RESOURCE_TELEMETRY_PIPE
$oldTelemetryAcknowledgement = `
    $env:LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION
$productProcess = $null
$telemetryPipe = $null
$telemetryReader = $null
$telemetryWriter = $null
$samples = [System.Collections.Generic.List[object]]::new()
$sessionStartedUtc = $null
$workerProcessId = $null
$workerExitOrRestartCount = 0
$orphanWorkerCountAtEnd = 0
$ownedProfileCountAtEnd = $null
$cleanupTelemetry = $null
$sessionId = [Guid]::NewGuid().ToString('N')
$telemetryPipeName = "LongGrid.ResourceTelemetry.$sessionId"
$lastTelemetrySequence = 0
$evidencePath = Join-Path $EvidenceDirectory `
    "long-grid-m4c2b2-$sessionId.json"

try {
    $env:LONGGRID_ENABLE_DESKTOP_HOST = '1'
    $env:LONGGRID_DISABLE_DESKTOP_INTERACTION = '1'
    $env:LONGGRID_RESOURCE_TELEMETRY_PIPE = $telemetryPipeName
    $env:LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION = '1'

    $productProcess = Start-Process -FilePath $appPath -PassThru
    $windowDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $productProcess.Refresh()
    } while (-not $productProcess.HasExited -and
        $productProcess.MainWindowHandle -eq [IntPtr]::Zero -and
        [DateTime]::UtcNow -lt $windowDeadline)

    Assert-Condition (-not $productProcess.HasExited) `
        'LongGrid.App exited before the resource-stability session began.'
    Assert-Condition ($productProcess.MainWindowHandle -ne [IntPtr]::Zero) `
        'LongGrid.App did not expose a main window within 15 seconds.'

    $telemetryPipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        '.',
        $telemetryPipeName,
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous)
    $telemetryPipe.Connect(15000)
    $telemetryReader = [System.IO.StreamReader]::new(
        $telemetryPipe,
        [System.Text.Encoding]::UTF8,
        $false,
        4096,
        $true)
    $telemetryWriter = [System.IO.StreamWriter]::new(
        $telemetryPipe,
        [System.Text.UTF8Encoding]::new($false),
        4096,
        $true)
    $telemetryWriter.AutoFlush = $true

    $sessionStartedUtc = [DateTimeOffset]::UtcNow
    $deadlineUtc = $sessionStartedUtc.AddHours($durationHours)
    do {
        $sampledUtc = [DateTimeOffset]::UtcNow
        $productProcess.Refresh()
        if ($productProcess.HasExited) {
            break
        }

        $telemetryWriter.WriteLine('snapshot')
        $telemetryReadTask = $telemetryReader.ReadLineAsync()
        Assert-Condition ($telemetryReadTask.Wait(15000)) `
            'Formal App resource telemetry timed out after 15 seconds.'
        $telemetryJson = $telemetryReadTask.GetAwaiter().GetResult()
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($telemetryJson)) `
            'Formal App resource telemetry disconnected during the session.'
        $telemetry = $telemetryJson | ConvertFrom-Json
        Assert-Condition (
            $telemetry.SchemaVersion -eq 1 -and
            $telemetry.Sequence -eq ($lastTelemetrySequence + 1) -and
            -not $telemetry.ContainsPathsNamesContentHandlesOrProcessIds -and
            $telemetry.FormalThumbnailWorkerIntegrated -and
            $telemetry.WorkerProcessCount -eq 1 -and
            $telemetry.ActiveOwnedProfileCount -eq 1 -and
            -not $telemetry.OwnedProfileDeletionConfirmed
        ) 'Formal App resource telemetry violated its restricted worker contract.'
        $lastTelemetrySequence = $telemetry.Sequence

        $workerProcesses = @(
            Get-Process -Name 'LongGrid.ThumbnailWorker' `
                -ErrorAction SilentlyContinue
        )
        Assert-Condition ($workerProcesses.Count -eq 1) `
            'The formal App must own exactly one restricted thumbnail worker.'
        $workerProcess = $workerProcesses[0]
        $workerProcess.Refresh()
        if ($null -eq $workerProcessId) {
            $workerProcessId = $workerProcess.Id
        } elseif ($workerProcessId -ne $workerProcess.Id) {
            $workerExitOrRestartCount++
            $workerProcessId = $workerProcess.Id
        }

        $samples.Add([pscustomobject]@{
            elapsedSeconds = [Math]::Round(
                ($sampledUtc - $sessionStartedUtc).TotalSeconds,
                3)
            privateBytes = [long]$productProcess.PrivateMemorySize64
            handleCount = [int]$productProcess.HandleCount
            threadCount = [int]$productProcess.Threads.Count
            workerPrivateBytes = [long]$workerProcess.PrivateMemorySize64
            workerHandleCount = [int]$workerProcess.HandleCount
            workerThreadCount = [int]$workerProcess.Threads.Count
            topLevelWindowCount = Get-TopLevelWindowCount $productProcess.Id
            mainWindowPresent = $productProcess.MainWindowHandle -ne [IntPtr]::Zero
            uiAutomationRootAvailable = Test-UiaRootAvailable `
                $productProcess.MainWindowHandle
            telemetry = $telemetry
        })
        $workerProcess.Dispose()

        if ($sampledUtc -ge $deadlineUtc) {
            break
        }

        Start-Sleep -Seconds $sampleSeconds
    } while ($true)
}
finally {
    if ($null -ne $telemetryWriter) {
        try {
            $telemetryWriter.WriteLine('complete')
            $cleanupReadTask = $telemetryReader.ReadLineAsync()
            if ($cleanupReadTask.Wait(15000)) {
                $cleanupJson = $cleanupReadTask.GetAwaiter().GetResult()
                if (-not [string]::IsNullOrWhiteSpace($cleanupJson)) {
                    $candidateCleanupTelemetry =
                        $cleanupJson | ConvertFrom-Json
                    if ($candidateCleanupTelemetry.SchemaVersion -eq 1 -and
                        $candidateCleanupTelemetry.Sequence -eq
                            ($lastTelemetrySequence + 1) -and
                        -not $candidateCleanupTelemetry.ContainsPathsNamesContentHandlesOrProcessIds -and
                        -not $candidateCleanupTelemetry.FormalThumbnailWorkerIntegrated -and
                        $candidateCleanupTelemetry.WorkerProcessCount -eq 0 -and
                        $candidateCleanupTelemetry.ActiveOwnedProfileCount -eq 0 -and
                        $candidateCleanupTelemetry.OwnedProfileDeletionConfirmed) {
                        $cleanupTelemetry = $candidateCleanupTelemetry
                        $ownedProfileCountAtEnd = [int]
                            $cleanupTelemetry.ActiveOwnedProfileCount
                    }
                }
            }
        }
        catch {
            $cleanupTelemetry = $null
            $ownedProfileCountAtEnd = $null
        }
        $telemetryWriter.Dispose()
    }
    if ($null -ne $telemetryReader) {
        $telemetryReader.Dispose()
    }
    if ($null -ne $telemetryPipe) {
        $telemetryPipe.Dispose()
    }

    if ($null -ne $productProcess) {
        $productProcess.Refresh()
        if (-not $productProcess.HasExited) {
            $null = $productProcess.CloseMainWindow()
            if (-not $productProcess.WaitForExit(15000)) {
                Stop-Process -Id $productProcess.Id -Force
                $productProcess.WaitForExit()
            }
        }

        $productProcess.Dispose()
    }

    $workerExitDeadline = [DateTime]::UtcNow.AddSeconds(15)
    $remainingWorkerCount = 0
    do {
        $remainingWorkers = @(
            Get-Process -Name 'LongGrid.ThumbnailWorker' `
                -ErrorAction SilentlyContinue
        )
        $remainingWorkerCount = $remainingWorkers.Count
        foreach ($remainingWorker in $remainingWorkers) {
            $remainingWorker.Dispose()
        }
        if ($remainingWorkerCount -eq 0) {
            break
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $workerExitDeadline)
    $orphanWorkerCountAtEnd = $remainingWorkerCount

    if ($null -eq $oldDesktopHostOptIn) {
        Remove-Item Env:LONGGRID_ENABLE_DESKTOP_HOST -ErrorAction SilentlyContinue
    } else {
        $env:LONGGRID_ENABLE_DESKTOP_HOST = $oldDesktopHostOptIn
    }

    if ($null -eq $oldInteractionEmergencyDisable) {
        Remove-Item Env:LONGGRID_DISABLE_DESKTOP_INTERACTION `
            -ErrorAction SilentlyContinue
    } else {
        $env:LONGGRID_DISABLE_DESKTOP_INTERACTION = `
            $oldInteractionEmergencyDisable
    }

    if ($null -eq $oldTelemetryPipe) {
        Remove-Item Env:LONGGRID_RESOURCE_TELEMETRY_PIPE `
            -ErrorAction SilentlyContinue
    } else {
        $env:LONGGRID_RESOURCE_TELEMETRY_PIPE = $oldTelemetryPipe
    }

    if ($null -eq $oldTelemetryAcknowledgement) {
        Remove-Item Env:LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION `
            -ErrorAction SilentlyContinue
    } else {
        $env:LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION = `
            $oldTelemetryAcknowledgement
    }
}

$sessionEndedUtc = [DateTimeOffset]::UtcNow
$expectedSampleCount = [int](
    ($durationHours * 3600 / $sampleSeconds) + 1)
$sampleCoveragePercent = [Math]::Round(
    [Math]::Min(100, 100 * $samples.Count / $expectedSampleCount),
    3)
$maximumObservedSampleGap = Get-MaximumSampleGap @($samples)
$maximumObservedUiaMisses = Get-MaximumConsecutiveUiaMisses @($samples)
$eligibleSamples = @(
    $samples | Where-Object elapsedSeconds -ge ($warmupMinutes * 60)
)
$stateRevisionDriftCount = Get-StateRevisionDriftCount $eligibleSamples
$windowSampleCount = [int]($comparisonWindowMinutes * 60 / $sampleSeconds)
$firstWindow = @($eligibleSamples | Select-Object -First $windowSampleCount)
$lastWindow = @($eligibleSamples | Select-Object -Last $windowSampleCount)

$summary = $null
if ($firstWindow.Count -gt 0 -and $lastWindow.Count -gt 0) {
    $firstPrivateMedian = Get-Median @($firstWindow.privateBytes)
    $lastPrivateMedian = Get-Median @($lastWindow.privateBytes)
    $firstHandleMedian = Get-Median @($firstWindow.handleCount)
    $lastHandleMedian = Get-Median @($lastWindow.handleCount)
    $firstThreadMedian = Get-Median @($firstWindow.threadCount)
    $lastThreadMedian = Get-Median @($lastWindow.threadCount)
    $firstWorkerPrivateMedian = Get-Median @($firstWindow.workerPrivateBytes)
    $lastWorkerPrivateMedian = Get-Median @($lastWindow.workerPrivateBytes)
    $firstWorkerHandleMedian = Get-Median @($firstWindow.workerHandleCount)
    $lastWorkerHandleMedian = Get-Median @($lastWindow.workerHandleCount)
    $firstWorkerThreadMedian = Get-Median @($firstWindow.workerThreadCount)
    $lastWorkerThreadMedian = Get-Median @($lastWindow.workerThreadCount)
    $firstWindowMedian = Get-Median @($firstWindow.topLevelWindowCount)
    $lastWindowMedian = Get-Median @($lastWindow.topLevelWindowCount)
    $maximumWindowCount = @(
        $eligibleSamples.topLevelWindowCount | Measure-Object -Maximum
    )[0].Maximum
    $privateDelta = $lastPrivateMedian - $firstPrivateMedian
    $privateSlope = Get-LinearSlopePerHour $eligibleSamples 'privateBytes'
    $handleDelta = $lastHandleMedian - $firstHandleMedian
    $handleSlope = Get-LinearSlopePerHour $eligibleSamples 'handleCount'
    $threadDelta = $lastThreadMedian - $firstThreadMedian
    $threadSlope = Get-LinearSlopePerHour $eligibleSamples 'threadCount'
    $workerPrivateDelta =
        $lastWorkerPrivateMedian - $firstWorkerPrivateMedian
    $workerPrivateSlope = Get-LinearSlopePerHour `
        $eligibleSamples 'workerPrivateBytes'
    $workerHandleDelta = $lastWorkerHandleMedian - $firstWorkerHandleMedian
    $workerHandleSlope = Get-LinearSlopePerHour `
        $eligibleSamples 'workerHandleCount'
    $workerThreadDelta = $lastWorkerThreadMedian - $firstWorkerThreadMedian
    $workerThreadSlope = Get-LinearSlopePerHour `
        $eligibleSamples 'workerThreadCount'
    $windowDelta = $lastWindowMedian - $firstWindowMedian
    $summary = [ordered]@{
        expectedSampleCount = $expectedSampleCount
        sampleCoveragePercent = $sampleCoveragePercent
        maximumSampleGapSeconds = $maximumObservedSampleGap
        privateBytesFinalMedianDelta = $privateDelta
        privateBytesSlopePerHour = $privateSlope
        handleFinalMedianDelta = $handleDelta
        handleSlopePerHour = $handleSlope
        threadFinalMedianDelta = $threadDelta
        threadSlopePerHour = $threadSlope
        workerPrivateBytesFinalMedianDelta = $workerPrivateDelta
        workerPrivateBytesSlopePerHour = $workerPrivateSlope
        workerHandleFinalMedianDelta = $workerHandleDelta
        workerHandleSlopePerHour = $workerHandleSlope
        workerThreadFinalMedianDelta = $workerThreadDelta
        workerThreadSlopePerHour = $workerThreadSlope
        workerExitOrRestartCount = $workerExitOrRestartCount
        orphanWorkerCountAtEnd = $orphanWorkerCountAtEnd
        ownedProfileCountAtEnd = $ownedProfileCountAtEnd
        topLevelWindowFinalMedianDelta = $windowDelta
        topLevelWindowMaximum = $maximumWindowCount
        uiAutomationMisses = @(
            $eligibleSamples | Where-Object { -not $_.uiAutomationRootAvailable }
        ).Count
        maximumConsecutiveUiaMisses = $maximumObservedUiaMisses
        unexpectedStateRevisionDriftCount = $stateRevisionDriftCount
        formalStateRevisionTelemetryAvailable = $true
        workerActivityObserved = $true
        partialProcessBudgetsWithinLimits =
            $sampleCoveragePercent -ge $minimumSampleCoveragePercent -and
            $maximumObservedSampleGap -le $maximumSampleGapSeconds -and
            $privateDelta -le $privateBytesMedianDeltaLimit -and
            $privateSlope -le $privateBytesSlopeLimitPerHour -and
            $handleDelta -le $handleMedianDeltaLimit -and
            $handleSlope -le $handleSlopeLimitPerHour -and
            $threadDelta -le $threadMedianDeltaLimit -and
            $threadSlope -le $threadSlopeLimitPerHour -and
            $workerPrivateDelta -le $privateBytesMedianDeltaLimit -and
            $workerPrivateSlope -le $privateBytesSlopeLimitPerHour -and
            $workerHandleDelta -le $handleMedianDeltaLimit -and
            $workerHandleSlope -le $handleSlopeLimitPerHour -and
            $workerThreadDelta -le $threadMedianDeltaLimit -and
            $workerThreadSlope -le $threadSlopeLimitPerHour -and
            $workerExitOrRestartCount -eq 0 -and
            $orphanWorkerCountAtEnd -eq 0 -and
            $ownedProfileCountAtEnd -eq 0 -and
            $windowDelta -le $windowMedianDeltaLimit -and
            ($maximumWindowCount - $firstWindowMedian) -le `
                $windowTransientIncreaseLimit -and
            $maximumObservedUiaMisses -le $maximumConsecutiveUiaMisses -and
            $stateRevisionDriftCount -eq 0
    }
}

$evidence = [ordered]@{
    schemaVersion = 1
    purpose = $sessionContract.purpose
    slice = 'M4c2b2'
    commit = $commit
    operatorId = $OperatorId
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    startedUtc = $sessionStartedUtc.ToString('O')
    endedUtc = $sessionEndedUtc.ToString('O')
    elapsedHours = [Math]::Round(
        ($sessionEndedUtc - $sessionStartedUtc).TotalHours,
        6)
    resultStatus = 'PendingReal24HourEvidenceReview'
    budgets = $sessionContract.budgets
    sampleCount = $samples.Count
    samples = $samples
    summary = $summary
    cleanupTelemetry = $cleanupTelemetry
    blockers = @(
        'Real24HourEvidenceNotReviewed'
    )
    canProduceM4cPass = $false
    containsPathsNamesContentHandlesOrProcessIds = $false
}

$temporaryEvidencePath = "$evidencePath.pending"
$evidenceJson = $evidence | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText(
    $temporaryEvidencePath,
    $evidenceJson,
    [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryEvidencePath -Destination $evidencePath

Write-Output (
    'M4c2b2 formal worker evidence was written to the explicit evidence directory. ' +
    'The result remains PendingReal24HourEvidenceReview and cannot produce M4c Pass.'
)
exit 4
