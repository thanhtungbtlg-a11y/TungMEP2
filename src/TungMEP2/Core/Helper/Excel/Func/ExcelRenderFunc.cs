using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel.Func;

internal static class ExcelRenderFunc
{
	private sealed class PageSetupSnapshot
	{
		private bool _hasPrintArea;

		private string? _printArea;

		private bool _hasPaperSize;

		private XlPaperSize _paperSize;

		private bool _hasOrientation;

		private XlPageOrientation _orientation;

		private bool _hasZoom;

		private object? _zoom;

		private bool _hasFitToPagesWide;

		private object? _fitToPagesWide;

		private bool _hasFitToPagesTall;

		private object? _fitToPagesTall;

		private bool _hasPrintGridlines;

		private bool _printGridlines;

		private bool _hasLeftMargin;

		private double _leftMargin;

		private bool _hasRightMargin;

		private double _rightMargin;

		private bool _hasTopMargin;

		private double _topMargin;

		private bool _hasBottomMargin;

		private double _bottomMargin;

		private bool _hasHeaderMargin;

		private double _headerMargin;

		private bool _hasFooterMargin;

		private double _footerMargin;

		public void Capture(PageSetup pageSetup)
		{
			_hasPrintArea = TryGet(() => pageSetup.PrintArea, out _printArea);
			_hasPaperSize = TryGet(() => pageSetup.PaperSize, out _paperSize);
			_hasOrientation = TryGet(() => pageSetup.Orientation, out _orientation);
			_hasZoom = TryGet(() => pageSetup.Zoom, out _zoom);
			_hasFitToPagesWide = TryGet(() => pageSetup.FitToPagesWide, out _fitToPagesWide);
			_hasFitToPagesTall = TryGet(() => pageSetup.FitToPagesTall, out _fitToPagesTall);
			_hasPrintGridlines = TryGet(() => pageSetup.PrintGridlines, out _printGridlines);
			_hasLeftMargin = TryGet(() => pageSetup.LeftMargin, out _leftMargin);
			_hasRightMargin = TryGet(() => pageSetup.RightMargin, out _rightMargin);
			_hasTopMargin = TryGet(() => pageSetup.TopMargin, out _topMargin);
			_hasBottomMargin = TryGet(() => pageSetup.BottomMargin, out _bottomMargin);
			_hasHeaderMargin = TryGet(() => pageSetup.HeaderMargin, out _headerMargin);
			_hasFooterMargin = TryGet(() => pageSetup.FooterMargin, out _footerMargin);
		}

		public void Restore(PageSetup pageSetup)
		{
			if (_hasPrintArea)
			{
				TrySet(delegate
				{
					pageSetup.PrintArea = _printArea;
				});
			}
			if (_hasPaperSize)
			{
				TrySet(delegate
				{
					pageSetup.PaperSize = _paperSize;
				});
			}
			if (_hasOrientation)
			{
				TrySet(delegate
				{
					pageSetup.Orientation = _orientation;
				});
			}
			if (_hasZoom)
			{
				TrySet(delegate
				{
					pageSetup.Zoom = _zoom;
				});
			}
			if (_hasFitToPagesWide)
			{
				TrySet(delegate
				{
					pageSetup.FitToPagesWide = _fitToPagesWide;
				});
			}
			if (_hasFitToPagesTall)
			{
				TrySet(delegate
				{
					pageSetup.FitToPagesTall = _fitToPagesTall;
				});
			}
			if (_hasPrintGridlines)
			{
				TrySet(delegate
				{
					pageSetup.PrintGridlines = _printGridlines;
				});
			}
			if (_hasLeftMargin)
			{
				TrySet(delegate
				{
					pageSetup.LeftMargin = _leftMargin;
				});
			}
			if (_hasRightMargin)
			{
				TrySet(delegate
				{
					pageSetup.RightMargin = _rightMargin;
				});
			}
			if (_hasTopMargin)
			{
				TrySet(delegate
				{
					pageSetup.TopMargin = _topMargin;
				});
			}
			if (_hasBottomMargin)
			{
				TrySet(delegate
				{
					pageSetup.BottomMargin = _bottomMargin;
				});
			}
			if (_hasHeaderMargin)
			{
				TrySet(delegate
				{
					pageSetup.HeaderMargin = _headerMargin;
				});
			}
			if (_hasFooterMargin)
			{
				TrySet(delegate
				{
					pageSetup.FooterMargin = _footerMargin;
				});
			}
		}

