[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installer = Join-Path (Split-Path -Parent $PSScriptRoot) 'release\TungMEP2\Install-TungMEP2.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TungMEP2-Installer-' + [Guid]::NewGuid().ToString('N'))
$originalLocalAppData = $env:LOCALAPPDATA
$originalAppData = $env:APPDATA
try {
    $package = Join-Path $testRoot 'package'
    $bin = Join-Path $package 'net48'
    [IO.Directory]::CreateDirectory($bin) | Out-Null
    Copy-Item -LiteralPath $installer -Destination $package
    $env:LOCALAPPDATA = Join-Path $testRoot 'local'
    $env:APPDATA = Join-Path $testRoot 'roaming'
    $fixtureMain = Join-Path $bin 'TungMEP2.dll'
    $fixtureEngine = Join-Path $bin 'TungMEP2.PrintSheets.dll'
    [IO.File]::WriteAllBytes($fixtureMain, [byte[]](1, 2, 3, 4))
    [IO.File]::WriteAllBytes($fixtureEngine, [byte[]](10, 20, 30))
    & (Join-Path $package 'Install-TungMEP2.ps1') | Out-Null
    $manifestPath = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\TungMEP2.addin'
    [xml]$firstManifest = Get-Content -LiteralPath $manifestPath -Raw
    $firstPath = [string]$firstManifest.RevitAddIns.AddIn.Assembly
    $firstEngine = Join-Path (Split-Path -Parent $firstPath) 'TungMEP2.PrintSheets.dll'
    $originalHash = (Get-FileHash -LiteralPath $firstEngine).Hash

    # Replacing only the external print engine must create another install
    # directory and preserve the first DLL for rollback / an already open host.
    [IO.File]::WriteAllBytes($fixtureEngine, [byte[]](40, 50, 60))
    & (Join-Path $package 'Install-TungMEP2.ps1') | Out-Null
    [xml]$secondManifest = Get-Content -LiteralPath $manifestPath -Raw
    $secondPath = [string]$secondManifest.RevitAddIns.AddIn.Assembly
    if ($firstPath -eq $secondPath) { throw 'Changing PrintSheets alone reuses the old installation directory.' }
    if ((Get-FileHash -LiteralPath $firstEngine).Hash -ne $originalHash) { throw 'The earlier print engine was overwritten.' }
    $secondEngine = Join-Path (Split-Path -Parent $secondPath) 'TungMEP2.PrintSheets.dll'
    if ((Get-FileHash -LiteralPath $secondEngine).Hash -ne (Get-FileHash -LiteralPath $fixtureEngine).Hash) {
        throw 'The manifest points to an installation with the wrong print engine.'
    }
    & (Join-Path $package 'Install-TungMEP2.ps1') | Out-Null
    [xml]$repeatManifest = Get-Content -LiteralPath $manifestPath -Raw
    if ([string]$repeatManifest.RevitAddIns.AddIn.Assembly -ne $secondPath) { throw 'Reinstalling an identical payload must be stable.' }
    Write-Host 'Installer preserves both builds when only PrintSheets changes; repeat install is stable.' -ForegroundColor Green
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $env:APPDATA = $originalAppData
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot).StartsWith('TungMEP2-Installer-', [StringComparison]::Ordinal)) {
        if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
    }
}
