$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceY = Join-Path $root 'Store\YData.json'
$sourceElbow = Join-Path $root 'Store\ElbowData.json'
$releaseY = Join-Path $root 'release\TungMEP2\net48\Store\YData.json'
$helper = Join-Path $root 'src\TungMEP2\Core\Helper\PipeUtilities.cs'
$dll = Join-Path $root 'release\TungMEP2\net48\TungMEP2.dll'

foreach ($path in @($sourceY, $sourceElbow, $releaseY, $helper, $dll)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required file: $path"
    }
}

$yText = Get-Content -LiteralPath $sourceY -Raw
$releaseYText = Get-Content -LiteralPath $releaseY -Raw
$elbowText = Get-Content -LiteralPath $sourceElbow -Raw
$helperText = Get-Content -LiteralPath $helper -Raw

foreach ($key in @('"125-125-L"', '"125-125-l"')) {
    if (-not $yText.Contains($key) -or -not $releaseYText.Contains($key)) {
        throw "Missing DN125 YData key: $key"
    }
}
if (-not $elbowText.Contains('"125"')) {
    throw 'Missing DN125 ElbowData.'
}
if (-not $helperText.Contains('CreateElbowWithAutoTransition') -or
    -not $helperText.Contains('TeeMasterWithAutoTransition') -or
    -not $helperText.Contains('NewTransitionFitting')) {
    throw 'Auto-transition implementation is incomplete.'
}

Write-Host 'PASS: DN125 elbow/Y data and automatic elbow/Tee transition fallback are packaged.' -ForegroundColor Green
