[CmdletBinding()]
param(
    [ValidateSet(
        'I19-01',
        'I19-02',
        'I19-03',
        'I19-04',
        'I19-05',
        'I19-06',
        'I19-07',
        'I19-08',
        'I19-09',
        'I19-10')]
    [string] $Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string] $OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\LongGrid.Spikes.DesktopHostWindowModels.csproj'
$phaseExitRunbook = Join-Path $projectRoot 'docs\12-phase-0-exit-runbook.md'
$facilitatorRunbook = Join-Path $projectRoot `
    'docs\manual-testing\issue-19-input-system-surface-runbook.md'

if ($env:OS -ne 'Windows_NT') {
    throw 'Issue #19 manual matrix sessions can only run on Windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET SDK selected by global.json.'
}

foreach ($requiredPath in @($projectPath, $phaseExitRunbook, $facilitatorRunbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Issue #19 session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario)) {
        throw 'Scenario is required and must be one ID from I19-01 through I19-10.'
    }

    if ([string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'OperatorId is required and must be one anonymous label from O1 through O9.'
    }
}

$scenarioCatalog = [ordered]@{
    'I19-01' = 'Keyboard'
    'I19-02' = 'Mouse'
    'I19-03' = 'TouchOrPen'
    'I19-04' = 'DragAndDrop'
    'I19-05' = 'Narrator'
    'I19-06' = 'HighContrastAndTextScale'
    'I19-07' = 'WinDAndPeek'
    'I19-08' = 'Fullscreen'
    'I19-09' = 'AltTabAndTaskView'
    'I19-10' = 'ExplorerRestart'
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
    purpose = 'Issue19ManualInputAndSystemSurfaceMatrix'
    scenario = if ($ValidateOnly) { 'I19-01-through-I19-10-required-at-runtime' } else { $Scenario }
    scenarioName = if ($ValidateOnly) { 'RecordedAtRuntime' } else { $scenarioCatalog[$Scenario] }
    operatorId = if ($ValidateOnly) { 'O1-O9-required-at-runtime' } else { $OperatorId }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    operatingSystem = [System.Environment]::OSVersion.Version.ToString()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    resultStatus = 'PendingManualEvidence'
    performsInputAutomation = $false
    changesSystemSettings = $false
    restartsExplorer = $false
    capturesEvidence = $false
    writesResultFile = $false
    requiresManualJudgment = $true
    requiresRecoveryConfirmation = $true
    startsFreshProcess = -not $ValidateOnly
}

if ($sessionContract.operatorIdentifierPolicy -ne 'AnonymousLabelsOnly' -or
    $sessionContract.resultStatus -ne 'PendingManualEvidence' -or
    $sessionContract.performsInputAutomation -or
    $sessionContract.changesSystemSettings -or
    $sessionContract.restartsExplorer -or
    $sessionContract.capturesEvidence -or
    $sessionContract.writesResultFile -or
    -not $sessionContract.requiresManualJudgment -or
    -not $sessionContract.requiresRecoveryConfirmation) {
    throw 'Issue #19 manual-session safety or evidence contract is invalid.'
}

$sessionContract | ConvertTo-Json -Depth 3

if ($ValidateOnly) {
    Write-Output 'Issue #19 manual matrix session chain validation passed; all scenario results remain pending manual evidence.'
    exit 0
}

Write-Warning (
    'Run only the selected scenario from the facilitator runbook. The launcher does not perform system actions, ' +
    'capture evidence, or decide Pass/Fail. Restore all changed system state before closing the session.'
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
$dotnetArguments += @('--', '--interactive-slice')

& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    throw "Issue #19 manual matrix session failed with exit code $LASTEXITCODE."
}
