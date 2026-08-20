[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$knownUnsafeRuntimePackageVersion = [version]'2.4.0.0'
$knownUnsafeXamlFileVersion = [version]'3.2.3.0'

if ($env:OS -ne 'Windows_NT') {
    throw 'The WinUI UIA runtime preflight can only run on Windows.'
}

$runtimePackages = @(
    Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' -ErrorAction Stop |
        Where-Object {
            $_.Architecture -eq 'X64' -and
            -not [string]::IsNullOrWhiteSpace($_.InstallLocation) -and
            (Test-Path -LiteralPath (
                Join-Path $_.InstallLocation 'Microsoft.UI.Xaml.dll'))
        } |
        Sort-Object -Property @{ Expression = { [version]$_.Version } } -Descending
)

if ($runtimePackages.Count -eq 0) {
    [ordered]@{
        schemaVersion = 1
        purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
        expected = [ordered]@{
            discoverableRuntime = $true
            knownUnsafePairAbsent = $true
        }
        actual = [ordered]@{
            discoverableRuntime = $false
            runtimePackageVersion = $null
            xamlFileVersion = $null
            knownUnsafePairAbsent = $null
        }
        difference = 'RuntimeNotDiscoverable'
        outcome = 'Inconclusive'
    } | ConvertTo-Json -Depth 4
    exit 0
}

$selectedRuntime = $runtimePackages[0]
$xamlPath = Join-Path $selectedRuntime.InstallLocation 'Microsoft.UI.Xaml.dll'
$xamlFileVersion = [version](
    (Get-Item -LiteralPath $xamlPath).VersionInfo.FileVersionRaw)
$runtimePackageVersion = [version]$selectedRuntime.Version
$knownUnsafePair =
    $runtimePackageVersion -eq $knownUnsafeRuntimePackageVersion -and
    $xamlFileVersion -eq $knownUnsafeXamlFileVersion

[ordered]@{
    schemaVersion = 1
    purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
    expected = [ordered]@{
        discoverableRuntime = $true
        knownUnsafePairAbsent = $true
    }
    actual = [ordered]@{
        discoverableRuntime = $true
        runtimePackageVersion = $runtimePackageVersion.ToString()
        xamlFileVersion = $xamlFileVersion.ToString()
        knownUnsafePairAbsent = -not $knownUnsafePair
    }
    difference = $(
        if ($knownUnsafePair) {
            'KnownUnsafeCrossProcessUiaRuntimePairPresent'
        }
        else {
            'None'
        })
    outcome = $(
        if ($knownUnsafePair) {
            'BlockedByKnownUpstream'
        }
        else {
            'Pass'
        })
} | ConvertTo-Json -Depth 4
