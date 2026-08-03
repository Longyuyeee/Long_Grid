[CmdletBinding()]
param(
    [ValidateSet('P1', 'P2', 'P3', 'P4', 'P5')]
    [string] $ParticipantId,

    [ValidateSet('KeyboardMouse', 'KeyboardOnly', 'Touch')]
    [string] $InputMode = 'KeyboardMouse',

    [ValidateSet('System', 'Light', 'Dark')]
    [string] $Theme = 'System',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$testPlan = Join-Path $projectRoot `
    'docs\usability\issue-23-first-organization-test-plan.md'
$facilitatorRunbook = Join-Path $projectRoot `
    'docs\usability\issue-23-facilitator-runbook.md'

if ($env:OS -ne 'Windows_NT') {
    throw 'Issue #23 usability sessions can only run on Windows.'
}

foreach ($requiredPath in @($startScript, $testPlan, $facilitatorRunbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Issue #23 session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly -and [string]::IsNullOrWhiteSpace($ParticipantId)) {
    throw 'ParticipantId is required and must be one anonymous label from P1 through P5.'
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
    purpose = 'Issue23FivePersonUsabilitySession'
    participantId = if ($ValidateOnly) { 'P1-P5-required-at-runtime' } else { $ParticipantId }
    participantIdentifierPolicy = 'AnonymousLabelsOnly'
    inputMode = if ($ValidateOnly) { 'RecordedAtRuntime' } else { $InputMode }
    theme = if ($ValidateOnly) { 'RecordedAtRuntime' } else { $Theme }
    commit = $commit
    operatingSystem = [System.Environment]::OSVersion.Version.ToString()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    recordsPersonalData = $false
    capturesScreenshots = $false
    enumeratesDesktop = $false
    writesResultFile = $false
    resultStatus = 'ResultsPending'
    startsFreshProcess = -not $ValidateOnly
}

if ($sessionContract.participantIdentifierPolicy -ne 'AnonymousLabelsOnly' -or
    $sessionContract.recordsPersonalData -or
    $sessionContract.capturesScreenshots -or
    $sessionContract.enumeratesDesktop -or
    $sessionContract.writesResultFile -or
    $sessionContract.resultStatus -ne 'ResultsPending') {
    throw 'Issue #23 usability-session privacy or evidence contract is invalid.'
}

$sessionContract | ConvertTo-Json -Depth 3

if ($ValidateOnly) {
    Write-Output 'Issue #23 usability session chain validation passed; results remain pending.'
    exit 0
}

Write-Warning (
    'Do not record names, accounts, file names, paths, desktop screenshots, or unprompted hints. ' +
    'Close the app before starting the next participant so all in-memory state is reset.'
)

$startArguments = @{
    Configuration = $Configuration
    Architecture = 'x64'
}
if ($NoRestore) {
    $startArguments.NoRestore = $true
}
if ($NoBuild) {
    $startArguments.NoBuild = $true
}

& $startScript @startArguments
if ($LASTEXITCODE -ne 0) {
    throw "Issue #23 usability session failed with exit code $LASTEXITCODE."
}
