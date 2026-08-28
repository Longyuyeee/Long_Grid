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

function Resolve-LongGridDotNetHost {
    param([string]$WorkingDirectory)

    $candidates = [Collections.Generic.List[string]]::new()
    foreach ($programFilesRoot in @(
        $env:ProgramW6432,
        $env:ProgramFiles,
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFiles))) {
        if (-not [string]::IsNullOrWhiteSpace($programFilesRoot)) {
            $candidates.Add((Join-Path $programFilesRoot 'dotnet\dotnet.exe'))
        }
    }

    foreach ($command in @(Get-Command dotnet -All -ErrorAction SilentlyContinue)) {
        if (-not [string]::IsNullOrWhiteSpace($command.Source)) {
            $candidates.Add($command.Source)
        }
    }

    $checked = [Collections.Generic.List[string]]::new()
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $checked.Add($candidate)
        Push-Location $WorkingDirectory
        try {
            $version = @(& $candidate --version 2>$null)
            $exitCode = $LASTEXITCODE
        }
        catch {
            $exitCode = 1
            $version = @()
        }
        finally {
            Pop-Location
        }

        if ($exitCode -eq 0 -and
            -not [string]::IsNullOrWhiteSpace(($version -join ''))) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    $checkedText = if ($checked.Count -eq 0) {
        'no dotnet hosts were found'
    }
    else {
        $checked -join '; '
    }
    throw "No .NET SDK compatible with global.json was found. Checked: $checkedText"
}

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
