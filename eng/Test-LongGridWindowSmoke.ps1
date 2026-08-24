[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateRange(5, 120)]
    [int] $StabilitySeconds = 20,

    [ValidateRange(5, 30)]
    [int] $WindowTimeoutSeconds = 10,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$desktopFirstScript = Join-Path $PSScriptRoot `
    'Test-LongGridDesktopFirstStartup.ps1'

if ($WindowTimeoutSeconds -ne 10) {
    Write-Verbose `
        'WindowTimeoutSeconds is retained for compatibility; the desktop-first evidence uses its audited 15-second cold-start ceiling.'
}

$arguments = @{
    Configuration = $Configuration
    StabilitySeconds = $StabilitySeconds
}
if ($NoBuild) {
    $arguments.NoBuild = $true
}

& $desktopFirstScript @arguments
