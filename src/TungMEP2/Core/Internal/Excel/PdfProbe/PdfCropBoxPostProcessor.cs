using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace VP_RevitAPI.Internal.Excel.PdfProbe;

internal static class PdfCropBoxPostProcessor
{
	private readonly record struct ContentBounds(double Left, double Bottom, double Right, double Top);

	private const double PaddingPoint = 6.0;

	private const double MinimumEdgePoint = 36.0;

	public static PdfCropResult TryCropToDefinedContent(string pdfPath)
	{
		if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
		{
			return PdfCropResult.Skipped("Crop=Skipped(FileNotFound)");
		}
		try
		{
			if (!TryMeasureContentBounds(pdfPath, out ContentBounds bounds, out string measureInfo))
			{
				return PdfCropResult.Skipped("Crop=Skipped(" + measureInfo + ")");
			}
			string tempPath = Path.Combine(Path.GetDirectoryName(pdfPath) ?? Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(pdfPath) + ".crop.tmp.pdf");
			using (PdfSharpCore.Pdf.PdfDocument document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
			{
				if (document.PageCount < 1)
				{
					return PdfCropResult.Skipped("Crop=Skipped(EmptyPdf)");
				}
				PdfPage page = document.Pages[0];
				double pageWidth = page.Width.Point;
				double pageHeight = page.Height.Point;
				double left = Clamp(bounds.Left - 6.0, 0.0, pageWidth - 36.0);
				double right = Clamp(bounds.Right + 6.0, left + 36.0, pageWidth);
				double bottom = Clamp(bounds.Bottom - 6.0, 0.0, pageHeight - 36.0);
				double top = Clamp(bounds.Top + 6.0, bottom + 36.0, pageHeight);
				PdfSharpCore.Pdf.PdfRectangle rect = (page.CropBox = new PdfSharpCore.Pdf.PdfRectangle(new XRect(left, bottom, right - left, top - bottom)));
				page.TrimBox = rect;
				document.Save(tempPath);
			}
			File.Copy(tempPath, pdfPath, overwrite: true);
			File.Delete(tempPath);
			return PdfCropResult.Applied($"Crop=Applied(L={bounds.Left:F2},B={bounds.Bottom:F2},R={bounds.Right:F2},T={bounds.Top:F2})");
		}
		catch (Exception ex)
		{
			return PdfCropResult.Skipped("Crop=Skipped(" + ex.Message + ")");
		}
	}

	private static bool TryMeasureContentBounds(string pdfPath, out ContentBounds bounds, out string info)
	{
		using UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
		Page page = document.GetPage(1);
		double pageWidth = page.Width;
		double pageHeight = page.Height;
		double left = double.MaxValue;
		double right = double.MinValue;
		double bottom = double.MaxValue;
		double top = double.MinValue;
		bool hasContent = false;
		int letterCount = 0;
		foreach (Letter letter in page.Letters)
		{
			if (TryIncludeRectangle(letter.GlyphRectangle.Left, letter.GlyphRectangle.Bottom, letter.GlyphRectangle.Right, letter.GlyphRectangle.Top, ref left, ref bottom, ref right, ref top))
			{
				hasContent = true;
				letterCount++;
			}
		}
		int pathCount = 0;
		foreach (PdfPath path in page.ExperimentalAccess.Paths)
		{
			if (path.IsClipping)
			{
				continue;
			}
			UglyToad.PdfPig.Core.PdfRectangle? pathBounds = path.GetBoundingRectangle();
			if (pathBounds.HasValue)
			{
				double pathLeft = pathBounds.Value.Left;
				double pathBottom = pathBounds.Value.Bottom;
				double pathRight = pathBounds.Value.Right;
				double pathTop = pathBounds.Value.Top;
				if (!IsAlmostFullPage(pathLeft, pathBottom, pathRight, pathTop, pageWidth, pageHeight) && TryIncludeRectangle(pathLeft, pathBottom, pathRight, pathTop, ref left, ref bottom, ref right, ref top))
				{
					hasContent = true;
					pathCount++;
				}
			}
		}
		int imageCount = 0;
		foreach (IPdfImage image in page.GetImages())
		{
			UglyToad.PdfPig.Core.PdfRectangle imageBounds = image.Bounds;
			if (!IsAlmostFullPage(imageBounds.Left, imageBounds.Bottom, imageBounds.Right, imageBounds.Top, pageWidth, pageHeight) && TryIncludeRectangle(imageBounds.Left, imageBounds.Bottom, imageBounds.Right, imageBounds.Top, ref left, ref bottom, ref right, ref top))
			{
				hasContent = true;
				imageCount++;
			}
		}
		if (!hasContent)
		{
			bounds = default(ContentBounds);
			info = "NoRenderableContent";
			return false;
		}
		if (right - left < 36.0 || top - bottom < 36.0)
		{
			bounds = default(ContentBounds);
			info = "ContentTooSmall";
			return false;
		}
		bounds = new ContentBounds(left, bottom, right, top);
		info = $"Ok(Text={letterCount},Paths={pathCount},Images={imageCount})";
		return true;
	}

	private static bool TryIncludeRectangle(double candidateLeft, double candidateBottom, double candidateRight, double candidateTop, ref double left, ref double bottom, ref double right, ref double top)
	{
		if (double.IsNaN(candidateLeft) || double.IsNaN(candidateBottom) || double.IsNaN(candidateRight) || double.IsNaN(candidateTop) || double.IsInfinity(candidateLeft) || double.IsInfinity(candidateBottom) || double.IsInfinity(candidateRight) || double.IsInfinity(candidateTop) || candidateRight <= candidateLeft || candidateTop <= candidateBottom)
		{
			return false;
		}
		left = Math.Min(left, candidateLeft);
		bottom = Math.Min(bottom, candidateBottom);
		right = Math.Max(right, candidateRight);
		top = Math.Max(top, candidateTop);
		return true;
	}

	private static bool IsAlmostFullPage(double left, double bottom, double right, double top, double pageWidth, double pageHeight)
	{
		double width = right - left;
		double height = top - bottom;
		if (width <= 0.0 || height <= 0.0)
		{
			return false;
		}
		return width >= pageWidth * 0.98 && height >= pageHeight * 0.98;
	}

	private static double Clamp(double value, double min, double max)
	{
		return Math.Max(min, Math.Min(max, value));
	}
}
