[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(5, 120)]
    [int]$StabilitySeconds = 20,

    [ValidateRange(5, 30)]
    [int]$WindowTimeoutSeconds = 10,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot (
    "src\LongGrid.App\bin\$Configuration\net8.0-windows10.0.19041.0\$runtimeIdentifier")
$executablePath = Join-Path $outputDirectory 'LongGrid.App.exe'
$startedProcess = $null
$cleanExitObserved = $false
$observedWindowHandle = [IntPtr]::Zero
$expectedWindowTitle = 'Long' + [char]0x65B9 + [char]0x683C

if (-not ('LongGridWindowSmokeNativeMethods' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class LongGridWindowSmokeNativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
'@
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'The Long Grid real-window smoke test can only run on Windows.'
}

$existingProcesses = @(
    Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
        Where-Object { $_.HandleCount -gt 0 }
)
Assert-Condition ($existingProcesses.Count -eq 0) `
    "The real-window smoke test requires a clean session; found existing PID(s): $($existingProcesses.Id -join ', ')."

Push-Location $projectRoot
try {
    if (-not $NoBuild) {
        & dotnet build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    Assert-Condition (Test-Path -LiteralPath $executablePath) `
        "LongGrid.App executable was not found: $executablePath"

    $startedAt = Get-Date
    $startedProcess = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory $outputDirectory `
        -PassThru

    $windowDeadline = $startedAt.AddSeconds($WindowTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 100
        $startedProcess.Refresh()
    } while (
        -not $startedProcess.HasExited -and
        $startedProcess.MainWindowHandle -eq [IntPtr]::Zero -and
        (Get-Date) -lt $windowDeadline)

    Assert-Condition (-not $startedProcess.HasExited) `
        'LongGrid.App exited before exposing its main window.'
    Assert-Condition ($startedProcess.MainWindowHandle -ne [IntPtr]::Zero) `
        "LongGrid.App did not expose a main window within $WindowTimeoutSeconds seconds."
    Assert-Condition ($startedProcess.MainWindowTitle -eq $expectedWindowTitle) `
        "Expected the real Long Grid window title; actual '$($startedProcess.MainWindowTitle)'."
    Assert-Condition $startedProcess.Responding `
        'LongGrid.App exposed a window but Windows reported it as not responding.'

    $observedWindowTitle = $startedProcess.MainWindowTitle
    $observedWindowHandle = $startedProcess.MainWindowHandle
    $windowReadyMilliseconds = [int]((Get-Date) - $startedAt).TotalMilliseconds
    $stabilityDeadline = (Get-Date).AddSeconds($StabilitySeconds)
    do {
        Start-Sleep -Milliseconds 250
        $startedProcess.Refresh()
        Assert-Condition (-not $startedProcess.HasExited) `
            "LongGrid.App exited during the $StabilitySeconds-second real-window stability interval."
        Assert-Condition $startedProcess.Responding `
            "LongGrid.App stopped responding during the $StabilitySeconds-second stability interval."
        Assert-Condition (
            [LongGridWindowSmokeNativeMethods]::IsWindow($observedWindowHandle)
        ) 'The originally attested LongGrid main window disappeared during the stability interval.'
    } while ((Get-Date) -lt $stabilityDeadline)

    $closeRequested = [LongGridWindowSmokeNativeMethods]::PostMessage(
        $observedWindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Assert-Condition $closeRequested `
        'Windows did not accept WM_CLOSE for the LongGrid.App main window.'
    $cleanExitObserved = $startedProcess.WaitForExit(10000)
    Assert-Condition $cleanExitObserved `
        'LongGrid.App did not complete its audited shutdown drain within 10 seconds.'
    Assert-Condition ($startedProcess.ExitCode -eq 0) `
        "Expected LongGrid.App exit code 0; actual $($startedProcess.ExitCode)."

    [pscustomobject]@{
        Result = 'Pass'
        TestKind = 'RealProcessAndWindowLifecycle'
        Expected = [pscustomobject]@{
            WindowTitle = $expectedWindowTitle
            WindowReadyWithinSeconds = $WindowTimeoutSeconds
            ResponsiveForSeconds = $StabilitySeconds
            ExitCode = 0
            CrossProcessUiaQueried = $false
        }
        Actual = [pscustomobject]@{
            WindowTitle = $observedWindowTitle
            WindowReadyMilliseconds = $windowReadyMilliseconds
            ResponsiveForSeconds = $StabilitySeconds
            ExitCode = $startedProcess.ExitCode
            CrossProcessUiaQueried = $false
        }
        Difference = 'None'
    } | ConvertTo-Json -Depth 4
}
finally {
    if (
        $null -ne $startedProcess -and
        -not $cleanExitObserved -and
        -not $startedProcess.HasExited)
    {
        if ($observedWindowHandle -ne [IntPtr]::Zero) {
            $null = [LongGridWindowSmokeNativeMethods]::PostMessage(
                $observedWindowHandle,
                0x0010,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
        }
        if (-not $startedProcess.WaitForExit(5000)) {
            Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Pop-Location
}
