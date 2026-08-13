[CmdletBinding()]
param(
    [ValidateSet('B6C2-01', 'B6C2-02', 'B6C2-03')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeControlledEnvironment,
    [switch] $AcknowledgeNoExplicitInteraction,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\desktop-interaction-intent-session-runbook.md'

foreach ($requiredPath in @($startScript, $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Intent-session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario) -or
        [string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'Scenario and anonymous OperatorId are required for a live session.'
    }

    if (-not $AcknowledgeControlledEnvironment -or
        -not $AcknowledgeNoExplicitInteraction -or
        -not $AcknowledgeRecoveryPlan) {
        throw 'Controlled environment, no-Explicit boundary and recovery acknowledgements are required.'
    }

    if ([Environment]::GetEnvironmentVariable(
            'LONGGRID_DISABLE_DESKTOP_INTERACTION',
            'Process') -eq '1') {
        throw 'The emergency interaction disable is active; the controlled session will not start.'
    }
}

$contract = [ordered]@{
    schemaVersion = 1
    purpose = 'DesktopInteractionIntentPreparationManualSession'
    scenarios = 'B6C2-01-through-B6C2-03'
    scenario = if ($ValidateOnly) { 'RequiredAtRuntime' } else { $Scenario }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    finalResultStatus = 'PendingManualEvidence'
    preparesIntentOnly = $true
    entersExplicitInteraction = $false
    mutatesDesktopFiles = $false
    sendsSyntheticInput = $false
    installsGlobalHooks = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresControlledEnvironment = $true
    requiresNoExplicitAcknowledgement = $true
    requiresRecoveryConfirmation = $true
}

if ($contract.finalResultStatus -ne 'PendingManualEvidence' -or
    -not $contract.preparesIntentOnly -or
    $contract.entersExplicitInteraction -or
    $contract.mutatesDesktopFiles -or
    $contract.sendsSyntheticInput -or
    $contract.installsGlobalHooks -or
    $contract.capturesEvidence -or
    $contract.writesResultFile -or
    -not $contract.requiresControlledEnvironment -or
    -not $contract.requiresNoExplicitAcknowledgement -or
    -not $contract.requiresRecoveryConfirmation) {
    throw 'Intent-session safety contract is invalid.'
}

$contract | ConvertTo-Json -Depth 3
if ($ValidateOnly) {
    Write-Output 'Intent preparation session validation passed; live results remain pending and Explicit stays disabled.'
    exit 0
}

Write-Warning (
    "Execute only $Scenario from the runbook. This launcher enables Intent preparation policy only; " +
    'it does not enable input capture, Explicit mode, file operations, evidence capture or final Pass.'
)

$flagNames = @(
    'LONGGRID_ENABLE_DESKTOP_HOST',
    'LONGGRID_ENABLE_DESKTOP_INTERACTION',
    'LONGGRID_ENABLE_DESKTOP_INTENT_BRIDGE',
    'LONGGRID_ACKNOWLEDGE_DESKTOP_INTENT_SESSION'
)
$previousValues = @{}
foreach ($flagName in $flagNames) {
    $previousValues[$flagName] = [Environment]::GetEnvironmentVariable(
        $flagName,
        'Process')
}

try {
    foreach ($flagName in $flagNames) {
        [Environment]::SetEnvironmentVariable($flagName, '1', 'Process')
    }

    & $startScript `
        -Configuration $Configuration `
        -Architecture x64 `
        -NoRestore:$NoRestore `
        -NoBuild:$NoBuild
    if ($LASTEXITCODE -ne 0) {
        throw "Long方格 intent session exited with code $LASTEXITCODE."
    }
}
finally {
    foreach ($flagName in $flagNames) {
        [Environment]::SetEnvironmentVariable(
            $flagName,
            $previousValues[$flagName],
            'Process')
    }
}