		private static bool TryGet<T>(Func<T> getter, out T value)
		{
			try
			{
				value = getter();
				return true;
			}
			catch
			{
				value = default(T);
				return false;
			}
		}

		private static void TrySet(System.Action setter)
		{
			try
			{
				setter();
			}
			catch
			{
			}
		}
	}

	private const byte MinVisibleAlpha = 8;

	private const double AutoCropContentThreshold = 18.0;

	private const int AutoCropPaddingPx = 3;

	private const string KeepPdfPreviewEnvVar = "VP_EXCEL_KEEP_PDF_PREVIEW";

	private const string PdfPreviewLayoutEnvVar = "VP_EXCEL_PDF_PREVIEW_LAYOUT";

	private const double NarrowMarginLeftRightInch = 0.25;

	private const double NarrowMarginTopBottomInch = 0.75;

	private const double NarrowMarginHeaderFooterInch = 0.3;

	private const double TightMarginLeftRightInch = 0.05;

	private const double TightMarginTopBottomInch = 0.05;

	private const double TightMarginHeaderFooterInch = 0.05;

	private const int PdfPreviewPaperSizeCode = 112;

	private static readonly XlPaperSize PdfPreviewPaperSize = (XlPaperSize)112;

	internal static string PdfPreviewModeToken => ShouldKeepPdfPreviewAlongsideImage() ? "Enabled" : "Disabled";

	internal static string PdfPageSetupModeToken => IsPdfTightCropEnabled() ? "A3TightFit" : "A4NarrowFit";

