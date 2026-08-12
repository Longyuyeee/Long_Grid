[CmdletBinding()]
param(
    [ValidateSet('A5-01', 'A5-02', 'A5-03', 'A5-04', 'A5-05', 'A5-06')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeControlledEnvironment,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\desktop-host-product-session-matrix-runbook.md'

foreach ($requiredPath in @($startScript, $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "DesktopHost product-session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario) -or
        [string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'Scenario and anonymous OperatorId are required for a live session.'
    }

    if (-not $AcknowledgeControlledEnvironment -or
        -not $AcknowledgeRecoveryPlan) {
        throw 'Controlled-environment and recovery-plan acknowledgements are required.'
    }
}

$contract = [ordered]@{
    schemaVersion = 1
    purpose = 'DesktopHostReadOnlyProductSessionMatrix'
    scenarios = 'A5-01-through-A5-06'
    scenario = if ($ValidateOnly) { 'RequiredAtRuntime' } else { $Scenario }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    finalResultStatus = 'PendingManualEvidence'
    observerPassIsFinalPass = $false
    desktopHostDevelopmentOptIn = $true
    mutatesDesktopFiles = $false
    sendsSyntheticInput = $false
    changesDisplayOrSessionState = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresManualVisualReview = $true
    requiresManualNarratorReview = $true
    requiresRecoveryConfirmation = $true
}

if ($contract.finalResultStatus -ne 'PendingManualEvidence' -or
    $contract.observerPassIsFinalPass -or
    $contract.mutatesDesktopFiles -or
    $contract.sendsSyntheticInput -or
    $contract.changesDisplayOrSessionState -or
    $contract.capturesEvidence -or
    $contract.writesResultFile -or
    -not $contract.requiresManualVisualReview -or
    -not $contract.requiresManualNarratorReview -or
    -not $contract.requiresRecoveryConfirmation) {
    throw 'DesktopHost product-session safety contract is invalid.'
}

$contract | ConvertTo-Json -Depth 3
if ($ValidateOnly) {
    Write-Output 'DesktopHost product-session matrix validation passed; every final result remains pending manual evidence.'
    exit 0
}

Write-Warning (
    "Execute only $Scenario from the runbook. This launcher does not perform the system action, " +
    'capture evidence, or convert an observation into Pass. Close Long方格 after restoring the baseline.'
)

$previousFlag = [Environment]::GetEnvironmentVariable(
    'LONGGRID_ENABLE_DESKTOP_HOST',
    'Process')
try {
    [Environment]::SetEnvironmentVariable(
        'LONGGRID_ENABLE_DESKTOP_HOST',
        '1',
        'Process')
    & $startScript `
        -Configuration $Configuration `
        -Architecture x64 `
        -NoRestore:$NoRestore `
        -NoBuild:$NoBuild
    if ($LASTEXITCODE -ne 0) {
        throw "Long方格 session exited with code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'LONGGRID_ENABLE_DESKTOP_HOST',
        $previousFlag,
        'Process')
}
