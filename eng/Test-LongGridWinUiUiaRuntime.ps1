[CmdletBinding()]
param(
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$knownUnsafeRuntimePackageVersion = [version]'2.4.0.0'
$knownUnsafeXamlFileVersion = [version]'3.2.3.0'
$requiredArchitecture = 'X64'
$requiredDdlmArchitectureSuffix = 'x6'
$projectRoot = Split-Path $PSScriptRoot -Parent
$appPackageLockPath = Join-Path $projectRoot `
    'src\LongGrid.App\packages.lock.json'

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

function ConvertTo-FourPartVersion {
    param([version]$Version)

    [version]('{0}.{1}.{2}.{3}' -f
        $Version.Major,
        $Version.Minor,
        [Math]::Max(0, $Version.Build),
        [Math]::Max(0, $Version.Revision))
}

function ConvertFrom-XamlFileVersionRead {
    param([scriptblock]$ReadVersion)

    try {
        $rawVersion = & $ReadVersion
        if ($null -eq $rawVersion -or
            [string]::IsNullOrWhiteSpace($rawVersion.ToString())) {
            return $null
        }
        return [version]$rawVersion
    }
    catch {
        return $null
    }
}

function Get-XamlFileVersion {
    param([string]$InstallLocation)

    ConvertFrom-XamlFileVersionRead {
        if ([string]::IsNullOrWhiteSpace($InstallLocation)) {
            return $null
        }
        $xamlPath = Join-Path $InstallLocation 'Microsoft.UI.Xaml.dll'
        if (-not (Test-Path -LiteralPath $xamlPath -PathType Leaf)) {
            return $null
        }
        (Get-Item -LiteralPath $xamlPath).VersionInfo.FileVersionRaw
    }
}

function Get-RuntimePackageInventory {
    param([scriptblock]$ReadPackages)

    try {
        $packages = @(& $ReadPackages)
        return [pscustomobject]@{
            Discoverable = $true
            Packages = $packages
        }
    }
    catch {
        return [pscustomobject]@{
            Discoverable = $false
            Packages = @()
        }
    }
}

function Get-ProjectRuntimeMinimumVersion {
    Assert-Condition `
        (Test-Path -LiteralPath $appPackageLockPath -PathType Leaf) `
        "LongGrid.App package lock was not found: $appPackageLockPath"

    $lock = Get-Content -LiteralPath $appPackageLockPath -Raw |
        ConvertFrom-Json
    $resolvedVersions = @(
        @(
            foreach ($framework in $lock.dependencies.PSObject.Properties) {
                $runtime = $framework.Value.'Microsoft.WindowsAppSDK.Runtime'
                if ($null -ne $runtime -and
                    -not [string]::IsNullOrWhiteSpace($runtime.resolved)) {
                    [string]$runtime.resolved
                }
            }
        ) | Select-Object -Unique
    )

    Assert-Condition `
        ($resolvedVersions.Count -eq 1) `
        ('LongGrid.App package lock must resolve exactly one ' +
            'Microsoft.WindowsAppSDK.Runtime version.')
    ConvertTo-FourPartVersion ([version]$resolvedVersions[0])
}

function ConvertFrom-SingletonPackageVersion {
    param([version]$Version)

    if ($Version.Major -lt 8000) { return $null }
    [version]('{0}.{1}.{2}.{3}' -f
        ($Version.Major - 8000),
        $Version.Minor,
        $Version.Build,
        $Version.Revision)
}

function Get-RuntimePreflightResult {
    param(
        [object[]]$Packages,
        [version]$MinimumRuntimeVersion,
        [bool]$PackageInventoryDiscoverable = $true
    )

    if ($null -eq $MinimumRuntimeVersion) {
        return [ordered]@{
            schemaVersion = 5
            purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
            expected = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $true
                selectedFrameworkMetadataDiscoverable = $true
                runtimePackageSetComplete = $true
                knownUnsafePairAbsent = $true
            }
            actual = [ordered]@{
                projectRuntimeTargetDiscoverable = $false
                projectRuntimeMinimumVersion = $null
                runtimePackageInventoryDiscoverable = $null
                discoverableRuntime = $null
                selectedFrameworkMetadataDiscoverable = $null
                runtimePackageSetComplete = $false
                missingRequiredPackages = @('ProjectRuntimeTarget')
                knownUnsafePairAbsent = $null
            }
            difference = 'ProjectRuntimeTargetNotDiscoverable'
            outcome = 'Inconclusive'
        }
    }

    $minimumRuntimeVersion = ConvertTo-FourPartVersion $MinimumRuntimeVersion
    $frameworkPackageName =
        "Microsoft.WindowsAppRuntime.$($minimumRuntimeVersion.Major)"

    if (-not $PackageInventoryDiscoverable) {
        return [ordered]@{
            schemaVersion = 5
            purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
            expected = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $true
                selectedFrameworkMetadataDiscoverable = $true
                runtimePackageSetComplete = $true
                knownUnsafePairAbsent = $true
            }
            actual = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                projectRuntimeMinimumVersion =
                    $minimumRuntimeVersion.ToString()
                runtimePackageInventoryDiscoverable = $false
                discoverableRuntime = $null
                selectedFrameworkMetadataDiscoverable = $null
                runtimePackageVersion = $null
                xamlFileVersion = $null
                runtimePackageSetComplete = $null
                frameworkPackagePresent = $null
                mainPackagePresent = $null
                singletonPackagePresent = $null
                ddlmPackagePresent = $null
                missingRequiredPackages = @('RuntimePackageInventory')
                knownUnsafePairAbsent = $null
            }
            difference = 'RuntimePackageInventoryNotDiscoverable'
            outcome = 'Inconclusive'
        }
    }

    $frameworkPackages = @(
        $Packages |
            Where-Object {
                $_.Name -eq $frameworkPackageName -and
                $_.Architecture -eq $requiredArchitecture -and
                [version]$_.Version -ge $minimumRuntimeVersion
            } |
            Sort-Object -Property Version -Descending
    )

    if ($frameworkPackages.Count -eq 0) {
        return [ordered]@{
            schemaVersion = 5
            purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
            expected = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $true
                selectedFrameworkMetadataDiscoverable = $true
                runtimePackageSetComplete = $true
                knownUnsafePairAbsent = $true
            }
            actual = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                projectRuntimeMinimumVersion =
                    $minimumRuntimeVersion.ToString()
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $false
                selectedFrameworkMetadataDiscoverable = $null
                runtimePackageVersion = $null
                xamlFileVersion = $null
                runtimePackageSetComplete = $false
                frameworkPackagePresent = $false
                mainPackagePresent = $false
                singletonPackagePresent = $false
                ddlmPackagePresent = $false
                expectedFrameworkPackage =
                    "$frameworkPackageName@$($minimumRuntimeVersion.ToString())-or-later"
                missingRequiredPackages = @(
                    "$frameworkPackageName@$($minimumRuntimeVersion.ToString())-or-later"
                )
                knownUnsafePairAbsent = $null
            }
            difference = 'RuntimeFrameworkNotDiscoverable'
            outcome = 'Inconclusive'
        }
    }

    $selectedRuntime = $frameworkPackages[0]
    $runtimePackageVersion = [version]$selectedRuntime.Version
    if ($null -eq $selectedRuntime.XamlFileVersion) {
        return [ordered]@{
            schemaVersion = 5
            purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
            expected = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $true
                selectedFrameworkMetadataDiscoverable = $true
                runtimePackageSetComplete = $true
                knownUnsafePairAbsent = $true
            }
            actual = [ordered]@{
                projectRuntimeTargetDiscoverable = $true
                projectRuntimeMinimumVersion =
                    $minimumRuntimeVersion.ToString()
                runtimePackageInventoryDiscoverable = $true
                discoverableRuntime = $true
                selectedFrameworkMetadataDiscoverable = $false
                runtimePackageVersion = $runtimePackageVersion.ToString()
                xamlFileVersion = $null
                runtimePackageSetComplete = $null
                frameworkPackagePresent = $true
                mainPackagePresent = $null
                singletonPackagePresent = $null
                ddlmPackagePresent = $null
                missingRequiredPackages = @()
                knownUnsafePairAbsent = $null
            }
            difference = 'SelectedRuntimeFrameworkMetadataNotDiscoverable'
            outcome = 'Inconclusive'
        }
    }
    $xamlFileVersion = [version]$selectedRuntime.XamlFileVersion
    $mainPackageName =
        "MicrosoftCorporationII.WinAppRuntime.Main.$($minimumRuntimeVersion.Major)"
    $singletonPackageName =
        'MicrosoftCorporationII.WinAppRuntime.Singleton'
    $ddlmPackageName =
        "Microsoft.WinAppRuntime.DDLM.$($minimumRuntimeVersion.ToString())-$requiredDdlmArchitectureSuffix"

    $mainPackages = @(
        $Packages | Where-Object {
            $_.Name -eq $mainPackageName -and
            $_.Architecture -eq $requiredArchitecture -and
            [version]$_.Version -ge $minimumRuntimeVersion
        } | Sort-Object -Property Version -Descending
    )
    $singletonPackages = @(
        @(
            foreach ($package in $Packages) {
                if ($package.Name -ne $singletonPackageName -or
                    $package.Architecture -ne $requiredArchitecture) {
                    continue
                }
                $runtimeVersion = ConvertFrom-SingletonPackageVersion `
                    ([version]$package.Version)
                if ($null -ne $runtimeVersion -and
                    $runtimeVersion.Major -eq $minimumRuntimeVersion.Major -and
                    $runtimeVersion -ge $minimumRuntimeVersion) {
                    [pscustomobject]@{
                        Package = $package
                        RuntimeVersion = $runtimeVersion
                    }
                }
            }
        ) | Sort-Object -Property RuntimeVersion -Descending
    )
    $mainPackagePresent = $mainPackages.Count -gt 0
    $singletonPackagePresent = $singletonPackages.Count -gt 0
    $ddlmPackagePresent = @(
        $Packages | Where-Object {
            $_.Name -eq $ddlmPackageName -and
            $_.Architecture -eq $requiredArchitecture -and
            [version]$_.Version -eq $minimumRuntimeVersion
        }
    ).Count -gt 0

    $missingRequiredPackages = @()
    if (-not $mainPackagePresent) {
        $missingRequiredPackages +=
            "$mainPackageName@$($minimumRuntimeVersion.ToString())-or-later"
    }
    if (-not $singletonPackagePresent) {
        $missingRequiredPackages +=
            "$singletonPackageName@runtime-$($minimumRuntimeVersion.ToString())-or-later"
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
        schemaVersion = 5
        purpose = 'LongGridWinUiCrossProcessUiaRuntimePreflight'
        expected = [ordered]@{
            projectRuntimeTargetDiscoverable = $true
            runtimePackageInventoryDiscoverable = $true
            discoverableRuntime = $true
            selectedFrameworkMetadataDiscoverable = $true
            runtimePackageSetComplete = $true
            knownUnsafePairAbsent = $true
        }
        actual = [ordered]@{
            projectRuntimeTargetDiscoverable = $true
            projectRuntimeMinimumVersion =
                $minimumRuntimeVersion.ToString()
            runtimePackageInventoryDiscoverable = $true
            discoverableRuntime = $true
            selectedFrameworkMetadataDiscoverable = $true
            runtimePackageVersion = $runtimePackageVersion.ToString()
            xamlFileVersion = $xamlFileVersion.ToString()
            runtimePackageSetComplete = $runtimePackageSetComplete
            frameworkPackagePresent = $true
            mainPackagePresent = $mainPackagePresent
            singletonPackagePresent = $singletonPackagePresent
            ddlmPackagePresent = $ddlmPackagePresent
            expectedMainPackage = $mainPackageName
            expectedSingletonPackage =
                "$singletonPackageName@runtime-$($minimumRuntimeVersion.ToString())-or-later"
            expectedDdlmPackage = $ddlmPackageName
            selectedMainPackageVersion = if ($mainPackagePresent) {
                ([version]$mainPackages[0].Version).ToString()
            } else { $null }
            selectedSingletonPackageVersion = if ($singletonPackagePresent) {
                ([version]$singletonPackages[0].Package.Version).ToString()
            } else { $null }
            missingRequiredPackages = $missingRequiredPackages
            knownUnsafePairAbsent = -not $knownUnsafePair
        }
        difference = $difference
        outcome = $outcome
    }
}

