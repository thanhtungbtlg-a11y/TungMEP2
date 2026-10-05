[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class TungMepIconRenderer
{
    private static readonly Color Background = Color.FromArgb(255, 20, 31, 50);
    private static readonly Color Border = Color.FromArgb(255, 58, 76, 103);
    private static readonly Color Foreground = Color.White;

    public static void Render(string key, string outputPath, int outputSize)
    {
        using (var canvas = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(4f, 4f);

            Color accent = AccentFor(key);
            using (var tile = RoundedRect(1.5f, 1.5f, 29f, 29f, 6f))
            using (var fill = new SolidBrush(Background))
            using (var outline = new Pen(Border, 1f))
            {
                g.FillPath(fill, tile);
                g.DrawPath(outline, tile);
            }

            using (var accentBrush = new SolidBrush(accent))
                g.FillEllipse(accentBrush, 4f, 4f, 3f, 3f);

            using (var white = new Pen(Foreground, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var color = new Pen(accent, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var whiteBrush = new SolidBrush(Foreground))
            using (var colorBrush = new SolidBrush(accent))
            {
                DrawGlyph(g, key.ToLowerInvariant(), white, color, whiteBrush, colorBrush);
            }

            using (var result = new Bitmap(outputSize, outputSize, PixelFormat.Format32bppArgb))
            using (var target = Graphics.FromImage(result))
            {
                target.CompositingMode = CompositingMode.SourceCopy;
                target.CompositingQuality = CompositingQuality.HighQuality;
                target.InterpolationMode = InterpolationMode.HighQualityBicubic;
                target.SmoothingMode = SmoothingMode.HighQuality;
                target.PixelOffsetMode = PixelOffsetMode.HighQuality;
                target.DrawImage(canvas, new Rectangle(0, 0, outputSize, outputSize), 0, 0, 128, 128, GraphicsUnit.Pixel);
                result.Save(outputPath, ImageFormat.Png);
            }
        }
    }

    private static Color AccentFor(string key)
    {
        key = key.ToLowerInvariant();
        if (key.Contains("sheet") || key.Contains("print") || key.Contains("crop") || key.Contains("title") || key.Contains("lineweight")) return Color.FromArgb(255, 80, 150, 255);
        if (key.Contains("excel")) return Color.FromArgb(255, 52, 190, 126);
        if (key.Contains("vent") || key.Contains("pipe") || key.Contains("ey") || key.Contains("sk") || key.Contains("fitting") || key.Contains("e9045")) return Color.FromArgb(255, 38, 201, 190);
        if (key.Contains("arrow") || key.Contains("plus") || key.Contains("minus")) return Color.FromArgb(255, 255, 169, 64);
        if (key.Contains("sync") || key.Contains("workset") || key.Contains("temporary") || key.Contains("ribbon")) return Color.FromArgb(255, 174, 108, 255);
        if (key.Contains("dis") || key.Contains("avoid") || key.Contains("fix")) return Color.FromArgb(255, 255, 101, 115);
        return Color.FromArgb(255, 52, 202, 255);
    }

    private static void DrawGlyph(Graphics g, string key, Pen w, Pen a, Brush wb, Brush ab)
    {
        switch (key)
        {
            case "align3d":
                Arrow(g, a, 8, 24, 24, 24); Arrow(g, w, 8, 24, 8, 8); g.DrawLine(w, 8, 24, 21, 11); break;
            case "asheets":
                Page(g, w, 6, 8, 10, 16); Page(g, a, 16, 8, 10, 16); g.DrawLine(w, 9, 13, 13, 13); g.DrawLine(a, 19, 13, 23, 13); break;
            case "autoadjusttags":
                g.DrawRectangle(w, 8, 9, 13, 8); g.DrawLine(w, 14, 17, 14, 24); Arrow(g, a, 7, 24, 22, 24); break;
            case "avoidmep":
                g.DrawRectangle(a, 13, 11, 7, 10); g.DrawLines(w, new [] { new PointF(6,16), new PointF(10,16), new PointF(10,7), new PointF(24,7), new PointF(24,24) }); break;
            case "connectto":
                g.DrawLine(w, 6, 16, 12, 16); g.DrawLine(w, 20, 16, 26, 16); g.DrawEllipse(a, 11, 13, 6, 6); g.DrawEllipse(a, 15, 13, 6, 6); break;
            case "createprintset":
                Page(g, w, 7, 7, 13, 18); g.DrawLine(a, 19, 19, 26, 19); g.DrawLine(a, 22.5f, 15.5f, 22.5f, 22.5f); break;
            case "cropviewport":
                Corner(g, w, 7, 7, 1, 1); Corner(g, w, 25, 7, -1, 1); Corner(g, w, 7, 25, 1, -1); Corner(g, w, 25, 25, -1, -1); g.DrawRectangle(a, 10, 10, 12, 12); break;
            case "disvent":
                g.DrawLine(w, 6, 16, 12, 16); g.DrawLine(w, 20, 16, 26, 16); Cross(g, a, 16, 16, 4); break;
            case "down45arrow": Arrow(g, a, 8, 8, 24, 24); break;
            case "down90arrow": Arrow(g, a, 16, 7, 16, 25); break;
            case "dsheets":
                Page(g, a, 10, 7, 13, 17); Page(g, w, 6, 11, 13, 15); break;
            case "dwg":
                g.DrawRectangle(w, 7, 7, 18, 18); g.DrawLine(a, 10, 22, 16, 10); g.DrawLine(a, 16, 10, 22, 22); g.DrawLine(w, 12, 18, 20, 18); break;
            case "e9045":
                g.DrawLines(w, new [] { new PointF(7,23), new PointF(7,10), new PointF(20,10) }); g.DrawLines(a, new [] { new PointF(12,25), new PointF(19,18), new PointF(25,18) }); break;
            case "excel": DrawText(g, "X", wb, 17, 16, 16, FontStyle.Bold); g.DrawRectangle(a, 7, 7, 18, 18); break;
            case "extendfitting":
                g.DrawLine(w, 6, 16, 17, 16); g.DrawEllipse(w, 13, 13, 6, 6); Arrow(g, a, 18, 16, 26, 16); break;
            case "ey":
                g.DrawLine(w, 16, 25, 16, 16); g.DrawLine(a, 16, 16, 8, 8); g.DrawLine(a, 16, 16, 24, 8); break;
            case "fittingrotate":
                g.DrawLine(w, 8, 21, 16, 21); g.DrawLine(w, 16, 21, 16, 12); ArcArrow(g, a, 16, 16, 9); break;
            case "fixpipe":
                g.DrawLine(w, 6, 17, 25, 17); g.DrawEllipse(w, 12, 13, 8, 8); Check(g, a, 15, 17); break;
            case "layerpipe":
                g.DrawLine(w, 7, 10, 25, 10); g.DrawLine(a, 7, 16, 25, 16); g.DrawLine(w, 7, 22, 25, 22); break;
            case "left45arrow": Arrow(g, a, 25, 8, 8, 24); break;
            case "lineweight":
                using (var p1 = new Pen(Foreground, 1f)) using (var p2 = new Pen(Foreground, 2f)) using (var p3 = new Pen(a.Color, 3f)) { g.DrawLine(p1, 7, 9, 25, 9); g.DrawLine(p2, 7, 16, 25, 16); g.DrawLine(p3, 7, 23, 25, 23); } break;
            case "minuss": g.DrawLine(a, 8, 16, 24, 16); break;
            case "pluss": g.DrawLine(a, 8, 16, 24, 16); g.DrawLine(a, 16, 8, 16, 24); break;
            case "printer":
                g.DrawRectangle(w, 9, 6, 14, 8); g.DrawRectangle(a, 7, 13, 18, 10); g.DrawRectangle(w, 10, 19, 12, 7); break;
            case "putviewsinsheet":
                Page(g, w, 6, 6, 20, 21); g.DrawRectangle(a, 9, 10, 6, 5); g.DrawRectangle(a, 17, 10, 6, 5); g.DrawRectangle(a, 9, 18, 14, 5); break;
            case "ribbonicon":
                g.DrawEllipse(w, 9, 9, 14, 14); g.FillEllipse(ab, 13, 13, 6, 6); g.DrawLine(a, 16, 5, 16, 9); g.DrawLine(a, 16, 23, 16, 27); g.DrawLine(a, 5, 16, 9, 16); g.DrawLine(a, 23, 16, 27, 16); break;
            case "right45arrow": Arrow(g, a, 7, 8, 24, 24); break;
            case "savesyncview":
                ArcArrow(g, w, 16, 15, 8); Arrow(g, a, 16, 17, 16, 26); break;
            case "setstt":
                DrawText(g, "#", wb, 16, 16, 16, FontStyle.Bold); g.DrawLine(a, 7, 24, 25, 24); break;
            case "setworkset":
                g.DrawRectangle(w, 7, 8, 14, 6); g.DrawRectangle(w, 9, 14, 14, 6); g.DrawRectangle(w, 11, 20, 14, 5); Check(g, a, 20, 13); break;
            case "sk":
                g.DrawEllipse(w, 7, 9, 14, 14); Arrow(g, a, 8, 16, 20, 16); g.DrawLine(w, 24, 8, 24, 24); break;
            case "syncview":
                ArcArrow(g, w, 16, 16, 9); ArcArrow(g, a, 16, 16, 5); break;
            case "temporaryplane":
                g.DrawPolygon(w, new [] { new PointF(6,19), new PointF(18,8), new PointF(26,14), new PointF(14,25) }); g.DrawLine(a, 10, 13, 22, 20); break;
            case "titlecheck":
                Page(g, w, 7, 6, 18, 21); g.DrawLine(a, 10, 11, 22, 11); Check(g, a, 17, 19); break;
            case "up45arrow": Arrow(g, a, 8, 24, 24, 8); break;
            case "up90arrow": Arrow(g, a, 16, 25, 16, 7); break;
            case "vent":
                g.DrawLine(w, 16, 25, 16, 9); g.DrawLine(w, 10, 25, 22, 25); g.DrawArc(a, 9, 4, 8, 8, 210, 140); g.DrawArc(a, 15, 4, 8, 8, 210, 140); break;
            default:
                DrawText(g, Abbreviation(key), wb, 16, 17, key.Length > 8 ? 8 : 10, FontStyle.Bold); break;
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        float d = radius * 2f; var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + width - d, y, d, d, 270, 90);
        p.AddArc(x + width - d, y + height - d, d, d, 0, 90); p.AddArc(x, y + height - d, d, d, 90, 90); p.CloseFigure(); return p;
    }

    private static void Arrow(Graphics g, Pen p, float x1, float y1, float x2, float y2)
    {
        g.DrawLine(p, x1, y1, x2, y2); float angle = (float)Math.Atan2(y2-y1, x2-x1); float s = 4.5f;
        g.DrawLine(p, x2, y2, x2 - s*(float)Math.Cos(angle-.65), y2 - s*(float)Math.Sin(angle-.65));
        g.DrawLine(p, x2, y2, x2 - s*(float)Math.Cos(angle+.65), y2 - s*(float)Math.Sin(angle+.65));
    }
    private static void ArcArrow(Graphics g, Pen p, float cx, float cy, float radius)
    {
        g.DrawArc(p, cx-radius, cy-radius, radius*2, radius*2, 35, 280); Arrow(g, p, cx-radius*.25f, cy-radius*.97f, cx+radius*.18f, cy-radius*.98f);
    }
    private static void Page(Graphics g, Pen p, float x, float y, float w, float h) { g.DrawRectangle(p, x, y, w, h); g.DrawLine(p, x+w-4, y, x+w, y+4); }
    private static void Corner(Graphics g, Pen p, float x, float y, float sx, float sy) { g.DrawLine(p, x, y, x+sx*6, y); g.DrawLine(p, x, y, x, y+sy*6); }
    private static void Cross(Graphics g, Pen p, float x, float y, float r) { g.DrawLine(p, x-r, y-r, x+r, y+r); g.DrawLine(p, x+r, y-r, x-r, y+r); }
    private static void Check(Graphics g, Pen p, float x, float y) { g.DrawLines(p, new [] { new PointF(x-4,y), new PointF(x-1,y+3), new PointF(x+5,y-4) }); }
    private static string Abbreviation(string key) { string s = key.Replace("_", ""); return s.Length <= 2 ? s.ToUpperInvariant() : s.Substring(0,2).ToUpperInvariant(); }
    private static void DrawText(Graphics g, string text, Brush brush, float cx, float cy, float size, FontStyle style)
    {
        using (var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(text, font, brush, new PointF(cx, cy), format);
    }
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$iconDirectory = Join-Path $root 'src\TungMEP2\resources\icons'
$previewPath = Join-Path $root 'TungMEP2-icons-preview.png'
$files = Get-ChildItem -LiteralPath $iconDirectory -File -Filter '*.png'

foreach ($file in $files) {
    if ($file.BaseName -notmatch '^(?<key>.+?)(?:_?)(?<size>16|32)$') {
        throw "Khong nhan dien duoc kich thuoc icon: $($file.Name)"
    }
    [TungMepIconRenderer]::Render($Matches.key, $file.FullName, [int]$Matches.size)
}

$largeIcons = Get-ChildItem -LiteralPath $iconDirectory -File -Filter '*32.png' | Sort-Object Name
$columns = 6
$cellWidth = 150
$cellHeight = 62
$rows = [Math]::Ceiling($largeIcons.Count / $columns)
$preview = [Drawing.Bitmap]::new($columns * $cellWidth, $rows * $cellHeight)
$graphics = [Drawing.Graphics]::FromImage($preview)
$graphics.Clear([Drawing.Color]::FromArgb(245, 247, 251))
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$font = [Drawing.Font]::new('Segoe UI', 9)
$textBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(30, 40, 58))

for ($i = 0; $i -lt $largeIcons.Count; $i++) {
    $column = $i % $columns
    $row = [Math]::Floor($i / $columns)
    $x = $column * $cellWidth
    $y = $row * $cellHeight
    $image = [Drawing.Image]::FromFile($largeIcons[$i].FullName)
    $graphics.DrawImage($image, $x + 8, $y + 8, 40, 40)
    $graphics.DrawString($largeIcons[$i].BaseName, $font, $textBrush, $x + 54, $y + 19)
    $image.Dispose()
}

$preview.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $font.Dispose(); $textBrush.Dispose(); $preview.Dispose()

Write-Host "Da tao $($files.Count) icon va preview: $previewPath" -ForegroundColor Green
