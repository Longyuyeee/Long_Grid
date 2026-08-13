[CmdletBinding()]
param(
    [ValidateSet(
        'B6C3-01', 'B6C3-02', 'B6C3-03', 'B6C3-04',
        'B6C3-05', 'B6C3-06', 'B6C3-07', 'B6C3-08')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeControlledEnvironment,
    [switch] $AcknowledgeIsolatedSource,
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
    'docs\manual-testing\desktop-interaction-input-forwarding-session-runbook.md'

foreach ($requiredPath in @($startScript, $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Input-forwarding session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario) -or
        [string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'Scenario and anonymous OperatorId are required for a live session.'
    }

    if (-not $AcknowledgeControlledEnvironment -or
        -not $AcknowledgeIsolatedSource -or
        -not $AcknowledgeNoExplicitInteraction -or
        -not $AcknowledgeRecoveryPlan) {
        throw 'Controlled environment, isolated source, no-Explicit boundary and recovery acknowledgements are required.'
    }

    if ([Environment]::GetEnvironmentVariable(
            'LONGGRID_DISABLE_DESKTOP_INTERACTION',
            'Process') -eq '1') {
        throw 'The emergency interaction disable is active; the controlled session will not start.'
    }
}

$contract = [ordered]@{
    schemaVersion = 1
    purpose = 'DesktopInteractionIsolatedInputForwardingManualSession'
    scenarios = 'B6C3-01-through-B6C3-08'
    scenario = if ($ValidateOnly) { 'RequiredAtRuntime' } else { $Scenario }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    finalResultStatus = 'PendingManualEvidence'
    forwardsNormalizedInputOnly = $true
    requiresSourceAttestation = $true
    rejectsInjectedInput = $true
    rejectsAutoRepeat = $true
    preparesIntentOnly = $true
    capturesGlobalInput = $false
    sendsSyntheticInput = $false
    entersExplicitInteraction = $false
    mutatesDesktopFiles = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresControlledEnvironment = $true
    requiresRecoveryConfirmation = $true
}

if ($contract.finalResultStatus -ne 'PendingManualEvidence' -or
    -not $contract.forwardsNormalizedInputOnly -or
    -not $contract.requiresSourceAttestation -or
    -not $contract.rejectsInjectedInput -or
    -not $contract.rejectsAutoRepeat -or
    -not $contract.preparesIntentOnly -or
    $contract.capturesGlobalInput -or
    $contract.sendsSyntheticInput -or
    $contract.entersExplicitInteraction -or
    $contract.mutatesDesktopFiles -or
    $contract.capturesEvidence -or
    $contract.writesResultFile -or
    -not $contract.requiresControlledEnvironment -or
    -not $contract.requiresRecoveryConfirmation) {
    throw 'Input-forwarding session safety contract is invalid.'
}

$contract | ConvertTo-Json -Depth 3
if ($ValidateOnly) {
    Write-Output 'Isolated input-forwarding session validation passed; live results remain pending and Explicit stays disabled.'
    exit 0
}

Write-Warning (
    "Execute only $Scenario from the runbook. This launcher enables the isolated " +
    'normalization-to-preparation path; it does not capture global input, send input, ' +
    'enter Explicit, mutate files, capture evidence or claim final Pass.'
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
        throw "Long方格 input-forwarding session exited with code $LASTEXITCODE."
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
