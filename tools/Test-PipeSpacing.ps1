[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$collectorPath = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\PipeSpacing\PipeSpacingClusterCollector.cs'
$configPath = Join-Path $root 'src\TungMEP2\Core\State\PipeSpacingConfigStore.cs'
$windowPath = Join-Path $root 'src\TungMEP2\Core\UI\UI_PipeSpacingWindow.cs'
$boundaryPath = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\PipeSpacing\PipeSpacingBoundaryService.cs'
$executorPath = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\PipeSpacing\PipeSpacingExecutor.cs'
$runnerPath = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\PipeSpacing\PipeSpacingRunner.cs'
$commandPath = Join-Path $root 'src\TungMEP2\Core\Commands\CTN\Command_PipeSpacing.cs'
$applicationPath = Join-Path $root 'src\TungMEP2\Core\Application.cs'

if (-not (Test-Path -LiteralPath $collectorPath)) {
    throw 'PipeSpacing bounded cluster collector is missing.'
}

$collectorSource = [IO.File]::ReadAllText($collectorPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'PipeSpacingTraversalPolicy.TryPartition',
    'BuiltInCategory.OST_PipeFitting',
    'BuiltInCategory.OST_PipeAccessory',
    'BuiltInCategory.OST_MechanicalEquipment',
    'BuiltInCategory.OST_PlumbingFixtures',
    'ConnectorType.Logical',
    'BoundaryStretchSegments',
    'AllOwnedElementIds',
    'HasPinnedElement'
)) {
    if (-not $collectorSource.Contains($contract)) {
        throw "PipeSpacing collector contract is missing: $contract"
    }
}

if ($collectorSource.Contains('ElementTransformUtils.')) {
    throw 'PipeSpacing collector must remain read-only.'
}
if (-not (Test-Path -LiteralPath $configPath)) {
    throw 'PipeSpacingConfigStore is missing.'
}
if (-not (Test-Path -LiteralPath $windowPath)) {
    throw 'UI_PipeSpacingWindow is missing.'
}
$windowSource = [IO.File]::ReadAllText($windowPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'Equal Clear Gap',
    'Equal Center Spacing',
    'DN Rule Table',
    'Include insulation',
    'Include fitting envelope',
    'DataGrid',
    'Reset defaults',
    'PreviewBeforeApply'
)) {
    if (-not $windowSource.Contains($contract)) {
        throw "PipeSpacing settings window contract is missing: $contract"
    }
}

if (-not (Test-Path -LiteralPath $boundaryPath)) {
    throw 'PipeSpacingBoundaryService is missing.'
}
if (-not (Test-Path -LiteralPath $executorPath)) {
    throw 'PipeSpacingExecutor is missing.'
}
$boundarySource = [IO.File]::ReadAllText($boundaryPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'PipeSpacingBoundaryState',
    'FixedEndpoint',
    'MovingEndpoint',
    'BoundaryOwnerUniqueId',
    'MovingOwnerUniqueId',
    'TryCapture',
    'TryStretchAndVerify',
    'LocationCurve',
    'Line.CreateBound',
    'IsConnectedTo'
)) {
    if (-not $boundarySource.Contains($contract)) {
        throw "PipeSpacing boundary contract is missing: $contract"
    }
}
$executorSource = [IO.File]::ReadAllText($executorPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'Transaction(document, "TungMEP2 - PipeSpacing")',
    'SubTransaction',
    'ElementTransformUtils.MoveElements',
    'RigidElementIds',
    'UniqueId',
    '.Pinned',
    '.GroupId',
    'RollBack',
    'ValidateFinalClearances',
    'GetElementOverrides',
    'SetElementOverrides',
    'RefreshActiveView'
)) {
    if (-not $executorSource.Contains($contract)) {
        throw "PipeSpacing executor contract is missing: $contract"
    }
}
if ($executorSource.Contains('BoundaryStretchSegments.Select(segment => segment.PipeId).Concat(entry.Cluster.RigidElementIds)')) {
    throw 'Boundary stretch Pipe IDs must never be included in the rigid MoveElements set.'
}
$restoreIndex = $executorSource.IndexOf('RestorePreviewOverrides(uiDocument.ActiveView')
$commitIndex = $executorSource.IndexOf('transaction.Commit()')
if ($restoreIndex -lt 0 -or $commitIndex -lt 0 -or $restoreIndex -gt $commitIndex) {
    throw 'Preview overrides must be restored before the outer transaction commits.'
}

