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
    "Taskbar compatibility worker was not found: $workerPath"

$before = Invoke-Probe
$beforeIdentity = @(Get-WindowIdentity $before)
$after = Invoke-Probe
$afterIdentity = @(Get-WindowIdentity $after)
$identitiesUnchanged =
    (Compare-Object $beforeIdentity $afterIdentity).Count -eq 0
$probePassed =
    $before.ProbeOutcome -eq 'Pass' -and
    $after.ProbeOutcome -eq 'Pass'
$failClosed =
    $before.RuntimeAdmission -ne 'Allowed' -and
    $after.RuntimeAdmission -ne 'Allowed'
$reportedNoMutation =
    -not $before.Actual.ModifiedSystemState -and
    -not $after.Actual.ModifiedSystemState

$difference = @()
if (-not $probePassed) { $difference += 'ReadOnlyProbeFailed' }
if (-not $identitiesUnchanged) { $difference += 'TaskbarWindowIdentityChanged' }
if (-not $reportedNoMutation) { $difference += 'ProbeReportedSystemMutation' }
if (-not $failClosed) { $difference += 'UncertifiedRuntimeWasAdmitted' }

$result = [ordered]@{
    schemaVersion = 1
    purpose = 'TaskbarR1ReadOnlyCompatibilityRealTest'
    expected = [ordered]@{
        probeOutcome = 'Pass'
        taskbarWindowIdentityUnchanged = $true
        modifiedSystemState = $false
        runtimeAdmission = 'Denied until a physical R4 build matrix certifies the build'
    }
    actual = [ordered]@{
        operatingSystemVersion = $after.Actual.OperatingSystemVersion
        windowsBuild = $after.Actual.WindowsBuild
        sessionId = $after.Actual.SessionId
        taskbarWindows = $after.Actual.TaskbarWindows
        conflictingProcesses = $after.Actual.ConflictingProcesses
        probeOutcome = $after.ProbeOutcome
        runtimeAdmission = $after.RuntimeAdmission
        taskbarWindowIdentityUnchanged = $identitiesUnchanged
        modifiedSystemState = $after.Actual.ModifiedSystemState
    }
    difference = if ($difference.Count -eq 0) { 'None' } else { $difference }
    outcome = if ($difference.Count -eq 0) { 'Pass' } else { 'Fail' }
}

$result | ConvertTo-Json -Depth 8
Assert-Condition ($result.outcome -eq 'Pass') `
    "Taskbar R1 real-test difference: $($difference -join ', ')"
