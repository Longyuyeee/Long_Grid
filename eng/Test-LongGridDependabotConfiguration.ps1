[CmdletBinding()]
param(
    [string]$ConfigurationPath = '.github/dependabot.yml'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedConfigurationPath = if ([System.IO.Path]::IsPathRooted($ConfigurationPath)) {
    [System.IO.Path]::GetFullPath($ConfigurationPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $ConfigurationPath))
}

function Get-ConfigurationDifferences {
    param([Parameter(Mandatory)][string]$Text)

    $differences = @()
    foreach ($entry in ([ordered]@{
            'schema version 2' = '(?m)^version: 2$'
            'single updates root' = '(?m)^updates:$'
            'GitHub Actions ecosystem' = '(?m)^  - package-ecosystem: "github-actions"$'
            'repository root directory' = '(?m)^    directory: "/"$'
            'main target branch' = '(?m)^    target-branch: "main"$'
            'weekly interval' = '(?m)^      interval: "weekly"$'
            'Monday schedule' = '(?m)^      day: "monday"$'
            'finite schedule time' = '(?m)^      time: "04:00"$'
            'Hong Kong timezone' = '(?m)^      timezone: "Asia/Hong_Kong"$'
            'bounded open pull requests' = '(?m)^    open-pull-requests-limit: 2$'
            'CI commit prefix' = '(?m)^      prefix: "ci"$'
        }).GetEnumerator()) {
        if ($Text -notmatch $entry.Value) {
            $differences += "missing:$($entry.Key)"
        }
    }

    if ([regex]::Matches($Text, '(?m)^  - package-ecosystem:').Count -ne 1) {
        $differences += 'updates:not-exactly-one-ecosystem'
    }
    if ([regex]::Matches($Text, '(?m)^version:').Count -ne 1) {
        $differences += 'schema:not-exactly-one-version'
    }
    foreach ($entry in ([ordered]@{
            'non-actions ecosystem' = '(?m)^  - package-ecosystem: "(?!github-actions")[^"]+"$'
            'non-main target branch' = '(?m)^    target-branch: "(?!main")[^"]+"$'
            'daily schedule' = '(?m)^      interval: "daily"$'
            'monthly schedule' = '(?m)^      interval: "monthly"$'
            'private registry' = '(?m)^registries:'
            'secret reference' = '(?i)secrets\.'
            'automatic merge directive' = '(?i)auto-?merge'
        }).GetEnumerator()) {
        if ($Text -match $entry.Value) {
            $differences += "forbidden:$($entry.Key)"
        }
    }
    return $differences
}

if (-not (Test-Path -LiteralPath $resolvedConfigurationPath -PathType Leaf)) {
    throw "Dependabot configuration is missing: $resolvedConfigurationPath"
}
$configuration = Get-Content -LiteralPath $resolvedConfigurationPath -Raw -Encoding UTF8
$actualDifferences = @(Get-ConfigurationDifferences -Text $configuration)
if ($actualDifferences.Count -gt 0) {
    throw "Dependabot configuration contract failed:`n$($actualDifferences -join "`n")"
}

$dailyConfiguration = $configuration.Replace(
    'interval: "weekly"',
    'interval: "daily"')
$dailyDifferences = @(Get-ConfigurationDifferences -Text $dailyConfiguration)
if ($dailyDifferences -notcontains 'missing:weekly interval' -or
    $dailyDifferences -notcontains 'forbidden:daily schedule') {
    throw 'Dependabot contract accepted an intentionally daily update schedule.'
}

$nugetConfiguration = $configuration.Replace(
    'package-ecosystem: "github-actions"',
    'package-ecosystem: "nuget"')
$nugetDifferences = @(Get-ConfigurationDifferences -Text $nugetConfiguration)
if ($nugetDifferences -notcontains 'missing:GitHub Actions ecosystem' -or
    $nugetDifferences -notcontains 'forbidden:non-actions ecosystem') {
    throw 'Dependabot contract accepted an intentionally expanded NuGet ecosystem.'
}

$registryConfiguration = $configuration + "`nregistries:`n  private:`n    type: nuget-feed`n"
$registryDifferences = @(Get-ConfigurationDifferences -Text $registryConfiguration)
if ($registryDifferences -notcontains 'forbidden:private registry') {
    throw 'Dependabot contract accepted an intentionally added private registry.'
}

$targetConfiguration = $configuration.Replace(
    'target-branch: "main"',
    'target-branch: "develop"')
$targetDifferences = @(Get-ConfigurationDifferences -Text $targetConfiguration)
if ($targetDifferences -notcontains 'missing:main target branch' -or
    $targetDifferences -notcontains 'forbidden:non-main target branch') {
    throw 'Dependabot contract accepted an intentionally changed target branch.'
}

[ordered]@{
    outcome = 'Pass'
    ecosystem = 'github-actions'
    directory = '/'
    targetBranch = 'main'
    interval = 'weekly'
    timezone = 'Asia/Hong_Kong'
    openPullRequestsLimit = 2
    dailyNegativeDifferences = @($dailyDifferences)
    ecosystemNegativeDifferences = @($nugetDifferences)
    registryNegativeDifference = 'forbidden:private registry'
    targetNegativeDifferences = @($targetDifferences)
    autoMergeEnabled = $false
} | ConvertTo-Json -Depth 4
