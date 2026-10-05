[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'src\TungMEP2\Core\Internal\Ctn\FitSpace'
$paths = @{
    Model = Join-Path $folder 'FitSpaceRevitModel.cs'
    Connectors = Join-Path $folder 'FitSpaceConnectorService.cs'
    Collector = Join-Path $folder 'FitSpaceCollector.cs'
    Geometry = Join-Path $folder 'FitSpaceGeometry.cs'
    Analysis = Join-Path $folder 'FitSpaceAnalysisService.cs'
	Executor = Join-Path $folder 'FitSpaceExecutor.cs'
	Runner = Join-Path $folder 'FitSpaceRunner.cs'
	Command = Join-Path $root 'src\TungMEP2\Core\Commands\CTN\Command_FitSpace.cs'
}

foreach ($item in $paths.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $item.Value)) {
        throw "FitSpace $($item.Key) source is missing: $($item.Value)"
    }
}

$collector = [IO.File]::ReadAllText($paths.Collector, [Text.Encoding]::UTF8)
foreach ($contract in @(
    'TryCollectMany',
    'MechanicalFitting',
    'PartType.Elbow',
    'Domain.DomainPiping',
    'ConnectorType.End',
    'FitSpaceOwnershipPolicy.Classify',
    'OwnedElementIds'
)) {
    if (-not $collector.Contains($contract)) {
        throw "FitSpace collector contract is missing: $contract"
    }
}

$connectorSource = [IO.File]::ReadAllText($paths.Connectors, [Text.Encoding]::UTF8)
foreach ($contract in @('Connector.Id', 'OwnerUniqueId', 'GetPhysicalPipingConnectors', 'FindByKey')) {
    if (-not $connectorSource.Contains($contract)) {
        throw "FitSpace connector contract is missing: $contract"
    }
}
if (-not $connectorSource.Contains('MaximumConnectorAxisDeviationDegrees') -or
	-not $connectorSource.Contains('IsConnectorAxisOpposed')) {
	throw 'FitSpace connector validation must use an explicit angular deviation tolerance.'
}
foreach ($contract in @('OriginalSystemId', 'ConnectorProfileType', 'OriginalRadius', 'OriginalWidth', 'OriginalHeight')) {
	if (-not $connectorSource.Contains($contract) -and -not [IO.File]::ReadAllText($paths.Model, [Text.Encoding]::UTF8).Contains($contract)) {
		throw "FitSpace connector state contract is missing: $contract"
	}
}

$geometry = [IO.File]::ReadAllText($paths.Geometry, [Text.Encoding]::UTF8)
foreach ($contract in @('Line.CreateUnbound', 'TryIntersectUnboundLines', 'TryFindElbowApex', 'FitSpaceMath.TryCalculate', 'IsAlreadyNormalized')) {
    if (-not $geometry.Contains($contract)) {
        throw "FitSpace geometry contract is missing: $contract"
    }
}
if (-not $geometry.Contains('CreateLinePreservingEndpointOrder')) {
	throw 'FitSpace geometry must preserve LocationCurve endpoint order.'
}
if (-not $geometry.Contains('PipeUtilities.GetElbowLengthInternal')) {
	throw 'FitSpace target spacing must use the same ElbowData takeout as E90to45.'
}
if ($geometry.Contains('Select(pipe => pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)')) {
	throw 'FitSpace target spacing must not use nominal Pipe DN as elbow takeout.'
}

$analysis = [IO.File]::ReadAllText($paths.Analysis, [Text.Encoding]::UTF8)
foreach ($contract in @('TryCreatePlan', '.Pinned', '.GroupId', 'CheckoutStatus.OwnedByOtherUser', 'FitSpaceElementSnapshot')) {
    if (-not $analysis.Contains($contract)) {
        throw "FitSpace analysis contract is missing: $contract"
    }
}
if (-not $analysis.Contains('DesignOption.GetActiveDesignOptionId')) {
	throw 'FitSpace analysis must validate the active design option.'
}

foreach ($source in @($collector, $connectorSource, $geometry, $analysis)) {
    if ($source.Contains('new Transaction(') -or $source.Contains('new SubTransaction(')) {
        throw 'FitSpace collection/geometry/analysis must remain read-only.'
    }
}

