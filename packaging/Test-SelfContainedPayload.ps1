[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'
foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.UI.Xaml.dll', 'LongGrid.App.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageRoot $file) -PathType Leaf)) {
        throw "Self-contained payload is missing: $file"
    }
}
foreach ($component in @('LongGrid.App', 'LongGrid.TaskbarWorker', 'LongGrid.ThumbnailWorker')) {
    foreach ($suffix in @('.exe', '.dll', '.deps.json', '.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageRoot "$component$suffix") -PathType Leaf)) {
            throw "Self-contained component is missing: $component$suffix"
        }
    }
    $configuration = Get-Content -LiteralPath (Join-Path $PackageRoot "$component.runtimeconfig.json") -Raw | ConvertFrom-Json
    $runtime = $configuration.runtimeOptions
    if ($null -eq $runtime -or $null -ne $runtime.framework -or $null -ne $runtime.frameworks -or
        @($runtime.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).Count -ne 1) {
        throw "Component still requires a shared .NET runtime: $component"
    }
}
Write-Output 'Self-contained payload structure passed (not a launch or clean-machine acceptance result).'
