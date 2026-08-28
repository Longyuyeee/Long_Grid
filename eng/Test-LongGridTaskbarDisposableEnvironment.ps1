[CmdletBinding()]
param(
    [string]$ConfigurationPath,

    [string]$EvidenceDirectory,

    [switch]$PrepareConfiguration,

    [switch]$RequireReady
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-FullPath {
    param([string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function ConvertTo-XmlText {
    param([string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-BoundedHardwareEvidence {
    $queryScript = @'
$ErrorActionPreference = 'Stop'
$computerSystem = Get-CimInstance `
    -ClassName Win32_ComputerSystem `
    -OperationTimeoutSec 2
$processors = @(Get-CimInstance `
    -ClassName Win32_Processor `
    -OperationTimeoutSec 2)
[ordered]@{
    physicalMemoryBytes = [long]$computerSystem.TotalPhysicalMemory
    virtualizationFirmwareEnabled =
        $processors.Count -gt 0 -and
        @($processors | Where-Object {
            -not $_.VirtualizationFirmwareEnabled
        }).Count -eq 0
    secondLevelAddressTranslation =
        $processors.Count -gt 0 -and
        @($processors | Where-Object {
            -not $_.SecondLevelAddressTranslationExtensions
        }).Count -eq 0
} | ConvertTo-Json -Compress
'@
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($queryScript))
    $currentPowerShell = (Get-Process -Id $PID).Path
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $currentPowerShell
    $startInfo.Arguments = "-NoProfile -EncodedCommand $encodedCommand"
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return $null
        }

        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(6000)) {
            $process.Kill()
            $process.WaitForExit(2000) | Out-Null
            return $null
        }

        $errorText = $errorOutput.GetAwaiter().GetResult()
        $outputText = $output.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or
            -not [string]::IsNullOrWhiteSpace($errorText) -or
            [string]::IsNullOrWhiteSpace($outputText)) {
            return $null
        }

        return $outputText | ConvertFrom-Json
    }
    catch {
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit(2000) | Out-Null
            }
        }
        catch {
            # Hardware evidence remains unavailable and fails closed below.
        }
        return $null
    }
    finally {
        $process.Dispose()
    }
}

