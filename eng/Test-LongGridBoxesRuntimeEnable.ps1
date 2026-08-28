[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateRange(1, 5000)]
    [int] $MaximumRuntimeEnableMilliseconds = 1000,

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
$environmentName = 'LONGGRID_BOXES_RUNTIME_ENABLE_EVIDENCE_SESSION'
$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'LongGridBoxesRuntimeEnableEvidence'))
$evidenceDirectory = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $sessionId))
$initialReadyPath = Join-Path $evidenceDirectory 'initial-ready.json'
$initialAckPath = Join-Path $evidenceDirectory 'initial-observed.ack'
$disabledReadyPath = Join-Path $evidenceDirectory 'disabled-ready.json'
$disabledAckPath = Join-Path $evidenceDirectory 'disabled-observed.ack'
$resultPath = Join-Path $evidenceDirectory 'runtime-enable-result.json'
$previousEnvironment = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$primary = $null
$secondary = $null
$controlCenterTitle = 'Long' + [char]0x65B9 + [char]0x683C
$desktopHostTitle = $controlCenterTitle + [char]0x684C + [char]0x9762 + [char]0x53EA + [char]0x8BFB + [char]0x5BBF + [char]0x4E3B

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Wait-ForPath {
    param([string] $Path, [int] $TimeoutMilliseconds)
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return
        }
        Start-Sleep -Milliseconds 10
    }
    throw "Timed out waiting for runtime-enable evidence: $(Split-Path -Leaf $Path)"
}

if (-not ('LongGridRuntimeEnableNative' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class LongGridRuntimeEnableWindowFact
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; }
    public bool Visible { get; set; }
}

public static class LongGridRuntimeEnableNative
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

    public static LongGridRuntimeEnableWindowFact[] Enumerate(int expectedProcessId)
    {
        var facts = new List<LongGridRuntimeEnableWindowFact>();
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
            facts.Add(new LongGridRuntimeEnableWindowFact
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
    return @([LongGridRuntimeEnableNative]::Enumerate($ProcessId))
}

function Get-VisibleDesktopHosts {
    param([int] $ProcessId)
    return @(Get-ProductWindows -ProcessId $ProcessId | Where-Object {
        $_.Visible -and $_.Title -eq $desktopHostTitle
    })
}

$existing = @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
    Where-Object { $_.HandleCount -gt 0 })
Assert-Condition ($existing.Count -eq 0) `
    "Runtime-enable evidence requires a clean live session; found PID(s): $($existing.Id -join ', ')."

$expectedPrefix = $evidenceRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Assert-Condition ($evidenceDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) `
    'Runtime-enable evidence directory escaped its temporary root.'

