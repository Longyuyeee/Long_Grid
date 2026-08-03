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
$xamlPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml'
$codeBehindPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml.cs'
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
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

function Get-XamlNodeByAutomationId {
    param(
        [System.Xml.XmlDocument]$Document,
        [string]$AutomationId
    )

    $matches = @(
        $Document.SelectNodes('//*') |
            Where-Object {
                $_.GetAttribute('AutomationProperties.AutomationId') -eq $AutomationId
            }
    )

    Assert-Condition ($matches.Count -eq 1) `
        "Expected exactly one XAML node with AutomationId '$AutomationId'; found $($matches.Count)."
    return $matches[0]
}

function Test-SourceContract {
    [xml]$document = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
    $codeBehind = Get-Content -LiteralPath $codeBehindPath -Raw -Encoding UTF8
    $requiredIds = @(
        'LongGridRoot',
        'ShellNavigation',
        'NavOverview',
        'NavAppearance',
        'NavSafety',
        'OverviewPanel',
        'AppearancePanel',
        'SafetyPanel',
        'CurrentModeCard',
        'FileOperationCard',
        'DesktopHostCard',
        'CurrentModeValue',
        'FileOperationValue',
        'DesktopHostValue',
        'ResponsiveStatusText',
        'ContentScrollViewer',
        'ThemeSystem',
        'ThemeLight',
        'ThemeDark',
        'ThemeStatusText'
    )

    foreach ($automationId in $requiredIds) {
        $null = Get-XamlNodeByAutomationId $document $automationId
    }

    $rootNode = Get-XamlNodeByAutomationId $document 'LongGridRoot'
    Assert-Condition ($rootNode.GetAttribute('AutomationProperties.Name').Length -gt 0) `
        'LongGridRoot must keep a semantic accessibility name.'
    $themeStatusNode = Get-XamlNodeByAutomationId $document 'ThemeStatusText'
    Assert-Condition (
        $themeStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'ThemeStatusText must politely announce in-process theme changes.'

    $scrollViewer = $document.SelectSingleNode("//*[local-name()='ScrollViewer']")
    Assert-Condition ($null -ne $scrollViewer) 'The content ScrollViewer is missing.'
    Assert-Condition (
        $scrollViewer.GetAttribute('HorizontalScrollMode') -eq 'Disabled'
    ) 'Horizontal scrolling must stay disabled; compact content must reflow.'

    $expectedAccessKeys = @{
        NavOverview = '1'
        NavAppearance = '2'
        NavSafety = '3'
    }
    foreach ($entry in $expectedAccessKeys.GetEnumerator()) {
        $node = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition ($node.GetAttribute('AccessKey') -eq $entry.Value) `
            "Navigation item '$($entry.Key)' must keep AccessKey '$($entry.Value)'."
    }

    foreach ($themeId in @('ThemeSystem', 'ThemeLight', 'ThemeDark')) {
        $node = Get-XamlNodeByAutomationId $document $themeId
        Assert-Condition ($node.GetAttribute('Checked') -eq 'ThemeOption_Checked') `
            "Theme option '$themeId' must use the audited memory-only handler."
    }

    Assert-Condition ($codeBehind -match 'RootLayout\.RequestedTheme') `
        'The theme handler must apply RequestedTheme to the app root.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Default') `
        'The theme handler must preserve a follow-system mode.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Light') `
        'The theme handler must expose a light mode.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Dark') `
        'The theme handler must expose a dark mode.'
    Assert-Condition ($codeBehind -match 'CompactBreakpoint\s*=\s*760') `
        'The audited compact/wide breakpoint must remain 760 effective pixels.'
    Assert-Condition ($codeBehind -match 'RootLayout\.SizeChanged') `
        'Responsive layout must follow the effective root size.'
    Assert-Condition ($codeBehind -match 'NavigationViewPaneDisplayMode\.LeftMinimal') `
        'Compact layout must use the minimal navigation pane.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus') `
        'Responsive layout must expose its actual state to UI Automation.'
    Assert-Condition ($codeBehind -match 'XamlRoot\?\.RasterizationScale') `
        'Initial window sizing must convert effective pixels using the XAML scale.'
    Assert-Condition ($codeBehind -match 'DisplayArea\.GetFromWindowId') `
        'Initial window sizing must use the active display work area.'
    Assert-Condition ($codeBehind -match 'MaximumWorkAreaFraction\s*=\s*0\.9') `
        'The initial window must remain bounded to 90 percent of the work area.'
    Assert-Condition ($codeBehind -match 'RuntimeStatusSnapshot\.CreateDevelopmentReadOnly') `
        'The UI must obtain its capability state from the audited Core snapshot.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*CurrentModeValue') `
        'The current runtime mode must expose a machine-readable UIA status.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*FileOperationValue') `
        'The file-operation boundary must expose a machine-readable UIA status.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*DesktopHostValue') `
        'The DesktopHost boundary must expose a machine-readable UIA status.'

    $forbiddenPatterns = @(
        'System\.IO\.',
        '\bFile\.',
        '\bDirectory\.',
        'Environment\.GetFolderPath',
        'LongGrid\.Core\.DesktopItems',
        '\bDesktopCatalog\s*\.',
        'ShellChange',
        'LongGrid\.Core\.DesktopHost',
        'DesktopHostCompositeTransactionCoordinator',
        'DesktopHostWindowPlanner'
    )
    foreach ($pattern in $forbiddenPatterns) {
        Assert-Condition (-not ($codeBehind -match $pattern)) `
            "UI code-behind crossed the read-only slice boundary: '$pattern'."
    }

    return [ordered]@{
        requiredAutomationIds = $requiredIds.Count
        accessKeys = $expectedAccessKeys.Count
        themeModes = 3
        responsiveBreakpoints = 1
        compactWidth = 720
        dpiAwareInitialSize = 'pass'
        coreRuntimeStatus = 'development-read-only'
        readOnlyBoundary = 'pass'
    }
}

