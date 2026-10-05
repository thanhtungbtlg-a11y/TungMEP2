[CmdletBinding()]
param(
    [string]$PackageBin,
    [switch]$EngineOnly
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackageBin)) {
    $PackageBin = Join-Path (Split-Path -Parent $PSScriptRoot) 'release\TungMEP2\net48'
}
$legacyPath = Join-Path $PackageBin 'TungMEP2.PrintSheets.dll'
if (-not (Test-Path -LiteralPath $legacyPath)) {
    throw 'PrintSheets 2026.07.31.1 is not bundled with TungMEP2.'
}

$opCodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $op = $field.GetValue($null)
    $opCodes[[int]$op.Value] = $op
}

function Get-ReferencedMethods {
    param([Reflection.MethodBase]$Method)
    $body = $Method.GetMethodBody()
    if ($null -eq $body) { return }
    $il = $body.GetILAsByteArray()
    $offset = 0
    while ($offset -lt $il.Length) {
        $value = [int]$il[$offset++]
        if ($value -eq 254) {
            $value = ([int]65024 -bor [int]$il[$offset++]) - 65536
        }
        $op = $opCodes[$value]
        if ($null -eq $op) { throw "Unknown IL instruction $value in $($Method.Name) at $offset." }
        switch ($op.OperandType.ToString()) {
            'InlineNone' { $operandSize = 0 }
            'ShortInlineBrTarget' { $operandSize = 1 }
            'ShortInlineI' { $operandSize = 1 }
            'ShortInlineVar' { $operandSize = 1 }
            'InlineVar' { $operandSize = 2 }
            'InlineI8' { $operandSize = 8 }
            'InlineR' { $operandSize = 8 }
            'InlineSwitch' { $operandSize = 4 + 4 * [BitConverter]::ToInt32($il, $offset) }
            default { $operandSize = 4 }
        }
        if ($op.OperandType.ToString() -eq 'InlineMethod') {
            $methodArguments = [Type[]]@()
            if ($Method -is [Reflection.MethodInfo]) { $methodArguments = $Method.GetGenericArguments() }
            $Method.Module.ResolveMethod([BitConverter]::ToInt32($il, $offset),
                $Method.DeclaringType.GetGenericArguments(), $methodArguments)
        }
        $offset += $operandSize
    }
}

function Get-EngineCallGraph {
    param([Reflection.MethodBase]$Entry)
    $pending = [Collections.Generic.Queue[Reflection.MethodBase]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new()
    $pending.Enqueue($Entry)
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        $key = $current.Module.ModuleVersionId.ToString() + ':' + $current.MetadataToken
        if (-not $seen.Add($key)) { continue }
        foreach ($called in @(Get-ReferencedMethods $current)) {
            $called
            if ($called.Module -eq $Entry.Module) { $pending.Enqueue($called) }
        }
    }
}