Push-Location $projectRoot
try {
    if (-not $NoBuild) {
        $dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot
        & $dotnetHostPath build $projectPath --configuration $Configuration `
            --runtime $runtimeIdentifier --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    Assert-Condition (Test-Path -LiteralPath $executablePath -PathType Leaf) `
        "LongGrid.App executable was not found: $executablePath"
    $null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
    Assert-Condition (-not ((Get-Item -LiteralPath $evidenceDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) `
        'Runtime-enable evidence directory must not be a reparse point.'

    [Environment]::SetEnvironmentVariable($environmentName, $sessionId, 'Process')
    $primary = Start-Process -FilePath $executablePath `
        -WorkingDirectory $outputDirectory -PassThru

    Wait-ForPath -Path $initialReadyPath -TimeoutMilliseconds 15000
    $initialHosts = Get-VisibleDesktopHosts -ProcessId $primary.Id
    Assert-Condition ($initialHosts.Count -gt 0) `
        'Initial product evidence state did not expose a real visible DesktopHost HWND.'
    Set-Content -LiteralPath $initialAckPath -Value 'observed' -NoNewline

    Wait-ForPath -Path $disabledReadyPath -TimeoutMilliseconds 5000
    $disabledHosts = Get-VisibleDesktopHosts -ProcessId $primary.Id
    Assert-Condition ($disabledHosts.Count -eq 0) `
        'Disabling boxes did not release every visible DesktopHost HWND.'
    Set-Content -LiteralPath $disabledAckPath -Value 'observed' -NoNewline

    Wait-ForPath -Path $resultPath -TimeoutMilliseconds 5000
    $productEvidence = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $restoredHosts = Get-VisibleDesktopHosts -ProcessId $primary.Id
    $elapsed = [int]$productEvidence.Actual.RuntimeBoxesEnableMilliseconds
    $differenceMilliseconds = $elapsed - $MaximumRuntimeEnableMilliseconds
    $passed = $productEvidence.Outcome -eq 'Pass' `
        -and $initialHosts.Count -gt 0 `
        -and $disabledHosts.Count -eq 0 `
        -and $restoredHosts.Count -gt 0 `
        -and $differenceMilliseconds -le 0

    $secondary = Start-Process -FilePath $executablePath `
        -ArgumentList '--long-grid-runtime-enable-activation-probe' `
        -WorkingDirectory $outputDirectory -PassThru
    Assert-Condition ($secondary.WaitForExit(10000)) `
        'The runtime-enable secondary process did not redirect and exit.'
    $activationDeadline = [DateTime]::UtcNow.AddSeconds(5)
    $controlCenters = @()
    do {
        Start-Sleep -Milliseconds 25
        $controlCenters = @(Get-ProductWindows -ProcessId $primary.Id |
            Where-Object { $_.Visible -and $_.Title -eq $controlCenterTitle })
    } while ($controlCenters.Count -eq 0 -and [DateTime]::UtcNow -lt $activationDeadline)
    Assert-Condition ($controlCenters.Count -eq 1) `
        'Redirected activation did not expose exactly one control center.'
    Assert-Condition ([LongGridRuntimeEnableNative]::PostMessage(
        $controlCenters[0].Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) `
        'Windows did not accept WM_CLOSE for the runtime-enable control center.'
    Assert-Condition ($primary.WaitForExit(10000)) `
        'LongGrid.App did not drain after runtime-enable evidence.'

    [pscustomobject]@{
        SchemaVersion = 1
        Purpose = 'Pf001RuntimeBoxesEnableRealWindowEvidence'
        Expected = [pscustomobject]@{
            InitialVisibleDesktopHostCountMinimum = 1
            DisabledVisibleDesktopHostCount = 0
            RestoredVisibleDesktopHostCountMinimum = 1
            RuntimeBoxesEnableBudgetMilliseconds = $MaximumRuntimeEnableMilliseconds
            RemainingLiveProcessCount = 0
        }
        Actual = [pscustomobject]@{
            InitialVisibleDesktopHostCount = $initialHosts.Count
            DisabledVisibleDesktopHostCount = $disabledHosts.Count
            RestoredVisibleDesktopHostCount = $restoredHosts.Count
            RuntimeBoxesEnableMilliseconds = $elapsed
            RuntimeBoxesEnableDifferenceMilliseconds = $differenceMilliseconds
            ProductInternalDifference = $productEvidence.Difference
            RemainingLiveProcessCount = @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
                Where-Object { $_.HandleCount -gt 0 }).Count
        }
        Difference = if ($passed) { 'None' } else {
            if ($differenceMilliseconds -gt 0) {
                "RuntimeBoxesEnableExceededBy$($differenceMilliseconds)Milliseconds"
            }
            else { 'RuntimeBoxesEnableRealWindowMismatch' }
        }
        Outcome = if ($passed) { 'Pass' } else { 'Fail' }
    } | ConvertTo-Json -Depth 4

    Assert-Condition $passed `
        "Runtime boxes enable exceeded or violated the real-window contract; actual $elapsed ms, budget $MaximumRuntimeEnableMilliseconds ms."
}
finally {
    [Environment]::SetEnvironmentVariable($environmentName, $previousEnvironment, 'Process')
    if ($null -ne $secondary -and -not $secondary.HasExited) {
        Stop-Process -Id $secondary.Id -Force -ErrorAction SilentlyContinue
        $secondary.WaitForExit()
    }
    if ($null -ne $primary -and -not $primary.HasExited) {
        Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
        $primary.WaitForExit()
    }
    if (Test-Path -LiteralPath $evidenceDirectory) {
        $resolved = [IO.Path]::GetFullPath($evidenceDirectory)
        if (-not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean runtime-enable evidence outside the temporary root.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Pop-Location
}

$remaining = @(Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
    Where-Object { $_.HandleCount -gt 0 })
Assert-Condition ($remaining.Count -eq 0) `
    "Runtime-enable evidence left live LongGrid.App PID(s): $($remaining.Id -join ', ')."
