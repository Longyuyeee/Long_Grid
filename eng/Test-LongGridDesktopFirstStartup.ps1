[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateRange(0, 120)]
    [int] $StabilitySeconds = 0,

    [ValidateRange(1000, 15000)]
    [int] $MaximumColdProcessReadyMilliseconds = 10000,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot (
    "src\LongGrid.App\bin\$Configuration\net8.0-windows10.0.19041.0\$runtimeIdentifier")
$executablePath = Join-Path $outputDirectory 'LongGrid.App.exe'
$environmentName = 'LONGGRID_DESKTOP_FIRST_STARTUP_EVIDENCE_SESSION'
$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'LongGridDesktopFirstEvidence'))
$evidenceDirectory = [System.IO.Path]::GetFullPath((Join-Path $evidenceRoot $sessionId))
$previousEnvironment = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$explicit = $null
$primary = $null
$secondary = $null
$cleanExitObserved = $false
$controlCenterTitle = 'Long' + [char]0x65B9 + [char]0x683C
$desktopHostTitle = $controlCenterTitle + [char]0x684C + [char]0x9762 + [char]0x53EA + [char]0x8BFB + [char]0x5BBF + [char]0x4E3B

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

if (-not ('LongGridDesktopFirstNative' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class LongGridDesktopFirstWindowFact
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; }
    public bool Visible { get; set; }
}

public static class LongGridDesktopFirstNative
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static LongGridDesktopFirstWindowFact[] Enumerate(int expectedProcessId)
    {
        var facts = new List<LongGridDesktopFirstWindowFact>();
        EnumWindows((window, _) =>
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId != (uint)expectedProcessId)
            {
                return true;
            }

            int length = GetWindowTextLength(window);
            var text = new StringBuilder(length + 1);
            GetWindowText(window, text, text.Capacity);
            facts.Add(new LongGridDesktopFirstWindowFact
            {
                Handle = window,
                Title = text.ToString(),
                Visible = IsWindowVisible(window),
            });
            return true;
        }, IntPtr.Zero);
        return facts.ToArray();
    }
}
'@
}

function Get-ProductWindows {
    param([int] $ProcessId)
    return @([LongGridDesktopFirstNative]::Enumerate($ProcessId))
}

$existing = @(
    Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
        Where-Object { $_.HandleCount -gt 0 }
)
Assert-Condition ($existing.Count -eq 0) `
    "Desktop-first startup evidence requires a clean live session; found PID(s): $($existing.Id -join ', ')."

$expectedPrefix = $evidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
Assert-Condition ($evidenceDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) `
    'Desktop-first evidence directory escaped the temporary evidence root.'

