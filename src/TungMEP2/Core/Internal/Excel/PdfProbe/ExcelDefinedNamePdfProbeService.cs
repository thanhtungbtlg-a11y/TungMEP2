using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using Microsoft.Office.Interop.Excel;
using VP_RevitAPI.Helper.Excel;

namespace VP_RevitAPI.Internal.Excel.PdfProbe;

internal static class ExcelDefinedNamePdfProbeService
{
	private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".xls" };

	private const string PreferredPrinterName = "Adobe PDF";

	private const string PreferredPaperName = "JIS B0";

	public static ExcelDefinedNamePdfProbeResult ExportSingle(string excelPath, string sheetName, string definedName)
	{
		if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Đường dẫn Excel không hợp lệ hoặc file không tồn tại.");
		}
		if (string.IsNullOrWhiteSpace(definedName))
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Defined Name không được để trống.");
		}
		string extension = Path.GetExtension(excelPath);
		if (!SupportedExtensions.Contains(extension))
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Định dạng không hỗ trợ: " + extension);
		}
		try
		{
			using ExcelInteropSession session = ExcelInteropSession.OpenReadOnly(excelPath);
			return ExportFromWorkbook(session.Workbook, excelPath, sheetName, definedName);
		}
		catch (Exception ex)
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Không thể mở workbook: " + ex.Message);
		}
	}

	private static ExcelDefinedNamePdfProbeResult ExportFromWorkbook(Workbook workbook, string excelPath, string targetSheetName, string targetDefinedName)
	{
		ExcelDefinedNamePdfProbeResult result = null;
		bool foundTarget = false;
		SheetScopedNameScanner.ForEachDefinedName(workbook, delegate(Worksheet? worksheet, Name name, bool isWorkbookScope)
		{
			string text = (isWorkbookScope ? "Workbook" : (worksheet?.Name ?? "Workbook"));
			string name2 = name.Name;
			if (string.Equals(text, targetSheetName, StringComparison.OrdinalIgnoreCase) && string.Equals(name2, targetDefinedName, StringComparison.OrdinalIgnoreCase))
			{
				foundTarget = true;
				result = ExportSingleName(workbook, excelPath, text, name2, worksheet, name);
			}
		});
		if (!foundTarget)
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Không tìm thấy defined name theo scope đã chọn.");
		}
		return result ?? ExcelDefinedNamePdfProbeResult.Fail("Không thể tạo PDF từ defined name đã chọn.");
	}

	private static ExcelDefinedNamePdfProbeResult ExportSingleName(Workbook workbook, string excelPath, string sheetName, string definedName, Worksheet? worksheet, Name name)
	{
		string stage = "ResolveRange";
		Range range = null;
		Worksheet resolvedWorksheet = worksheet;
		PageSetup pageSetup = null;
		string originalPrintArea = null;
		object originalZoom = null;
		object originalFitWide = null;
		object originalFitTall = null;
		XlPageOrientation originalOrientation = XlPageOrientation.xlPortrait;
		try
		{
			range = name.RefersToRange;
			if (range == null)
			{
				return ExcelDefinedNamePdfProbeResult.Fail("Defined name không trỏ đến range.");
			}
			if (range.Areas.Count > 1)
			{
				return ExcelDefinedNamePdfProbeResult.Fail("Multi-area range chưa được hỗ trợ cho PDF test.");
			}
			if (resolvedWorksheet == null)
			{
				resolvedWorksheet = range.Worksheet;
			}
			if (resolvedWorksheet == null)
			{
				return ExcelDefinedNamePdfProbeResult.Fail("Không xác định được worksheet của defined name.");
			}
			string warningMessage = string.Empty;
			stage = "SetPrinter";
			if (!TrySwitchToPreferredPrinterWithFallback(workbook.Application, out string printerInfo, out bool usingPreferredPrinter))
			{
				return ExcelDefinedNamePdfProbeResult.Fail("Không thể truy cập printer để export PDF. " + printerInfo + " | Stage=SetPrinter");
			}
			if (!usingPreferredPrinter)
			{
				warningMessage = "[Fallback Printer] " + printerInfo;
			}
			string outputPath = ExcelDefinedNameExportPathPolicy.BuildPdfPath(excelPath, sheetName, definedName);
			Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
			stage = "ApplyPageSetup";
			pageSetup = resolvedWorksheet.PageSetup;
			originalPrintArea = pageSetup.PrintArea;
			originalZoom = pageSetup.Zoom;
			originalFitWide = pageSetup.FitToPagesWide;
			originalFitTall = pageSetup.FitToPagesTall;
			originalOrientation = pageSetup.Orientation;
			pageSetup.PrintArea = range.get_Address((object)false, (object)false, XlReferenceStyle.xlA1, Type.Missing, Type.Missing);
			pageSetup.Zoom = false;
			pageSetup.FitToPagesWide = 1;
			pageSetup.FitToPagesTall = 1;
			if (!TryApplyPreferredPaperWithFallback(pageSetup, workbook.Application.ActivePrinter, out string paperInfo, out bool usingPreferredPaper))
			{
				return ExcelDefinedNamePdfProbeResult.Fail("Không thể thiết lập paper size. " + paperInfo + " | Stage=ApplyPageSetup");
			}
			if (!usingPreferredPaper)
			{
				warningMessage = (string.IsNullOrWhiteSpace(warningMessage) ? ("[Fallback Paper] " + paperInfo) : (warningMessage + " | [Fallback Paper] " + paperInfo));
			}
			bool isLandscape = SafeDouble(range.Width) >= SafeDouble(range.Height);
			pageSetup.Orientation = ((!isLandscape) ? XlPageOrientation.xlPortrait : XlPageOrientation.xlLandscape);
			stage = "ExportPdf";
			resolvedWorksheet.ExportAsFixedFormat(XlFixedFormatType.xlTypePDF, outputPath, XlFixedFormatQuality.xlQualityStandard, true, false, Type.Missing, Type.Missing, false, Type.Missing);
			stage = "CropPdf";
			PdfCropResult cropResult = PdfCropBoxPostProcessor.TryCropToDefinedContent(outputPath);
			double widthMm = PointToMillimeter(SafeDouble(range.Width));
			double heightMm = PointToMillimeter(SafeDouble(range.Height));
			string summary = string.Format("Exported PDF: {0} | ActivePrinter={1} | TargetPaper={2} | Range={3} | Size={4:F2}x{5:F2} mm | Orientation={6}", Path.GetFileName(outputPath), workbook.Application.ActivePrinter, "JIS B0", range.get_Address((object)false, (object)false, XlReferenceStyle.xlA1, Type.Missing, Type.Missing), widthMm, heightMm, isLandscape ? "Landscape" : "Portrait");
			if (!string.IsNullOrWhiteSpace(warningMessage))
			{
				summary = summary + " | " + warningMessage;
			}
			summary = summary + " | Crop=" + cropResult.Message;
			return ExcelDefinedNamePdfProbeResult.Success(outputPath, widthMm, heightMm, isLandscape, summary);
		}
		catch (Exception ex)
		{
			return ExcelDefinedNamePdfProbeResult.Fail("Lỗi export PDF: " + ex.Message + " | Stage=" + stage);
		}
		finally
		{
			if (pageSetup != null)
			{
				try
				{
					pageSetup.PrintArea = originalPrintArea ?? string.Empty;
					pageSetup.Zoom = originalZoom;
					pageSetup.FitToPagesWide = originalFitWide;
					pageSetup.FitToPagesTall = originalFitTall;
					pageSetup.Orientation = originalOrientation;
				}
				catch
				{
				}
				ExcelInteropSession.TryRelease(pageSetup);
			}
			if (resolvedWorksheet != worksheet)
			{
				ExcelInteropSession.TryRelease(resolvedWorksheet);
			}
			ExcelInteropSession.TryRelease(range);
		}
	}

	private static bool TrySwitchToPreferredPrinterWithFallback(Microsoft.Office.Interop.Excel.Application application, out string info, out bool usingPreferredPrinter)
	{
		usingPreferredPrinter = false;
		string activePrinter = application.ActivePrinter ?? string.Empty;
		if (activePrinter.IndexOf("Adobe PDF", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			usingPreferredPrinter = true;
			info = "Active printer already '" + activePrinter + "'.";
			return true;
		}
		if (!PrinterSettings.InstalledPrinters.Cast<string>().Any((string name) => string.Equals(name, "Adobe PDF", StringComparison.OrdinalIgnoreCase)))
		{
			info = "Không tìm thấy 'Adobe PDF' trong InstalledPrinters. Dùng printer hiện tại '" + activePrinter + "'.";
			return true;
		}
		IEnumerable<string> candidates = BuildPrinterCandidates(activePrinter);
		foreach (string candidate in candidates)
		{
			try
			{
				application.ActivePrinter = candidate;
				if ((application.ActivePrinter ?? string.Empty).IndexOf("Adobe PDF", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					usingPreferredPrinter = true;
					info = "Switched printer to '" + application.ActivePrinter + "'.";
					return true;
				}
			}
			catch
			{
			}
		}
		info = "Không set được ActivePrinter sang 'Adobe PDF'. Dùng printer hiện tại '" + activePrinter + "'.";
		return true;
	}

	private static IEnumerable<string> BuildPrinterCandidates(string currentActivePrinter)
	{
		List<string> candidates = new List<string>();
		string activePort = TryExtractPort(currentActivePrinter);
		if (!string.IsNullOrWhiteSpace(activePort))
		{
			candidates.Add("Adobe PDF on " + activePort);
		}
		candidates.Add("Adobe PDF on PORTPROMPT:");
		for (int index = 0; index <= 99; index++)
		{
			candidates.Add(string.Format("{0} on Ne{1:00}:", "Adobe PDF", index));
		}
		candidates.Add("Adobe PDF on LPT1:");
		return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
	}

	private static bool TryApplyPreferredPaperWithFallback(PageSetup pageSetup, string activePrinter, out string info, out bool usingPreferredPaper)
	{
		usingPreferredPaper = false;
		if (!TryResolvePreferredPaperRawKind(activePrinter, out int rawKind, out string resolveInfo))
		{
			info = "Không resolve được paper 'JIS B0' trên printer hiện tại. " + resolveInfo;
			return true;
		}
		try
		{
			pageSetup.PaperSize = (XlPaperSize)rawKind;
			usingPreferredPaper = true;
			info = string.Format("Áp dụng paper '{0}' (RawKind={1}).", "JIS B0", rawKind);
			return true;
		}
		catch (Exception ex)
		{
			info = $"Set PaperSize thất bại với RawKind={rawKind}. {ex.Message}";
			return true;
		}
	}

	private static bool TryResolvePreferredPaperRawKind(string activePrinter, out int rawKind, out string info)
	{
		rawKind = 0;
		string printerName = TryExtractPrinterName(activePrinter);
		if (string.IsNullOrWhiteSpace(printerName))
		{
			info = "ActivePrinter không parse được tên printer: '" + activePrinter + "'.";
			return false;
		}
		PrinterSettings settings = new PrinterSettings
		{
			PrinterName = printerName
		};
		if (!settings.IsValid)
		{
			info = "Printer '" + printerName + "' không hợp lệ trong Windows.";
			return false;
		}
		PaperSize matched = settings.PaperSizes.Cast<PaperSize>().FirstOrDefault((PaperSize size) => size.PaperName.IndexOf("JIS B0", StringComparison.OrdinalIgnoreCase) >= 0);
		if (matched == null)
		{
			info = "Printer '" + printerName + "' không có paper 'JIS B0'.";
			return false;
		}
		rawKind = matched.RawKind;
		info = $"Resolved '{matched.PaperName}' RawKind={rawKind}.";
		return true;
	}

	private static string? TryExtractPrinterName(string activePrinter)
	{
		int markerIndex = activePrinter.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
		if (markerIndex <= 0)
		{
			return string.IsNullOrWhiteSpace(activePrinter) ? null : activePrinter.Trim();
		}
		string printerName = activePrinter.Substring(0, markerIndex).Trim();
		return string.IsNullOrWhiteSpace(printerName) ? null : printerName;
	}

	private static string? TryExtractPort(string activePrinter)
	{
		int markerIndex = activePrinter.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
		if (markerIndex < 0)
		{
			return null;
		}
		string port = activePrinter.Substring(markerIndex + " on ".Length).Trim();
		return string.IsNullOrWhiteSpace(port) ? null : port;
	}

	private static double SafeDouble(object value)
	{
		try
		{
			return Convert.ToDouble(value);
		}
		catch
		{
			return 0.0;
		}
	}

	private static double PointToMillimeter(double point)
	{
		return point * 25.4 / 72.0;
	}
}