if ($ContractOnly) {
    $projectRuntimeMinimumVersion = Get-ProjectRuntimeMinimumVersion
    $contractRuntimeMinimumVersion = [version]'2.3.1.0'
    $readableXamlVersion = ConvertFrom-XamlFileVersionRead {
        '3.2.2.0'
    }
    $failedXamlVersionRead = ConvertFrom-XamlFileVersionRead {
        throw 'Injected XAML metadata read failure.'
    }
    $invalidXamlVersionRead = ConvertFrom-XamlFileVersionRead {
        'not-a-version'
    }
    $safeFramework = New-NormalizedPackage `
        'Microsoft.WindowsAppRuntime.2' `
        ([version]'2.3.1.0') `
        'X64' `
        $readableXamlVersion
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
            'Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6' `
            ([version]'2.3.1.0') `
            'X64')
    )
    $higherCompatibleComplete = @(
        (New-NormalizedPackage `
            'Microsoft.WindowsAppRuntime.2' `
            ([version]'2.3.2.0') `
            'X64' `
            ([version]'3.2.2.0'))
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Main.2' `
            ([version]'2.3.2.0') `
            'X64')
        (New-NormalizedPackage `
            'MicrosoftCorporationII.WinAppRuntime.Singleton' `
            ([version]'8002.3.2.0') `
            'X64')
        (New-NormalizedPackage `
            'Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6' `
            ([version]'2.3.1.0') `
            'X64')
    )
    $unreadableHighestFramework = @(
        $safeComplete
        (New-NormalizedPackage `
            'Microsoft.WindowsAppRuntime.2' `
            ([version]'2.3.2.0') `
            'X64' `
            $failedXamlVersionRead)
    )
    $readableInventory = Get-RuntimePackageInventory {
        $safeComplete
    }
    $failedInventory = Get-RuntimePackageInventory {
        throw 'Injected runtime package inventory failure.'
    }

    $missingTarget = Get-RuntimePreflightResult @() $null
    $missingFramework = Get-RuntimePreflightResult `
        @() $contractRuntimeMinimumVersion
    $incomplete = Get-RuntimePreflightResult `
        @($safeFramework) $contractRuntimeMinimumVersion
    $unsafe = Get-RuntimePreflightResult `
        $unsafeComplete $contractRuntimeMinimumVersion
    $safe = Get-RuntimePreflightResult `
        $safeComplete $contractRuntimeMinimumVersion
    $higherCompatible = Get-RuntimePreflightResult `
        $higherCompatibleComplete $contractRuntimeMinimumVersion
    $unreadableHighest = Get-RuntimePreflightResult `
        $unreadableHighestFramework $contractRuntimeMinimumVersion
    $inventoryFailure = Get-RuntimePreflightResult `
        $failedInventory.Packages `
        $contractRuntimeMinimumVersion `
        $failedInventory.Discoverable
    Assert-Condition `
        ($missingTarget.difference -eq 'ProjectRuntimeTargetNotDiscoverable') `
        'Missing project runtime metadata must remain inconclusive.'
    Assert-Condition `
        ($readableXamlVersion -eq [version]'3.2.2.0' -and
            $null -eq $failedXamlVersionRead -and
            $null -eq $invalidXamlVersionRead) `
        ('XAML metadata access and format failures must normalize to an ' +
            'unreadable result without terminating the preflight.')
    Assert-Condition `
        ($readableInventory.Discoverable -and
            @($readableInventory.Packages).Count -eq 4 -and
            -not $failedInventory.Discoverable -and
            @($failedInventory.Packages).Count -eq 0 -and
            $inventoryFailure.difference -eq
                'RuntimePackageInventoryNotDiscoverable' -and
            $inventoryFailure.outcome -eq 'Inconclusive') `
        ('Runtime package inventory failures must remain distinct from an ' +
            'empty but discoverable package inventory.')
    Assert-Condition `
        ($missingFramework.outcome -eq 'Inconclusive') `
        'Missing framework inventory must remain inconclusive.'
    Assert-Condition `
        ($incomplete.outcome -eq 'BlockedByIncompleteRuntime' -and
            $incomplete.actual.missingRequiredPackages.Count -eq 3) `
        'Incomplete runtime inventory must fail closed with all missing packages.'
    Assert-Condition `
        ($unsafe.outcome -eq 'BlockedByKnownUpstream') `
        ('A complete known-unsafe runtime pair must remain blocked. ' +
            "Actual=$($unsafe.outcome); " +
            "Missing=$($unsafe.actual.missingRequiredPackages -join ',').")
    Assert-Condition `
        ($safe.outcome -eq 'Pass') `
        'A complete safe runtime package set must pass.'
    Assert-Condition `
        ($higherCompatible.outcome -eq 'Pass' -and
            $higherCompatible.actual.runtimePackageVersion -eq '2.3.2.0' -and
            $higherCompatible.actual.expectedDdlmPackage -eq
                'Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6') `
        ('A newer compatible package set must retain the project-locked ' +
            'DDLM identity.')
    Assert-Condition `
        ($unreadableHighest.outcome -eq 'Inconclusive' -and
            $unreadableHighest.difference -eq
                'SelectedRuntimeFrameworkMetadataNotDiscoverable' -and
            $unreadableHighest.actual.runtimePackageVersion -eq '2.3.2.0' -and
            -not $unreadableHighest.actual.selectedFrameworkMetadataDiscoverable) `
        ('The highest compatible Framework must be selected before its XAML ' +
            'metadata is evaluated; an unreadable selected candidate must not ' +
            'fall back to an older readable Framework.')

    [ordered]@{
        schemaVersion = 5
        purpose = 'LongGridWinUiRuntimePackageSetContract'
        projectRuntimeMinimumVersion =
            $projectRuntimeMinimumVersion.ToString()
        scenarios = 9
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

if ($env:OS -ne 'Windows_NT') {
    throw 'The WinUI UIA runtime preflight can only run on Windows.'
}

$projectRuntimeMinimumVersion = try {
    Get-ProjectRuntimeMinimumVersion
}
catch {
    $null
}
if ($null -eq $projectRuntimeMinimumVersion) {
    Get-RuntimePreflightResult @() $null | ConvertTo-Json -Depth 6
    exit 0
}
$frameworkPackageName =
    "Microsoft.WindowsAppRuntime.$($projectRuntimeMinimumVersion.Major)"
$packageInventory = Get-RuntimePackageInventory {
    Get-AppxPackage -ErrorAction Stop |
        Where-Object {
            $_.Name -eq $frameworkPackageName -or
            $_.Name -like 'MicrosoftCorporationII.WinAppRuntime.Main.*' -or
            $_.Name -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton' -or
            $_.Name -like 'Microsoft.WinAppRuntime.DDLM.*'
        }
}
if (-not $packageInventory.Discoverable) {
    Get-RuntimePreflightResult `
        @() `
        $projectRuntimeMinimumVersion `
        $false | ConvertTo-Json -Depth 6
    exit 0
}
$installedPackages = @($packageInventory.Packages)
$normalizedPackages = @(
    foreach ($package in $installedPackages) {
        $xamlFileVersion = $null
        if ($package.Name -eq $frameworkPackageName) {
            $xamlFileVersion = Get-XamlFileVersion $package.InstallLocation
        }
        New-NormalizedPackage `
            $package.Name `
            ([version]$package.Version) `
            $package.Architecture.ToString() `
            $xamlFileVersion
    }
)

Get-RuntimePreflightResult `
    $normalizedPackages `
    $projectRuntimeMinimumVersion | ConvertTo-Json -Depth 6
