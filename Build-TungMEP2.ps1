[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\TungMEP2\TungMEP2.csproj'
$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$buildOutput = Join-Path $root 'build'
$packageRoot = Join-Path $root 'release\TungMEP2'
$packageBin = Join-Path $packageRoot 'net48'

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw 'Khong tim thay .NET SDK. Cai .NET 8 SDK hoac giu thu muc .dotnet di kem source.'
    }
    $dotnet = $dotnetCommand.Source
}

$revitApi = 'C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll'
$revitApiUi = 'C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll'
if (-not (Test-Path -LiteralPath $revitApi) -or -not (Test-Path -LiteralPath $revitApiUi)) {
    throw 'Khong tim thay Revit 2024 API tai C:\Program Files\Autodesk\Revit 2024.'
}

& $dotnet build $project -c Release --nologo --verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build that bai voi exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $packageBin) {
    $expectedPackageBin = Join-Path $root 'release\TungMEP2\net48'
    if (-not [string]::Equals($packageBin, $expectedPackageBin, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Duong dan package khong an toan.'
    }
    Remove-Item -LiteralPath $packageBin -Recurse -Force
}

[IO.Directory]::CreateDirectory($packageBin) | Out-Null
Copy-Item -LiteralPath (Join-Path $buildOutput 'TungMEP2.dll') -Destination $packageBin -Force
Copy-Item -Path (Join-Path $root 'lib\*.dll') -Destination $packageBin -Force
Copy-Item -LiteralPath (Join-Path $root 'Store') -Destination $packageBin -Recurse -Force

$mainAssembly = Join-Path $packageBin 'TungMEP2.dll'
$ascii = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($mainAssembly))
$forbidden = @('LicenseGate', 'ActivateLicense', 'license.vplic', 'MachineFingerprint', 'LicenseActivation', 'set_ResourceAssembly')
foreach ($term in $forbidden) {
    if ($ascii.Contains($term)) {
        throw "Build con chua thanh phan kich hoat: $term"
    }
}

$zipPath = Join-Path $root 'TungMEP2-Revit2024.zip'
$expectedZipPath = Join-Path $root 'TungMEP2-Revit2024.zip'
if (-not [string]::Equals($zipPath, $expectedZipPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Duong dan ZIP khong an toan.'
}
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

Write-Host "Build va dong goi thanh cong: $packageRoot" -ForegroundColor Green
Write-Host "ZIP: $zipPath" -ForegroundColor Green