$executor = [IO.File]::ReadAllText($paths.Executor, [Text.Encoding]::UTF8)
foreach ($contract in @(
	'TransactionGroup(document, "TungMEP2 - FitSpace")',
	'new Transaction(document',
	'ElementTransformUtils.MoveElement',
	'LocationCurve',
	'document.Regenerate()',
	'RollBack()',
	'Assimilate()',
	'ValidateFarEndpoints',
	'ValidateElementIdentity',
	'ValidateConnectorBindings',
	'ValidateOrientation',
	'ValidateTargetApexes',
	'ValidateMinimumLength'
)) {
	if (-not $executor.Contains($contract)) {
		throw "FitSpace executor contract is missing: $contract"
	}
}
foreach ($contract in @(
	'FitSpaceFailurePreprocessor',
	'FitSpacePendingTransactionFinalizer',
	'SetFailuresPreprocessor',
	'SetTransactionFinalizer',
	'SetForcedModalHandling(true)',
	'TransactionStatus.Pending',
	'ArmForPending',
	'ExternalElementSnapshots',
	'CaptureExternalNetworkForExecution',
	'ValidateExternalNetwork',
	'ValidateConnectorState',
	'ValidateBindingGeometry'
)) {
	if (-not $executor.Contains($contract) -and -not $collector.Contains($contract) -and
		-not [IO.File]::ReadAllText($paths.Model, [Text.Encoding]::UTF8).Contains($contract)) {
		throw "FitSpace safety contract is missing: $contract"
	}
}
$captureIndex = $executor.IndexOf('CaptureExternalNetworkForExecution', [StringComparison]::Ordinal)
$executeIndex = $executor.IndexOf('ExecuteCluster(document', [StringComparison]::Ordinal)
if ($captureIndex -lt 0 -or $executeIndex -lt 0 -or $captureIndex -gt $executeIndex) {
	throw 'FitSpace must capture the current external network immediately before mutation.'
}

$mutationSource = $executor + "`n" + $connectorSource
foreach ($contract in @('DisconnectFrom', 'ConnectTo')) {
	if (-not $mutationSource.Contains($contract)) {
		throw "FitSpace mutation implementation is missing: $contract"
	}
}

foreach ($contract in @('TryDisconnect', 'TryReconnect', 'IsConnected')) {
	if (-not $connectorSource.Contains($contract)) {
		throw "FitSpace connector mutation contract is missing: $contract"
	}
}

$fitSpaceSources = ($paths.Values | ForEach-Object {
	if (Test-Path -LiteralPath $_) { [IO.File]::ReadAllText($_, [Text.Encoding]::UTF8) }
}) -join "`n"
foreach ($forbidden in @('Document.Delete', '.CreateElbow(', 'NewElbowFitting', 'Pipe.Create(', 'Elbow45To90', 'Elbow90To45')) {
	if ($fitSpaceSources.Contains($forbidden)) {
		throw "FitSpace must preserve identity; forbidden API/reference found: $forbidden"
	}
}

$runner = [IO.File]::ReadAllText($paths.Runner, [Text.Encoding]::UTF8)
foreach ($contract in @(
	'Selection.GetElementIds()',
	'PickObjects(',
	'ObjectType.Element',
	'OST_PipeFitting',
	'TryCreatePlan',
	'TryExecute',
	'AdjustedCount',
	'AlreadyNormalizedCount',
	'SkippedCount',
	'RolledBackCount'
)) {
	if (-not $runner.Contains($contract)) {
		throw "FitSpace runner contract is missing: $contract"
	}
}
$expectedPrompt = "Ch$([char]0x1ECD)n Pipe gi$([char]0x1EEF)a ho$([char]0x1EB7)c hai elbow c$([char]0x1EA7)n chu$([char]0x1EA9)n h$([char]0x00F3)a kho$([char]0x1EA3)ng c$([char]0x00E1)ch"
if (-not $runner.Contains($expectedPrompt)) {
	throw 'FitSpace runner selection prompt is missing or changed.'
}
if ($runner.IndexOf('TryCreatePlan', [StringComparison]::Ordinal) -gt $runner.IndexOf('TryExecute', [StringComparison]::Ordinal)) {
	throw 'FitSpace must analyze before execution.'
}

$command = [IO.File]::ReadAllText($paths.Command, [Text.Encoding]::UTF8)
foreach ($contract in @('Command_FitSpace : SecuredExternalCommand', 'FitSpaceRunner.Run', 'OperationCanceledException')) {
	if (-not $command.Contains($contract)) {
		throw "FitSpace command contract is missing: $contract"
	}
}

