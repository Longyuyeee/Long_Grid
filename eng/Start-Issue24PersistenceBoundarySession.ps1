[CmdletBinding()]
param(
    [ValidateSet('I24-01', 'I24-02')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [string] $TargetRoot,

    [ValidateSet('PrepareBaseline', 'AttemptFailure', 'RecoverAndRetry')]
    [string] $Phase,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeDedicatedEnvironment,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$phaseExitRunbook = Join-Path $projectRoot 'docs\12-phase-0-exit-runbook.md'
$sessionRunbook = Join-Path $projectRoot `
    'docs\manual-testing\issue-24-persistence-boundary-runbook.md'
$configurationAudit = Join-Path $projectRoot `
    'docs\28-product-configuration-contract-audit.md'
$hostProject = Join-Path $projectRoot `
    'tools\LongGrid.Tools.PersistenceBoundarySession\LongGrid.Tools.PersistenceBoundarySession.csproj'
$markerName = '.longgrid-issue24-dedicated-volume-v1'
$markerContent = 'LONGGRID-ISSUE24-DEDICATED-VOLUME-V1'

if ($env:OS -ne 'Windows_NT') {
    throw 'Issue #24 dedicated-environment sessions can only run on Windows.'
}

foreach ($requiredPath in @(
    $phaseExitRunbook,
    $sessionRunbook,
    $configurationAudit,
    $hostProject)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Issue #24 session dependency was not found: $requiredPath"
    }
}

$scenarioCatalog = [ordered]@{
    'I24-01' = 'RealVolumeCapacityOrQuotaExhaustion'
    'I24-02' = 'ReadOnlyDedicatedVolume'
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario)) {
        throw 'Scenario is required and must be I24-01 or I24-02.'
    }

    if ([string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'OperatorId is required and must be one anonymous label from O1 through O9.'
    }

    if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
        throw 'TargetRoot is required and must be the root of a disposable dedicated test volume.'
    }

    if ([string]::IsNullOrWhiteSpace($Phase)) {
        throw 'Phase is required and must be PrepareBaseline, AttemptFailure, or RecoverAndRetry.'
    }

    if (-not $AcknowledgeDedicatedEnvironment) {
        throw 'AcknowledgeDedicatedEnvironment is required before a live Issue #24 session.'
    }

    if (-not $AcknowledgeRecoveryPlan) {
        throw 'AcknowledgeRecoveryPlan is required before a live Issue #24 session.'
    }

    $resolvedTarget = (Resolve-Path -LiteralPath $TargetRoot -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolvedTarget -PathType Container)) {
        throw 'TargetRoot must resolve to an existing directory.'
    }

    $targetVolumeRoot = [System.IO.Path]::GetPathRoot($resolvedTarget)
    $projectVolumeRoot = [System.IO.Path]::GetPathRoot($projectRoot)
    if ([string]::IsNullOrWhiteSpace($targetVolumeRoot) -or
        [string]::IsNullOrWhiteSpace($projectVolumeRoot)) {
        throw 'The target or project volume root could not be resolved.'
    }

    $targetUri = [System.Uri]::new($resolvedTarget)
    if ($targetUri.IsUnc) {
        throw 'UNC and network roots are outside the approved Issue #24 dedicated-volume scope.'
    }

    $normalizedTarget = $resolvedTarget.TrimEnd('\')
    $normalizedTargetRoot = $targetVolumeRoot.TrimEnd('\')
    if (-not [string]::Equals(
            $normalizedTarget,
            $normalizedTargetRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'TargetRoot must be the dedicated test volume root, not a subdirectory.'
    }

    if ([string]::Equals(
            $targetVolumeRoot,
            $projectVolumeRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The dedicated test volume must not be the workspace or system-under-test source volume.'
    }

    $systemVolumeRoot = if ([string]::IsNullOrWhiteSpace($env:SystemDrive)) {
        $null
    }
    else {
        [System.IO.Path]::GetPathRoot("$($env:SystemDrive)\")
    }
    if (-not [string]::IsNullOrWhiteSpace($systemVolumeRoot) -and
        [string]::Equals(
            $targetVolumeRoot,
            $systemVolumeRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Windows system volume must never be used for an Issue #24 dedicated-volume session.'
    }

    $markerPath = Join-Path $resolvedTarget $markerName
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw 'The dedicated-volume marker is missing. Follow the Issue #24 runbook before continuing.'
    }

    $actualMarker = (Get-Content -LiteralPath $markerPath -Raw).Trim()
    if ($actualMarker -ne $markerContent) {
        throw 'The dedicated-volume marker content is invalid.'
    }

    $sessionDirectory = Join-Path $resolvedTarget 'LongGrid-Issue24-ProductStore-Session'
}

$commit = 'unavailable'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = & git -C $projectRoot rev-parse --short=12 HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) {
        $commit = $candidate.Trim()
    }
}

$sessionContract = [ordered]@{
    schemaVersion = 1
    purpose = 'Issue24ProductionPersistenceBoundaryDedicatedEnvironment'
    scenario = if ($ValidateOnly) { 'I24-01-or-I24-02-required-at-runtime' } else { $Scenario }
    scenarioName = if ($ValidateOnly) { 'RecordedAtRuntime' } else { $scenarioCatalog[$Scenario] }
    phase = if ($ValidateOnly) { 'PrepareBaseline-AttemptFailure-or-RecoverAndRetry-required-at-runtime' } else { $Phase }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    operatingSystem = [System.Environment]::OSVersion.Version.ToString()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    resultStatus = 'PendingDedicatedEnvironmentEvidence'
    targetIdentifierRecorded = $false
    readsTargetMarker = -not $ValidateOnly
    liveSessionWritesTargetVolume = $true
    writesTargetVolume = -not $ValidateOnly
    fillsTargetVolume = $false
    changesVolumeState = $false
    changesAcl = $false
    runsPersistenceProbe = $false
    runsProductConfigurationStoreHost = -not $ValidateOnly
    usesFormalProductConfigurationStore = $true
    capturesEvidence = $false
    writesResultFile = $false
    requiresDedicatedEnvironmentAcknowledgement = $true
    requiresRecoveryPlanAcknowledgement = $true
    requiresManualExecution = $true
    requiresManualEvidenceReview = $true
}

if ($sessionContract.operatorIdentifierPolicy -ne 'AnonymousLabelsOnly' -or
    $sessionContract.resultStatus -ne 'PendingDedicatedEnvironmentEvidence' -or
    $sessionContract.targetIdentifierRecorded -or
    -not $sessionContract.liveSessionWritesTargetVolume -or
    $sessionContract.writesTargetVolume -ne (-not $ValidateOnly) -or
    $sessionContract.fillsTargetVolume -or
    $sessionContract.changesVolumeState -or
    $sessionContract.changesAcl -or
    $sessionContract.runsPersistenceProbe -or
    $sessionContract.runsProductConfigurationStoreHost -ne (-not $ValidateOnly) -or
    -not $sessionContract.usesFormalProductConfigurationStore -or
    $sessionContract.capturesEvidence -or
    $sessionContract.writesResultFile -or
    -not $sessionContract.requiresDedicatedEnvironmentAcknowledgement -or
    -not $sessionContract.requiresRecoveryPlanAcknowledgement -or
    -not $sessionContract.requiresManualExecution -or
    -not $sessionContract.requiresManualEvidenceReview) {
    throw 'Issue #24 dedicated-environment safety or evidence contract is invalid.'
}

$sessionContract | ConvertTo-Json -Depth 3

if ($ValidateOnly) {
    Write-Output 'Issue #24 dedicated-environment session chain validation passed; all real-volume results remain pending.'
    exit 0
}

$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot

Write-Warning (
    "Preflight passed for $Scenario / $Phase. The formal ProductConfigurationStore host writes only its fixed session " +
    'directory on the marked disposable volume. It does not fill the volume, change volume/ACL state, capture evidence ' +
    'or decide final Pass. Follow the selected phase in the runbook and keep the final result Pending until manual review.'
)

$arguments = @(
    'run',
    '--project', $hostProject,
    '--configuration', $Configuration
)
if ($NoRestore) {
    $arguments += '--no-restore'
}
if ($NoBuild) {
    $arguments += '--no-build'
}
$arguments += @(
    '--',
    '--phase', $Phase,
    '--directory', $sessionDirectory
)

& $dotnetHostPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Issue #24 formal product-store phase failed with exit code $LASTEXITCODE."
}
