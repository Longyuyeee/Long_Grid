[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$NoRestore,
    [switch]$NoBuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'LongGrid.sln'
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$runtimeIdentifier = "win-$Architecture"
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'

if (-not (Test-Path -LiteralPath $dotnetResolverPath -PathType Leaf)) {
    throw "The shared .NET host resolver was not found: $dotnetResolverPath"
}
. $dotnetResolverPath

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid UI Shell can only start on Windows.'
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "LongGrid.App project was not found: $projectPath"
}

$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot

Push-Location $projectRoot
try {
    if (-not $NoRestore) {
        & $dotnetHostPath restore $solutionPath --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $NoBuild) {
        & $dotnetHostPath build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    if ($ValidateOnly) {
        $m1Validation = & (Join-Path `
            $PSScriptRoot `
            'Test-LongGridM1ManualEvidenceSession.ps1') `
            -ValidateOnly |
            ConvertFrom-Json
        if ($m1Validation.outcome -ne 'Pass') {
            throw 'M1 manual product journey harness validation failed.'
        }
        Write-Output "Long Grid startup chain validation passed: $Configuration / $Architecture / dotnet=$dotnetHostPath"
        exit 0
    }

    & $dotnetHostPath run `
        --project $projectPath `
        --configuration $Configuration `
        --runtime $runtimeIdentifier `
        --no-build

    if ($LASTEXITCODE -ne 0) {
        throw "Long Grid startup failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
