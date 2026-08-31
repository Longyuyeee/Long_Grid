[CmdletBinding()]
param(
    [string]$Solution = 'LongGrid.sln',

    [ValidateRange(1, 2)]
    [int]$MaximumAttempts = 2,

    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$transientHostDifference =
    'Unable to locate dotnet CLI. Ensure that it is on the PATH.'

function Test-TransientFormatHostDifference {
    param(
        [int]$ExitCode,
        [object[]]$Output
    )

    if ($ExitCode -eq 0) {
        return $false
    }

    return ($Output -join [Environment]::NewLine).Contains(
        $transientHostDifference)
}

if ($ContractOnly) {
    $exactTransient = Test-TransientFormatHostDifference `
        -ExitCode 1 `
        -Output @($transientHostDifference)
    $realFormatDifference = Test-TransientFormatHostDifference `
        -ExitCode 2 `
        -Output @('Code style issues were found.')
    $successfulRun = Test-TransientFormatHostDifference `
        -ExitCode 0 `
        -Output @($transientHostDifference)
    if (-not $exactTransient -or $realFormatDifference -or $successfulRun) {
        throw 'The bounded dotnet format retry contract is invalid.'
    }

    [ordered]@{
        schemaVersion = 1
        purpose = 'LongGridDotNetFormatGate'
        maximumAttempts = 2
        exactTransientDifference = $transientHostDifference
        retriesExactTransientHostDifference = $exactTransient
        retriesOtherFormatFailures = $realFormatDifference
        retriesSuccessfulRun = $successfulRun
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

$solutionPath = if ([IO.Path]::IsPathRooted($Solution)) {
    [IO.Path]::GetFullPath($Solution)
}
else {
    [IO.Path]::GetFullPath((Join-Path $projectRoot $Solution))
}
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Solution was not found: $solutionPath"
}

$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot
$transientRetryObserved = $false
for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
    Push-Location $projectRoot
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $formatOutput = @(
            & $dotnetHostPath format $solutionPath `
                --verify-no-changes `
                --no-restore 2>&1
        )
        $formatExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Pop-Location
    }

    foreach ($line in $formatOutput) {
        Write-Host $line
    }
    if ($formatExitCode -eq 0) {
        [ordered]@{
            schemaVersion = 1
            purpose = 'LongGridDotNetFormatGate'
            dotnetHost = $dotnetHostPath
            attempts = $attempt
            transientRetryObserved = $transientRetryObserved
            outcome = 'Pass'
        } | ConvertTo-Json
        exit 0
    }

    $isTransientHostDifference = Test-TransientFormatHostDifference `
        -ExitCode $formatExitCode `
        -Output $formatOutput
    if ($attempt -lt $MaximumAttempts -and $isTransientHostDifference) {
        $transientRetryObserved = $true
        Write-Warning (
            'dotnet format hit the known transient host-discovery difference; ' +
            'retrying once with the same resolved SDK host.')
        continue
    }

    throw (
        "dotnet format failed with exit code $formatExitCode on " +
        "attempt $attempt of $MaximumAttempts.")
}
