[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $projectRoot '.github\workflows\ci.yml'
$packScriptPath = Join-Path $PSScriptRoot 'Pack-LongGrid.ps1'
$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding UTF8
$packScript = Get-Content -LiteralPath $packScriptPath -Raw -Encoding UTF8

$rcStepMatch = [regex]::Match(
    $workflow,
    '(?ms)^\s*- name: Build and audit internal unsigned RC delivery set\s*$(.*?)(?=^\s*- name:|\z)')
if (-not $rcStepMatch.Success) {
    throw 'CI internal RC delivery step was not found.'
}

$rcStep = $rcStepMatch.Value
foreach ($requiredFragment in @(
    '-File ./eng/Build-LongGridReleaseCandidate.ps1',
    '-SkipQualityGates',
    '-NoToolRestore'
)) {
    if (-not $rcStep.Contains($requiredFragment)) {
        throw "CI internal RC delivery step is missing '$requiredFragment'."
    }
}

if ($rcStep.Contains('-NoRestore')) {
    throw 'CI must let the RC entry restore its win-x64 self-contained runtime packs.'
}

foreach ($requiredFragment in @(
    "& `$dotnetHostPath restore `$projectPath",
    '--runtime $runtimeIdentifier',
    '-p:WindowsAppSDKSelfContained=true',
    "'--no-restore'"
)) {
    if (-not $packScript.Contains($requiredFragment)) {
        throw "Portable packaging restore contract is missing '$requiredFragment'."
    }
}

$restorePosition = $packScript.IndexOf(
    "& `$dotnetHostPath restore `$projectPath",
    [System.StringComparison]::Ordinal)
$publishPosition = $packScript.IndexOf(
    "'publish', `$projectPath",
    [System.StringComparison]::Ordinal)
if ($restorePosition -lt 0 -or
    $publishPosition -lt 0 -or
    $restorePosition -ge $publishPosition) {
    throw 'The self-contained runtime restore must precede publish.'
}

[pscustomobject]@{
    schemaVersion = 1
    purpose = 'CiReleaseRuntimeRestore'
    runtimeIdentifier = 'win-x64'
    rcOwnsSpecializedRestore = $true
    cacheIndependent = $true
    outcome = 'Pass'
} | ConvertTo-Json