function Find-UiaElement {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [int]$TimeoutSeconds = 5
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition
        )
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "UI Automation element '$AutomationId' was not found within $TimeoutSeconds seconds."
}

function Select-UiaElement {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        return
    }

    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }

    throw "Element '$($Element.Current.AutomationId)' exposes neither SelectionItem nor Invoke."
}

function Wait-UiaName {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$ExpectedText,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($Element.Current.Name -like "*$ExpectedText*") {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Element '$($Element.Current.AutomationId)' did not expose expected text '$ExpectedText'."
}

function Assert-VerticallyStacked {
    param(
        [System.Windows.Automation.AutomationElement[]]$Elements,
        [System.Windows.Rect]$ContainerBounds
    )

    $previousBottom = [double]::NegativeInfinity
    foreach ($element in $Elements) {
        $bounds = $element.Current.BoundingRectangle
        Assert-Condition ($bounds.Width -gt 0 -and $bounds.Height -gt 0) `
            "Element '$($element.Current.AutomationId)' has no compact bounds; offscreen=$($element.Current.IsOffscreen), bounds=$bounds."
        Assert-Condition ($bounds.Left -ge $ContainerBounds.Left - 1) `
            "Element '$($element.Current.AutomationId)' overflows the compact left edge."
        Assert-Condition ($bounds.Right -le $ContainerBounds.Right + 1) `
            "Element '$($element.Current.AutomationId)' overflows the compact right edge."
        Assert-Condition ($bounds.Top -ge $previousBottom - 1) `
            "Element '$($element.Current.AutomationId)' overlaps the previous compact card."
        $previousBottom = $bounds.Bottom
    }
}

function Scroll-UiaToMetrics {
    param([System.Windows.Automation.AutomationElement]$ScrollViewer)

    $pattern = $null
    if (-not $ScrollViewer.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern,
            [ref]$pattern)) {
        throw 'ContentScrollViewer does not expose the Scroll pattern.'
    }

    $scrollPattern = [System.Windows.Automation.ScrollPattern]$pattern
    if ($scrollPattern.Current.VerticallyScrollable) {
        $scrollPattern.SetScrollPercent(
            [System.Windows.Automation.ScrollPattern]::NoScroll,
            25)
        Start-Sleep -Milliseconds 250
    }
}