$revitDirectory = 'C:\Program Files\Autodesk\Revit 2024'
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $fileName = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    foreach ($directory in @($PackageBin, $revitDirectory)) {
        $candidate = Join-Path $directory $fileName
        if (Test-Path -LiteralPath $candidate) {
            return [Reflection.Assembly]::ReflectionOnlyLoadFrom($candidate)
        }
    }
    try { return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name) }
    catch { return $null }
}
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolver)
try {
    # RevitAPIUI is a mixed-mode host assembly. Inspect metadata without running
    # its native code in an external PowerShell process.
    [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $revitDirectory 'RevitAPI.dll')) | Out-Null
    [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $revitDirectory 'RevitAPIUI.dll')) | Out-Null
    $legacy = [Reflection.Assembly]::ReflectionOnlyLoadFrom($legacyPath)
    $window = $legacy.GetType('VP_RevitAPI.UI.UI_PrintSheetsWindow', $true)
    $instanceFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    foreach ($fieldName in @('SttStartTextBox', 'SttStatusTextBlock', 'SheetsDataGrid', 'SheetSourcesListBox')) {
        if ($null -eq $window.GetField($fieldName, $instanceFlags)) {
            throw "Restored PrintSheets is missing manual-STT UI: $fieldName."
        }
    }
    $handlers = @('OnGenerateSttClick', 'OnApplySttClick', 'OnMoveSourceUpClick',
        'OnMoveSourceDownClick', 'OnMoveSheetUpClick', 'OnMoveSheetDownClick')
    $connect = $window.GetMethod('System.Windows.Markup.IComponentConnector.Connect', $instanceFlags)
    if ($null -eq $connect) { throw 'Compiled PrintSheets UI connector is missing.' }
    $wiredMethods = @(Get-ReferencedMethods $connect | ForEach-Object Name)
    foreach ($handler in $handlers) {
        if ($null -eq $window.GetMethod($handler, $instanceFlags) -or $wiredMethods -notcontains $handler) {
            throw "PrintSheets button is absent or not wired: $handler."
        }
    }

    # Inspect the actual embedded UI, not the possibly newer source beside it.
    $resourceAssembly = [Reflection.Assembly]::LoadFrom($legacyPath)
    $stream = $resourceAssembly.GetManifestResourceStream('TungMEP2.PrintSheets.g.resources')
    if ($null -eq $stream) { throw 'PrintSheets WPF resources are missing.' }
    $reader = [Resources.ResourceReader]::new($stream)
    try {
        $bamlText = $null
        foreach ($entry in $reader) {
            if ([string]$entry.Key -ieq 'core/ui/ui_printsheetswindow.baml') {
                $memory = [IO.MemoryStream]::new()
                try {
                    $entry.Value.CopyTo($memory)
                    $bamlText = [Text.Encoding]::UTF8.GetString($memory.ToArray())
                }
                finally { $memory.Dispose() }
            }
        }
        # Code points keep this test usable in Windows PowerShell's ANSI script
        # reader, while checking the exact Vietnamese text embedded in BAML.
        $sourceLabel = 'Ngu' + [char]0x1ed3 + 'n sheet'
        $generateLabel = 'T' + [char]0x1ea1 + 'o STT theo th' + [char]0x1ee9 + ' t' + [char]0x1ef1 + ' PDF'
        $applyLabel = 'Ghi STT v' + [char]0xe0 + 'o Sheet'
        foreach ($label in @('PDF #', $sourceLabel, 'STT', $generateLabel, $applyLabel,
            'ExportOrderText', 'SourceDisplay', 'ProposedStt')) {
            if ($null -eq $bamlText -or -not $bamlText.Contains($label)) {
                throw "Restored DLL does not contain the requested UI item: $label."
            }
        }
    }
    finally { $reader.Dispose(); $stream.Dispose() }

    $exportCalls = @(Get-EngineCallGraph ($window.GetMethod('OnExportClick', $instanceFlags)))
    foreach ($forbidden in @('EnsureSttForExport', 'SetSttValues', 'OrderSheetsForExport')) {
        if ($exportCalls.Name -contains $forbidden) {
            throw "Export must not silently write STT: reachable $forbidden."
        }
    }
    if ($exportCalls.Name -notcontains 'set_OrderedViewList' -or $exportCalls.Name -notcontains 'set_IsAutomatic') {
        throw 'Virtual PDF printing must explicitly supply the manual sheet order.'
    }
    $generateCalls = @(Get-EngineCallGraph ($window.GetMethod('OnGenerateSttClick', $instanceFlags)))
    if ($generateCalls.Name -contains 'SetSttValues') { throw 'Generate STT must remain a draft operation.' }
    $applyCalls = @(Get-EngineCallGraph ($window.GetMethod('OnApplySttClick', $instanceFlags)))
    if ($applyCalls.Name -notcontains 'SetSttValues') { throw 'Explicit Apply STT does not write STT.' }
    $exportSelectionCalls = @(Get-ReferencedMethods ($window.GetMethod('ResolveExportSheets', $instanceFlags)))
    if (@($exportSelectionCalls | Where-Object { $_.Name -match '^(OrderBy|ThenBy|Sort)' }).Count -gt 0) {
        throw 'Export selection must retain the displayed PDF order, not sort again.'
    }
    $legacyCommand = $legacy.GetType('TungMEP2.PrintSheets.Command_PrintSheets', $true)
    $commandCalls = @(Get-ReferencedMethods ($legacyCommand.GetMethod('Execute')))
    if ($commandCalls.Name -notcontains 'ShowDialog' -or $commandCalls.Name -contains 'Show') {
        throw 'The restored engine must use modal printing, not the newer modeless mode.'
    }
    if ($legacy.GetName().Version.ToString() -ne '2026.10.3.1') { throw 'Wrong maintenance engine version.' }
    foreach ($entry in @(
        @('RefreshSheetsFromSelectedSources', 'Restore'),
        @('OnSheetExportSelectionChanged', 'SetSelection'),
        @('SetSheetSelectionState', 'SelectAll'),
        @('ResolveExportSheets', 'DistinctInOrder'))) {
        $calls = @(Get-EngineCallGraph ($window.GetMethod($entry[0], $instanceFlags)))
        if (-not @($calls | Where-Object { $_.DeclaringType.FullName -eq 'VP_RevitAPI.Helper.Sheets.PrintSheetSelection' -and $_.Name -eq $entry[1] }).Count) {
            throw "UI does not use tested selection helper: $($entry[0])."
        }
    }
    $utilities = $legacy.GetType('VP_RevitAPI.Helper.Sheets.SheetExportUtilities', $true)
    $exportBodyCalls = @($utilities.GetMethods() | Where-Object Name -eq 'ExportSheets' | ForEach-Object { Get-EngineCallGraph $_ })
    if (-not @($exportBodyCalls | Where-Object { $_.DeclaringType.FullName -eq 'VP_RevitAPI.Helper.Sheets.PrintSheetSelection' -and $_.Name -eq 'DistinctInOrder' }).Count) {
        throw 'Export boundary is not defensively deduplicated.'
    }
    Write-Host 'Manual STT columns, compiled button wiring, modal mode and export call graph passed.' -ForegroundColor Green
    if ($EngineOnly) { return }

    $integrated = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $PackageBin 'TungMEP2.dll'))
    $commandType = $integrated.GetType('VP_RevitAPI.Commands.LAYOUT.Command_PrintSheets', $true)
    $execute = $commandType.GetMethod('Execute', [Reflection.BindingFlags]'Public,Instance')

    # Verify the ribbon entry actually calls the pinned command, rather than
    # leaving an unused legacy DLL next to the old integrated implementation.
    $legacyExecuteCalls = @(Get-ReferencedMethods $execute | Where-Object {
        $_.Name -eq 'Execute' -and
        $_.DeclaringType.FullName -eq 'TungMEP2.PrintSheets.Command_PrintSheets' -and
        $_.DeclaringType.Assembly.GetName().Name -eq 'TungMEP2.PrintSheets'
    }).Count
    if ($legacyExecuteCalls -ne 1) {
        throw 'The TungMEP2 ribbon command must dispatch once to PrintSheets 2026.07.31.1.'
    }

    if (-not @($commandType.GetInterfaces().FullName).Contains('Autodesk.Revit.UI.IExternalCommand')) {
        throw 'Integrated PrintSheets must retain the Revit external-command contract.'
    }
    if ($null -eq $legacyCommand.GetMethod('Execute')) { throw 'Pinned external command is missing.' }
    Write-Host 'PrintSheets 2026.10.03.1 package and ribbon dispatch tests passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolver)
}
