[CmdletBinding()]
param(
    [string]$Solution = "LongGrid.sln"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'

if (-not (Test-Path -LiteralPath $dotnetResolverPath -PathType Leaf)) {
    throw "The shared .NET host resolver was not found: $dotnetResolverPath"
}
. $dotnetResolverPath
$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot

$json = & $dotnetHostPath list $Solution package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw "dotnet package vulnerability scan failed with exit code $LASTEXITCODE."
}

$report = $json | ConvertFrom-Json
$findings = @()

foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)

        foreach ($package in $packages) {
            if ($null -eq $package) {
                continue
            }

            foreach ($vulnerability in @($package.vulnerabilities)) {
                if ($null -eq $vulnerability) {
                    continue
                }

                $findings += [PSCustomObject]@{
                    Project  = $project.path
                    Package  = $package.id
                    Version  = $package.resolvedVersion
                    Severity = $vulnerability.severity
                    Advisory = $vulnerability.advisoryurl
                }
            }
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | Format-Table -AutoSize | Out-String | Write-Error
    throw "Found $($findings.Count) vulnerable package reference(s)."
}

Write-Output "Package vulnerability gate passed: no known vulnerable packages. dotnet=$dotnetHostPath"
