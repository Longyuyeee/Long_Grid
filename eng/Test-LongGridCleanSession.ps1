[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$NoBuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$uiScript = Join-Path $PSScriptRoot 'Test-LongGridUi.ps1'
$singleInstanceScript = Join-Path $PSScriptRoot 'Test-LongGridSingleInstance.ps1'
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$uiScriptCode = Get-Content -LiteralPath $uiScript -Raw -Encoding UTF8
$singleInstanceScriptCode = Get-Content `
    -LiteralPath $singleInstanceScript `
    -Raw `
    -Encoding UTF8

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-LongGridProcesses {
    @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue
    )
}

function Assert-CleanSession {
    param([string]$Checkpoint)

    $processes = @(Get-LongGridProcesses)
    Assert-Condition ($processes.Count -eq 0) `
        "$Checkpoint requires zero LongGrid.App processes; found PID(s): $($processes.Id -join ', '). This harness never terminates processes it did not start."
}

Push-Location $projectRoot
try {
    if ($ValidateOnly) {
        Assert-Condition (
            $uiScriptCode.Contains("Get-Process -Name 'LongGrid.App'") -and
            $uiScriptCode.Contains('zero-existing-processes') -and
            $uiScriptCode.Contains('zero-remaining-processes') -and
            $uiScriptCode.Contains('will not terminate processes it did not start') -and
            $singleInstanceScriptCode.Contains(
                "Single-instance validation left LongGrid.App process")
        ) 'The live scripts must enforce clean-session preflight and postflight without terminating foreign processes.'

        $uiJson = & powershell -NoProfile -ExecutionPolicy Bypass `
            -File $uiScript `
            -Configuration $Configuration `
            -Architecture $Architecture `
            -ContractOnly
        Assert-Condition ($LASTEXITCODE -eq 0) `
            'The 137-ID UI source contract validation failed.'
        $uiResult = $uiJson | ConvertFrom-Json
        Assert-Condition (
            $uiResult.contract.requiredAutomationIds -eq 137 -and
            $uiResult.outcome -eq 'Pass'
        ) 'The clean-session chain requires the complete 137-ID UI source contract.'

        $singleContract = & powershell -NoProfile -ExecutionPolicy Bypass `
            -File $singleInstanceScript `
            -Configuration $Configuration `
            -Architecture $Architecture `
            -ContractOnly
        Assert-Condition ($LASTEXITCODE -eq 0) `
            'The single-instance source contract validation failed.'
        Assert-Condition (
            ($singleContract -join "`n").Contains(
                'single-instance source contract passed')
        ) 'The single-instance source contract did not return its pass marker.'

        [ordered]@{
            schemaVersion = 1
            purpose = 'LongGridCleanSessionUiaAndSingleInstance'
            mode = 'validate-only'
            requiredAutomationIds = 137
            startsProcess = $false
            terminatesForeignProcess = $false
            liveEvidence = 'PendingCleanInteractiveSession'
            outcome = 'Pass'
        } | ConvertTo-Json
        exit 0
    }

    Assert-CleanSession 'Clean-session preflight'
    if (-not $NoBuild) {
        $buildResult = & powershell -NoProfile -ExecutionPolicy Bypass `
            -File $startScript `
            -Configuration $Configuration `
            -Architecture $Architecture `
            -ValidateOnly
        Assert-Condition ($LASTEXITCODE -eq 0) `
            'The clean-session build and startup-chain validation failed.'
        Assert-Condition (
            ($buildResult -join "`n").Contains(
                'startup chain validation passed')
        ) 'The startup chain did not return its pass marker.'
    }

    $uiArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $uiScript,
        '-Configuration', $Configuration,
        '-Architecture', $Architecture,
        '-NoBuild'
    )

    $uiJson = & powershell @uiArguments
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'The live clean-session UIA validation failed.'
    $uiResult = $uiJson | ConvertFrom-Json
    Assert-Condition (
        $uiResult.contract.requiredAutomationIds -eq 137 -and
        $uiResult.live.cleanSessionStart -eq 'zero-existing-processes' -and
        $uiResult.live.cleanSessionEnd -eq 'zero-remaining-processes' -and
        $uiResult.outcome -eq 'Pass'
    ) 'The live UIA result did not preserve the 137-ID and clean-session contract.'

    Assert-CleanSession 'Between UIA and single-instance validation'
    $singleLive = & powershell -NoProfile -ExecutionPolicy Bypass `
        -File $singleInstanceScript `
        -Configuration $Configuration `
        -Architecture $Architecture `
        -NoBuild
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'The live single-instance validation failed.'
    Assert-Condition (
        ($singleLive -join "`n").Contains(
            'live single-instance validation passed')
    ) 'The live single-instance validation did not return its pass marker.'
    Assert-CleanSession 'Clean-session postflight'

    [ordered]@{
        schemaVersion = 1
        purpose = 'LongGridCleanSessionUiaAndSingleInstance'
        mode = 'live'
        requiredAutomationIds = 137
        uiOutcome = $uiResult.outcome
        responsiveLayout = $uiResult.live.responsiveLayout
        singleInstance = 'redirect-exit-restore'
        cleanSessionStart = 'zero-existing-processes'
        cleanSessionEnd = 'zero-remaining-processes'
        terminatesForeignProcess = $false
        outcome = 'Pass'
    } | ConvertTo-Json
}
finally {
    Pop-Location
}
