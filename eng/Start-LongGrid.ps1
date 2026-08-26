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

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid UI Shell can only start on Windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET SDK selected by global.json.'
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "LongGrid.App project was not found: $projectPath"
}

Push-Location $projectRoot
try {
    if (-not $NoRestore) {
        & dotnet restore $solutionPath --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $NoBuild) {
        & dotnet build $projectPath `
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
        Write-Output "Long Grid startup chain validation passed: $Configuration / $Architecture"
        exit 0
    }

    & dotnet run `
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
