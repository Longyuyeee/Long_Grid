[CmdletBinding()]
param(
    [ValidateSet('I24-01', 'I24-02')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [string] $TargetRoot,

    [switch] $AcknowledgeDedicatedEnvironment,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$phaseExitRunbook = Join-Path $projectRoot 'docs\12-phase-0-exit-runbook.md'
$sessionRunbook = Join-Path $projectRoot `
    'docs\manual-testing\issue-24-persistence-boundary-runbook.md'
$configurationAudit = Join-Path $projectRoot `
    'docs\28-product-configuration-contract-audit.md'
$markerName = '.longgrid-issue24-dedicated-volume-v1'
$markerContent = 'LONGGRID-ISSUE24-DEDICATED-VOLUME-V1'

if ($env:OS -ne 'Windows_NT') {
    throw 'Issue #24 dedicated-environment sessions can only run on Windows.'
}

foreach ($requiredPath in @($phaseExitRunbook, $sessionRunbook, $configurationAudit)) {
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
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    operatingSystem = [System.Environment]::OSVersion.Version.ToString()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    resultStatus = 'PendingDedicatedEnvironmentEvidence'
    targetIdentifierRecorded = $false
    readsTargetMarkerOnly = -not $ValidateOnly
    writesTargetVolume = $false
    fillsTargetVolume = $false
    changesVolumeState = $false
    changesAcl = $false
    runsPersistenceProbe = $false
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
    $sessionContract.writesTargetVolume -or
    $sessionContract.fillsTargetVolume -or
    $sessionContract.changesVolumeState -or
    $sessionContract.changesAcl -or
    $sessionContract.runsPersistenceProbe -or
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

Write-Warning (
    'Preflight passed. This launcher does not write, fill, mount, unmount, or change the target volume and does not ' +
    'run the persistence probe. Follow only the selected scenario in the runbook, restore the disposable environment, ' +
    'and record a manual result. The final status remains PendingDedicatedEnvironmentEvidence.'
)
