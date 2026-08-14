[CmdletBinding()]
param(
    [ValidateSet('E2B1-01', 'E2B1-02', 'E2B1-03', 'E2B1-04', 'E2B1-05')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeControlledEnvironment,
    [switch] $AcknowledgeExplicitWithoutFileOperations,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\desktop-interaction-activation-session-runbook.md'

foreach ($requiredPath in @($startScript, $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Activation-session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario) -or
        [string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'Scenario and anonymous OperatorId are required for a live session.'
    }

    if (-not $AcknowledgeControlledEnvironment -or
        -not $AcknowledgeExplicitWithoutFileOperations -or
        -not $AcknowledgeRecoveryPlan) {
        throw 'Controlled environment, Explicit/no-file-operation and recovery acknowledgements are required.'
    }

    if ([Environment]::GetEnvironmentVariable(
            'LONGGRID_DISABLE_DESKTOP_INTERACTION',
            'Process') -eq '1') {
        throw 'The emergency interaction disable is active; the product session will not start.'
    }
}

$contract = [ordered]@{
    schemaVersion = 1
    purpose = 'DesktopInteractionProductActivationManualSession'
    scenarios = 'E2B1-01-through-E2B1-05'
    scenario = if ($ValidateOnly) { 'RequiredAtRuntime' } else { $Scenario }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    finalResultStatus = 'PendingManualEvidence'
    startsProductApp = $true
    ownsPerDisplayFiniteActivationWindows = $true
    acceptsPhysicalPointerAppKeyboardAndUia = $true
    entersExplicitInteraction = $true
    exposesItemSelection = $false
    capturesGlobalInput = $false
    sendsSyntheticInput = $false
    installsGlobalHooks = $false
    integratesExplorerWorkerW = $false
    mutatesDesktopFiles = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresControlledEnvironment = $true
    requiresRecoveryConfirmation = $true
}

if ($contract.finalResultStatus -ne 'PendingManualEvidence' -or
    -not $contract.startsProductApp -or
    -not $contract.ownsPerDisplayFiniteActivationWindows -or
    -not $contract.acceptsPhysicalPointerAppKeyboardAndUia -or
    -not $contract.entersExplicitInteraction -or
    $contract.exposesItemSelection -or
    $contract.capturesGlobalInput -or
    $contract.sendsSyntheticInput -or
    $contract.installsGlobalHooks -or
    $contract.integratesExplorerWorkerW -or
    $contract.mutatesDesktopFiles -or
    $contract.capturesEvidence -or
    $contract.writesResultFile -or
    -not $contract.requiresControlledEnvironment -or
    -not $contract.requiresRecoveryConfirmation) {
    throw 'Product activation-session safety contract is invalid.'
}

$contract | ConvertTo-Json -Depth 3
if ($ValidateOnly) {
    Write-Output 'Product activation-session validation passed; physical input and Narrator results remain pending.'
    exit 0
}

Write-Warning (
    "Execute only $Scenario from the runbook. This session can enter Explicit " +
    'interaction but cannot select desktop items or mutate desktop files.'
)

$flagNames = @(
    'LONGGRID_ENABLE_DESKTOP_HOST',
    'LONGGRID_ENABLE_DESKTOP_INTERACTION',
    'LONGGRID_ENABLE_DESKTOP_INTENT_BRIDGE',
    'LONGGRID_ACKNOWLEDGE_DESKTOP_INTENT_SESSION',
    'LONGGRID_ENABLE_DESKTOP_INPUT_FORWARDING',
    'LONGGRID_ACKNOWLEDGE_DESKTOP_INPUT_FORWARDING_SESSION'
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
        throw "Long方格 product activation session exited with code $LASTEXITCODE."
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
