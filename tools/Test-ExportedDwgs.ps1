[CmdletBinding()]
param(
    [string]$OutputFolder = 'C:\Users\tungnt2303\Desktop\TungMEP2\PrintSheets'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$coreConsole = 'C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe'
$auditScript = Join-Path $PSScriptRoot 'Audit-Dwg-ReadOnly.scr'

if (-not (Test-Path -LiteralPath $coreConsole)) {
    throw "AutoCAD Core Console not found: $coreConsole"
}
if (-not (Test-Path -LiteralPath $auditScript)) {
    throw "Audit script not found: $auditScript"
}
if (-not (Test-Path -LiteralPath $OutputFolder)) {
    throw "DWG output folder not found: $OutputFolder"
}

$drawings = @(
    Get-ChildItem -LiteralPath $OutputFolder -File -Filter '*.dwg' |
        Sort-Object Name
)
if ($drawings.Count -eq 0) {
    throw "No DWG files found in: $OutputFolder"
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$workRoot = Join-Path $root "build\dwg-qc-$runId"
$reportRoot = Join-Path $root 'reports'
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
[IO.Directory]::CreateDirectory($reportRoot) | Out-Null

function Get-AuditMetric {
    param(
        [string]$Text,
        [string]$Name,
        [int]$DefaultValue = -1
    )

    $match = [regex]::Match(
        $Text,
        "(?m)^TM2_QC_$([regex]::Escape($Name))=(\d+)")
    if (-not $match.Success) {
        return $DefaultValue
    }

    return [int]$match.Groups[1].Value
}

$results = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $drawings.Count; $index++) {
    $drawing = $drawings[$index]
    $number = $index + 1
    Write-Host "[$number/$($drawings.Count)] $($drawing.Name)"

    $copyPath = Join-Path $workRoot ('drawing-{0:D3}.dwg' -f $number)
    $logPath = Join-Path $workRoot ('drawing-{0:D3}.log' -f $number)
    $errorLogPath = Join-Path $workRoot ('drawing-{0:D3}.error.log' -f $number)
    Copy-Item -LiteralPath $drawing.FullName -Destination $copyPath
    $copyHashBefore = (Get-FileHash -LiteralPath $copyPath -Algorithm SHA256).Hash

    $arguments = @(
        '/i', $copyPath,
        '/s', $auditScript,
        '/l', 'en-US'
    )
    $process = Start-Process `
        -FilePath $coreConsole `
        -ArgumentList $arguments `
        -RedirectStandardOutput $logPath `
        -RedirectStandardError $errorLogPath `
        -WindowStyle Hidden `
        -PassThru `
        -Wait

    $copyHashAfter = (Get-FileHash -LiteralPath $copyPath -Algorithm SHA256).Hash
    $text = [Text.Encoding]::Unicode.GetString(
        [IO.File]::ReadAllBytes($logPath))
    $errorText = if ((Get-Item -LiteralPath $errorLogPath).Length -gt 0) {
        Get-Content -LiteralPath $errorLogPath -Raw
    }
    else {
        ''
    }

    $auditMatch = [regex]::Match(
        $text,
        'Total errors found\s+(\d+)\s+fixed\s+(\d+)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $auditErrors = if ($auditMatch.Success) {
        [int]$auditMatch.Groups[1].Value
    }
    else {
        -1
    }

    $fontWarnings = [regex]::Matches(
        $text,
        '(?im)^(?=.*(?:font|shape))(?=.*(?:not found|missing|substitut)).*$').Count
    $xrefWarnings = [regex]::Matches(
        $text,
        '(?im)^.*(?:unresolved|not found).*(?:xref|reference).*$').Count
    $proxyWarnings = [regex]::Matches(
        $text,
        '(?im)^.*proxy (?:object|entity).*$').Count
    $scriptErrors = [regex]::Matches($text, '(?im)^;\s*error:').Count

    $paperEntities = Get-AuditMetric $text 'PAPER_ENTITIES'
    $paperText = Get-AuditMetric $text 'PAPER_TEXT'
    $paperViewports = Get-AuditMetric $text 'PAPER_VIEWPORTS'
    $paperInserts = Get-AuditMetric $text 'PAPER_INSERTS'
    $blockEntities = Get-AuditMetric $text 'BLOCK_ENTITIES'
    $blockText = Get-AuditMetric $text 'BLOCK_TEXT'
    $blockLinework = Get-AuditMetric $text 'BLOCK_LINEWORK'
    $blockHatches = Get-AuditMetric $text 'BLOCK_HATCHES'
    $entityLineweights = Get-AuditMetric $text 'EXPLICIT_ENTITY_LINEWEIGHTS'
    $entityLinetypes = Get-AuditMetric $text 'EXPLICIT_ENTITY_LINETYPES'
    $layerLinetypes = Get-AuditMetric $text 'NONCONTINUOUS_LAYERS'
    $layers = Get-AuditMetric $text 'LAYERS'
    $linetypes = Get-AuditMetric $text 'LINETYPES'
    $xrefs = Get-AuditMetric $text 'XREFS'

    $issues = [System.Collections.Generic.List[string]]::new()
    if ($process.ExitCode -ne 0) {
        $issues.Add("CoreConsole exit $($process.ExitCode)")
    }
    if ($auditErrors -ne 0) {
        $issues.Add("AUDIT errors: $auditErrors")
    }
    if ($scriptErrors -ne 0) {
        $issues.Add("QC script errors: $scriptErrors")
    }
    if ($paperEntities -lt 3 -or $paperViewports -lt 1 -or $paperInserts -lt 1) {
        $issues.Add('Paper Space/title-block structure is incomplete')
    }
    if ($blockEntities -lt 10 -or $blockLinework -lt 1 -or $blockText -lt 1) {
        $issues.Add('Exported sheet geometry or text is nearly empty')
    }
    if ($entityLineweights -lt 1 -and $entityLinetypes -lt 1 -and $layerLinetypes -lt 1) {
        $issues.Add('No non-default lineweight/linetype evidence')
    }
    if ($fontWarnings -gt 0) {
        $issues.Add("Font/shape warnings: $fontWarnings")
    }
    if ($xrefWarnings -gt 0) {
        $issues.Add("Unresolved references: $xrefWarnings")
    }
    if ($proxyWarnings -gt 0) {
        $issues.Add("Proxy warnings: $proxyWarnings")
    }
    if ($copyHashBefore -ne $copyHashAfter) {
        $issues.Add('Audit copy changed despite read-only workflow')
    }
    if (-not [string]::IsNullOrWhiteSpace($errorText)) {
        $issues.Add('CoreConsole wrote to stderr')
    }

    $results.Add([pscustomobject]@{
        FileName = $drawing.Name
        Bytes = $drawing.Length
        AuditErrors = $auditErrors
        PaperEntities = $paperEntities
        PaperText = $paperText
        PaperViewports = $paperViewports
        PaperInserts = $paperInserts
        BlockEntities = $blockEntities
        BlockText = $blockText
        BlockLinework = $blockLinework
        BlockHatches = $blockHatches
        ExplicitEntityLineweights = $entityLineweights
        ExplicitEntityLinetypes = $entityLinetypes
        NonContinuousLayers = $layerLinetypes
        Layers = $layers
        Linetypes = $linetypes
        Xrefs = $xrefs
        FontWarnings = $fontWarnings
        XrefWarnings = $xrefWarnings
        ProxyWarnings = $proxyWarnings
        ScriptErrors = $scriptErrors
        CopyHashUnchanged = ($copyHashBefore -eq $copyHashAfter)
        Status = if ($issues.Count -eq 0) { 'PASS' } else { 'REVIEW' }
        Issues = $issues -join '; '
    })

    Remove-Item -LiteralPath $copyPath -Force
}

$csvPath = Join-Path $reportRoot "DWG-QC-$runId.csv"
$jsonPath = Join-Path $reportRoot "DWG-QC-$runId.json"
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$review = @($results | Where-Object Status -ne 'PASS')
$summary = [pscustomobject]@{
    Total = $results.Count
    Passed = $results.Count - $review.Count
    Review = $review.Count
    AuditErrors = ($results | Measure-Object AuditErrors -Sum).Sum
    FontWarnings = ($results | Measure-Object FontWarnings -Sum).Sum
    XrefWarnings = ($results | Measure-Object XrefWarnings -Sum).Sum
    ProxyWarnings = ($results | Measure-Object ProxyWarnings -Sum).Sum
    MinimumPaperInserts = ($results | Measure-Object PaperInserts -Minimum).Minimum
    MinimumPaperViewports = ($results | Measure-Object PaperViewports -Minimum).Minimum
    MinimumBlockText = ($results | Measure-Object BlockText -Minimum).Minimum
    MinimumBlockLinework = ($results | Measure-Object BlockLinework -Minimum).Minimum
    MinimumExplicitLineweights = ($results | Measure-Object ExplicitEntityLineweights -Minimum).Minimum
    CsvReport = $csvPath
    JsonReport = $jsonPath
    WorkLogs = $workRoot
}

$summary | Format-List
if ($review.Count -gt 0) {
    Write-Host 'Files requiring review:' -ForegroundColor Yellow
    $review | Select-Object FileName, Issues | Format-Table -AutoSize -Wrap
}
else {
    Write-Host 'All exported DWG files passed structural QC.' -ForegroundColor Green
}