if (-not (Test-Path -LiteralPath $runnerPath)) {
    throw 'PipeSpacingRunner is missing.'
}
if (-not (Test-Path -LiteralPath $commandPath)) {
    throw 'Command_PipeSpacing is missing.'
}
$runnerSource = [IO.File]::ReadAllText($runnerPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'Selection.GetElementIds()',
    'PickObjects(',
    'StraightPipeSelectionFilter',
    'Pipe spacing selection prompt',
    'PipeSpacingConfigStore.Load',
    'PipeSpacingConfigStore.Save',
    'PipeSpacingAnalysisService.TryCreatePlan',
    'UI_PipeSpacingWindow',
    'PipeSpacingExecutor.TryApply'
)) {
    if (-not $runnerSource.Contains($contract)) {
        throw "PipeSpacing runner contract is missing: $contract"
    }
}
$commandSource = [IO.File]::ReadAllText($commandPath, [Text.Encoding]::UTF8)
if (-not $commandSource.Contains('class Command_PipeSpacing : SecuredExternalCommand')) {
    throw 'Command_PipeSpacing must inherit SecuredExternalCommand.'
}
$applicationSource = [IO.File]::ReadAllText($applicationPath, [Text.Encoding]::UTF8)
if (-not $applicationSource.Contains('["PipeSpacing"]')) {
    throw 'PipeSpacing ribbon tooltip is missing.'
}
$pipeLayerIndex = $applicationSource.IndexOf('AddPushButton<Command_PipeLayer>("PipeLayer")')
$pipeSpacingIndex = $applicationSource.IndexOf('AddPushButton<Command_PipeSpacing>("PipeSpacing")')
if ($pipeLayerIndex -lt 0 -or $pipeSpacingIndex -lt 0 -or
    [Math]::Abs($pipeSpacingIndex - $pipeLayerIndex) -gt 500) {
    throw 'PipeSpacing must be registered beside PipeLayer in PLUMBING.'
}

$assemblyPath = Join-Path $root 'build\TungMEP2.dll'
$revitDirectory = 'C:\Program Files\Autodesk\Revit 2024'

$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $fileName = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    foreach ($directory in @((Join-Path $root 'build'), (Join-Path $root 'lib'), $revitDirectory)) {
        $candidate = Join-Path $directory $fileName
        if (Test-Path -LiteralPath $candidate) {
            return [Reflection.Assembly]::LoadFrom($candidate)
        }
    }
    return $null
}

