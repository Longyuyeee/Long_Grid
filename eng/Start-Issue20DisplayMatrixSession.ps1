[CmdletBinding()]
param(
    [ValidateSet(
        'I20-01',
        'I20-02',
        'I20-03',
        'I20-04',
        'I20-05',
        'I20-06',
        'I20-07',
        'I20-08')]
    [string] $Scenario,

    [ValidateSet('Attach', 'Detach')]
    [string] $HotPlugAction,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateRange(5, 900)]
    [int] $WatchSeconds = 120,

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
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$projectPath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DisplayTopology\LongGrid.Spikes.DisplayTopology.csproj'
$phaseExitRunbook = Join-Path $projectRoot 'docs\12-phase-0-exit-runbook.md'
$facilitatorRunbook = Join-Path $projectRoot `
    'docs\manual-testing\issue-20-dynamic-display-session-runbook.md'

if ($env:OS -ne 'Windows_NT') {
    throw 'Issue #20 display-matrix sessions can only run on Windows.'
}

foreach ($requiredPath in @($projectPath, $phaseExitRunbook, $facilitatorRunbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Issue #20 session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario)) {
        throw 'Scenario is required and must be one ID from I20-01 through I20-08.'
    }

    if ([string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'OperatorId is required and must be one anonymous label from O1 through O9.'
    }

    if (-not $AcknowledgeControlledEnvironment) {
        throw 'AcknowledgeControlledEnvironment is required before a live display-matrix session.'
    }

    if (-not $AcknowledgeRecoveryPlan) {
        throw 'AcknowledgeRecoveryPlan is required before a live display-matrix session.'
    }

    if ($Scenario -eq 'I20-03' -and [string]::IsNullOrWhiteSpace($HotPlugAction)) {
        throw 'I20-03 requires HotPlugAction Attach or Detach.'
    }

    if ($Scenario -ne 'I20-03' -and -not [string]::IsNullOrWhiteSpace($HotPlugAction)) {
        throw 'HotPlugAction is only valid for I20-03.'
    }
}

$scenarioCatalog = [ordered]@{
    'I20-01' = [ordered]@{ name = 'DpiScaleRoundTrip'; probe = 'scale' }
    'I20-02' = [ordered]@{ name = 'RotationRoundTrip'; probe = 'rotate' }
    'I20-03' = [ordered]@{ name = 'DisplayHotPlug'; probe = 'runtime-selection' }
    'I20-04' = [ordered]@{ name = 'ProjectionModes'; probe = 'projection' }
    'I20-05' = [ordered]@{ name = 'SleepResume'; probe = 'sleep-resume' }
    'I20-06' = [ordered]@{ name = 'LockUnlock'; probe = 'lock-unlock' }
    'I20-07' = [ordered]@{ name = 'RemoteSessionRoundTrip'; probe = 'remote-session' }
    'I20-08' = [ordered]@{ name = 'CrossDpiWindowMove'; probe = 'scale' }
}

$probeScenario = 'RecordedAtRuntime'
if (-not $ValidateOnly) {
    $probeScenario = $scenarioCatalog[$Scenario].probe
    if ($Scenario -eq 'I20-03') {
        $probeScenario = $HotPlugAction.ToLowerInvariant()
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
    purpose = 'Issue20DynamicDisplayAndSessionMatrix'
    scenario = if ($ValidateOnly) { 'I20-01-through-I20-08-required-at-runtime' } else { $Scenario }
    scenarioName = if ($ValidateOnly) { 'RecordedAtRuntime' } else { $scenarioCatalog[$Scenario].name }
    probeScenario = $probeScenario
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    operatingSystem = [System.Environment]::OSVersion.Version.ToString()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    watchSeconds = if ($ValidateOnly) { '5-through-900-required-at-runtime' } else { $WatchSeconds }
    finalResultStatus = 'PendingManualEvidence'
    observerPassIsFinalPass = $false
    performsDisplayMutation = $false
    performsPowerMutation = $false
    performsSessionMutation = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresControlledEnvironmentAcknowledgement = $true
    requiresRecoveryPlanAcknowledgement = $true
    requiresManualVisualReview = $true
    requiresManualInputReview = $true
    startsFreshProcess = -not $ValidateOnly
}

if ($sessionContract.operatorIdentifierPolicy -ne 'AnonymousLabelsOnly' -or
    $sessionContract.finalResultStatus -ne 'PendingManualEvidence' -or
    $sessionContract.observerPassIsFinalPass -or
    $sessionContract.performsDisplayMutation -or
    $sessionContract.performsPowerMutation -or
    $sessionContract.performsSessionMutation -or
    $sessionContract.capturesEvidence -or
    $sessionContract.writesResultFile -or
    -not $sessionContract.requiresControlledEnvironmentAcknowledgement -or
    -not $sessionContract.requiresRecoveryPlanAcknowledgement -or
    -not $sessionContract.requiresManualVisualReview -or
    -not $sessionContract.requiresManualInputReview) {
    throw 'Issue #20 display-session safety or evidence contract is invalid.'
}

$sessionContract | ConvertTo-Json -Depth 4

if ($ValidateOnly) {
    Write-Output 'Issue #20 display matrix session chain validation passed; all final scenario results remain pending manual evidence.'
    exit 0
}

$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot

Write-Warning (
    'The observer never changes display, power, or session state. Perform only the selected controlled action, ' +
    'restore the original state, and complete visual/input review before assigning a final result.'
)

$dotnetArguments = @(
    'run',
    '--project',
    $projectPath,
    '--configuration',
    $Configuration
)
if ($NoRestore) {
    $dotnetArguments += '--no-restore'
}
if ($NoBuild) {
    $dotnetArguments += '--no-build'
}
$dotnetArguments += @(
    '--',
    '--matrix-scenario',
    $probeScenario,
    '--watch-seconds',
    $WatchSeconds,
    '--json'
)

& $dotnetHostPath @dotnetArguments
$probeExitCode = $LASTEXITCODE
if ($probeExitCode -eq 0) {
    Write-Output 'Observer evidence completed; the final result remains PendingManualEvidence until recovery and manual review are recorded.'
    exit 0
}

if ($probeExitCode -eq 4) {
    Write-Warning 'The observer result is Inconclusive. Do not convert it to Pass; record the missing signal or unsupported environment.'
    exit 4
}

throw "Issue #20 display observer failed with exit code $probeExitCode."
