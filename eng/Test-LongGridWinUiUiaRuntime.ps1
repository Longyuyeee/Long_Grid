[CmdletBinding()]
param(
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$knownUnsafeRuntimePackageVersion = [version]'2.4.0.0'
$knownUnsafeXamlFileVersion = [version]'3.2.3.0'
$requiredArchitecture = 'X64'
$requiredDdlmArchitectureSuffix = 'x6'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function New-NormalizedPackage {
    param(
        [string]$Name,
        [version]$Version,
        [string]$Architecture,
        [version]$XamlFileVersion = $null
    )

    [pscustomobject]@{
        Name = $Name
        Version = $Version
        Architecture = $Architecture
        XamlFileVersion = $XamlFileVersion
    }
}

function Get-RuntimePreflightResult {
    param([object[]]$Packages)

    $frameworkPackages = @(
        $Packages |
            Where-Object {
                $_.Name -eq 'Microsoft.WindowsAppRuntime.2' -and
                $_.Architecture -eq $requiredArchitecture -and
                $null -ne $_.XamlFileVersion
            } |
            Sort-Object -Property Version -Descending
    )

    if ($frameworkPackages.Count -eq 0) {
        return [ordered]@{
            schemaVersion = 2
            purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
            expected = [ordered]@{
                discoverableRuntime = $true
                runtimePackageSetComplete = $true
                knownUnsafePairAbsent = $true
            }
            actual = [ordered]@{
                discoverableRuntime = $false
                runtimePackageVersion = $null
                xamlFileVersion = $null
                runtimePackageSetComplete = $false
                frameworkPackagePresent = $false
                mainPackagePresent = $false
                singletonPackagePresent = $false
                ddlmPackagePresent = $false
                missingRequiredPackages = @('Framework')
                knownUnsafePairAbsent = $null
            }
            difference = 'RuntimeFrameworkNotDiscoverable'
            outcome = 'Inconclusive'
        }
    }

    $selectedRuntime = $frameworkPackages[0]
    $runtimePackageVersion = [version]$selectedRuntime.Version
    $xamlFileVersion = [version]$selectedRuntime.XamlFileVersion
    $mainPackageName =
        "MicrosoftCorporationII.WinAppRuntime.Main.$($runtimePackageVersion.Major)"
    $singletonPackageName =
        'MicrosoftCorporationII.WinAppRuntime.Singleton'
    $ddlmPackageName =
        "Microsoft.WinAppRuntime.DDLM.$($runtimePackageVersion.ToString())-$requiredDdlmArchitectureSuffix"
    $singletonPackageVersion = [version](
        '{0}.{1}.{2}.{3}' -f
            (8000 + $runtimePackageVersion.Major),
            $runtimePackageVersion.Minor,
            $runtimePackageVersion.Build,
            $runtimePackageVersion.Revision)

    $mainPackagePresent = @(
        $Packages | Where-Object {
            $_.Name -eq $mainPackageName -and
            $_.Architecture -eq $requiredArchitecture -and
            [version]$_.Version -eq $runtimePackageVersion
        }
    ).Count -gt 0
    $singletonPackagePresent = @(
        $Packages | Where-Object {
            $_.Name -eq $singletonPackageName -and
            $_.Architecture -eq $requiredArchitecture -and
            [version]$_.Version -eq $singletonPackageVersion
        }
    ).Count -gt 0
    $ddlmPackagePresent = @(
        $Packages | Where-Object {
            $_.Name -eq $ddlmPackageName -and
            $_.Architecture -eq $requiredArchitecture -and
            [version]$_.Version -eq $runtimePackageVersion
        }
    ).Count -gt 0

    $missingRequiredPackages = @()
    if (-not $mainPackagePresent) { $missingRequiredPackages += $mainPackageName }
    if (-not $singletonPackagePresent) {
        $missingRequiredPackages +=
            "$singletonPackageName@$($singletonPackageVersion.ToString())"
    }
    if (-not $ddlmPackagePresent) { $missingRequiredPackages += $ddlmPackageName }
    $runtimePackageSetComplete = $missingRequiredPackages.Count -eq 0
    $knownUnsafePair =
        $runtimePackageVersion -eq $knownUnsafeRuntimePackageVersion -and
        $xamlFileVersion -eq $knownUnsafeXamlFileVersion

    $difference = if (-not $runtimePackageSetComplete) {
        'IncompleteRuntimePackageSet'
    }
    elseif ($knownUnsafePair) {
        'KnownUnsafeCrossProcessUiaRuntimePairPresent'
    }
    else {
        'None'
    }
    $outcome = if (-not $runtimePackageSetComplete) {
        'BlockedByIncompleteRuntime'
    }
    elseif ($knownUnsafePair) {
        'BlockedByKnownUpstream'
    }
    else {
        'Pass'
    }

    [ordered]@{
        schemaVersion = 2
        purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
        expected = [ordered]@{
            discoverableRuntime = $true
            runtimePackageSetComplete = $true
            knownUnsafePairAbsent = $true
        }
        actual = [ordered]@{
            discoverableRuntime = $true
            runtimePackageVersion = $runtimePackageVersion.ToString()
            xamlFileVersion = $xamlFileVersion.ToString()
            runtimePackageSetComplete = $runtimePackageSetComplete
            frameworkPackagePresent = $true
            mainPackagePresent = $mainPackagePresent
            singletonPackagePresent = $singletonPackagePresent
            ddlmPackagePresent = $ddlmPackagePresent
            expectedMainPackage = $mainPackageName
            expectedSingletonPackage =
                "$singletonPackageName@$($singletonPackageVersion.ToString())"
            expectedDdlmPackage = $ddlmPackageName
            missingRequiredPackages = $missingRequiredPackages
            knownUnsafePairAbsent = -not $knownUnsafePair
        }
        difference = $difference
        outcome = $outcome
    }
}

if ($ContractOnly) {
    $safeFramework = New-NormalizedPackage `
        'Microsoft.WindowsAppRuntime.2' `
        ([version]'2.3.1.0') `
        'X64' `
        ([version]'3.2.2.0')
    $safeComplete = @(
        $safeFramework
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Main.2' `
            ([version]'2.3.1.0') `
            'X64')
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Singleton' `
            ([version]'8002.3.1.0') `
            'X64')
        (New-NormalizedPackage `
            'Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6' `
            ([version]'2.3.1.0') `
            'X64')
    )
    $unsafeComplete = @(
        (New-NormalizedPackage `
            'Microsoft.WindowsAppRuntime.2' `
            ([version]'2.4.0.0') `
            'X64' `
            ([version]'3.2.3.0'))
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Main.2' `
            ([version]'2.4.0.0') `
            'X64')
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Singleton' `
            ([version]'8002.4.0.0') `
            'X64')
        (New-NormalizedPackage `
            'Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6' `
            ([version]'2.4.0.0') `
            'X64')
    )

    $missingFramework = Get-RuntimePreflightResult @()
    $incomplete = Get-RuntimePreflightResult @($safeFramework)
    $unsafe = Get-RuntimePreflightResult $unsafeComplete
    $safe = Get-RuntimePreflightResult $safeComplete
    Assert-Condition `
        ($missingFramework.outcome -eq 'Inconclusive') `
        'Missing framework inventory must remain inconclusive.'
    Assert-Condition `
        ($incomplete.outcome -eq 'BlockedByIncompleteRuntime' -and
            $incomplete.actual.missingRequiredPackages.Count -eq 3) `
        'Incomplete runtime inventory must fail closed with all missing packages.'
    Assert-Condition `
        ($unsafe.outcome -eq 'BlockedByKnownUpstream') `
        'A complete known-unsafe runtime pair must remain blocked.'
    Assert-Condition `
        ($safe.outcome -eq 'Pass') `
        'A complete safe runtime package set must pass.'

    [ordered]@{
        schemaVersion = 1
        purpose = 'LongGridWinUiRuntimePackageSetContract'
        scenarios = 4
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

if ($env:OS -ne 'Windows_NT') {
    throw 'The WinUI UIA runtime preflight can only run on Windows.'
}

$installedPackages = @(
    Get-AppxPackage -ErrorAction Stop |
        Where-Object {
            $_.Name -eq 'Microsoft.WindowsAppRuntime.2' -or
            $_.Name -like 'MicrosoftCorporationII.WinAppRuntime.Main.*' -or
            $_.Name -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton' -or
            $_.Name -like 'Microsoft.WinAppRuntime.DDLM.*'
        }
)
$normalizedPackages = @(
    foreach ($package in $installedPackages) {
        $xamlFileVersion = $null
        if ($package.Name -eq 'Microsoft.WindowsAppRuntime.2' -and
            -not [string]::IsNullOrWhiteSpace($package.InstallLocation)) {
            $xamlPath = Join-Path $package.InstallLocation 'Microsoft.UI.Xaml.dll'
            if (Test-Path -LiteralPath $xamlPath -PathType Leaf) {
                $xamlFileVersion = [version](
                    (Get-Item -LiteralPath $xamlPath).VersionInfo.FileVersionRaw)
            }
        }
        New-NormalizedPackage `
            $package.Name `
            ([version]$package.Version) `
            $package.Architecture.ToString() `
            $xamlFileVersion
    }
)

Get-RuntimePreflightResult $normalizedPackages | ConvertTo-Json -Depth 6
