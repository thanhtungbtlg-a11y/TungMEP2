[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceBin = Join-Path $PSScriptRoot 'net48'
$sourceAssembly = Join-Path $sourceBin 'TungMEP2.dll'
if (-not (Test-Path -LiteralPath $sourceAssembly)) {
    throw "Package khong hop le: thieu $sourceAssembly"
}

$installBase = Join-Path $env:LOCALAPPDATA 'TungMEP2\Revit2024'
$buildHash = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
$buildId = $buildHash.Substring(0, 12)
$printSheetsAssembly = Join-Path $sourceBin 'TungMEP2.PrintSheets.dll'
if (Test-Path -LiteralPath $printSheetsAssembly) {
    # A dependency-only rollback may leave TungMEP2.dll unchanged. Do not
    # overwrite a previous installation (or a DLL still loaded by Revit).
    $printSheetsHash = (Get-FileHash -LiteralPath $printSheetsAssembly -Algorithm SHA256).Hash
    $buildId += '-' + $printSheetsHash.Substring(0, 12)
}
$installRoot = Join-Path $installBase $buildId
$installBin = Join-Path $installRoot 'net48'
$addinDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$addinFile = Join-Path $addinDirectory 'TungMEP2.addin'

[IO.Directory]::CreateDirectory($installBin) | Out-Null
[IO.Directory]::CreateDirectory($addinDirectory) | Out-Null
Copy-Item -Path (Join-Path $sourceBin '*') -Destination $installBin -Recurse -Force

$assemblyPath = Join-Path $installBin 'TungMEP2.dll'
$escapedAssemblyPath = [Security.SecurityElement]::Escape($assemblyPath)
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>TungMEP2</Name>
    <Assembly>$escapedAssemblyPath</Assembly>
    <AddInId>66CC37B1-88C0-4BCB-85CB-D0F5B4C5B3A3</AddInId>
    <FullClassName>TungMEP2.Application</FullClassName>
    <VendorId>TungMEP</VendorId>
    <VendorDescription>TungMEP2 for Revit 2024</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

[IO.File]::WriteAllText($addinFile, $manifest, [Text.UTF8Encoding]::new($false))

Write-Host 'Da cai TungMEP2 cho Revit 2024.' -ForegroundColor Green
Write-Host "Add-in: $addinFile"
Write-Host "Files : $installRoot"
Write-Host 'Neu Revit dang mo, hay dong va mo lai Revit de nap add-in.'