[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    [Reflection.Assembly]::LoadFrom((Join-Path $revitDirectory 'RevitAPI.dll')) | Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
    $geometryType = $assembly.GetType(
        'VP_RevitAPI.Internal.Ctn.PipeSpacing.PipeSpacingGeometry',
        $false)
    if ($null -eq $geometryType) {
        throw 'PipeSpacingGeometry is missing.'
    }

    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $tryBuildAxes = $geometryType.GetMethod('TryBuildAxes', $flags)
    $projectExtent = $geometryType.GetMethod('ProjectExtent', $flags)
    if ($null -eq $tryBuildAxes -or $null -eq $projectExtent) {
        throw 'PipeSpacingGeometry axis or extent contract is missing.'
    }

    $configType = $assembly.GetType('VP_RevitAPI.State.PipeSpacingConfigStore', $false)
    if ($null -eq $configType) {
        throw 'PipeSpacingConfigStore type is missing.'
    }
    $deserialize = $configType.GetMethod(
        'DeserializeForDocument',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $deserialize) {
        throw 'PipeSpacingConfigStore.DeserializeForDocument is missing.'
    }
    $defaultSettings = $deserialize.Invoke($null, [object[]]@('{bad json', 'project-a'))
    $settingsType = $defaultSettings.GetType()
    $clearGap = [double]$settingsType.GetProperty(
        'ClearGapMm',
        [Reflection.BindingFlags]'Instance,NonPublic').GetValue($defaultSettings)
    $version = [int]$settingsType.GetProperty(
        'Version',
        [Reflection.BindingFlags]'Instance,NonPublic').GetValue($defaultSettings)
    if ([Math]::Abs($clearGap - 75.0) -gt 1e-8 -or $version -ne 1) {
        throw 'Malformed config must return versioned 75 mm defaults.'
    }
    $emptyRulesJson = '{"Version":1,"Projects":[{"DocumentKey":"project-a","Settings":{"Version":1,"Mode":2,"ClearGapMm":75,"CenterSpacingMm":200,"FallbackClearGapMm":90,"ParallelAngleToleranceDegrees":2,"PreviewBeforeApply":true,"DnRules":[]}}]}'
    $restoredSettings = $deserialize.Invoke($null, [object[]]@($emptyRulesJson, 'project-a'))
    $restoredRules = $settingsType.GetProperty(
        'DnRules',
        [Reflection.BindingFlags]'Instance,NonPublic').GetValue($restoredSettings)
    $restoredFallback = [double]$settingsType.GetProperty(
        'FallbackClearGapMm',
        [Reflection.BindingFlags]'Instance,NonPublic').GetValue($restoredSettings)
    if ($restoredRules.Count -ne 1 -or [Math]::Abs($restoredFallback - 90.0) -gt 1e-8) {
        throw 'Current config with an empty DN table must restore one default rule and preserve fallback gap.'
    }
    $oldJson = '{"Version":0,"Projects":[{"DocumentKey":"project-a","Settings":{"Version":0,"Mode":0,"ClearGapMm":999,"CenterSpacingMm":999,"FallbackClearGapMm":999,"ParallelAngleToleranceDegrees":2,"PreviewBeforeApply":true,"DnRules":[]}}]}'
    $oldSettings = $deserialize.Invoke($null, [object[]]@($oldJson, 'project-a'))
    $oldGap = [double]$settingsType.GetProperty(
        'ClearGapMm',
        [Reflection.BindingFlags]'Instance,NonPublic').GetValue($oldSettings)
    if ([Math]::Abs($oldGap - 75.0) -gt 1e-8) {
        throw 'Unsupported config version must return defaults instead of stale values.'
    }

    $directions = [System.Collections.Generic.List[Autodesk.Revit.DB.XYZ]]::new()
    $directions.Add([Autodesk.Revit.DB.XYZ]::new(1, 0, 0.05))
    $directions.Add([Autodesk.Revit.DB.XYZ]::new(-1, 0, -0.02))
    $axisArguments = [object[]]@(
        $directions,
        [Autodesk.Revit.DB.XYZ]::BasisX,
        [Autodesk.Revit.DB.XYZ]::BasisY,
        0.999,
        $null,
        $null,
        $null)
    if (-not [bool]$tryBuildAxes.Invoke($null, $axisArguments)) {
        throw "Parallel sloped directions were rejected: $($axisArguments[6])"
    }
    $runAxis = [Autodesk.Revit.DB.XYZ]$axisArguments[4]
    $spacingAxis = [Autodesk.Revit.DB.XYZ]$axisArguments[5]
    if ([Math]::Abs($runAxis.Z) -gt 1e-9 -or
        $runAxis.DotProduct([Autodesk.Revit.DB.XYZ]::BasisX) -lt 0.999 -or
        $spacingAxis.DotProduct([Autodesk.Revit.DB.XYZ]::BasisY) -lt 0.999) {
        throw 'Horizontal run must produce +X run and +Y stable spacing axis.'
    }

    $vertical = [System.Collections.Generic.List[Autodesk.Revit.DB.XYZ]]::new()
    $vertical.Add([Autodesk.Revit.DB.XYZ]::BasisZ)
    $verticalArguments = [object[]]@(
        $vertical,
        [Autodesk.Revit.DB.XYZ]::BasisX,
        [Autodesk.Revit.DB.XYZ]::BasisY,
        0.999,
        $null,
        $null,
        $null)
    if ([bool]$tryBuildAxes.Invoke($null, $verticalArguments)) {
        throw 'Near-vertical Pipe direction must be rejected.'
    }

    $mixed = [System.Collections.Generic.List[Autodesk.Revit.DB.XYZ]]::new()
    $mixed.Add([Autodesk.Revit.DB.XYZ]::BasisX)
    $mixed.Add([Autodesk.Revit.DB.XYZ]::BasisY)
    $mixedArguments = [object[]]@(
        $mixed,
        [Autodesk.Revit.DB.XYZ]::BasisX,
        [Autodesk.Revit.DB.XYZ]::BasisY,
        0.999,
        $null,
        $null,
        $null)
    if ([bool]$tryBuildAxes.Invoke($null, $mixedArguments)) {
        throw 'Nonparallel Pipe directions must be rejected.'
    }

    $box = [Autodesk.Revit.DB.BoundingBoxXYZ]::new()
    $box.Min = [Autodesk.Revit.DB.XYZ]::new(-2, -3, -1)
    $box.Max = [Autodesk.Revit.DB.XYZ]::new(4, 5, 2)
    $extent = [double]$projectExtent.Invoke(
        $null,
        [object[]]@($box, [Autodesk.Revit.DB.XYZ]::BasisY))
    if ([Math]::Abs($extent - 8.0) -gt 1e-8) {
        throw "Bounding-box projected extent must be 8. Actual=$extent"
    }

    $analysisPath = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\PipeSpacing\PipeSpacingAnalysisService.cs'
    if (-not (Test-Path -LiteralPath $analysisPath)) {
        throw 'PipeSpacingAnalysisService is missing.'
    }
    $analysisSource = [IO.File]::ReadAllText($analysisPath, [Text.Encoding]::UTF8)
    $odIndex = $analysisSource.IndexOf('RBS_PIPE_OUTER_DIAMETER')
    $geometryFallbackIndex = $analysisSource.IndexOf('ProjectExtent')
    if ($odIndex -lt 0 -or $geometryFallbackIndex -lt 0 -or $odIndex -gt $geometryFallbackIndex) {
        throw 'OD resolution must try RBS_PIPE_OUTER_DIAMETER before geometry fallback.'
    }
    if (-not $analysisSource.Contains('IncludeInsulation') -or
        -not $analysisSource.Contains('IncludeFittingEnvelope')) {
        throw 'Analysis must gate insulation and fitting envelope by settings.'
    }
    if (-not $analysisSource.Contains('Cluster.HasPinnedElement') -or
        -not $analysisSource.Contains('target.IsAnchor')) {
        throw 'Analysis must allow Pin only for the zero-move anchor cluster.'
    }
    if (-not $analysisSource.Contains('scalarPlan.MinimumClearGap < 0.0') -or
        -not $analysisSource.Contains('hasMatchingBand')) {
        throw 'Analysis must warn about overlapping center spacing and DN fallback pairs.'
    }

    Write-Host 'PipeSpacing collector/geometry contract tests passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