	public static bool RenderRangeToJpegByXps(Worksheet worksheet, Range range, string outputPath, out string error)
	{
		PageSetup pageSetup = null;
		PageSetupSnapshot pageSetupSnapshot = new PageSetupSnapshot();
		string tempXpsPath = Path.Combine(Path.GetTempPath(), $"vp_excel_export_{Guid.NewGuid():N}.xps");
		string currentStage = "Init";
		bool keepPdfPreviewAlongsideImage = false;
		bool selectionPaperSizeApplied = false;
		bool previewPaperSizeApplied = false;
		XlPageOrientation selectionOrientation = XlPageOrientation.xlPortrait;
		XlPageOrientation previewOrientation = XlPageOrientation.xlPortrait;
		error = string.Empty;
		try
		{
			currentStage = "AcquirePageSetup";
			pageSetup = worksheet.PageSetup;
			pageSetupSnapshot.Capture(pageSetup);
			currentStage = "ResolvePrintArea";
			string targetPrintArea = range.get_Address((object)false, (object)false, XlReferenceStyle.xlA1, Type.Missing, Type.Missing);
			currentStage = "ApplySelectionPageSetup";
			selectionPaperSizeApplied = ApplySelectionPageSetup(worksheet, range, pageSetup, targetPrintArea, out selectionOrientation);
			currentStage = "PrepareOutputDirectory";
			string outputDirectory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
			}
			currentStage = "ResolvePreviewMode";
			keepPdfPreviewAlongsideImage = ShouldKeepPdfPreviewAlongsideImage();
			if (keepPdfPreviewAlongsideImage)
			{
				currentStage = "ApplyPdfPreviewPageSetup";
				previewPaperSizeApplied = ApplyPdfPreviewPageSetup(worksheet, range, pageSetup, targetPrintArea, out previewOrientation);
				currentStage = "ExportPdfPreview";
				TryExportPdfPreview(worksheet, outputPath);
				currentStage = "ReapplySelectionPageSetup";
				selectionPaperSizeApplied = ApplySelectionPageSetup(worksheet, range, pageSetup, targetPrintArea, out selectionOrientation);
			}
			else
			{
				currentStage = "DeletePdfPreview";
				DeletePdfPreviewIfExists(outputPath);
			}
			currentStage = "ExportXps";
			worksheet.ExportAsFixedFormat(XlFixedFormatType.xlTypeXPS, tempXpsPath, XlFixedFormatQuality.xlQualityStandard, true, false, Type.Missing, Type.Missing, false, Type.Missing);
			if (!File.Exists(tempXpsPath))
			{
				error = BuildFailure("XPS fallback did not generate output file.");
				return false;
			}
			currentStage = "OpenXpsDocument";
			using XpsDocument xpsDocument = new XpsDocument(tempXpsPath, FileAccess.Read);
			currentStage = "ReadFixedDocumentSequence";
			FixedDocumentSequence fixedDocumentSequence = xpsDocument.GetFixedDocumentSequence();
			if (fixedDocumentSequence == null)
			{
				error = BuildFailure("XPS fallback produced no fixed document sequence.");
				return false;
			}
			currentStage = "ReadDocumentPaginator";
			DocumentPaginator paginator = fixedDocumentSequence.DocumentPaginator;
			if (paginator == null)
			{
				error = BuildFailure("XPS fallback cannot access document paginator.");
				return false;
			}
			currentStage = "ComputePageCount";
			paginator.ComputePageCount();
			if (paginator.PageCount < 1)
			{
				error = BuildFailure("XPS fallback produced no pages.");
				return false;
			}
			currentStage = "GetFirstPage";
			DocumentPage page = paginator.GetPage(0);
			if (page.Visual == null)
			{
				error = BuildFailure("XPS fallback page has no visual content.");
				return false;
			}
			Size pageSize = page.Size;
			double widthDip = ((pageSize.Width > 0.0) ? pageSize.Width : 816.0);
			double heightDip = ((pageSize.Height > 0.0) ? pageSize.Height : 1056.0);
			int pixelWidth = Math.Max(1, (int)Math.Round(widthDip * 600.0 / 96.0));
			int pixelHeight = Math.Max(1, (int)Math.Round(heightDip * 600.0 / 96.0));
			currentStage = "RenderPageToBitmap";
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext context = drawingVisual.RenderOpen())
			{
				context.DrawRectangle(Brushes.White, null, new Rect(0.0, 0.0, widthDip, heightDip));
				VisualBrush visualBrush = new VisualBrush(page.Visual)
				{
					Stretch = Stretch.Fill
				};
				context.DrawRectangle(visualBrush, null, new Rect(0.0, 0.0, widthDip, heightDip));
			}
			RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 600.0, 600.0, PixelFormats.Pbgra32);
			bitmap.Render(drawingVisual);
			currentStage = "AutoCropBitmap";
			BitmapSource croppedBitmap;
			BitmapSource outputBitmap = (TryAutoCropBitmap(bitmap, out croppedBitmap) ? croppedBitmap : bitmap);
			currentStage = "SavePng";
			PngBitmapEncoder encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(outputBitmap));
			using FileStream stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
			encoder.Save(stream);
			return true;
		}
		catch (Exception ex)
		{
			error = BuildFailure(ex.GetType().Name + ": " + ex.Message, ex);
			return false;
		}
		finally
		{
			if (pageSetup != null)
			{
				try
				{
					pageSetupSnapshot.Restore(pageSetup);
				}
				catch
				{
				}
			}
			if (File.Exists(tempXpsPath))
			{
				try
				{
					File.Delete(tempXpsPath);
				}
				catch
				{
				}
			}
			ExcelInteropSession.TryRelease(pageSetup);
		}
		string BuildFailure(string message, Exception? exception = null)
		{
			string logPath = WriteRenderFailureLog(outputPath, tempXpsPath, currentStage, message, exception, worksheet, range, keepPdfPreviewAlongsideImage, selectionPaperSizeApplied, previewPaperSizeApplied, selectionOrientation, previewOrientation);
			return string.IsNullOrWhiteSpace(logPath) ? (message + " | Stage=" + currentStage) : (message + " | Stage=" + currentStage + " | Log=" + logPath);
		}
	}

	private static bool TryAutoCropBitmap(BitmapSource source, out BitmapSource? cropped)
	{
		cropped = null;
		BitmapSource workingSource = ((source.Format == PixelFormats.Bgra32) ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0));
		int width = workingSource.PixelWidth;
		int height = workingSource.PixelHeight;
		if (width <= 2 || height <= 2)
		{
			return false;
		}
		int stride = width * 4;
		byte[] pixels = new byte[stride * height];
		workingSource.CopyPixels(pixels, stride, 0);
		GetPixelBgra(pixels, stride, 0, 0, out var b1, out var g1, out var r1, out var a);
		GetPixelBgra(pixels, stride, width - 1, 0, out var b2, out var g2, out var r2, out a);
		GetPixelBgra(pixels, stride, 0, height - 1, out var b3, out var g3, out var r3, out a);
		GetPixelBgra(pixels, stride, width - 1, height - 1, out var b4, out var g4, out var r4, out a);
		double bgB = (double)(b1 + b2 + b3 + b4) * 0.25;
		double bgG = (double)(g1 + g2 + g3 + g4) * 0.25;
		double bgR = (double)(r1 + r2 + r3 + r4) * 0.25;
		int minX = width;
		int minY = height;
		int maxX = -1;
		int maxY = -1;
		for (int y = 0; y < height; y++)
		{
			int row = y * stride;
			for (int x = 0; x < width; x++)
			{
				int index = row + x * 4;
				byte b5 = pixels[index];
				byte g5 = pixels[index + 1];
				byte r5 = pixels[index + 2];
				byte a2 = pixels[index + 3];
				if (a2 <= 8)
				{
					continue;
				}
				double deltaB = Math.Abs((double)(int)b5 - bgB);
				double deltaG = Math.Abs((double)(int)g5 - bgG);
				double deltaR = Math.Abs((double)(int)r5 - bgR);
				double luminanceDelta = 0.114 * deltaB + 0.587 * deltaG + 0.299 * deltaR;
				if (!(luminanceDelta <= 18.0))
				{
					if (x < minX)
					{
						minX = x;
					}
					if (y < minY)
					{
						minY = y;
					}
					if (x > maxX)
					{
						maxX = x;
					}
					if (y > maxY)
					{
						maxY = y;
					}
				}
			}
		}
		if (maxX < minX || maxY < minY)
		{
			return false;
		}
		int left = Math.Max(0, minX - 3);
		int top = Math.Max(0, minY - 3);
		int right = Math.Min(width, maxX + 1 + 3);
		int bottom = Math.Min(height, maxY + 1 + 3);
		int cropWidth = right - left;
		int cropHeight = bottom - top;
		if (cropWidth <= 2 || cropHeight <= 2)
		{
			return false;
		}
		if (cropWidth == width && cropHeight == height)
		{
			return false;
		}
		cropped = new CroppedBitmap(workingSource, new Int32Rect(left, top, cropWidth, cropHeight));
		return true;
	}

	private static void GetPixelBgra(byte[] pixels, int stride, int x, int y, out byte b, out byte g, out byte r, out byte a)
	{
		int index = y * stride + x * 4;
		b = pixels[index];
		g = pixels[index + 1];
		r = pixels[index + 2];
		a = pixels[index + 3];
	}

	private static bool ApplySelectionPageSetup(Worksheet worksheet, Range range, PageSetup pageSetup, string targetPrintArea, out XlPageOrientation orientation)
	{
		pageSetup.PrintArea = targetPrintArea;
		bool paperSizeApplied = TrySetPaperSize(pageSetup, PdfPreviewPaperSize);
		orientation = ResolveOrientation(range);
		pageSetup.Orientation = orientation;
		pageSetup.Zoom = false;
		pageSetup.FitToPagesWide = 1;
		pageSetup.FitToPagesTall = 1;
		pageSetup.PrintGridlines = false;
		double narrowLeftRight = worksheet.Application.InchesToPoints(0.25);
		double narrowTopBottom = worksheet.Application.InchesToPoints(0.75);
		double narrowHeaderFooter = worksheet.Application.InchesToPoints(0.3);
		pageSetup.LeftMargin = narrowLeftRight;
		pageSetup.RightMargin = narrowLeftRight;
		pageSetup.TopMargin = narrowTopBottom;
		pageSetup.BottomMargin = narrowTopBottom;
		pageSetup.HeaderMargin = narrowHeaderFooter;
		pageSetup.FooterMargin = narrowHeaderFooter;
		return paperSizeApplied;
	}

	private static bool ApplyPdfPreviewPageSetup(Worksheet worksheet, Range range, PageSetup pageSetup, string targetPrintArea, out XlPageOrientation orientation)
	{
		pageSetup.PrintArea = targetPrintArea;
		orientation = ResolveOrientation(range);
		pageSetup.Orientation = orientation;
		pageSetup.Zoom = false;
		pageSetup.FitToPagesWide = 1;
		pageSetup.FitToPagesTall = 1;
		pageSetup.PrintGridlines = false;
		bool paperSizeApplied;
		if (IsPdfTightCropEnabled())
		{
			paperSizeApplied = TrySetPaperSize(pageSetup, PdfPreviewPaperSize);
			double tightLeftRight = worksheet.Application.InchesToPoints(0.05);
			double tightTopBottom = worksheet.Application.InchesToPoints(0.05);
			double tightHeaderFooter = worksheet.Application.InchesToPoints(0.05);
			pageSetup.LeftMargin = tightLeftRight;
			pageSetup.RightMargin = tightLeftRight;
			pageSetup.TopMargin = tightTopBottom;
			pageSetup.BottomMargin = tightTopBottom;
			pageSetup.HeaderMargin = tightHeaderFooter;
			pageSetup.FooterMargin = tightHeaderFooter;
			return paperSizeApplied;
		}
		paperSizeApplied = TrySetPaperSize(pageSetup, PdfPreviewPaperSize);
		double narrowLeftRight = worksheet.Application.InchesToPoints(0.25);
		double narrowTopBottom = worksheet.Application.InchesToPoints(0.75);
		double narrowHeaderFooter = worksheet.Application.InchesToPoints(0.3);
		pageSetup.LeftMargin = narrowLeftRight;
		pageSetup.RightMargin = narrowLeftRight;
		pageSetup.TopMargin = narrowTopBottom;
		pageSetup.BottomMargin = narrowTopBottom;
		pageSetup.HeaderMargin = narrowHeaderFooter;
		pageSetup.FooterMargin = narrowHeaderFooter;
		return paperSizeApplied;
	}

	private static string WriteRenderFailureLog(string outputPath, string tempXpsPath, string stage, string message, Exception? exception, Worksheet worksheet, Range range, bool keepPdfPreviewAlongsideImage, bool selectionPaperSizeApplied, bool previewPaperSizeApplied, XlPageOrientation selectionOrientation, XlPageOrientation previewOrientation)
	{
		try
		{
			string logPath = BuildRenderDiagnosticLogPath(outputPath);
			string workbookPath = SafeToString(() => (worksheet.Parent is Workbook workbook) ? workbook.FullName : string.Empty);
			string worksheetName = SafeToString(() => worksheet.Name);
			string rangeAddress = SafeToString(() => range.get_Address((object)false, (object)false, XlReferenceStyle.xlA1, Type.Missing, Type.Missing));
			string activePrinter = SafeToString(() => worksheet.Application.ActivePrinter);
			string excelVersion = SafeToString(() => worksheet.Application.Version);
			string keepPdfEnv = Environment.GetEnvironmentVariable("VP_EXCEL_KEEP_PDF_PREVIEW") ?? string.Empty;
			string previewLayoutEnv = Environment.GetEnvironmentVariable("VP_EXCEL_PDF_PREVIEW_LAYOUT") ?? string.Empty;
			StringBuilder builder = new StringBuilder();
			builder.AppendLine("Excel Render Diagnostics");
			builder.AppendLine($"TimestampUtc={DateTime.UtcNow:O}");
			builder.AppendLine("Stage=" + stage);
			builder.AppendLine("Message=" + message);
			builder.AppendLine("ExceptionType=" + (exception?.GetType().FullName ?? string.Empty));
			builder.AppendLine("StackTrace=" + (exception?.StackTrace ?? string.Empty));
			builder.AppendLine("Workbook=" + workbookPath);
			builder.AppendLine("Worksheet=" + worksheetName);
			builder.AppendLine("RangeAddress=" + rangeAddress);
			builder.AppendLine("OutputPath=" + outputPath);
			builder.AppendLine("TempXpsPath=" + tempXpsPath);
			builder.AppendLine("ActivePrinter=" + activePrinter);
			builder.AppendLine("ExcelVersion=" + excelVersion);
			builder.AppendLine("KeepPdfPreviewEnv=" + keepPdfEnv);
			builder.AppendLine("PdfPreviewLayoutEnv=" + previewLayoutEnv);
			builder.AppendLine($"KeepPdfPreviewResolved={keepPdfPreviewAlongsideImage}");
			builder.AppendLine("PdfPreviewModeToken=" + PdfPreviewModeToken);
			builder.AppendLine("PdfPageSetupModeToken=" + PdfPageSetupModeToken);
			builder.AppendLine($"PaperSizeCode={112}");
			builder.AppendLine($"SelectionPaperSizeApplied={selectionPaperSizeApplied}");
			builder.AppendLine($"PreviewPaperSizeApplied={previewPaperSizeApplied}");
			builder.AppendLine($"SelectionOrientation={selectionOrientation}");
			builder.AppendLine($"PreviewOrientation={previewOrientation}");
			File.WriteAllText(logPath, builder.ToString());
			return logPath;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string BuildRenderDiagnosticLogPath(string outputPath)
	{
		string directory = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
		string fileName = Path.GetFileNameWithoutExtension(outputPath);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			fileName = "excel_render";
		}
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, fileName + ".render.log");
	}

	private static string SafeToString(Func<object?> getter)
	{
		try
		{
			object value = getter();
			if (value == null)
			{
				return string.Empty;
			}
			string text = value.ToString() ?? string.Empty;
			return text.Replace('\r', ' ').Replace('\n', ' ');
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool TrySetPaperSize(PageSetup pageSetup, XlPaperSize paperSize)
	{
		try
		{
			pageSetup.PaperSize = paperSize;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static XlPageOrientation ResolveOrientation(Range range)
	{
		try
		{
			double width = Convert.ToDouble(range.Width);
			double height = Convert.ToDouble(range.Height);
			return (!(width >= height)) ? XlPageOrientation.xlPortrait : XlPageOrientation.xlLandscape;
		}
		catch
		{
			return XlPageOrientation.xlPortrait;
		}
	}

	private static void TryExportPdfPreview(Worksheet worksheet, string outputPath)
	{
		try
		{
			string pdfPath = Path.ChangeExtension(outputPath, ".pdf");
			worksheet.ExportAsFixedFormat(XlFixedFormatType.xlTypePDF, pdfPath, XlFixedFormatQuality.xlQualityStandard, true, false, Type.Missing, Type.Missing, false, Type.Missing);
		}
		catch
		{
		}
	}

	private static void DeletePdfPreviewIfExists(string outputPath)
	{
		string pdfPath = Path.ChangeExtension(outputPath, ".pdf");
		if (!File.Exists(pdfPath))
		{
			return;
		}
		try
		{
			File.Delete(pdfPath);
		}
		catch
		{
		}
	}

	private static bool ShouldKeepPdfPreviewAlongsideImage()
	{
		string rawValue = Environment.GetEnvironmentVariable("VP_EXCEL_KEEP_PDF_PREVIEW");
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return true;
		}
		string value = rawValue.Trim();
		if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPdfTightCropEnabled()
	{
		string rawValue = Environment.GetEnvironmentVariable("VP_EXCEL_PDF_PREVIEW_LAYOUT");
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return true;
		}
		string value = rawValue.Trim();
		if (string.Equals(value, "legacy", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "a4", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}
}
