[CmdletBinding()]
param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) { $AssemblyPath = Join-Path $root 'build\TungMEP2.dll' }
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directories = @((Split-Path -Parent $AssemblyPath), (Join-Path $root 'lib'), 'C:\Program Files\Autodesk\Revit 2024')
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = [Reflection.AssemblyName]::new($eventArgs.Name).Name + '.dll'
    foreach ($directory in $directories) {
        $candidate = Join-Path $directory $file
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($candidate) }
    }
    try { return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name) } catch { return $null }
}
$opCodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $op = $field.GetValue($null); $opCodes[[int]$op.Value] = $op
}
function Get-Instructions {
    param([Reflection.MethodBase]$Method)
    $body = $Method.GetMethodBody()
    if ($null -eq $body) { return }
    $il = $body.GetILAsByteArray(); $offset = 0
    while ($offset -lt $il.Length) {
        $value = [int]$il[$offset++]
        if ($value -eq 254) { $value = ([int]65024 -bor [int]$il[$offset++]) - 65536 }
        $op = $opCodes[$value]
        if ($null -eq $op) { throw 'Unknown IL instruction.' }
        switch ($op.OperandType.ToString()) {
            'InlineNone' { $size = 0 }
            'ShortInlineBrTarget' { $size = 1 }
            'ShortInlineI' { $size = 1 }
            'ShortInlineVar' { $size = 1 }
            'InlineVar' { $size = 2 }
            'InlineI8' { $size = 8 }
            'InlineR' { $size = 8 }
            'InlineSwitch' { $size = 4 + 4 * [BitConverter]::ToInt32($il, $offset) }
            default { $size = 4 }
        }
        $called = $null
        if ($op.OperandType.ToString() -eq 'InlineMethod') {
            $methodArgs = [Type[]]@()
            if ($Method -is [Reflection.MethodInfo]) { $methodArgs = $Method.GetGenericArguments() }
            $called = $Method.Module.ResolveMethod([BitConverter]::ToInt32($il, $offset), $Method.DeclaringType.GetGenericArguments(), $methodArgs)
        }
        [pscustomobject]@{ Op = $op.Name; Called = $called }
        $offset += $size
    }
}
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($AssemblyPath)
    $command = $assembly.GetType('VP_RevitAPI.Commands.CTN.Command_Auto90', $true)
    if (@($command.GetInterfaces().FullName) -notcontains 'Autodesk.Revit.UI.IExternalCommand') { throw 'Auto90 is not a native external command.' }
    $flags = [Reflection.BindingFlags]'NonPublic,Instance'
    $calls = @(Get-Instructions ($command.GetMethod('ExecuteCore', $flags)) | Where-Object { $null -ne $_.Called })
    if (@($calls | Where-Object { $_.Called.Name -eq 'Run' -and $_.Called.DeclaringType.FullName -eq 'VP_RevitAPI.Internal.Ctn.Auto90Runner' }).Count -ne 1) { throw 'Command does not dispatch once to Auto90Runner.' }
    $runner = $assembly.GetType('VP_RevitAPI.Internal.Ctn.Auto90Runner', $true)
    $instructions = @(Get-Instructions ($runner.GetMethod('Run', [Reflection.BindingFlags]'NonPublic,Static')))
    $nativeCalls = 0
    for ($i = 0; $i -lt $instructions.Count; $i++) {
        $called = $instructions[$i].Called
        if ($null -ne $called -and $called.Name -eq 'TryExecute' -and $called.DeclaringType.FullName -eq 'VP_RevitAPI.Internal.Ctn.DirectY.DirectYAutoYRunner') {
            $nativeCalls++
            if ($i -lt 1 -or $instructions[$i-1].Op -ne 'ldc.i4.1') { throw 'Auto90 dispatch must explicitly enable perpendicular Tee mode.' }
        }
    }
    if ($nativeCalls -ne 1) { throw 'Auto90 must use the native DirectY insertion/cleanup pipeline once.' }
    # The manifest entry point inherits the ribbon implementation; private methods are not inherited.
    $entryPoint = $assembly.GetType('TungMEP2.Application', $true)
    $application = $assembly.GetType('VP_RevitAPI.Application', $true)
    if ($entryPoint.BaseType -ne $application) { throw 'Manifest entry point does not inherit the verified ribbon implementation.' }
    $ribbonCalls = @(Get-Instructions ($application.GetMethod('CreateRibbon', $flags)) | Where-Object { $null -ne $_.Called -and $_.Called.IsGenericMethod })
    if (@($ribbonCalls | Where-Object { $_.Called.Name -eq 'AddPushButton' -and @($_.Called.GetGenericArguments().FullName) -contains $command.FullName }).Count -ne 1) { throw 'Auto90 is not wired once into the real ribbon.' }
    Write-Host 'Auto90 native command, ribbon dispatch and explicit Tee90 pipeline mode verified (metadata only).' -ForegroundColor Green
}
finally { [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolver) }