$applicationPath = Join-Path $root 'src\TungMEP2\Core\Application.cs'
$application = [IO.File]::ReadAllText($applicationPath, [Text.Encoding]::UTF8)
foreach ($contract in @(
	'["FitSpace"]',
	'AddPushButton<Command_FitSpace>("FitSpace")',
	'E9045_16.png',
	'E9045_32.png'
)) {
	if (-not $application.Contains($contract)) {
		throw "FitSpace ribbon contract is missing: $contract"
	}
}
$elbow45Index = $application.IndexOf('AddPushButton<Command_Elbow45To90>("E45to90")', [StringComparison]::Ordinal)
$fitSpaceIndex = $application.IndexOf('AddPushButton<Command_FitSpace>("FitSpace")', [StringComparison]::Ordinal)
$pipeLayerIndex = $application.IndexOf('AddPushButton<Command_PipeLayer>("PipeLayer")', [StringComparison]::Ordinal)
if ($elbow45Index -lt 0 -or $fitSpaceIndex -lt $elbow45Index -or $pipeLayerIndex -lt $fitSpaceIndex) {
	throw 'FitSpace ribbon button must be placed after E45to90 and before PipeLayer.'
}

$assemblyPath = Join-Path $root 'build\TungMEP2.dll'
$revitDirectory = 'C:\Program Files\Autodesk\Revit 2024'
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $fileName = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    foreach ($directory in @((Join-Path $root 'build'), (Join-Path $root 'lib'), $revitDirectory)) {
        $candidate = Join-Path $directory $fileName
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}

[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    [Reflection.Assembly]::LoadFrom((Join-Path $revitDirectory 'RevitAPI.dll')) | Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
    $geometryType = $assembly.GetType('VP_RevitAPI.Internal.Ctn.FitSpace.FitSpaceGeometry', $false)
    if ($null -eq $geometryType) { throw 'FitSpaceGeometry type is missing from build.' }
    $method = $geometryType.GetMethod('TryIntersectUnboundLines', [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $method) { throw 'FitSpaceGeometry.TryIntersectUnboundLines is missing.' }

    $lineX = [Autodesk.Revit.DB.Line]::CreateUnbound([Autodesk.Revit.DB.XYZ]::Zero, [Autodesk.Revit.DB.XYZ]::BasisX)
    $lineY = [Autodesk.Revit.DB.Line]::CreateUnbound([Autodesk.Revit.DB.XYZ]::Zero, [Autodesk.Revit.DB.XYZ]::BasisY)
    $arguments = [object[]]@($lineX, $lineY, 1e-6, $null)
    if (-not [bool]$method.Invoke($null, $arguments)) { throw 'Perpendicular unbound lines must intersect.' }
    $point = [Autodesk.Revit.DB.XYZ]$arguments[3]
    if ($point.DistanceTo([Autodesk.Revit.DB.XYZ]::Zero) -gt 1e-8) { throw 'Unbound line intersection must be origin.' }

    $skew = [Autodesk.Revit.DB.Line]::CreateUnbound(
        [Autodesk.Revit.DB.XYZ]::new(0, 0, 1),
        [Autodesk.Revit.DB.XYZ]::BasisY)
    $skewArguments = [object[]]@($lineX, $skew, 1e-6, $null)
    if ([bool]$method.Invoke($null, $skewArguments)) { throw 'Skew lines outside tolerance must be rejected.' }

	$orderMethod = $geometryType.GetMethod('CreateLinePreservingEndpointOrder', [Reflection.BindingFlags]'Static,NonPublic')
	if ($null -eq $orderMethod) { throw 'FitSpace endpoint-order helper is missing.' }
	$original = [Autodesk.Revit.DB.Line]::CreateBound(
		[Autodesk.Revit.DB.XYZ]::new(10, 0, 0),
		[Autodesk.Revit.DB.XYZ]::new(0, 0, 0))
	$orderArguments = [object[]]@(
		$original,
		[Autodesk.Revit.DB.XYZ]::new(0, 0, 0),
		[Autodesk.Revit.DB.XYZ]::new(1, 0, 0),
		[Autodesk.Revit.DB.XYZ]::new(10, 0, 0),
		[Autodesk.Revit.DB.XYZ]::new(9, 0, 0))
	$ordered = [Autodesk.Revit.DB.Line]$orderMethod.Invoke($null, $orderArguments)
	if ($ordered.GetEndPoint(0).DistanceTo([Autodesk.Revit.DB.XYZ]::new(9, 0, 0)) -gt 1e-8 -or
		$ordered.GetEndPoint(1).DistanceTo([Autodesk.Revit.DB.XYZ]::new(1, 0, 0)) -gt 1e-8) {
		throw 'FitSpace target line reversed the original endpoint order.'
	}

    Write-Host 'FitSpace contracts passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
