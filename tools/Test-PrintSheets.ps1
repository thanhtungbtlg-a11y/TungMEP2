[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packageBin = Join-Path $root 'release\TungMEP2\net48'
$mainAssembly = Join-Path $packageBin 'TungMEP2.dll'
$revitDirectory = 'C:\Program Files\Autodesk\Revit 2024'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

if (-not (Test-Path -LiteralPath $mainAssembly)) {
    throw "Build output not found: $mainAssembly"
}

$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)

    $assemblyName = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    foreach ($directory in @($packageBin, $revitDirectory)) {
        $candidate = Join-Path $directory $assemblyName
        if (Test-Path -LiteralPath $candidate) {
            return [Reflection.Assembly]::LoadFrom($candidate)
        }
    }

    return $null
}

[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TungMEP2-PrintSheetsSmoke-' + [Guid]::NewGuid().ToString('N'))

try {
    [Reflection.Assembly]::LoadFrom((Join-Path $revitDirectory 'RevitAPI.dll')) | Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($mainAssembly)

    $sheetUtilitiesType = $assembly.GetType('VP_RevitAPI.Helper.SheetUtilities', $true)
    Assert-True (
        $null -ne $sheetUtilitiesType.GetMethod('SetSttValues')) `
        'PrintSheets must expose explicit STT write from the sheet list'
    Assert-True (
        $null -ne $sheetUtilitiesType.GetMethod('EnsureSttForExport')) `
        'PrintSheets must expose automatic STT validation and repair'
    $requiresSttRepairMethod = $sheetUtilitiesType.GetMethod(
        'RequiresAutomaticSttRepair',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $requiresSttRepairMethod) `
        'automatic STT repair must validate raw STT values'
    $validSttArguments = [object[]]::new(2)
    $validSttArguments[0] = [string[]]@('1', '2', '3')
    $validSttArguments[1] = [int]1
    $missingSttArguments = [object[]]::new(2)
    $missingSttArguments[0] = [string[]]@('1', '', '3')
    $missingSttArguments[1] = [int]1
    $duplicateSttArguments = [object[]]::new(2)
    $duplicateSttArguments[0] = [string[]]@('1', '1', '3')
    $duplicateSttArguments[1] = [int]1
    $invalidSttArguments = [object[]]::new(2)
    $invalidSttArguments[0] = [string[]]@('1', 'A', '3')
    $invalidSttArguments[1] = [int]1
    $gappedSttArguments = [object[]]::new(2)
    $gappedSttArguments[0] = [string[]]@('1', '3', '5')
    $gappedSttArguments[1] = [int]1
    $wrongOrderSttArguments = [object[]]::new(2)
    $wrongOrderSttArguments[0] = [string[]]@('3', '1', '2')
    $wrongOrderSttArguments[1] = [int]1
    Assert-True (
        -not [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $validSttArguments)) `
        'STT 1..n in natural Sheet Number order must be preserved'
    Assert-True (
        [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $missingSttArguments)) `
        'missing STT must trigger automatic repair'
    Assert-True (
        [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $duplicateSttArguments)) `
        'duplicate STT must trigger automatic repair'
    Assert-True (
        [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $invalidSttArguments)) `
        'non-numeric STT must trigger automatic repair'
    Assert-True (
        [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $gappedSttArguments)) `
        'gapped STT must be rebuilt as a contiguous 1..n sequence'
    Assert-True (
        [bool]$requiresSttRepairMethod.Invoke(
            $null,
            $wrongOrderSttArguments)) `
        'STT out of natural Sheet Number order must trigger automatic repair'

    $utilitiesType = $assembly.GetType('VP_RevitAPI.Helper.Sheets.SheetExportUtilities', $true)
    Assert-True (
        $null -ne $utilitiesType.GetMethod('ListAvailableSheetListSchedules')) `
        'PrintSheets must list Sheet List schedules as sources'
    Assert-True (
        $null -ne $utilitiesType.GetMethod('ResolveSheetsFromSheetListSchedule')) `
        'PrintSheets must resolve filtered sheets from a Sheet List schedule'
    Assert-True (
        $null -ne $utilitiesType.GetMethod('ListAvailablePrinters')) `
        'PrintSheets must list Revit Native PDF and installed PDF virtual printers'
    Assert-True (
        $null -ne $utilitiesType.GetMethod('ListAvailablePrintSetups')) `
        'PrintSheets must list Revit Print Setups for virtual printers'
    $normalizeMethod = $utilitiesType.GetMethod('NormalizePdfFileBaseName')
    $prepareFolderMethod = $utilitiesType.GetMethod('PrepareOutputFolder')
    $countRenameMethod = $utilitiesType.GetMethod('CountPrintedPdfFilesNeedingRename')
    $renameMethod = $utilitiesType.GetMethod('RenamePrintedPdfFiles')
    $createPdfOptionsMethod = $utilitiesType.GetMethod(
        'CreatePdfExportOptions',
        [Reflection.BindingFlags]'NonPublic,Static')
    $createDwgOptionsMethod = $utilitiesType.GetMethod(
        'CreateDwgExportOptions',
        [Reflection.BindingFlags]'NonPublic,Static')
    $copyDwgStagingArtifactsMethod = $utilitiesType.GetMethod(
        'CopyDwgStagingArtifacts',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $copyDwgStagingArtifactsMethod) 'DWG export must support local staging artifacts'
    $dwgStagingRootField = $utilitiesType.GetField(
        'DwgStagingRootName',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [string]$dwgStagingRootField.GetRawConstantValue() -eq 'TungMEP2-DwgExport') `
        'DWG export must retain isolated local staging'
    $pdfStagingRootField = $utilitiesType.GetField(
        'PdfStagingRootName',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [string]$pdfStagingRootField.GetRawConstantValue() -eq 'TungMEP2-PdfExport') `
        'PDF export must retain isolated local staging'
    $virtualPdfStagingRootField = $utilitiesType.GetField(
        'VirtualPdfStagingRootName',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [string]$virtualPdfStagingRootField.GetRawConstantValue() -eq 'TungMEP2-VirtualPdfPrint') `
        'virtual PDF printers must use isolated local staging'
    $virtualPrinterTimeoutField = $utilitiesType.GetField(
        'VirtualPrinterOutputTimeoutMilliseconds',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [int]$virtualPrinterTimeoutField.GetRawConstantValue() -eq 60000) `
        'virtual PDF printer output must have a bounded wait'
    $virtualPrinterCombinedTimeoutField = $utilitiesType.GetField(
        'VirtualPrinterCombinedOutputTimeoutMilliseconds',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [int]$virtualPrinterCombinedTimeoutField.GetRawConstantValue() -eq 300000) `
        'combined virtual PDF output must allow enough time for PDF-XChange Lite'
    $virtualPrinterStabilityField = $utilitiesType.GetField(
        'VirtualPrinterFileStabilityMilliseconds',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [int]$virtualPrinterStabilityField.GetRawConstantValue() -eq 3000) `
        'virtual PDF output must remain stable before completion is reported'
    $exportVirtualPdfMethod = $utilitiesType.GetMethod(
        'ExportSheetsToVirtualPdf',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $exportVirtualPdfMethod) `
        'PrintSheets must export through selected PDF virtual printers'
    $printVirtualPdfMethod = $utilitiesType.GetMethod(
        'PrintSheetsWithVirtualPrinter',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $printVirtualPdfMethod) `
        'PrintSheets must configure Revit PrintManager for virtual printers'
    $isLikelyPdfPrinterMethod = $utilitiesType.GetMethod(
        'IsLikelyPdfVirtualPrinter',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $isLikelyPdfPrinterMethod) `
        'PrintSheets must distinguish likely PDF virtual printers'
    Assert-True (
        [bool]$isLikelyPdfPrinterMethod.Invoke($null, [object[]]@([string]'PDF-XChange Lite'))) `
        'PDF-XChange Lite must be recognized as a PDF virtual printer'
    Assert-True (
        [bool]$isLikelyPdfPrinterMethod.Invoke($null, [object[]]@([string]'Foxit PDF Printer'))) `
        'Foxit PDF Printer must be recognized as a PDF virtual printer'
    Assert-True (
        -not [bool]$isLikelyPdfPrinterMethod.Invoke($null, [object[]]@([string]'Office Laser Printer'))) `
        'physical printers must not be offered as PDF output engines'
    $isCompletePdfMethod = $utilitiesType.GetMethod(
        'IsCompletePdfFile',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $isCompletePdfMethod) `
        'virtual-printer output must be validated as a complete PDF'
    $waitForVirtualPrinterPdfMethod = $utilitiesType.GetMethod(
        'WaitForVirtualPrinterPdf',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $waitForVirtualPrinterPdfMethod) `
        'virtual-printer output must wait for a stable completed PDF'
    foreach ($methodName in @(
        'ConfigurePdfXChangeLiteAutoSave',
        'FindPdfXChangeLitePdfSaverExecutable',
        'DumpPdfXChangeLiteOptions',
        'SetPdfXChangeLiteSaveOptions',
        'TryParsePdfXChangeBooleanOption')) {
        $method = $utilitiesType.GetMethod(
            $methodName,
            [Reflection.BindingFlags]'NonPublic,Static')
        Assert-True ($null -ne $method) `
            "PDF-XChange Lite silent output must contain $methodName"
    }
    $pdfXChangeCommandTimeoutField = $utilitiesType.GetField(
        'PdfXChangeCommandTimeoutMilliseconds',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True (
        [int]$pdfXChangeCommandTimeoutField.GetRawConstantValue() -eq 15000) `
        'PDF-XChange Lite settings command must have a bounded wait'
    $parsePdfXChangeBooleanMethod = $utilitiesType.GetMethod(
        'TryParsePdfXChangeBooleanOption',
        [Reflection.BindingFlags]'NonPublic,Static')
    $parsePdfXChangeArguments = [object[]]::new(3)
    $parsePdfXChangeArguments[0] = [string[]]@(
        'Save.ShowSaveDialog=1; Yes',
        'Save.RunApp=0; No')
    $parsePdfXChangeArguments[1] = [string]'Save.ShowSaveDialog'
    $parsePdfXChangeArguments[2] = [bool]$false
    Assert-True (
        [bool]$parsePdfXChangeBooleanMethod.Invoke(
            $null,
            $parsePdfXChangeArguments)) `
        'PDF-XChange Lite option parser must read ShowSaveDialog'
    Assert-True ([bool]$parsePdfXChangeArguments[2]) `
        'PDF-XChange Lite option parser must preserve the enabled value'
    $replaceFileFromStagingMethod = $utilitiesType.GetMethod(
        'ReplaceFileFromStaging',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $replaceFileFromStagingMethod) 'exports must replace existing files from staging'
    $getSafeBaseFileNameLengthMethod = $utilitiesType.GetMethod(
        'GetSafeBaseFileNameLength',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ($null -ne $getSafeBaseFileNameLengthMethod) 'final filenames must account for the destination path length'
    $applyNamingRuleMethod = $utilitiesType.GetMethod(
        'ApplySheetNumberAndNameNamingRule',
        [Reflection.BindingFlags]'NonPublic,Static')
    $pdfBatchSizeField = $utilitiesType.GetField(
        'PdfBatchSize',
        [Reflection.BindingFlags]'NonPublic,Static')
    Assert-True ([int]$pdfBatchSizeField.GetRawConstantValue() -eq 10) 'PDF batch size must remain responsive for pause and stop'

    $windowType = $assembly.GetType('VP_RevitAPI.UI.UI_PrintSheetsWindow', $true)
    $printerOptionType = $assembly.GetType(
        'VP_RevitAPI.Helper.Sheets.PrinterOption',
        $true)
    $nativePrinterOption = $printerOptionType.GetMethod(
        'CreateNativePdf',
        [Reflection.BindingFlags]'Public,Static').Invoke($null, @())
    Assert-True ($nativePrinterOption.IsNativePdf) `
        'Revit Native PDF must remain the default output engine'
    Assert-True ([string]::IsNullOrWhiteSpace($nativePrinterOption.DriverName)) `
        'Revit Native PDF must not depend on a Windows printer driver'
    $createVirtualPrinterMethod = $printerOptionType.GetMethod(
        'CreateVirtualPrinter',
        [Reflection.BindingFlags]'Public,Static')
    $pdfXChangeLiteOption = $createVirtualPrinterMethod.Invoke(
        $null,
        [object[]]@([string]'PDF-XChange Lite', [bool]$false))
    $foxitOption = $createVirtualPrinterMethod.Invoke(
        $null,
        [object[]]@([string]'Foxit PDF Printer', [bool]$true))
    $printerOptions = [Array]::CreateInstance($printerOptionType, 3)
    $printerOptions.SetValue($nativePrinterOption, 0)
    $printerOptions.SetValue($foxitOption, 1)
    $printerOptions.SetValue($pdfXChangeLiteOption, 2)
    $defaultPrinterArguments = [object[]]::new(1)
    $defaultPrinterArguments[0] = $printerOptions
    $defaultPrinter = $utilitiesType.GetMethod(
        'SelectDefaultPdfPrinter').Invoke(
            $null,
            $defaultPrinterArguments)
    Assert-True (
        $defaultPrinter.DriverName -eq 'PDF-XChange Lite') `
        'PDF-XChange Lite must be the default PDF engine when installed'
    $fallbackPrinterOptions = [Array]::CreateInstance($printerOptionType, 2)
    $fallbackPrinterOptions.SetValue($nativePrinterOption, 0)
    $fallbackPrinterOptions.SetValue($foxitOption, 1)
    $fallbackPrinterArguments = [object[]]::new(1)
    $fallbackPrinterArguments[0] = $fallbackPrinterOptions
    $fallbackPrinter = $utilitiesType.GetMethod(
        'SelectDefaultPdfPrinter').Invoke(
            $null,
            $fallbackPrinterArguments)
    Assert-True ($fallbackPrinter.IsNativePdf) `
        'Native PDF must be used only when PDF-XChange Lite is unavailable'
    $sheetSourceOptionType = $assembly.GetType(
        'VP_RevitAPI.UI.UI_PrintSheetsWindow+SheetSourceOption',
        $true)
    Assert-True ($null -ne $sheetSourceOptionType) `
        'PrintSheets UI must expose a unified Print Set/Schedule source option'
    $pdfProcessingOptionType = $assembly.GetType(
        'VP_RevitAPI.UI.UI_PrintSheetsWindow+PdfProcessingOption',
        $true)
    Assert-True ($null -ne $pdfProcessingOptionType) `
        'PrintSheets UI must expose vector and font-safe PDF processing options'
    foreach ($methodName in @(
        'TogglePause',
        'RequestStop',
        'WaitForExportPermission',
        'OnWindowClosing',
        'InitializeWindowPresentation',
        'SuppressSelectionForPdfExport',
        'RestoreSelectionAfterExport',
        'OnSheetSourcesSelectionChanged',
        'OnSheetSourceCheckedChanged',
        'OnSelectAllSourcesClick',
        'OnSelectNoSourcesClick',
        'OnMoveSourceUpClick',
        'OnMoveSourceDownClick',
        'OnToggleSettingsClick',
        'RefreshSheetsFromSelectedSources',
        'OnMoveSheetUpClick',
        'OnMoveSheetDownClick',
        'OnGenerateSttClick',
        'OnApplySttClick',
        'InitializePdfProcessingList',
        'BuildDefaultCombinedPdfName',
        'OnPrinterSelectionChanged',
        'RefreshPdfSetupOptionsForSelectedEngine')) {
        $method = $windowType.GetMethod(
            $methodName,
            [Reflection.BindingFlags]'NonPublic,Instance')
        Assert-True ($null -ne $method) "PrintSheets window must contain $methodName"
    }
    $uiSource = Get-Content -LiteralPath (
        Join-Path $root 'src\TungMEP2\Core\UI\UI_PrintSheetsWindow.cs') -Raw
    Assert-True (
        -not $uiSource.Contains('SheetUtilities.EnsureSttForExport')) `
        'PrintSheets export flow must not silently rewrite STT'
    Assert-True (
        $uiSource.Contains('SheetUtilities.SetSttValues')) `
        'PrintSheets sheet list must explicitly write user-approved STT values'
    $dwgOutputFolderField = $windowType.GetField(
        'DwgOutputFolderTextBox',
        [Reflection.BindingFlags]'NonPublic,Instance')
    Assert-True ($null -ne $dwgOutputFolderField) 'PrintSheets window must contain a separate DWG folder control'
    $uiDocumentField = $windowType.GetField(
        '_uiDocument',
        [Reflection.BindingFlags]'NonPublic,Instance')
    Assert-True ($null -ne $uiDocumentField) 'PrintSheets must retain UIDocument to suppress PDF selection highlights'
    $sheetSourcesListField = $windowType.GetField(
        'SheetSourcesListBox',
        [Reflection.BindingFlags]'NonPublic,Instance')
    Assert-True ($null -ne $sheetSourcesListField) `
        'PrintSheets window must expose grouped Print Set and Sheet Schedule sources'
    foreach ($fieldName in @(
        'SttStartTextBox',
        'SttStatusTextBlock',
        'ToggleSettingsButton',
        'SourceControlsPanel',
        'ExportSettingsPanel',
        'SheetListControlsPanel',
        'SttControlsPanel')) {
        $field = $windowType.GetField(
            $fieldName,
            [Reflection.BindingFlags]'NonPublic,Instance')
        Assert-True ($null -ne $field) `
            "PrintSheets grouped order/STT UI must contain $fieldName"
    }
    $pdfOutputProcessingField = $windowType.GetField(
        'PdfOutputProcessingComboBox',
        [Reflection.BindingFlags]'NonPublic,Instance')
    Assert-True ($null -ne $pdfOutputProcessingField) `
        'PrintSheets window must expose the PDF font-safe processing selector'
    foreach ($fieldName in @(
        'PrinterComboBox',
        'PdfSetupLabelTextBlock',
        'PdfProcessingLabelTextBlock',
        'PdfEngineInfoTextBlock')) {
        $field = $windowType.GetField(
            $fieldName,
            [Reflection.BindingFlags]'NonPublic,Instance')
        Assert-True ($null -ne $field) `
            "PrintSheets window must expose virtual-printer UI field $fieldName"
    }
    foreach ($dimension in @{
        DefaultWindowWidth = 1180
        DefaultWindowHeight = 960
        MinimumWindowWidth = 960
        MinimumWindowHeight = 720
    }.GetEnumerator()) {
        $dimensionField = $windowType.GetField(
            $dimension.Key,
            [Reflection.BindingFlags]'NonPublic,Static')
        Assert-True (
            [double]$dimensionField.GetRawConstantValue() -eq [double]$dimension.Value) `
            "PrintSheets window dimension $($dimension.Key) must match the redesigned layout"
    }

    $generatedResources = $assembly.GetManifestResourceStream('TungMEP2.g.resources')
    Assert-True ($null -ne $generatedResources) 'WPF generated resources must be embedded'
    try {
        $resourceReader = [Resources.ResourceReader]::new($generatedResources)
        try {
            $printSheetsResourceFound = $false
            $resourceEnumerator = $resourceReader.GetEnumerator()
            while ($resourceEnumerator.MoveNext()) {
                if ([string]$resourceEnumerator.Key -ieq 'ui/ui_printsheetswindow.baml') {
                    $printSheetsResourceFound = $true
                    break
                }
            }
            Assert-True $printSheetsResourceFound 'redesigned PrintSheets XAML must be packaged'
        }
        finally {
            $resourceReader.Dispose()
        }
    }
    finally {
        $generatedResources.Dispose()
    }

    $summaryType = $assembly.GetType('VP_RevitAPI.Helper.Sheets.ExportSummary', $true)
    $summaryArguments = [object[]]::new(4)
    $summaryArguments[0] = [int]1
    $summaryArguments[1] = [int]2
    $summaryArguments[2] = [System.Collections.Generic.List[string]]::new()
    $summaryArguments[3] = [bool]$true
    $cancelledSummary = $summaryType.GetConstructors()[0].Invoke($summaryArguments)
    Assert-True ($cancelledSummary.WasCancelled) 'export summary must report a user stop'

    $normalized = [string]$normalizeMethod.Invoke($null, [object[]]@([string]'A/101.pdf'))
    Assert-True ($normalized -eq 'A_101') 'invalid filename characters must be sanitized'

    $reserved = [string]$normalizeMethod.Invoke($null, [object[]]@([string]'CON.pdf'))
    Assert-True ($reserved -eq '_CON') 'reserved Windows filenames must be protected'

    $longName = [string]$normalizeMethod.Invoke($null, [object[]]@([string](('A' * 220) + '.pdf')))
    Assert-True ($longName.Length -le 160) 'long filenames must be limited'

    $preparedFolder = [string]$prepareFolderMethod.Invoke($null, [object[]]@([string]$testRoot))
    Assert-True ([IO.Directory]::Exists($preparedFolder)) 'output folder must be created'
    Assert-True (@(Get-ChildItem -LiteralPath $preparedFolder -Filter '.tungmep2-write-test-*').Count -eq 0) 'write probe must be cleaned'

    $validPdfProbe = Join-Path $preparedFolder 'virtual-printer-valid.pdf'
    $invalidPdfProbe = Join-Path $preparedFolder 'virtual-printer-invalid.pdf'
    $incompletePdfProbe = Join-Path $preparedFolder 'virtual-printer-incomplete.pdf'
    [IO.File]::WriteAllBytes(
        $validPdfProbe,
        [Text.Encoding]::ASCII.GetBytes("%PDF-1.7 probe`n%%EOF`n"))
    [IO.File]::WriteAllBytes(
        $invalidPdfProbe,
        [Text.Encoding]::ASCII.GetBytes('not a pdf'))
    [IO.File]::WriteAllBytes(
        $incompletePdfProbe,
        [Text.Encoding]::ASCII.GetBytes('%PDF-1.7 still-writing'))
    Assert-True (
        [bool]$isCompletePdfMethod.Invoke($null, [object[]]@([string]$validPdfProbe))) `
        'valid staged virtual-printer PDF must pass signature validation'
    Assert-True (
        -not [bool]$isCompletePdfMethod.Invoke($null, [object[]]@([string]$invalidPdfProbe))) `
        'invalid virtual-printer output must not overwrite the destination PDF'
    Assert-True (
        -not [bool]$isCompletePdfMethod.Invoke($null, [object[]]@([string]$incompletePdfProbe))) `
        'PDF header without %%EOF must remain incomplete'
    $waitArguments = [object[]]::new(5)
    $waitArguments[0] = [string]$preparedFolder
    $waitArguments[1] = [string]$validPdfProbe
    $waitArguments[2] = [int]2000
    $waitArguments[3] = [int]300
    $waitArguments[4] = $null
    $waitStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $stablePdfPath = [string]$waitForVirtualPrinterPdfMethod.Invoke(
        $null,
        $waitArguments)
    $waitStopwatch.Stop()
    Assert-True ($stablePdfPath -eq $validPdfProbe) `
        'stable completed virtual PDF must be detected'
    Assert-True ($waitStopwatch.ElapsedMilliseconds -ge 250) `
        'virtual PDF must not be accepted immediately after its first %%EOF marker'

    $dwgStage = Join-Path $testRoot 'dwg-stage'
    $dwgDestination = Join-Path $testRoot 'dwg-destination'
    [IO.Directory]::CreateDirectory($dwgStage) | Out-Null
    [IO.Directory]::CreateDirectory($dwgDestination) | Out-Null
    [IO.File]::WriteAllBytes(
        (Join-Path $dwgStage 'A101.dwg'),
        [byte[]](10, 20, 30, 40))
    [IO.File]::WriteAllBytes(
        (Join-Path $dwgStage 'Model-ANHVEDAmàu.jpg'),
        [byte[]](50, 60, 70))
    [IO.File]::WriteAllBytes(
        (Join-Path $dwgDestination 'A101.dwg'),
        [byte[]](1))
    [IO.File]::WriteAllBytes(
        (Join-Path $dwgDestination 'Model-ANHVEDAmàu.jpg'),
        [byte[]](2))
    $copyDwgStagingArtifactsMethod.Invoke(
        $null,
        [object[]]@(
            [string]$dwgStage,
            [string]$dwgDestination,
            [string]'A101.dwg')) | Out-Null
    Assert-True (
        [IO.File]::Exists((Join-Path $dwgDestination 'A101.dwg'))) 'staged DWG must be copied to destination'
    Assert-True (
        [IO.File]::Exists((Join-Path $dwgDestination 'Model-ANHVEDAmàu.jpg'))) 'staged raster sidecar must be copied to destination'
    Assert-True (
        (Get-FileHash -LiteralPath (Join-Path $dwgStage 'A101.dwg')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $dwgDestination 'A101.dwg')).Hash) 'existing DWG must be overwritten by staged DWG'
    Assert-True (
        (Get-FileHash -LiteralPath (Join-Path $dwgStage 'Model-ANHVEDAmàu.jpg')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $dwgDestination 'Model-ANHVEDAmàu.jpg')).Hash) 'existing raster sidecar must be overwritten'
    Assert-True (
        @(Get-ChildItem -LiteralPath $dwgDestination -Filter '.tm2-*').Count -eq 0) 'overwrite must clean temporary replacement files'

    $longDestination = Join-Path $testRoot 'long-destination'
    [IO.Directory]::CreateDirectory($longDestination) | Out-Null
    $longSource = Join-Path $dwgStage 'long-path-source.dwg'
    [IO.File]::WriteAllBytes($longSource, [byte[]](80, 81, 82, 83))
    $longTargetName = ('FF-FA-0107-' + ('N' * 92) + '.dwg')
    $longTarget = Join-Path $longDestination $longTargetName
    Assert-True ($longTarget.Length -lt 260) 'long-path fixture target must remain valid'
    Assert-True (
        (Join-Path $longDestination ('.' + $longTargetName + '.tungmep2-new-' + ('0' * 32) + '.tmp')).Length -ge 260) `
        'long-path fixture must reproduce the former replacement-path failure'
    $replaceFileFromStagingMethod.Invoke(
        $null,
        [object[]]@([string]$longSource, [string]$longTarget)) | Out-Null
    Assert-True ([IO.File]::Exists($longTarget)) 'long valid destination path must be supported'
    Assert-True (
        (Get-FileHash -LiteralPath $longSource).Hash -eq
        (Get-FileHash -LiteralPath $longTarget).Hash) 'long-path replacement must preserve file contents'
    Assert-True (
        @(Get-ChildItem -LiteralPath $longDestination -Filter '.tm2-*').Count -eq 0) 'long-path replacement must clean short temporary files'

    $safeLength = [int]$getSafeBaseFileNameLengthMethod.Invoke(
        $null,
        [object[]]@([string]$longDestination, [string]'.dwg'))
    Assert-True (
        ((Join-Path $longDestination (('S' * $safeLength) + '.dwg')).Length) -le 259) `
        'path-aware filename limit must keep the final target inside the Windows limit'

    [IO.File]::WriteAllBytes((Join-Path $preparedFolder 'A101 - Plan.pdf'), [byte[]](1, 2, 3))
    [IO.File]::WriteAllBytes((Join-Path $preparedFolder 'Project - Sheet - A101 - Plan.pdf'), [byte[]](4, 5, 6))

    $candidateCount = [int]$countRenameMethod.Invoke($null, [object[]]@([string]$preparedFolder))
    Assert-True ($candidateCount -eq 1) 'exactly one legacy PDF should need renaming'

    $renameSummary = $renameMethod.Invoke($null, [object[]]@([string]$preparedFolder))
    Assert-True ($renameSummary.RenamedCount -eq 1) 'legacy PDF should be renamed'
    Assert-True ([IO.File]::Exists((Join-Path $preparedFolder 'A101 - Plan (2).pdf'))) 'rename must not overwrite an existing file'

    $pdfQualityType = [Reflection.Assembly]::LoadFrom(
        (Join-Path $revitDirectory 'RevitAPI.dll')).GetType(
            'Autodesk.Revit.DB.PDFExportQualityType',
            $true)
    $dpi300 = [Enum]::Parse($pdfQualityType, 'DPI300')
    $dpi600 = [Enum]::Parse($pdfQualityType, 'DPI600')

    $fastOptions = $createPdfOptionsMethod.Invoke(
        $null,
        [object[]]@($dpi300, [bool]$false, [string]'', [bool]$false))
    try {
        Assert-True ($fastOptions.ExportQuality.ToString() -eq 'DPI300') 'fast preset must use 300 DPI'
        Assert-True ($fastOptions.RasterQuality.ToString() -eq 'Medium') 'fast preset must use medium raster quality'
        Assert-True (-not $fastOptions.Combine) 'batch options must support separate PDF files'
        Assert-True (-not $fastOptions.AlwaysUseRaster) 'vector mode must preserve vector PDF output'

        $applyNamingRuleMethod.Invoke($null, [object[]]@($fastOptions)) | Out-Null
        $namingRule = $fastOptions.GetNamingRule()
        try {
            Assert-True ($namingRule.Count -eq 2) 'batch naming rule must contain sheet number and sheet name'
            Assert-True ($namingRule[0].Separator -eq ' - ') 'sheet number and name must use the expected separator'
        }
        finally {
            foreach ($rulePart in $namingRule) {
                $rulePart.Dispose()
            }
        }
    }
    finally {
        $fastOptions.Dispose()
    }

    $qualityOptions = $createPdfOptionsMethod.Invoke(
        $null,
        [object[]]@($dpi600, [bool]$true, [string]'Combined', [bool]$true))
    try {
        Assert-True ($qualityOptions.ExportQuality.ToString() -eq 'DPI600') 'quality preset must use 600 DPI'
        Assert-True ($qualityOptions.RasterQuality.ToString() -eq 'High') 'quality preset must use high raster quality'
        Assert-True ($qualityOptions.Combine) 'combined export must set Combine'
        Assert-True ($qualityOptions.AlwaysUseRaster) 'font-safe mode must force raster PDF output'
    }
    finally {
        $qualityOptions.Dispose()
    }

    $elementIdType = [Reflection.Assembly]::LoadFrom(
        (Join-Path $revitDirectory 'RevitAPI.dll')).GetType(
            'Autodesk.Revit.DB.ElementId',
            $true)
    $invalidElementId = $elementIdType.GetProperty(
        'InvalidElementId',
        [Reflection.BindingFlags]'Public,Static').GetValue($null)
    $defaultDwgOptions = $createDwgOptionsMethod.Invoke(
        $null,
        [object[]]@($null, $invalidElementId))
    try {
        Assert-True (-not $defaultDwgOptions.MergedViews) 'DWG export must not force MergedViews'
    }
    finally {
        $defaultDwgOptions.Dispose()
    }

    $storeType = $assembly.GetType('VP_RevitAPI.State.PrintSheetsRecentOutputFolderStore', $true)
    Assert-True ($null -ne $storeType.GetMethod('LoadPdfOutputFolder')) 'state must load the PDF folder separately'
    Assert-True ($null -ne $storeType.GetMethod('LoadDwgOutputFolder')) 'state must load the DWG folder separately'
    $storageMethod = $storeType.GetMethod(
        'GetStorageDirectoryPath',
        [Reflection.BindingFlags]'NonPublic,Static')
    $storagePath = [string]$storageMethod.Invoke($null, @())
    Assert-True ($storagePath -like '*\TungMEP2\State\PrintSheets') 'state must be stored under TungMEP2'
    Assert-True ($storagePath -notlike '*\VP_RevitAPI\*') 'state must not use the previous add-in folder'

    $assemblyBytes = [IO.File]::ReadAllBytes($mainAssembly)
    $ascii = [Text.Encoding]::ASCII.GetString($assemblyBytes)
    $unicode = [Text.Encoding]::Unicode.GetString($assemblyBytes)
    Assert-True ($ascii.Contains('PDFExportOptions')) 'binary must contain Revit Native PDF support'
    Assert-True ($ascii.Contains('TableCellCombinedParameterData')) 'binary must contain the PDF batch naming rule'
    Assert-True ($ascii.Contains('SubmitPrint')) 'binary must contain Revit virtual-printer submission support'
    Assert-True ($ascii.Contains('SelectNewPrintDriver')) 'binary must select the requested PDF virtual printer'

    Write-Host 'PrintSheets smoke test passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $leafName = Split-Path -Leaf $resolvedTestRoot
        if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            $leafName.StartsWith('TungMEP2-PrintSheetsSmoke-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
        }
    }
}