function Test-LiveUi {
    if ($env:OS -ne 'Windows_NT') {
        throw 'The live Long Grid UI smoke requires Windows.'
    }

    if (-not $NoBuild) {
        & dotnet restore $projectPath --locked-mode --runtime $runtimeIdentifier
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App restore failed with exit code $LASTEXITCODE."
        }

        & dotnet build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    $targetFramework = 'net8.0-windows10.0.19041.0'
    $appPath = Join-Path $projectRoot `
        "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier\LongGrid.App.exe"
    Assert-Condition (Test-Path -LiteralPath $appPath) `
        "LongGrid.App executable was not found: $appPath"

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LongGridWindowNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(
        IntPtr window,
        int x,
        int y,
        int width,
        int height,
        bool repaint);
}
'@

    $process = Start-Process -FilePath $appPath -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and
            -not $process.HasExited -and
            [DateTime]::UtcNow -lt $deadline)

        Assert-Condition (-not $process.HasExited) `
            'LongGrid.App exited before the UI Automation smoke could attach.'
        Assert-Condition ($process.MainWindowHandle -ne [IntPtr]::Zero) `
            'LongGrid.App did not expose a main window within 15 seconds.'

        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $expectedTitle = 'Long' + [char]0x65B9 + [char]0x683C
        Assert-Condition ($root.Current.Name -eq $expectedTitle) `
            "Unexpected window title '$($root.Current.Name)'."

        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        Wait-UiaName $responsiveStatus 'UI Shell'
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $navigation = Find-UiaElement $root 'ShellNavigation'
        $layoutRoot = Find-UiaElement $root 'LongGridRoot'
        $overview = Find-UiaElement $root 'NavOverview'
        $appearance = Find-UiaElement $root 'NavAppearance'
        $safety = Find-UiaElement $root 'NavSafety'
        foreach ($item in @($overview, $appearance, $safety)) {
            Assert-Condition $item.Current.IsKeyboardFocusable `
                "Navigation item '$($item.Current.AutomationId)' is not keyboard focusable."
        }
        Select-UiaElement $overview

        $windowBounds = $root.Current.BoundingRectangle
        Assert-Condition (
            [LongGridWindowNative]::MoveWindow(
                $process.MainWindowHandle,
                [int]$windowBounds.Left,
                [int]$windowBounds.Top,
                720,
                [int]$windowBounds.Height,
                $true)
        ) 'LongGrid.App could not be resized for the compact layout smoke.'
        Start-Sleep -Milliseconds 500
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $layoutRoot = Find-UiaElement $root 'LongGridRoot'
        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        $compactText = [string]([char]0x7D27) + [char]0x51D1
        Wait-UiaName $responsiveStatus $compactText
        $contentScrollViewer = Find-UiaElement $root 'ContentScrollViewer'
        Scroll-UiaToMetrics $contentScrollViewer
        $currentModeCard = Find-UiaElement $root 'CurrentModeValue'
        $fileOperationCard = Find-UiaElement $root 'FileOperationValue'
        $desktopHostCard = Find-UiaElement $root 'DesktopHostValue'
        Assert-Condition ($currentModeCard.Current.ItemStatus -eq 'DevelopmentReadOnly') `
            'The UI did not expose the Core development read-only mode.'
        Assert-Condition ($fileOperationCard.Current.ItemStatus -eq 'DisabledBySafetyPolicy') `
            'The UI did not expose the file-operation safety policy.'
        Assert-Condition ($desktopHostCard.Current.ItemStatus -eq 'Disconnected') `
            'The UI did not expose the disconnected DesktopHost boundary.'
        Assert-VerticallyStacked `
            @($currentModeCard, $fileOperationCard, $desktopHostCard) `
            $layoutRoot.Current.BoundingRectangle

        Assert-Condition (
            [LongGridWindowNative]::MoveWindow(
                $process.MainWindowHandle,
                [int]$windowBounds.Left,
                [int]$windowBounds.Top,
                [int]$windowBounds.Width,
                [int]$windowBounds.Height,
                $true)
        ) 'LongGrid.App could not restore the wide layout.'
        Start-Sleep -Milliseconds 500
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        Wait-UiaName $responsiveStatus 'UI Shell'
        $navigation = Find-UiaElement $root 'ShellNavigation'
        $appearance = Find-UiaElement $root 'NavAppearance'
        $safety = Find-UiaElement $root 'NavSafety'

        $appearance.SetFocus()
        Start-Sleep -Milliseconds 150
        Assert-Condition (
            [System.Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq
                'NavAppearance'
        ) 'Navigation focus could not be moved to NavAppearance.'
        Select-UiaElement $appearance

        $themeDark = Find-UiaElement $root 'ThemeDark'
        Assert-Condition $themeDark.Current.IsKeyboardFocusable `
            'The dark theme option is not keyboard focusable.'
        Select-UiaElement $themeDark
        $themeStatus = Find-UiaElement $root 'ThemeStatusText'
        $darkText = [string]([char]0x6DF1) + [char]0x8272
        Wait-UiaName $themeStatus $darkText

        $themeSystem = Find-UiaElement $root 'ThemeSystem'
        Select-UiaElement $themeSystem
        $systemText = [string]([char]0x8DDF) +
            [char]0x968F + [char]0x7CFB + [char]0x7EDF
        Wait-UiaName $themeStatus $systemText

        Select-UiaElement $safety
        $safetyPanel = Find-UiaElement $root 'SafetyPanel'
        Assert-Condition (-not $safetyPanel.Current.IsOffscreen) `
            'SafetyPanel stayed offscreen after selecting its navigation item.'

        return [ordered]@{
            windowTitle = $root.Current.Name
            processId = $process.Id
            navigationAutomationId = $navigation.Current.AutomationId
            navigationItems = 3
            keyboardFocus = 'pass'
            responsiveLayout = 'wide-compact-wide-720'
            responsiveItemStatus = $layoutRoot.Current.ItemStatus
            compactCards = 3
            coreRuntimeStatus = 'development-read-only'
            themeRoundTrip = 'system-dark-system'
            safetyNavigation = 'pass'
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }
    }
}

Push-Location $projectRoot
try {
    $contractResult = Test-SourceContract
    $liveResult = if ($ContractOnly) { $null } else { Test-LiveUi }

    [ordered]@{
        contract = $contractResult
        live = $liveResult
        mode = if ($ContractOnly) { 'contract-only' } else { 'contract-and-live' }
        outcome = 'Pass'
    } | ConvertTo-Json -Depth 4
}
finally {
    Pop-Location
}