Push-Location $projectRoot
try {
    if (-not $NoBuild) {
        $dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot
        & $dotnetHostPath build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    Assert-Condition (Test-Path -LiteralPath $executablePath -PathType Leaf) `
        "LongGrid.App executable was not found: $executablePath"
    $null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
    Assert-Condition (-not ((Get-Item -LiteralPath $evidenceDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) `
        'Desktop-first evidence directory must not be a reparse point.'

    [Environment]::SetEnvironmentVariable($environmentName, $sessionId, 'Process')

    $explicitStartedAt = Get-Date
    $explicit = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory $outputDirectory `
        -PassThru
    $explicitDeadline = $explicitStartedAt.AddSeconds(15)
    $explicitWindows = @()
    do {
        Start-Sleep -Milliseconds 25
        $explicit.Refresh()
        if (-not $explicit.HasExited) {
            $explicitWindows = Get-ProductWindows -ProcessId $explicit.Id
        }
    } while (
        -not $explicit.HasExited -and
        @($explicitWindows | Where-Object {
            $_.Visible -and $_.Title -eq $controlCenterTitle }).Count -eq 0 -and
        (Get-Date) -lt $explicitDeadline)

    $explicitControlCenters = @(
        $explicitWindows |
            Where-Object { $_.Visible -and $_.Title -eq $controlCenterTitle }
    )
    Assert-Condition (-not $explicit.HasExited) `
        'Explicit LongGrid.App launch exited before showing the control center.'
    Assert-Condition ($explicitControlCenters.Count -eq 1) `
        'Explicit LongGrid.App launch did not expose exactly one control center.'
    Assert-Condition $explicit.Responding `
        'The explicitly launched control center is not responding.'
    $explicitLaunchReadyMilliseconds =
        [int]((Get-Date) - $explicitStartedAt).TotalMilliseconds
    $explicitCloseRequested = [LongGridDesktopFirstNative]::PostMessage(
        $explicitControlCenters[0].Handle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Assert-Condition $explicitCloseRequested `
        'Windows did not accept WM_CLOSE for the explicitly launched control center.'
    Assert-Condition ($explicit.WaitForExit(10000)) `
        'Explicit LongGrid.App launch did not complete shutdown within 10 seconds.'
    Assert-Condition ($explicit.ExitCode -eq 0) `
        "Expected explicit launch exit code 0; actual $($explicit.ExitCode)."

    $startedAt = Get-Date
    $primary = Start-Process `
        -FilePath $executablePath `
        -ArgumentList '--background' `
        -WorkingDirectory $outputDirectory `
        -PassThru

    $desktopReadyDeadline = $startedAt.AddSeconds(15)
    $firstLaunchWindows = @()
    do {
        Start-Sleep -Milliseconds 25
        $primary.Refresh()
        if (-not $primary.HasExited) {
            $firstLaunchWindows = Get-ProductWindows -ProcessId $primary.Id
        }
    } while (
        -not $primary.HasExited -and
        @($firstLaunchWindows | Where-Object { $_.Visible -and $_.Title -eq $desktopHostTitle }).Count -eq 0 -and
        (Get-Date) -lt $desktopReadyDeadline)

    Assert-Condition (-not $primary.HasExited) `
        'LongGrid.App exited before the desktop-first host became ready.'
    $visibleDesktopHosts = @(
        $firstLaunchWindows |
            Where-Object { $_.Visible -and $_.Title -eq $desktopHostTitle }
    )
    $visibleControlCentersAtFirstLaunch = @(
        $firstLaunchWindows |
            Where-Object { $_.Visible -and $_.Title -eq $controlCenterTitle }
    )
    Assert-Condition ($visibleDesktopHosts.Count -gt 0) `
        'Normal startup did not expose a visible DesktopHost entry within 15 seconds.'
    Assert-Condition ($visibleControlCentersAtFirstLaunch.Count -eq 0) `
        'Normal startup unexpectedly activated the control center.'
    Assert-Condition $primary.Responding `
        'The desktop-first primary process is not responding.'
    $firstLaunchReadyMilliseconds = [int]((Get-Date) - $startedAt).TotalMilliseconds

    $stabilityDeadline = (Get-Date).AddSeconds($StabilitySeconds)
    while ((Get-Date) -lt $stabilityDeadline) {
        Start-Sleep -Milliseconds 250
        $primary.Refresh()
        Assert-Condition (-not $primary.HasExited) `
            "LongGrid.App exited during the $StabilitySeconds-second desktop-first stability interval."
        Assert-Condition $primary.Responding `
            "LongGrid.App stopped responding during the $StabilitySeconds-second desktop-first stability interval."
        $stableWindows = Get-ProductWindows -ProcessId $primary.Id
        Assert-Condition (
            @($stableWindows | Where-Object {
                $_.Visible -and $_.Title -eq $desktopHostTitle }).Count -gt 0
        ) 'The DesktopHost entry disappeared during the stability interval.'
        Assert-Condition (
            @($stableWindows | Where-Object {
                $_.Visible -and $_.Title -eq $controlCenterTitle }).Count -eq 0
        ) 'The control center activated during the stability interval.'
    }

    $secondary = Start-Process `
        -FilePath $executablePath `
        -ArgumentList '--long-grid-desktop-first-activation-probe' `
        -WorkingDirectory $outputDirectory `
        -PassThru
    Assert-Condition ($secondary.WaitForExit(10000)) `
        'The secondary process did not redirect and exit within 10 seconds.'
    Assert-Condition ($secondary.ExitCode -eq 0) `
        "The secondary process returned exit code $($secondary.ExitCode)."

    $activationDeadline = (Get-Date).AddSeconds(5)
    $activatedWindows = @()
    do {
        Start-Sleep -Milliseconds 25
        $primary.Refresh()
        if (-not $primary.HasExited) {
            $activatedWindows = Get-ProductWindows -ProcessId $primary.Id
        }
    } while (
        -not $primary.HasExited -and
        @($activatedWindows | Where-Object { $_.Visible -and $_.Title -eq $controlCenterTitle }).Count -eq 0 -and
        (Get-Date) -lt $activationDeadline)

    $visibleControlCentersAfterRedirect = @(
        $activatedWindows |
            Where-Object { $_.Visible -and $_.Title -eq $controlCenterTitle }
    )
    Assert-Condition (-not $primary.HasExited) `
        'The primary process exited while handling explicit activation.'
    Assert-Condition ($visibleControlCentersAfterRedirect.Count -eq 1) `
        'Redirected activation did not expose exactly one control center.'
    Assert-Condition (
        @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
            Where-Object { $_.HandleCount -gt 0 }).Count -eq 1
    ) 'More than one live LongGrid.App process remained after redirection.'

    $closeRequested = [LongGridDesktopFirstNative]::PostMessage(
        $visibleControlCentersAfterRedirect[0].Handle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    Assert-Condition $closeRequested `
        'Windows did not accept WM_CLOSE for the activated control center.'
    $cleanExitObserved = $primary.WaitForExit(10000)
    Assert-Condition $cleanExitObserved `
        'LongGrid.App did not complete its shutdown drain within 10 seconds.'
    Assert-Condition ($primary.ExitCode -eq 0) `
        "Expected primary exit code 0; actual $($primary.ExitCode)."

    $evidenceFiles = @(Get-ChildItem -LiteralPath $evidenceDirectory -Force)
    Assert-Condition ($evidenceFiles.Count -eq 0) `
        'Desktop-first startup unexpectedly wrote the temporary configuration root.'

    $coldProcessDifferenceMilliseconds =
        $firstLaunchReadyMilliseconds - $MaximumColdProcessReadyMilliseconds
    $explicitLaunchDifferenceMilliseconds =
        $explicitLaunchReadyMilliseconds - $MaximumColdProcessReadyMilliseconds
    $coldProcessWithinBudget = $coldProcessDifferenceMilliseconds -le 0
    $explicitLaunchWithinBudget = $explicitLaunchDifferenceMilliseconds -le 0
    $difference = if (-not $explicitLaunchWithinBudget) {
        "ExplicitControlCenterReadyExceededBy$($explicitLaunchDifferenceMilliseconds)Milliseconds"
    }
    elseif ($coldProcessWithinBudget) {
        'None'
    }
    else {
        "ColdProcessDesktopHostReadyExceededBy$($coldProcessDifferenceMilliseconds)Milliseconds"
    }

    [pscustomobject]@{
        SchemaVersion = 1
        Purpose = 'Pf001DesktopFirstStartupRealWindowEvidence'
        Expected = [pscustomobject]@{
            ExplicitLaunchControlCenterCount = 1
            ExplicitLaunchReadyBudgetMilliseconds =
                $MaximumColdProcessReadyMilliseconds
            FirstLaunchControlCenterVisible = $false
            FirstLaunchDesktopHostVisible = $true
            SecondaryExitCode = 0
            RedirectedControlCenterCount = 1
            RemainingLiveProcessCount = 0
            TemporaryConfigurationWriteCount = 0
            CrossProcessUiaQueried = $false
            ResponsiveForSeconds = $StabilitySeconds
            ColdProcessDesktopHostReadyBudgetMilliseconds =
                $MaximumColdProcessReadyMilliseconds
            RuntimeBoxesEnableBudgetMilliseconds = 1000
            RuntimeBoxesEnableMeasuredByThisScenario = $false
        }
        Actual = [pscustomobject]@{
            ExplicitLaunchControlCenterCount = $explicitControlCenters.Count
            ExplicitLaunchReadyMilliseconds = $explicitLaunchReadyMilliseconds
            ExplicitLaunchReadyDifferenceMilliseconds =
                $explicitLaunchDifferenceMilliseconds
            FirstLaunchControlCenterVisible = $visibleControlCentersAtFirstLaunch.Count -gt 0
            FirstLaunchDesktopHostCount = $visibleDesktopHosts.Count
            FirstLaunchReadyMilliseconds = $firstLaunchReadyMilliseconds
            SecondaryExitCode = $secondary.ExitCode
            RedirectedControlCenterCount = $visibleControlCentersAfterRedirect.Count
            RemainingLiveProcessCount = @(
                Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
                    Where-Object { $_.HandleCount -gt 0 }).Count
            TemporaryConfigurationWriteCount = $evidenceFiles.Count
            CrossProcessUiaQueried = $false
            ResponsiveForSeconds = $StabilitySeconds
            ColdProcessDesktopHostReadyDifferenceMilliseconds =
                $coldProcessDifferenceMilliseconds
            RuntimeBoxesEnableMilliseconds = $null
        }
        Difference = $difference
        Outcome = if ($explicitLaunchWithinBudget -and $coldProcessWithinBudget) {
            'Pass'
        }
        else {
            'Fail'
        }
    } | ConvertTo-Json -Depth 4

    Assert-Condition $explicitLaunchWithinBudget `
        "Explicit control center ready exceeded the $MaximumColdProcessReadyMilliseconds-ms product budget by $explicitLaunchDifferenceMilliseconds ms."
    Assert-Condition $coldProcessWithinBudget `
        "Cold-process DesktopHost ready exceeded the $MaximumColdProcessReadyMilliseconds-ms product recovery budget by $coldProcessDifferenceMilliseconds ms."
}
finally {
    [Environment]::SetEnvironmentVariable(
        $environmentName,
        $previousEnvironment,
        'Process')

    if ($null -ne $secondary -and -not $secondary.HasExited) {
        Stop-Process -Id $secondary.Id -Force -ErrorAction SilentlyContinue
        $secondary.WaitForExit()
    }
    if ($null -ne $explicit -and -not $explicit.HasExited) {
        $explicitControlCenter = @(
            Get-ProductWindows -ProcessId $explicit.Id |
                Where-Object { $_.Title -eq $controlCenterTitle }
        ) | Select-Object -First 1
        if ($null -ne $explicitControlCenter) {
            $null = [LongGridDesktopFirstNative]::PostMessage(
                $explicitControlCenter.Handle,
                0x0010,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
        }
        if (-not $explicit.WaitForExit(5000)) {
            Stop-Process -Id $explicit.Id -Force -ErrorAction SilentlyContinue
            $explicit.WaitForExit()
        }
    }
    if ($null -ne $primary -and -not $primary.HasExited) {
        $controlCenter = @(
            Get-ProductWindows -ProcessId $primary.Id |
                Where-Object { $_.Title -eq $controlCenterTitle }
        ) | Select-Object -First 1
        if ($null -ne $controlCenter) {
            $null = [LongGridDesktopFirstNative]::PostMessage(
                $controlCenter.Handle,
                0x0010,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
        }
        if (-not $primary.WaitForExit(5000)) {
            Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
            $primary.WaitForExit()
        }
    }

    if (Test-Path -LiteralPath $evidenceDirectory) {
        $resolved = [System.IO.Path]::GetFullPath($evidenceDirectory)
        if (-not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean an evidence directory outside the temporary root.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Pop-Location
}

$remaining = @(
    Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
        Where-Object { $_.HandleCount -gt 0 }
)
Assert-Condition ($remaining.Count -eq 0) `
    "Desktop-first startup evidence left live LongGrid.App PID(s): $($remaining.Id -join ', ')."
