[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $root 'build\TungMEP2.dll'
$revitDirectory = 'C:\Program Files\Autodesk\Revit 2024'

function Assert-Near {
    param(
        [double]$Actual,
        [double]$Expected,
        [double]$Tolerance,
        [string]$Message
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw "Assertion failed: $Message. Expected=$Expected Actual=$Actual"
    }
}

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

    # Regression contract: an open-open pair must win over a closer occupied
    # pair. Removing that preference would make MC-Align overlap closed
    # networks instead of preserving its existing connect behavior.
    $candidateType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepConnectorPairCandidate',
        $false)
    $policyType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepConnectorAlignmentPolicy',
        $false)
    if ($null -eq $candidateType -or $null -eq $policyType) {
        throw 'Connected-connector pair policy is missing.'
    }
    $choosePair = $policyType.GetMethod(
        'TryChoosePair',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $choosePair) {
        throw 'MepConnectorAlignmentPolicy.TryChoosePair is missing.'
    }

    $openPreferred = [Array]::CreateInstance($candidateType, 2)
    $openPreferred.SetValue(
        [Activator]::CreateInstance(
            $candidateType,
            [object[]]@(0, 0, $true, $true, $true, $true, 1.0)),
        0)
    $openPreferred.SetValue(
        [Activator]::CreateInstance(
            $candidateType,
            [object[]]@(1, 1, $false, $false, $true, $true, 10.0)),
        1)
    $openArguments = [object[]]@(
        [object]$openPreferred,
        $false,
        $true,
        $null
    )
    $openSuccess = [bool]$choosePair.Invoke($null, $openArguments)
    $openDecision = $openArguments[3]
    if (-not $openSuccess -or
        $openDecision.FixedIndex -ne 1 -or
        $openDecision.MovingIndex -ne 1 -or
        -not $openDecision.ShouldConnect -or
        $openDecision.UsesConnectedFallback) {
        throw 'Open-open pair must win over a closer occupied pair.'
    }

    # Regression contract: when no connector is open, the two requested tools
    # may use the occupied pair for geometry, but must not create a connection.
    $occupiedOnly = [Array]::CreateInstance($candidateType, 1)
    $occupiedOnly.SetValue(
        [Activator]::CreateInstance(
            $candidateType,
            [object[]]@(4, 5, $true, $true, $true, $true, 2.0)),
        0)
    $fallbackArguments = [object[]]@(
        [object]$occupiedOnly,
        $false,
        $true,
        $null
    )
    $fallbackSuccess = [bool]$choosePair.Invoke($null, $fallbackArguments)
    $fallbackDecision = $fallbackArguments[3]
    if (-not $fallbackSuccess -or
        $fallbackDecision.FixedIndex -ne 4 -or
        $fallbackDecision.MovingIndex -ne 5 -or
        $fallbackDecision.ShouldConnect -or
        -not $fallbackDecision.UsesConnectedFallback) {
        throw 'Occupied-pair fallback must align without connecting.'
    }

    # MoveConn keeps its legacy requirement for open connectors.
    $legacyArguments = [object[]]@(
        [object]$occupiedOnly,
        $false,
        $false,
        $null
    )
    if ([bool]$choosePair.Invoke($null, $legacyArguments)) {
        throw 'Occupied-pair fallback must stay disabled for legacy MoveConn.'
    }

    # Regression contract: only MC-Align and AlignMEP3D move a connected
    # component; legacy MoveConn keeps moving the selected element only.
    $alignmentModeType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepConnectorAlignmentMode',
        $true)
    $shouldUseCluster = $policyType.GetMethod(
        'ShouldUseConnectedCluster',
        [Reflection.BindingFlags]'Static,NonPublic')
    $shouldCreateConnection = $policyType.GetMethod(
        'ShouldCreateConnection',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $shouldUseCluster -or $null -eq $shouldCreateConnection) {
        throw 'Alignment execution policy is missing.'
    }
    $moveConnectMode = [Enum]::Parse($alignmentModeType, 'MoveConnect')
    $moveConnectAlignMode = [Enum]::Parse(
        $alignmentModeType,
        'MoveConnectAlign')
    $alignIn3DMode = [Enum]::Parse($alignmentModeType, 'AlignIn3D')
    if ([bool]$shouldUseCluster.Invoke($null, @($moveConnectMode)) -or
        -not [bool]$shouldUseCluster.Invoke($null, @($moveConnectAlignMode)) -or
        -not [bool]$shouldUseCluster.Invoke($null, @($alignIn3DMode))) {
        throw 'Connected-cluster transformation must be limited to MC-Align and AlignMEP3D.'
    }
    if (-not [bool]$shouldCreateConnection.Invoke(
            $null,
            @($moveConnectAlignMode, $openDecision)) -or
        [bool]$shouldCreateConnection.Invoke(
            $null,
            @($moveConnectAlignMode, $fallbackDecision)) -or
        [bool]$shouldCreateConnection.Invoke(
            $null,
            @($alignIn3DMode, $openDecision)) -or
        -not [bool]$shouldCreateConnection.Invoke(
            $null,
            @($moveConnectMode, $openDecision))) {
        throw 'Only connect-capable modes with an open-open pair may create a connection.'
    }

    # Break caught: MC-Align must keep endpoint connection as first priority,
    # use Pipe-body insertion only for a closed fixed Pipe plus an open end on
    # the selected moving Pipe, and never enable it for AlignMEP3D.
    $bodyPolicyType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepPipeBodyConnectionPolicy',
        $false)
    if ($null -eq $bodyPolicyType) {
        throw 'Pipe-body connection strategy policy is missing.'
    }
    $chooseStrategy = $bodyPolicyType.GetMethod(
        'ChooseStrategy',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $chooseStrategy) {
        throw 'MepPipeBodyConnectionPolicy.ChooseStrategy is missing.'
    }
    $strategyCases = @(
        @($moveConnectAlignMode, $true,  $true,  $false, $true,  $true,  'Endpoint'),
        @($moveConnectAlignMode, $false, $true,  $false, $true,  $true,  'PipeBody'),
        @($moveConnectAlignMode, $false, $true,  $false, $true,  $false, 'AlignOnly'),
        @($moveConnectAlignMode, $false, $false, $false, $true,  $true,  'AlignOnly'),
        @($moveConnectAlignMode, $false, $true,  $true,  $true,  $true,  'AlignOnly'),
        @($alignIn3DMode,        $true,  $true,  $false, $true,  $true,  'AlignOnly')
    )
    foreach ($case in $strategyCases) {
        $actualStrategy = $chooseStrategy.Invoke(
            $null,
            [object[]]@($case[0], $case[1], $case[2], $case[3], $case[4], $case[5]))
        if ($actualStrategy.ToString() -ne $case[6]) {
            throw "Wrong body strategy. Expected=$($case[6]) Actual=$actualStrategy"
        }
    }

    # Break caught: projection must use the bounded 3D pipe centerline, keep
    # the true elevation on horizontal/vertical/sloped mains, and reject an
    # insertion point inside the protected clearance at either endpoint.
    $bodyGeometryType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepPipeBodyConnectionGeometry',
        $false)
    if ($null -eq $bodyGeometryType) {
        throw 'Pipe-body connection geometry is missing.'
    }
    $projectInterior = $bodyGeometryType.GetMethod(
        'TryProjectInteriorPoint',
        [Reflection.BindingFlags]'Static,NonPublic')
    $areParallel = $bodyGeometryType.GetMethod(
        'AreParallel',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $projectInterior -or $null -eq $areParallel) {
        throw 'Pipe-body projection or direction API is missing.'
    }

    $projectionCases = @(
        @([Autodesk.Revit.DB.XYZ]::new(0,0,0),  [Autodesk.Revit.DB.XYZ]::new(10,0,0),  [Autodesk.Revit.DB.XYZ]::new(4,3,7), [Autodesk.Revit.DB.XYZ]::new(4,0,0)),
        @([Autodesk.Revit.DB.XYZ]::new(0,0,0),  [Autodesk.Revit.DB.XYZ]::new(0,0,10),  [Autodesk.Revit.DB.XYZ]::new(4,3,7), [Autodesk.Revit.DB.XYZ]::new(0,0,7)),
        @([Autodesk.Revit.DB.XYZ]::new(0,0,0),  [Autodesk.Revit.DB.XYZ]::new(10,0,10), [Autodesk.Revit.DB.XYZ]::new(5,3,5), [Autodesk.Revit.DB.XYZ]::new(5,0,5))
    )
    foreach ($case in $projectionCases) {
        $projectionArguments = [object[]]@($case[0], $case[1], $case[2], 0.25, $null)
        if (-not [bool]$projectInterior.Invoke($null, $projectionArguments)) {
            throw 'A valid interior projection was rejected.'
        }
        $actualPoint = [Autodesk.Revit.DB.XYZ]$projectionArguments[4]
        Assert-Near $actualPoint.X $case[3].X 1e-8 'Projected X'
        Assert-Near $actualPoint.Y $case[3].Y 1e-8 'Projected Y'
        Assert-Near $actualPoint.Z $case[3].Z 1e-8 'Projected Z'
    }

    $nearEndArguments = [object[]]@(
        [Autodesk.Revit.DB.XYZ]::new(0,0,0),
        [Autodesk.Revit.DB.XYZ]::new(10,0,0),
        [Autodesk.Revit.DB.XYZ]::new(0.5,3,0),
        1.0,
        $null)
    if ([bool]$projectInterior.Invoke($null, $nearEndArguments)) {
        throw 'Projection inside endpoint clearance must be rejected.'
    }

    foreach ($parallelCase in @(
        @([Autodesk.Revit.DB.XYZ]::BasisX, [Autodesk.Revit.DB.XYZ]::BasisX, $true),
        @([Autodesk.Revit.DB.XYZ]::BasisX, [Autodesk.Revit.DB.XYZ]::BasisX.Negate(), $true),
        @([Autodesk.Revit.DB.XYZ]::BasisX, [Autodesk.Revit.DB.XYZ]::BasisY, $false)
    )) {
        $actualParallel = [bool]$areParallel.Invoke(
            $null,
            [object[]]@($parallelCase[0], $parallelCase[1], 0.999))
        if ($actualParallel -ne $parallelCase[2]) {
            throw "Wrong parallel result. Expected=$($parallelCase[2]) Actual=$actualParallel"
        }
    }

    # Break caught: body planning must deterministically choose the nearest
    # valid open endpoint and execution may commit only after three physical
    # fitting ends report connected.
    $bodyCandidateType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepPipeBodyProjectionCandidate',
        $false)
    $bodyServiceType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepPipeBodyConnectionService',
        $false)
    if ($null -eq $bodyCandidateType -or $null -eq $bodyServiceType) {
        throw 'Pipe-body connection planning service is missing.'
    }
    $chooseBodyCandidate = $bodyServiceType.GetMethod(
        'TryChooseCandidate',
        [Reflection.BindingFlags]'Static,NonPublic')
    $hasThreeConnectedEnds = $bodyServiceType.GetMethod(
        'HasThreeConnectedEnds',
        [Reflection.BindingFlags]'Static,NonPublic')
    $tryCreateBodyPlan = $bodyServiceType.GetMethod(
        'TryCreatePlan',
        [Reflection.BindingFlags]'Static,NonPublic')
    $tryExecuteBodyPlan = $bodyServiceType.GetMethod(
        'TryExecute',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $chooseBodyCandidate -or
        $null -eq $hasThreeConnectedEnds -or
        $null -eq $tryCreateBodyPlan -or
        $null -eq $tryExecuteBodyPlan) {
        throw 'Pipe-body service contract is incomplete.'
    }

    $bodyCandidates = [Array]::CreateInstance($bodyCandidateType, 3)
    $bodyCandidates.SetValue(
        [Activator]::CreateInstance($bodyCandidateType, [object[]]@(
            3,
            [Autodesk.Revit.DB.XYZ]::new(8,2,0),
            [Autodesk.Revit.DB.XYZ]::new(8,0,0),
            2.0)),
        0)
    $bodyCandidates.SetValue(
        [Activator]::CreateInstance($bodyCandidateType, [object[]]@(
            1,
            [Autodesk.Revit.DB.XYZ]::new(4,1,0),
            [Autodesk.Revit.DB.XYZ]::new(4,0,0),
            1.0)),
        1)
    $bodyCandidates.SetValue(
        [Activator]::CreateInstance($bodyCandidateType, [object[]]@(
            2,
            [Autodesk.Revit.DB.XYZ]::new(6,1,0),
            [Autodesk.Revit.DB.XYZ]::new(6,0,0),
            1.0)),
        2)
    $candidateArguments = [object[]]@([object]$bodyCandidates, $null)
    if (-not [bool]$chooseBodyCandidate.Invoke($null, $candidateArguments)) {
        throw 'Nearest valid Pipe-body candidate was not selected.'
    }
    $chosenCandidate = $candidateArguments[1]
    if ($chosenCandidate.ConnectorIndex -ne 1 -or
        [Math]::Abs($chosenCandidate.Distance - 1.0) -gt 1e-8) {
        throw 'Pipe-body candidate selection must use distance then connector index.'
    }

    $emptyCandidates = [Array]::CreateInstance($bodyCandidateType, 0)
    $emptyCandidateArguments = [object[]]@([object]$emptyCandidates, $null)
    if ([bool]$chooseBodyCandidate.Invoke($null, $emptyCandidateArguments)) {
        throw 'Empty Pipe-body candidates must be rejected.'
    }
    if (-not [bool]$hasThreeConnectedEnds.Invoke(
            $null,
            [object[]](,[bool[]]@($true, $true, $true))) -or
        [bool]$hasThreeConnectedEnds.Invoke(
            $null,
            [object[]](,[bool[]]@($true, $true, $false, $false)))) {
        throw 'A Pipe-body junction is valid only when at least three ends are connected.'
    }

    $invalidCreateArguments = [object[]]@($null, $null, $null, $null, $null)
    if ([bool]$tryCreateBodyPlan.Invoke($null, $invalidCreateArguments) -or
        [string]::IsNullOrWhiteSpace([string]$invalidCreateArguments[4])) {
        throw 'TryCreatePlan must reject missing Revit inputs with a useful error.'
    }
    $invalidExecuteArguments = [object[]]@($null, $null, $null, $null, $null)
    if ([bool]$tryExecuteBodyPlan.Invoke($null, $invalidExecuteArguments) -or
        [string]::IsNullOrWhiteSpace([string]$invalidExecuteArguments[4])) {
        throw 'TryExecute must reject missing Revit inputs with a useful error.'
    }

    # Regression contract: connected-cluster traversal must include every
    # reachable element exactly once, even when the connector graph has cycles.
    $collectorType = $assembly.GetType(
        'VP_RevitAPI.Internal.Common.MepConnectedClusterCollector',
        $false)
    if ($null -eq $collectorType) {
        throw 'Connected-cluster collector is missing.'
    }
    $traverseMethod = $collectorType.GetMethod(
        'TryTraverseConnectedIds',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $traverseMethod) {
        throw 'MepConnectedClusterCollector.TryTraverseConnectedIds is missing.'
    }
    $graph = @{
        ([long]10) = [long[]]@(11, 12)
        ([long]11) = [long[]]@(10, 13)
        ([long]12) = [long[]]@(10)
        ([long]13) = [long[]]@(11)
    }
    $getConnectedIds = [Func[long, long[]]]{
        param([long]$elementId)
        if ($graph.ContainsKey($elementId)) {
            return [long[]]$graph[$elementId]
        }
        return [long[]]@()
    }
    $traverseArguments = [object[]]@(
        [long]10,
        [long]99,
        $getConnectedIds,
        $null
    )
    $traverseSuccess = [bool]$traverseMethod.Invoke(
        $null,
        $traverseArguments)
    $actualIds = [long[]]$traverseArguments[3]
    $expectedIds = [long[]]@(10, 11, 12, 13)
    if (-not $traverseSuccess -or
        [string]::Join(',', $actualIds) -ne [string]::Join(',', $expectedIds)) {
        throw 'Connected-cluster traversal must visit each reachable element once.'
    }

    # Regression contract: reaching the fixed element must reject the whole
    # operation before a transaction can alter the model.
    $sameNetworkArguments = [object[]]@(
        [long]10,
        [long]13,
        $getConnectedIds,
        $null
    )
    if ([bool]$traverseMethod.Invoke($null, $sameNetworkArguments)) {
        throw 'A moving cluster containing the fixed element must be rejected.'
    }
    if ([long[]]$sameNetworkArguments[3] -and
        ([long[]]$sameNetworkArguments[3]).Length -ne 0) {
        throw 'Rejected same-network traversal must not return a partial cluster.'
    }

    $geometryType = $assembly.GetType('VP_RevitAPI.Helper.MepAlignmentGeometry', $true)
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $intersectMethod = $geometryType.GetMethod('TryComputeMoveToIntersect', $flags)
    $slopeMethod = $geometryType.GetMethod('TryBuildPerpendicularSameSlopeDirection', $flags)

    $fixedOrigin = [Autodesk.Revit.DB.XYZ]::new(0, 0, 0)
    $fixedDirection = [Autodesk.Revit.DB.XYZ]::BasisX
    $movingOrigin = [Autodesk.Revit.DB.XYZ]::new(1, 1, 1)
    $movingDirection = [Autodesk.Revit.DB.XYZ]::BasisY
    $move = $null
    $intersection = $null
    $arguments = [object[]]@(
        $fixedOrigin,
        $fixedDirection,
        $movingOrigin,
        $movingDirection,
        $move,
        $intersection
    )
    $success = [bool]$intersectMethod.Invoke($null, $arguments)
    if (-not $success) {
        throw 'Assertion failed: skew lines should be alignable.'
    }
    $move = [Autodesk.Revit.DB.XYZ]$arguments[4]
    $intersection = [Autodesk.Revit.DB.XYZ]$arguments[5]
    Assert-Near $move.X 0 1e-8 'Move X'
    Assert-Near $move.Y 0 1e-8 'Move Y'
    Assert-Near $move.Z -1 1e-8 'Move Z'
    Assert-Near $intersection.X 1 1e-8 'Intersection X'
    Assert-Near $intersection.Y 0 1e-8 'Intersection Y'
    Assert-Near $intersection.Z 0 1e-8 'Intersection Z'

    $mainDirection = [Autodesk.Revit.DB.XYZ]::new(1, 0, 0.1)
    $branchDirection = [Autodesk.Revit.DB.XYZ]::new(0.2, 1, 0.05)
    $targetDirection = $null
    $slopeArguments = [object[]]@($mainDirection, $branchDirection, $targetDirection)
    $success = [bool]$slopeMethod.Invoke($null, $slopeArguments)
    if (-not $success) {
        throw 'Assertion failed: sloped main should produce a branch direction.'
    }
    $targetDirection = [Autodesk.Revit.DB.XYZ]$slopeArguments[2]
    $mainPlan = [Autodesk.Revit.DB.XYZ]::new($mainDirection.X, $mainDirection.Y, 0)
    $targetPlan = [Autodesk.Revit.DB.XYZ]::new($targetDirection.X, $targetDirection.Y, 0)
    Assert-Near ($mainPlan.Normalize().DotProduct($targetPlan.Normalize())) 0 1e-8 'Plan perpendicularity'
    $mainSlope = [Math]::Abs($mainDirection.Z / [Math]::Sqrt($mainDirection.X * $mainDirection.X + $mainDirection.Y * $mainDirection.Y))
    $targetSlope = [Math]::Abs($targetDirection.Z / [Math]::Sqrt($targetDirection.X * $targetDirection.X + $targetDirection.Y * $targetDirection.Y))
    Assert-Near $targetSlope $mainSlope 1e-8 'Slope preservation'

    $applicationSource = [IO.File]::ReadAllText(
        (Join-Path $root 'src\TungMEP2\Core\Application.cs'),
        [Text.Encoding]::UTF8)
    foreach ($commandName in @(
        'Command_MoveConnect',
        'Command_MoveConnectAlign',
        'Command_AlignMep3D',
        'Command_AlignBranch',
        'Command_AlignBranchPlus'
    )) {
        if (-not $applicationSource.Contains("AddPushButton<$commandName>")) {
            throw "Assertion failed: Ribbon is missing $commandName"
        }
    }

    $runnerSource = [IO.File]::ReadAllText(
        (Join-Path $root 'src\TungMEP2\Core\Internal\Common\MepConnectorAlignmentRunner.cs'),
        [Text.Encoding]::UTF8)
    if (-not $runnerSource.Contains('element is MEPCurve')) {
        throw 'Assertion failed: connector alignment selection must support Pipe/MEPCurve.'
    }

    Write-Host 'MEP alignment tests passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
