[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
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
        'VP_RevitAPI.Helper.FitRotateRotationGeometry',
        $false)
    if ($null -eq $geometryType) {
        throw 'FitRotateRotationGeometry is missing.'
    }

    $canRotate = $geometryType.GetMethod(
        'CanRotateConnectedBranch',
        [Reflection.BindingFlags]'Static,NonPublic')
    if ($null -eq $canRotate) {
        throw 'FitRotateRotationGeometry.CanRotateConnectedBranch is missing.'
    }

    $axisStart = [Autodesk.Revit.DB.XYZ]::new(0, 0, 0)
    $axisEnd = [Autodesk.Revit.DB.XYZ]::new(10, 0, 2)
    $pivot = [Autodesk.Revit.DB.XYZ]::new(5, 0, 1)

    # Existing working case: branch is perpendicular to the axis.
    if (-not [bool]$canRotate.Invoke(
            $null,
            [object[]]@(
                $axisStart,
                $axisEnd,
                $pivot,
                [Autodesk.Revit.DB.XYZ]::new(0, 1, 0),
                1e-6))) {
        throw 'A perpendicular branch on a sloped 3D axis must be supported.'
    }

    # Regression case: drainage branch is oblique to the axis and has slope.
    # FitRotate must not impose a 90-degree condition.
    if (-not [bool]$canRotate.Invoke(
            $null,
            [object[]]@(
                $axisStart,
                $axisEnd,
                $pivot,
                [Autodesk.Revit.DB.XYZ]::new(0.55, 0.80, -0.08),
                1e-6))) {
        throw 'An oblique sloped branch must be supported without a perpendicularity requirement.'
    }

    # The only geometric boundary requirement is that the connection pivot is
    # located on the real 3D rotation axis.
    if ([bool]$canRotate.Invoke(
            $null,
            [object[]]@(
                $axisStart,
                $axisEnd,
                [Autodesk.Revit.DB.XYZ]::new(5, 0.2, 1),
                [Autodesk.Revit.DB.XYZ]::new(0.55, 0.80, -0.08),
                1e-6))) {
        throw 'A branch whose pivot is off the rotation axis must be rejected.'
    }

    $windowSource = [IO.File]::ReadAllText(
        (Join-Path $root 'src\TungMEP2\Core\UI\UI_FitRotateWindow.cs'),
        [Text.Encoding]::UTF8)
    $disconnectIndex = $windowSource.IndexOf('DisconnectBoundaryConnections(boundaryConnections);')
    $rotateIndex = $windowSource.IndexOf('ElementTransformUtils.RotateElements(')
    $reconnectIndex = $windowSource.IndexOf('ReconnectBoundaryConnections(boundaryConnections);')
    if ($disconnectIndex -lt 0 -or
        $rotateIndex -lt 0 -or
        $reconnectIndex -lt 0 -or
        $disconnectIndex -gt $rotateIndex -or
        $reconnectIndex -lt $rotateIndex) {
        throw 'FitRotate must disconnect boundary connectors before rotation and reconnect them afterwards.'
    }

    Write-Host 'FitRotate regression tests passed.' -ForegroundColor Green
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
