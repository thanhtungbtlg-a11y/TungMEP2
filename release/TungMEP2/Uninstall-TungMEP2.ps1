[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'TungMEP2\Revit2024'
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TungMEP2\Revit2024'))
$resolvedTarget = [IO.Path]::GetFullPath($installRoot)
$addinFile = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\TungMEP2.addin'

if (-not [string]::Equals($resolvedTarget, $expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Tu choi go cai dat vi duong dan dich khong an toan.'
}

if (Test-Path -LiteralPath $addinFile) {
    Remove-Item -LiteralPath $addinFile -Force
}
if (Test-Path -LiteralPath $resolvedTarget) {
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

Write-Host 'Da go TungMEP2 khoi Revit 2024.' -ForegroundColor Green

