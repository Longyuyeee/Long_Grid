[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$projectPath = Join-Path $projectRoot `
    'src\LongGrid.TaskbarWorker\LongGrid.TaskbarWorker.csproj'
$workerPath = Join-Path $projectRoot `
    "src\LongGrid.TaskbarWorker\bin\$Configuration\net8.0-windows\win-x64\LongGrid.TaskbarWorker.exe"

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Probe {
    $output = & $workerPath --compatibility-probe
    $exitCode = $LASTEXITCODE
    Assert-Condition ($exitCode -eq 0) `
        "Taskbar compatibility worker exited with code $exitCode."
    return $output | ConvertFrom-Json
}

function Get-WindowIdentity {
    param($Report)

    return @(
        $Report.Actual.TaskbarWindows |
            ForEach-Object {
                "$($_.Handle)|$($_.WindowClass)|$($_.ProcessId)|$($_.ProcessName)"
            } |
            Sort-Object
    )
}

if (-not $NoBuild) {
    $dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot
    & $dotnetHostPath build $projectPath --configuration $Configuration --runtime win-x64
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'LongGrid.TaskbarWorker build failed.'
}
Assert-Condition (Test-Path -LiteralPath $workerPath -PathType Leaf) `
    "Taskbar worker was not found: $workerPath"

$before = Invoke-Probe
$blockedRequestId = [Guid]::NewGuid().ToString('N')
$blockedOutput = & $workerPath `
    --native-adapter-certification `
    --parent-pid $PID `
    --request-id $blockedRequestId
$blockedExitCode = $LASTEXITCODE
Assert-Condition ($blockedExitCode -eq 65) `
    'Certification entry must reject callers without the evidence switch.'
Assert-Condition ([string]::IsNullOrWhiteSpace(($blockedOutput -join ''))) `
    'Rejected certification entry must not disclose a response.'

$requestId = [Guid]::NewGuid().ToString('N')
$previousEvidence = $env:LONGGRID_TASKBAR_WORKER_EVIDENCE
try {
    $env:LONGGRID_TASKBAR_WORKER_EVIDENCE = '1'
    $output = & $workerPath `
        --native-adapter-certification `
        --parent-pid $PID `
        --request-id $requestId
    $exitCode = $LASTEXITCODE
}
finally {
    if ($null -eq $previousEvidence) {
        Remove-Item Env:LONGGRID_TASKBAR_WORKER_EVIDENCE `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:LONGGRID_TASKBAR_WORKER_EVIDENCE = $previousEvidence
    }
}
Assert-Condition ($exitCode -eq 0) `
    "Native adapter certification preflight exited with code $exitCode."
$certification = $output | ConvertFrom-Json
$after = Invoke-Probe

$beforeIdentity = @(Get-WindowIdentity $before)
$afterIdentity = @(Get-WindowIdentity $after)
$identitiesUnchanged =
    (Compare-Object $beforeIdentity $afterIdentity).Count -eq 0

$difference = @()
if ($certification.ProtocolVersion -ne 1) {
    $difference += 'ProtocolVersionMismatch'
}
if ($certification.Purpose -ne `
        'TaskbarR2NativeAdapterCertificationPreflight') {
    $difference += 'PurposeMismatch'
}
if ($certification.RequestId -ne $requestId) {
    $difference += 'RequestIdMismatch'
}
if ($certification.AdapterAvailability -ne 'Unavailable' -or
    $certification.AdapterId -ne 'None') {
    $difference += 'DefaultAdapterUnexpectedlyAvailable'
}
if ($certification.ModifiedSystemState -or
    $certification.Report.Actual.ModifiedSystemState -or
    $before.Actual.ModifiedSystemState -or
    $after.Actual.ModifiedSystemState) {
    $difference += 'SystemMutationReported'
}
if (-not $identitiesUnchanged) {
    $difference += 'TaskbarWindowIdentityChanged'
}
if ($before.RuntimeAdmission -eq 'Allowed' -or
    $after.RuntimeAdmission -eq 'Allowed') {
    $difference += 'UncertifiedBuildWasAdmitted'
}

$result = [ordered]@{
    schemaVersion = 1
    purpose = 'TaskbarR2A2cNativeAdapterCertificationRealTest'
    expected = [ordered]@{
        ungatedExitCode = 65
        adapterAvailability = 'Unavailable'
        adapterId = 'None'
        modifiedSystemState = $false
        taskbarWindowIdentityUnchanged = $true
        runtimeAdmission = 'Denied until the physical certification matrix passes'
    }
    actual = [ordered]@{
        operatingSystemVersion = $after.Actual.OperatingSystemVersion
        windowsBuild = $after.Actual.WindowsBuild
        ungatedExitCode = $blockedExitCode
        adapterAvailability = $certification.AdapterAvailability
        adapterId = $certification.AdapterId
        modifiedSystemState = $certification.ModifiedSystemState
        taskbarWindowIdentityUnchanged = $identitiesUnchanged
        runtimeAdmission = $after.RuntimeAdmission
        taskbarWindows = $after.Actual.TaskbarWindows
    }
    difference = if ($difference.Count -eq 0) { 'None' } else { $difference }
    outcome = if ($difference.Count -eq 0) { 'Pass' } else { 'Fail' }
}

$result | ConvertTo-Json -Depth 8
Assert-Condition ($result.outcome -eq 'Pass') `
    "Taskbar R2A2c real-test difference: $($difference -join ', ')"