function New-CertificationConfiguration {
    param(
        [string]$Destination,
        [string]$EvidencePath
    )

    $fullDestination = Get-FullPath $Destination
    $fullEvidencePath = Get-FullPath $EvidencePath
    $destinationParent = Split-Path -Parent $fullDestination
    [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    [System.IO.Directory]::CreateDirectory($fullEvidencePath) | Out-Null

    $sourceXml = ConvertTo-XmlText $projectRoot
    $evidenceXml = ConvertTo-XmlText $fullEvidencePath
    $configuration = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$sourceXml</HostFolder>
      <SandboxFolder>C:\LongGridSource</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$evidenceXml</HostFolder>
      <SandboxFolder>C:\LongGridEvidence</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <VideoInput>Disable</VideoInput>
  <AudioInput>Disable</AudioInput>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LongGridSource\eng\Test-LongGridTaskbarDisposableGuest.ps1</Command>
  </LogonCommand>
</Configuration>
"@
    [System.IO.File]::WriteAllText(
        $fullDestination,
        $configuration,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-ConfigurationEvidence {
    param([string]$Path)

    $evidence = [ordered]@{
        present = $false
        networkDisabled = $false
        sourceReadOnly = $false
        evidenceWriteOnly = $false
        clipboardDisabled = $false
        peripheralRedirectionDisabled = $false
        boundedGuestCommand = $false
    }
    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $evidence
    }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create((Get-FullPath $Path), $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $configuration = $document.Configuration
    $mappedFolders = @($configuration.MappedFolders.MappedFolder)
    $source = @($mappedFolders | Where-Object {
        $_.SandboxFolder -eq 'C:\LongGridSource'
    })
    $output = @($mappedFolders | Where-Object {
        $_.SandboxFolder -eq 'C:\LongGridEvidence'
    })
    $evidence.present = $true
    $evidence.networkDisabled = $configuration.Networking -eq 'Disable'
    $evidence.sourceReadOnly =
        $source.Count -eq 1 -and $source[0].ReadOnly -eq 'true'
    $evidence.evidenceWriteOnly =
        $output.Count -eq 1 -and $output[0].ReadOnly -eq 'false'
    $evidence.clipboardDisabled =
        $configuration.ClipboardRedirection -eq 'Disable'
    $evidence.peripheralRedirectionDisabled =
        $configuration.PrinterRedirection -eq 'Disable' -and
        $configuration.VideoInput -eq 'Disable' -and
        $configuration.AudioInput -eq 'Disable'
    $evidence.boundedGuestCommand =
        $configuration.LogonCommand.Command -eq `
            'powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LongGridSource\eng\Test-LongGridTaskbarDisposableGuest.ps1'
    return $evidence
}

if ($PrepareConfiguration) {
    if ([string]::IsNullOrWhiteSpace($ConfigurationPath) -or
        [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        throw 'Preparing a sandbox requires configuration and evidence paths.'
    }

    New-CertificationConfiguration `
        -Destination $ConfigurationPath `
        -EvidencePath $EvidenceDirectory
}

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$logicalProcessors = [System.Environment]::ProcessorCount
$windowsDirectory = [System.Environment]::GetFolderPath(
    [System.Environment+SpecialFolder]::Windows)
$launcherPath = Join-Path $windowsDirectory 'System32\WindowsSandbox.exe'
$launcherPresent = Test-Path -LiteralPath $launcherPath -PathType Leaf
$physicalMemoryBytes = 0L
$virtualizationFirmwareEnabled = $false
$slatAvailable = $false
$hardwareEvidence = if ($launcherPresent) {
    Get-BoundedHardwareEvidence
}
else {
    $null
}
$hardwareEvidenceCollected = $null -ne $hardwareEvidence
if ($hardwareEvidenceCollected) {
    $physicalMemoryBytes = [long]$hardwareEvidence.physicalMemoryBytes
    $virtualizationFirmwareEnabled =
        [bool]$hardwareEvidence.virtualizationFirmwareEnabled
    $slatAvailable =
        [bool]$hardwareEvidence.secondLevelAddressTranslation
}

$configurationEvidence = Get-ConfigurationEvidence $ConfigurationPath

$difference = @()
if (-not $runningOnWindows) { $difference += 'NotWindows' }
if ($architecture -notin @('X64', 'Arm64')) {
    $difference += 'UnsupportedArchitecture'
}
if ($logicalProcessors -lt 2) {
    $difference += 'InsufficientLogicalProcessors'
}
if (-not $hardwareEvidenceCollected) {
    $difference += 'HardwareEvidenceUnavailable'
}
else {
    if ($physicalMemoryBytes -lt 4GB) {
        $difference += 'InsufficientPhysicalMemory'
    }
    if (-not $virtualizationFirmwareEnabled) {
        $difference += 'VirtualizationFirmwareNotAttested'
    }
    if (-not $slatAvailable) {
        $difference += 'SecondLevelAddressTranslationNotAttested'
    }
}
if (-not $launcherPresent) {
    $difference += 'WindowsSandboxLauncherMissing'
}
if (-not $configurationEvidence.present) {
    $difference += 'SandboxConfigurationMissing'
}
if ($configurationEvidence.present -and
    (-not $configurationEvidence.networkDisabled -or
     -not $configurationEvidence.sourceReadOnly -or
     -not $configurationEvidence.evidenceWriteOnly -or
     -not $configurationEvidence.clipboardDisabled -or
     -not $configurationEvidence.peripheralRedirectionDisabled -or
     -not $configurationEvidence.boundedGuestCommand)) {
    $difference += 'SandboxConfigurationUnsafe'
}

$ready = $difference.Count -eq 0
$result = [ordered]@{
    schemaVersion = 1
    purpose = 'TaskbarR2B1DisposableEnvironmentAdmission'
    expected = [ordered]@{
        windows = $true
        architecture = 'X64 or Arm64'
        minimumLogicalProcessors = 2
        minimumPhysicalMemoryBytes = 4GB
        virtualizationFirmwareEnabled = $true
        secondLevelAddressTranslation = $true
        windowsSandboxLauncher = $true
        networkDisabled = $true
        sourceReadOnly = $true
        evidenceWriteOnly = $true
        clipboardAndPeripheralRedirectionDisabled = $true
        modifiedSystemState = $false
    }
    actual = [ordered]@{
        windows = $runningOnWindows
        operatingSystemVersion = [System.Environment]::OSVersion.Version.ToString()
        architecture = $architecture
        logicalProcessors = $logicalProcessors
        hardwareEvidenceCollected = $hardwareEvidenceCollected
        physicalMemoryBytes = $physicalMemoryBytes
        virtualizationFirmwareEnabled = $virtualizationFirmwareEnabled
        secondLevelAddressTranslation = $slatAvailable
        windowsSandboxLauncher = $launcherPresent
        configuration = $configurationEvidence
        modifiedSystemState = $false
        mutationAllowed = $false
    }
    difference = if ($ready) { 'None' } else { $difference }
    outcome = if ($ready) { 'ReadyToLaunch' } else { 'Blocked' }
}

$result | ConvertTo-Json -Depth 8
if ($RequireReady -and -not $ready) {
    exit 2
}
