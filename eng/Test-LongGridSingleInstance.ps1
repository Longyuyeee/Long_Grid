[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$NoBuild,
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$projectCode = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$programPath = Join-Path $projectRoot 'src\LongGrid.App\Program.cs'
$programCode = Get-Content -LiteralPath $programPath -Raw -Encoding UTF8
$appPath = Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs'
$appCode = Get-Content -LiteralPath $appPath -Raw -Encoding UTF8
$runtimeIdentifier = "win-$Architecture"

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

Assert-Condition ($projectCode.Contains('DISABLE_XAML_GENERATED_MAIN')) `
    'LongGrid.App must disable the generated XAML entry point.'
Assert-Condition ($programCode.Contains('FindOrRegisterForKey(MainInstanceKey)')) `
    'The custom entry point must register one stable app-instance key.'
Assert-Condition ($programCode.Contains('RedirectActivationToAsync(activation)')) `
    'A secondary process must forward its complete activation arguments.'
Assert-Condition ($programCode.Contains('mainInstance.Activated += MainInstance_Activated')) `
    'The primary process must subscribe before starting XAML.'
Assert-Condition ($programCode.Contains('PendingActivations.Enqueue(activation)')) `
    'Activations arriving before App construction must be retained in memory.'
Assert-Condition ($appCode.Contains('Program.ReleaseMainInstance()')) `
    'The primary key must be released immediately before the drained window closes.'
Assert-Condition ($appCode.Contains('window.DispatcherQueue.TryEnqueue(ActivateMainWindow)')) `
    'Redirected activation must cross onto the existing window UI queue.'
Assert-Condition ($appCode.Contains('OverlappedPresenterState.Minimized')) `
    'Redirected activation must restore a minimized main window.'

if ($ContractOnly) {
    Write-Output 'Long Grid single-instance source contract passed.'
    exit 0
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid single-instance live validation requires Windows.'
}

$existing = @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue)
Assert-Condition ($existing.Count -eq 0) `
    'Close the existing LongGrid.App process before live single-instance validation.'

Push-Location $projectRoot
$primary = $null
$secondary = $null
try {
    if (-not $NoBuild) {
        & dotnet build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    $executablePath = Join-Path $projectRoot `
        "src\LongGrid.App\bin\$Configuration\net8.0-windows10.0.19041.0\$runtimeIdentifier\LongGrid.App.exe"
    Assert-Condition (Test-Path -LiteralPath $executablePath) `
        "LongGrid.App executable was not found: $executablePath"

    if (-not ('LongGridSingleInstanceNative' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class LongGridSingleInstanceNative
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(IntPtr windowHandle, int command);
}
'@
    }

    $primary = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory (Split-Path -Parent $executablePath) `
        -PassThru

    $windowReadyDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $primary.Refresh()
    } while (
        -not $primary.HasExited -and
        $primary.MainWindowHandle -eq [IntPtr]::Zero -and
        [DateTime]::UtcNow -lt $windowReadyDeadline
    )

    Assert-Condition (-not $primary.HasExited) 'The primary process exited before opening its window.'
    Assert-Condition ($primary.MainWindowHandle -ne [IntPtr]::Zero) `
        'The primary process did not expose a main window within 15 seconds.'
    Assert-Condition (
        [LongGridSingleInstanceNative]::ShowWindowAsync($primary.MainWindowHandle, 6)
    ) 'The test could not minimize the primary window.'

    $minimizedDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while (
        -not [LongGridSingleInstanceNative]::IsIconic($primary.MainWindowHandle) -and
        [DateTime]::UtcNow -lt $minimizedDeadline
    ) {
        Start-Sleep -Milliseconds 100
    }
    Assert-Condition (
        [LongGridSingleInstanceNative]::IsIconic($primary.MainWindowHandle)
    ) 'The primary window did not enter the minimized state.'

    $secondary = Start-Process `
        -FilePath $executablePath `
        -ArgumentList '--long-grid-activation-probe' `
        -WorkingDirectory (Split-Path -Parent $executablePath) `
        -PassThru
    Assert-Condition ($secondary.WaitForExit(10000)) `
        'The secondary process did not redirect and exit within 10 seconds.'
    Assert-Condition ($secondary.ExitCode -eq 0) `
        "The secondary process returned exit code $($secondary.ExitCode)."

    $restoredDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $primary.Refresh()
    } while (
        -not $primary.HasExited -and
        [LongGridSingleInstanceNative]::IsIconic($primary.MainWindowHandle) -and
        [DateTime]::UtcNow -lt $restoredDeadline
    )

    Assert-Condition (-not $primary.HasExited) `
        'The primary process exited while handling redirected activation.'
    Assert-Condition (
        -not [LongGridSingleInstanceNative]::IsIconic($primary.MainWindowHandle)
    ) 'The primary window was not restored after redirected activation.'
    Assert-Condition (
        @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue).Count -eq 1
    ) 'More than one LongGrid.App process remained after redirection.'

    Write-Output 'Long Grid live single-instance validation passed: redirect, exit, restore.'
}
finally {
    if ($null -ne $secondary -and -not $secondary.HasExited) {
        Stop-Process -Id $secondary.Id -Force
        $secondary.WaitForExit()
    }

    if ($null -ne $primary -and -not $primary.HasExited) {
        $null = $primary.CloseMainWindow()
        if (-not $primary.WaitForExit(5000)) {
            Stop-Process -Id $primary.Id -Force
            $primary.WaitForExit()
        }
    }

    Pop-Location
}

$remaining = @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue)
Assert-Condition ($remaining.Count -eq 0) `
    "Single-instance validation left LongGrid.App process PID(s): $($remaining.Id -join ', ')."
