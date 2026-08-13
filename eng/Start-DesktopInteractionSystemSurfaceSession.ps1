[CmdletBinding()]
param(
    [ValidateSet('B6C3-05', 'B6C3-06', 'B6C3-07')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $AcknowledgeControlledEnvironment,
    [switch] $AcknowledgeSystemStateChange,
    [switch] $AcknowledgeReadOnlyDisplayTopologyObservation,
    [switch] $AcknowledgeNoExplicitInteraction,
    [switch] $AcknowledgeRecoveryPlan,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$probeProject = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\LongGrid.Spikes.DesktopHostWindowModels.csproj'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\desktop-interaction-input-forwarding-session-runbook.md'

foreach ($requiredPath in @($probeProject, $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "System-surface session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario) -or
        [string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'Scenario and anonymous OperatorId are required for a live session.'
    }

    if (-not $AcknowledgeControlledEnvironment -or
        -not $AcknowledgeSystemStateChange -or
        -not $AcknowledgeReadOnlyDisplayTopologyObservation -or
        -not $AcknowledgeNoExplicitInteraction -or
        -not $AcknowledgeRecoveryPlan) {
        throw 'Controlled environment, system-state, read-only topology, no-Explicit and recovery acknowledgements are required.'
    }

    if ([Environment]::GetEnvironmentVariable(
            'LONGGRID_DISABLE_DESKTOP_INTERACTION',
            'Process') -eq '1') {
        throw 'The emergency interaction disable is active; the controlled session will not start.'
    }
}

$contract = [ordered]@{
    schemaVersion = 1
    purpose = 'DesktopInteractionSystemSurfaceManualSession'
    supportedScenarios = 'B6C3-05-B6C3-06-and-B6C3-07'
    scenario = if ($ValidateOnly) { 'RequiredAtRuntime' } else { $Scenario }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    finalResultStatus = 'PendingManualEvidence'
    observesPublicWindowsSystemState = $true
    invalidatesPreparedIntentOnUnsafeEvent = $true
    hidesProbeSourceOnUnsafeEvent = $true
    requiresTwoSafeSamplesBeforeRecovery = $true
    observesDisplayTopologyGeneration = $true
    requiresAuthoritativeDisplayTopology = $true
    requiresStabilizedDisplayTopology = $true
    launchesProbeOwnedNativeSource = $true
    startsProductApp = $false
    changesSystemState = $false
    capturesGlobalInput = $false
    sendsSyntheticInput = $false
    entersExplicitInteraction = $false
    mutatesDesktopFiles = $false
    capturesEvidence = $false
    writesResultFile = $false
}

if ($contract.finalResultStatus -ne 'PendingManualEvidence' -or
    -not $contract.observesPublicWindowsSystemState -or
    -not $contract.invalidatesPreparedIntentOnUnsafeEvent -or
    -not $contract.hidesProbeSourceOnUnsafeEvent -or
    -not $contract.requiresTwoSafeSamplesBeforeRecovery -or
    -not $contract.observesDisplayTopologyGeneration -or
    -not $contract.requiresAuthoritativeDisplayTopology -or
    -not $contract.requiresStabilizedDisplayTopology -or
    -not $contract.launchesProbeOwnedNativeSource -or
    $contract.startsProductApp -or
    $contract.changesSystemState -or
    $contract.capturesGlobalInput -or
    $contract.sendsSyntheticInput -or
    $contract.entersExplicitInteraction -or
    $contract.mutatesDesktopFiles -or
    $contract.capturesEvidence -or
    $contract.writesResultFile) {
    throw 'System-surface session safety contract is invalid.'
}

$contract | ConvertTo-Json -Depth 3
if ($ValidateOnly) {
    Write-Output 'System-surface and read-only display-topology session validation passed; live evidence remains pending.'
    exit 0
}

Write-Warning (
    "Execute only $Scenario from the runbook. The probe observes public Windows " +
    'state and samples authoritative display topology without changing either. ' +
    'Only the operator can supply live evidence.'
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

    $arguments = @(
        'run',
        '--project', $probeProject,
        '--configuration', $Configuration
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }
    if ($NoBuild) {
        $arguments += '--no-build'
    }
    $arguments += @('--', '--native-input-system-surface-session')

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Long方格 system-surface session exited with code $LASTEXITCODE."
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
