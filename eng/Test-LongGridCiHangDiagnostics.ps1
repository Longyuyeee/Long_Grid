[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $projectRoot '.github\workflows\ci.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding UTF8

$requiredFragments = @(
    '--blame-hang',
    '--blame-hang-timeout 2m',
    '--blame-hang-dump-type none',
    'TestResults/**/Sequence*.xml'
)
foreach ($fragment in $requiredFragments) {
    if (-not $workflow.Contains($fragment)) {
        throw "CI hang diagnostic contract is missing '$fragment'."
    }
}

$standaloneBlameHangCount = [regex]::Matches(
    $workflow,
    '(?m)^\s*--blame-hang\s*$').Count
if ($standaloneBlameHangCount -ne 1) {
    throw "CI must declare exactly one standalone --blame-hang option."
}

if ($workflow -match '--blame-hang-dump-type\s+(mini|full)') {
    throw 'CI must not collect a VSTest memory dump for hang diagnostics.'
}

[pscustomobject]@{
    schemaVersion = 1
    purpose = 'VSTestHangDiagnostics'
    inactivityTimeout = '2m'
    memoryDump = $false
    sequenceEvidence = $true
    outcome = 'Pass'
} | ConvertTo-Json
