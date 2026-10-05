using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Helper.Sheets;

public static class SheetExportUtilities
{
	private const int MaxBaseFileNameLength = 160;
	private const int MaxLegacyWindowsPathLength = 259;
	private const int MinimumBaseFileNameLength = 16;
	private const int PdfBatchSize = 10;
	private const int VirtualPrinterOutputTimeoutMilliseconds = 60000;
	private const int VirtualPrinterCombinedOutputTimeoutMilliseconds = 300000;
	private const int VirtualPrinterFileStabilityMilliseconds = 3000;
	private const int VirtualPrinterPollingIntervalMilliseconds = 200;
	private const int PdfXChangeCommandTimeoutMilliseconds = 15000;
	private const string PdfStagingRootName = "TungMEP2-PdfExport";
	private const string VirtualPdfStagingRootName = "TungMEP2-VirtualPdfPrint";
	private const string DwgStagingRootName = "TungMEP2-DwgExport";

	private static readonly string[] VirtualPdfPrinterNameHints =
	{
		"PDF",
		"Foxit",
		"Bluebeam",
		"XChange",
		"Acrobat",
		"CutePDF",
		"doPDF",
		"Nitro",
		"Bullzip",
		"PDF24",
		"PrimoPDF"
	};

	private sealed class PdfXChangeLiteAutoSaveScope : IDisposable
	{
		private readonly string _pdfSaverPath;

		private readonly string _printerName;

		private readonly bool _showSaveDialog;

		private readonly bool _runApplication;

		private bool _disposed;

		private PdfXChangeLiteAutoSaveScope(
			string pdfSaverPath,
			string printerName,
			bool showSaveDialog,
			bool runApplication)
		{
			_pdfSaverPath = pdfSaverPath;
			_printerName = printerName;
			_showSaveDialog = showSaveDialog;
			_runApplication = runApplication;
		}

		public static PdfXChangeLiteAutoSaveScope Create(string printerName)
		{
			string pdfSaverPath =
				FindPdfXChangeLitePdfSaverExecutable() ??
				throw new InvalidOperationException(
					"Không tìm thấy pdfSaverL.exe của PDF-XChange Lite. " +
					"Hãy Repair hoặc cài lại PDF-XChange Lite.");
			IReadOnlyList<string> originalOptions =
				DumpPdfXChangeLiteOptions(pdfSaverPath, printerName);
			if (!TryParsePdfXChangeBooleanOption(
					originalOptions,
					"Save.ShowSaveDialog",
					out bool showSaveDialog) ||
				!TryParsePdfXChangeBooleanOption(
					originalOptions,
					"Save.RunApp",
					out bool runApplication))
			{
				throw new InvalidOperationException(
					"Không đọc được cấu hình Save của PDF-XChange Lite.");
			}

			PdfXChangeLiteAutoSaveScope scope =
				new PdfXChangeLiteAutoSaveScope(
					pdfSaverPath,
					printerName,
					showSaveDialog,
					runApplication);
			try
			{
				SetPdfXChangeLiteSaveOptions(
					pdfSaverPath,
					printerName,
					showSaveDialog: false,
					runApplication: false);
				IReadOnlyList<string> appliedOptions =
					DumpPdfXChangeLiteOptions(pdfSaverPath, printerName);
				if (!TryParsePdfXChangeBooleanOption(
						appliedOptions,
						"Save.ShowSaveDialog",
						out bool appliedShowSaveDialog) ||
					appliedShowSaveDialog)
				{
					throw new InvalidOperationException(
						"PDF-XChange Lite không chấp nhận chế độ Auto-Save. " +
						"Hãy cập nhật driver PDF-XChange Lite.");
				}

				return scope;
			}
			catch
			{
				scope.Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			try
			{
				SetPdfXChangeLiteSaveOptions(
					_pdfSaverPath,
					_printerName,
					_showSaveDialog,
					_runApplication);
			}
			catch
			{
				// Do not hide the export result if restoring printer preferences fails.
			}
		}
	}

	private sealed class ExportProgressTracker
	{
		private readonly Func<bool>? _continueExport;

		private readonly Action<string, int, int>? _progressChanged;

		public int CompletedItems { get; private set; }

		public int TotalItems { get; }

		public bool WasCancelled { get; private set; }

		public ExportProgressTracker(
			int totalItems,
			Func<bool>? continueExport,
			Action<string, int, int>? progressChanged)
		{
			TotalItems = Math.Max(0, totalItems);
			_continueExport = continueExport;
			_progressChanged = progressChanged;
		}

		public bool Checkpoint(string phase)
		{
			if (WasCancelled)
			{
				return false;
			}

			if (_continueExport != null && !_continueExport())
			{
				WasCancelled = true;
				Report(phase);
				return false;
			}

			Report(phase);
			return true;
		}

		public void Advance(int count, string phase)
		{
			CompletedItems = Math.Min(TotalItems, CompletedItems + Math.Max(0, count));
			Report(phase);
		}

		private void Report(string phase)
		{
			if (_progressChanged == null)
			{
				return;
			}

			try
			{
				_progressChanged(phase, CompletedItems, TotalItems);
			}
			catch
			{
			}
		}
	}

	private static readonly HashSet<string> ReservedWindowsFileNames = new HashSet<string>(
		new[]
		{
			"CON", "PRN", "AUX", "NUL",
			"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
			"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
		},
		StringComparer.OrdinalIgnoreCase);

	public static List<ViewSheet> ResolveSheetsFromSelection(UIDocument uiDocument)
	{
		return ResolveSheetsFromSelectionInternal(uiDocument, allowEmptySelection: false);
	}

	public static List<ViewSheet> ResolveSheetsFromSelectionOrPrintSet(UIDocument uiDocument)
	{
		return ResolveSheetsFromSelectionInternal(uiDocument, allowEmptySelection: true);
	}

	public static List<ViewSheetSet> ListAvailablePrintSets(Document document)
	{
		return new FilteredElementCollector(document)
			.OfClass(typeof(ViewSheetSet))
			.Cast<ViewSheetSet>()
			.OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static List<ViewSchedule> ListAvailableSheetListSchedules(Document document)
	{
		return new FilteredElementCollector(document)
			.OfClass(typeof(ViewSchedule))
			.Cast<ViewSchedule>()
			.Where(IsSheetListSchedule)
			.OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static List<ViewSheet> ResolveSheetsFromPrintSet(Document document, ElementId printSetId)
	{
		if (printSetId == ElementId.InvalidElementId)
		{
			throw new InvalidOperationException("Print Set không hợp lệ.");
		}

		if (!(document.GetElement(printSetId) is ViewSheetSet printSet))
		{
			throw new InvalidOperationException("Không tìm thấy Print Set đã chọn.");
		}

		List<ViewSheet> sheets = printSet.Views
			.Cast<View>()
			.OfType<ViewSheet>()
			.Where(sheet => !sheet.IsPlaceholder)
			.ToList();

		if (sheets.Count == 0)
		{
			throw new InvalidOperationException($"Print Set '{printSet.Name}' không có sheet hợp lệ.");
		}

		return OrderSheetsForExport(sheets);
	}

	public static List<ViewSheet> ResolveSheetsFromSheetListSchedule(
		Document document,
		ElementId scheduleId)
	{
		if (scheduleId == ElementId.InvalidElementId)
		{
			throw new InvalidOperationException("Sheet Schedule không hợp lệ.");
		}

		if (!(document.GetElement(scheduleId) is ViewSchedule schedule) ||
			!IsSheetListSchedule(schedule))
		{
			throw new InvalidOperationException(
				"Không tìm thấy Sheet Schedule đã chọn.");
		}

		List<ViewSheet> sheets = new FilteredElementCollector(document, schedule.Id)
			.OfClass(typeof(ViewSheet))
			.Cast<ViewSheet>()
			.Where(sheet => !sheet.IsPlaceholder)
			.GroupBy(sheet => sheet.Id.Value)
			.Select(group => group.First())
			.ToList();

		if (sheets.Count == 0)
		{
			throw new InvalidOperationException(
				$"Sheet Schedule '{schedule.Name}' không có sheet hợp lệ sau khi áp dụng filter.");
		}

		return OrderSheetsForExport(sheets);
	}

	private static bool IsSheetListSchedule(ViewSchedule schedule)
	{
		if (schedule == null ||
			schedule.IsTemplate ||
			schedule.IsTitleblockRevisionSchedule)
		{
			return false;
		}

		ElementId categoryId = schedule.Definition?.CategoryId;
		return categoryId != null &&
			categoryId == new ElementId(BuiltInCategory.OST_Sheets);
	}

	public static List<DwgExportSetupOption> ListAvailableDwgExportSetups(Document document)
	{
		ElementId activeId = ExportDWGSettings.GetActivePredefinedSettings(document)?.Id ?? ElementId.InvalidElementId;
		return new FilteredElementCollector(document)
			.OfClass(typeof(ExportDWGSettings))
			.Cast<ExportDWGSettings>()
			.Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
			.Select(setting => new DwgExportSetupOption(setting.Id, setting.Name, setting.Id == activeId))
			.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static List<PrintSetupOption> ListAvailablePrintSetups(Document document)
	{
		ElementId activeId = GetActivePrintSettingId(document);
		List<PrintSetupOption> setups = new List<PrintSetupOption>
		{
			new PrintSetupOption(
				ElementId.InvalidElementId,
				"In-Session",
				activeId == ElementId.InvalidElementId,
				isInSession: true)
		};
		setups.AddRange(
			new FilteredElementCollector(document)
				.OfClass(typeof(PrintSetting))
				.Cast<PrintSetting>()
				.Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
				.Select(setting => new PrintSetupOption(
					setting.Id,
					setting.Name,
					setting.Id == activeId,
					isInSession: false))
				.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase));
		return setups;
	}

	public static List<PrinterOption> ListAvailablePrinters(Document document)
	{
		string activePrinterName = GetActivePrinterName(document);
		List<PrinterOption> options = new List<PrinterOption>
		{
			PrinterOption.CreateNativePdf()
		};
		try
		{
			options.AddRange(
				PrinterSettings.InstalledPrinters
					.Cast<string>()
					.Where(name =>
						!string.IsNullOrWhiteSpace(name) &&
						IsLikelyPdfVirtualPrinter(name))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
					.Select(name => PrinterOption.CreateVirtualPrinter(
						name,
						string.Equals(
							name,
							activePrinterName,
							StringComparison.OrdinalIgnoreCase))));
		}
		catch
		{
			// Revit Native PDF remains available even when Windows Print Spooler is unavailable.
		}
		return options;
	}

	public static PrinterOption SelectDefaultPdfPrinter(
		IEnumerable<PrinterOption> printers)
	{
		List<PrinterOption> availablePrinters = printers?
			.Where(printer => printer != null)
			.ToList() ?? new List<PrinterOption>();
		if (availablePrinters.Count == 0)
		{
			return null;
		}

		return availablePrinters.FirstOrDefault(printer =>
				!printer.IsNativePdf &&
				IsPdfXChangeLitePrinterName(printer.DriverName ?? printer.Name)) ??
			availablePrinters.FirstOrDefault(printer => printer.IsNativePdf) ??
			availablePrinters.First();
	}

	private static bool IsPdfXChangeLitePrinterName(string printerName)
	{
		return !string.IsNullOrWhiteSpace(printerName) &&
			printerName.IndexOf(
				"PDF-XChange",
				StringComparison.OrdinalIgnoreCase) >= 0 &&
			printerName.IndexOf(
				"Lite",
				StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public static ExportSummary ExportSheets(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		bool exportPdf,
		bool exportDwg,
		string? printerName,
		ElementId printSetupId,
		bool combinePdf,
		string? combinedPdfName,
		ElementId dwgExportSetupId)
	{
		return ExportSheets(
			document,
			sheets,
			outputFolder,
			outputFolder,
			exportPdf,
			exportDwg,
			PDFExportQualityType.DPI600,
			combinePdf,
			combinedPdfName,
			dwgExportSetupId,
			virtualPrinterName: printerName,
			virtualPrintSetupId: printSetupId);
	}

	public static ExportSummary ExportSheets(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		bool exportPdf,
		bool exportDwg,
		string? printerName,
		ElementId printSetupId,
		PDFExportQualityType pdfQuality,
		bool combinePdf,
		string? combinedPdfName,
		ElementId dwgExportSetupId)
	{
		return ExportSheets(
			document,
			sheets,
			outputFolder,
			outputFolder,
			exportPdf,
			exportDwg,
			pdfQuality,
			combinePdf,
			combinedPdfName,
			dwgExportSetupId,
			virtualPrinterName: printerName,
			virtualPrintSetupId: printSetupId);
	}

	public static ExportSummary ExportSheets(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		bool exportPdf,
		bool exportDwg,
		PDFExportQualityType pdfQuality,
		bool combinePdf,
		string? combinedPdfName,
		ElementId dwgExportSetupId,
		Func<bool>? continueExport = null,
		Action<string, int, int>? progressChanged = null,
		bool alwaysUseRaster = false)
	{
		return ExportSheets(
			document,
			sheets,
			outputFolder,
			outputFolder,
			exportPdf,
			exportDwg,
			pdfQuality,
			combinePdf,
			combinedPdfName,
			dwgExportSetupId,
			continueExport,
			progressChanged,
			alwaysUseRaster,
			virtualPrinterName: null,
			virtualPrintSetupId: null);
	}

	public static ExportSummary ExportSheets(
		Document document,
		IList<ViewSheet> sheets,
		string pdfOutputFolder,
		string dwgOutputFolder,
		bool exportPdf,
		bool exportDwg,
		PDFExportQualityType pdfQuality,
		bool combinePdf,
		string? combinedPdfName,
		ElementId dwgExportSetupId,
		Func<bool>? continueExport = null,
		Action<string, int, int>? progressChanged = null,
		bool alwaysUseRaster = false,
		string? virtualPrinterName = null,
		ElementId? virtualPrintSetupId = null)
	{
		if (!exportPdf && !exportDwg)
		{
			throw new InvalidOperationException("Hãy chọn ít nhất một định dạng export: PDF hoặc DWG.");
		}

		pdfOutputFolder = exportPdf ? PrepareOutputFolder(pdfOutputFolder) : string.Empty;
		dwgOutputFolder = exportDwg ? PrepareOutputFolder(dwgOutputFolder) : string.Empty;
		List<string> errors = new List<string>();
		List<ViewSheet> printableSheets = new List<ViewSheet>();

		foreach (ViewSheet sheet in sheets)
		{
			if (!sheet.CanBePrinted)
			{
				errors.Add($"Sheet không thể in: {sheet.SheetNumber} - {sheet.Name}");
				continue;
			}

			printableSheets.Add(sheet);
		}

		// The UI already establishes the export order. Do not sort again here.
		List<ViewSheet> orderedPrintableSheets = printableSheets;
		int totalExportItems =
			(exportPdf ? (combinePdf ? 1 : orderedPrintableSheets.Count) : 0) +
			(exportDwg ? orderedPrintableSheets.Count : 0);
		ExportProgressTracker progress = new ExportProgressTracker(
			totalExportItems,
			continueExport,
			progressChanged);
		HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<long, string> baseNamesBySheetId = new Dictionary<long, string>();
		int perSheetBaseNameLength = MaxBaseFileNameLength;
		if (exportPdf && !combinePdf)
		{
			perSheetBaseNameLength = Math.Min(
				perSheetBaseNameLength,
				GetSafeBaseFileNameLength(pdfOutputFolder, ".pdf"));
		}
		if (exportDwg)
		{
			perSheetBaseNameLength = Math.Min(
				perSheetBaseNameLength,
				GetSafeBaseFileNameLength(dwgOutputFolder, ".dwg"));
		}

		foreach (ViewSheet sheet in orderedPrintableSheets)
		{
			string baseName = BuildBaseFileName(sheet);
			baseName = ReserveUniqueOutputBaseName(
				baseName,
				usedNames,
				perSheetBaseNameLength);
			baseNamesBySheetId[sheet.Id.Value] = baseName;
		}

		int pdfCount = 0;
		int dwgCount = 0;
		bool useVirtualPdfPrinter =
			exportPdf &&
			!string.IsNullOrWhiteSpace(virtualPrinterName);
		ElementId effectiveVirtualPrintSetupId =
			virtualPrintSetupId ?? ElementId.InvalidElementId;

		if (exportPdf && combinePdf && orderedPrintableSheets.Count > 0)
		{
			string normalizedCombinedName = NormalizePdfFileBaseName(combinedPdfName ?? string.Empty);
			if (string.IsNullOrWhiteSpace(normalizedCombinedName))
			{
				throw new InvalidOperationException("Tên file PDF gộp không hợp lệ.");
			}

			normalizedCombinedName = ReserveUniqueOutputBaseName(
				normalizedCombinedName,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				GetSafeBaseFileNameLength(pdfOutputFolder, ".pdf"));

			if (progress.Checkpoint("Chuẩn bị PDF gộp"))
			{
				try
				{
					bool exported = useVirtualPdfPrinter
						? ExportSheetsToVirtualPdf(
							document,
							orderedPrintableSheets,
							pdfOutputFolder,
							normalizedCombinedName,
							virtualPrinterName!,
							effectiveVirtualPrintSetupId,
							combineSheets: true,
							continueWaiting: () =>
								progress.Checkpoint(
									"Đang chờ PDF-XChange Lite gộp file"))
						: ExportSheetsToNativePdf(
							document,
							orderedPrintableSheets,
							pdfOutputFolder,
							normalizedCombinedName,
							pdfQuality,
							alwaysUseRaster);
					if (exported)
					{
						pdfCount = 1;
					}
					else
					{
						errors.Add("PDF gộp thất bại: Revit không tạo được file đầu ra.");
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					errors.Add("PDF gộp thất bại: " + ex.Message);
				}
				finally
				{
					progress.Advance(1, "PDF gộp");
				}
			}
		}
		else if (exportPdf)
		{
			pdfCount = useVirtualPdfPrinter
				? ExportIndividualPdfsWithVirtualPrinter(
					document,
					orderedPrintableSheets,
					pdfOutputFolder,
					baseNamesBySheetId,
					virtualPrinterName!,
					effectiveVirtualPrintSetupId,
					errors,
					progress)
				: ExportIndividualPdfsBatched(
					document,
					orderedPrintableSheets,
					pdfOutputFolder,
					baseNamesBySheetId,
					pdfQuality,
					alwaysUseRaster,
					errors,
					progress);
		}

		if (exportDwg && !progress.WasCancelled)
		{
			using (DWGExportOptions dwgOptions = CreateDwgExportOptions(document, dwgExportSetupId))
			{
				foreach (ViewSheet sheet in orderedPrintableSheets)
				{
					if (!progress.Checkpoint("Chuẩn bị DWG"))
					{
						break;
					}

					string baseName = baseNamesBySheetId[sheet.Id.Value];
					try
					{
						if (ExportDwgViaLocalStaging(
							document,
							sheet,
							dwgOptions,
							dwgOutputFolder,
							baseName,
							out string dwgError))
						{
							dwgCount++;
						}
						else
						{
							errors.Add($"DWG thất bại ({sheet.SheetNumber}): {dwgError}");
						}
					}
					catch (Exception ex)
					{
						errors.Add($"DWG thất bại ({sheet.SheetNumber}): {ex.Message}");
					}
					finally
					{
						progress.Advance(1, "DWG");
					}
				}
			}
		}

		return new ExportSummary(pdfCount, dwgCount, errors, progress.WasCancelled);
	}

	public static string PrepareOutputFolder(string outputFolder)
	{
		if (string.IsNullOrWhiteSpace(outputFolder))
		{
			throw new InvalidOperationException("Output folder không hợp lệ.");
		}

		string expandedPath = Environment.ExpandEnvironmentVariables(outputFolder.Trim().Trim('"'));
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(expandedPath);
			Directory.CreateDirectory(fullPath);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("Không thể tạo hoặc truy cập output folder.", ex);
		}

		string probePath = Path.Combine(fullPath, ".tungmep2-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			using (FileStream stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				stream.WriteByte(0);
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("Output folder không cho phép ghi file.", ex);
		}
		finally
		{
			if (File.Exists(probePath))
			{
				try
				{
					File.Delete(probePath);
				}
				catch
				{
				}
			}
		}

		return fullPath;
	}

	private static List<ViewSheet> ResolveSheetsFromSelectionInternal(UIDocument uiDocument, bool allowEmptySelection)
	{
		Document document = uiDocument.Document;
		IList<ElementId> selectionIds = uiDocument.Selection.GetElementIds().ToList();
		if (selectionIds.Count == 0)
		{
			return allowEmptySelection
				? new List<ViewSheet>()
				: throw new InvalidOperationException("Hãy chọn sheet trên Project Browser hoặc chọn Sheet List Schedule trước khi chạy lệnh.");
		}

		List<long> resolvedSheetIds = new List<long>();
		HashSet<long> selectedScheduleIds = new HashSet<long>();

		foreach (ElementId id in selectionIds)
		{
			Element element = document.GetElement(id);
			if (element is ViewSheet sheet)
			{
				resolvedSheetIds.Add(sheet.Id.Value);
			}
			else if (element is ScheduleSheetInstance scheduleInstance &&
				scheduleInstance.ScheduleId != ElementId.InvalidElementId)
			{
				selectedScheduleIds.Add(scheduleInstance.ScheduleId.Value);
			}
			else if (element is ViewSchedule selectedSchedule)
			{
				selectedScheduleIds.Add(selectedSchedule.Id.Value);
			}
		}

		foreach (long scheduleIdValue in selectedScheduleIds)
		{
			ElementId scheduleId = new ElementId(scheduleIdValue);
			if (!(document.GetElement(scheduleId) is ViewSchedule viewSchedule))
			{
				continue;
			}

			ElementId categoryId = viewSchedule.Definition?.CategoryId;
			if (categoryId == null || categoryId != new ElementId(BuiltInCategory.OST_Sheets))
			{
				if (!allowEmptySelection)
				{
					throw new InvalidOperationException($"Schedule '{viewSchedule.Name}' không phải Sheet List Schedule.");
				}
				continue;
			}

			IEnumerable<long> sheetIdsInSchedule = new FilteredElementCollector(document, viewSchedule.Id)
				.OfClass(typeof(ViewSheet))
				.Cast<ViewSheet>()
				.Where(sheet => !sheet.IsPlaceholder)
				.Select(sheet => sheet.Id.Value);
			resolvedSheetIds.AddRange(sheetIdsInSchedule);
		}

		List<ViewSheet> sheets = resolvedSheetIds
			.Distinct()
			.Select(idValue => document.GetElement(new ElementId(idValue)) as ViewSheet)
			.Where(sheet => sheet != null && !sheet.IsPlaceholder)
			.Cast<ViewSheet>()
			.ToList();

		if (sheets.Count == 0)
		{
			if (allowEmptySelection)
			{
				return new List<ViewSheet>();
			}

			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong selection.");
		}

		return OrderSheetsForExport(sheets);
	}

	public static List<ViewSheet> OrderSheetsForExport(IEnumerable<ViewSheet> sheets)
	{
		return sheets
			.Select(sheet =>
			{
				bool hasStt = TryGetSheetSttValue(sheet, out int sttValue);
				return new
				{
					Sheet = sheet,
					HasStt = hasStt,
					SttValue = sttValue
				};
			})
			.OrderBy(item => item.HasStt ? 0 : 1)
			.ThenBy(item => item.SttValue)
			.ThenBy(item => item.Sheet.SheetNumber, Comparer<string>.Create(CompareSheetNumberNatural))
			.ThenBy(item => item.Sheet.Id.Value)
			.Select(item => item.Sheet)
			.ToList();
	}

	private static bool TryGetSheetSttValue(ViewSheet sheet, out int sttValue)
	{
		sttValue = 0;
		Parameter sttParameter = sheet.LookupParameter("STT");
		if (sttParameter == null)
		{
			return false;
		}

		if (sttParameter.StorageType == StorageType.Integer)
		{
			sttValue = sttParameter.AsInteger();
			return true;
		}

		string rawValue = sttParameter.AsString();
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			rawValue = sttParameter.AsValueString();
		}

		return !string.IsNullOrWhiteSpace(rawValue) &&
			(int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sttValue) ||
			 int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out sttValue));
	}

	private static int CompareSheetNumberNatural(string left, string right)
	{
		left = left ?? string.Empty;
		right = right ?? string.Empty;
		if (ReferenceEquals(left, right))
		{
			return 0;
		}

		int leftIndex = 0;
		int rightIndex = 0;
		while (leftIndex < left.Length && rightIndex < right.Length)
		{
			bool leftIsDigit = char.IsDigit(left[leftIndex]);
			bool rightIsDigit = char.IsDigit(right[rightIndex]);
			if (leftIsDigit && rightIsDigit)
			{
				int leftStart = leftIndex;
				int rightStart = rightIndex;
				while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
				{
					leftIndex++;
				}
				while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
				{
					rightIndex++;
				}

				string leftDigits = left.Substring(leftStart, leftIndex - leftStart);
				string rightDigits = right.Substring(rightStart, rightIndex - rightStart);
				string leftSignificant = leftDigits.TrimStart('0');
				string rightSignificant = rightDigits.TrimStart('0');
				leftSignificant = leftSignificant.Length == 0 ? "0" : leftSignificant;
				rightSignificant = rightSignificant.Length == 0 ? "0" : rightSignificant;

				int significantLengthComparison = leftSignificant.Length.CompareTo(rightSignificant.Length);
				if (significantLengthComparison != 0)
				{
					return significantLengthComparison;
				}

				int numericComparison = string.CompareOrdinal(leftSignificant, rightSignificant);
				if (numericComparison != 0)
				{
					return numericComparison;
				}

				int digitLengthComparison = leftDigits.Length.CompareTo(rightDigits.Length);
				if (digitLengthComparison != 0)
				{
					return digitLengthComparison;
				}
				continue;
			}

			if (leftIsDigit != rightIsDigit)
			{
				return leftIsDigit ? -1 : 1;
			}

			int characterComparison = char.ToUpperInvariant(left[leftIndex])
				.CompareTo(char.ToUpperInvariant(right[rightIndex]));
			if (characterComparison != 0)
			{
				return characterComparison;
			}

			leftIndex++;
			rightIndex++;
		}

		return left.Length.CompareTo(right.Length);
	}

	public static string BuildBaseFileName(ViewSheet sheet)
	{
		return SanitizeFileName(sheet.SheetNumber + " - " + sheet.Name);
	}

	private static int ExportIndividualPdfsWithVirtualPrinter(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		IReadOnlyDictionary<long, string> baseNamesBySheetId,
		string printerName,
		ElementId printSetupId,
		ICollection<string> errors,
		ExportProgressTracker progress)
	{
		int exportedCount = 0;
		foreach (ViewSheet sheet in sheets)
		{
			if (!progress.Checkpoint("Chuẩn bị PDF qua máy in ảo"))
			{
				break;
			}

			string baseName = baseNamesBySheetId[sheet.Id.Value];
			try
			{
				if (ExportSheetsToVirtualPdf(
					document,
					new List<ViewSheet> { sheet },
					outputFolder,
					baseName,
					printerName,
					printSetupId,
					combineSheets: false,
					continueWaiting: () =>
						progress.Checkpoint(
							"Đang chờ máy in PDF hoàn tất")))
				{
					exportedCount++;
				}
				else
				{
					errors.Add(
						$"PDF thất bại ({sheet.SheetNumber}): " +
						$"máy in '{printerName}' không tạo được file PDF.");
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				errors.Add($"PDF thất bại ({sheet.SheetNumber}): {ex.Message}");
			}
			finally
			{
				progress.Advance(1, "PDF qua máy in ảo");
			}
		}

		return exportedCount;
	}

	private static int ExportIndividualPdfsBatched(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		IReadOnlyDictionary<long, string> baseNamesBySheetId,
		PDFExportQualityType pdfQuality,
		bool alwaysUseRaster,
		ICollection<string> errors,
		ExportProgressTracker progress)
	{
		int exportedCount = 0;
		for (int startIndex = 0; startIndex < sheets.Count; startIndex += PdfBatchSize)
		{
			List<ViewSheet> batch = sheets
				.Skip(startIndex)
				.Take(PdfBatchSize)
				.ToList();
			exportedCount += ExportPdfBatchWithFallback(
				document,
				batch,
				outputFolder,
				baseNamesBySheetId,
				pdfQuality,
				alwaysUseRaster,
				errors,
				progress);
			if (progress.WasCancelled)
			{
				break;
			}
		}

		return exportedCount;
	}

	private static int ExportPdfBatchWithFallback(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		IReadOnlyDictionary<long, string> baseNamesBySheetId,
		PDFExportQualityType pdfQuality,
		bool alwaysUseRaster,
		ICollection<string> errors,
		ExportProgressTracker progress)
	{
		if (sheets.Count == 0)
		{
			return 0;
		}
		if (!progress.Checkpoint("Chuẩn bị PDF"))
		{
			return 0;
		}

		if (sheets.Count == 1)
		{
			ViewSheet sheet = sheets[0];
			string baseName = baseNamesBySheetId[sheet.Id.Value];
			try
			{
				if (ExportSheetsToNativePdf(
					document,
					sheets,
					outputFolder,
					baseName,
					pdfQuality,
					alwaysUseRaster))
				{
					return 1;
				}

				errors.Add($"PDF thất bại ({sheet.SheetNumber}): Revit không tạo được file đầu ra.");
			}
			catch (Exception ex)
			{
				errors.Add($"PDF thất bại ({sheet.SheetNumber}): {ex.Message}");
			}
			finally
			{
				progress.Advance(1, "PDF");
			}

			return 0;
		}

		HashSet<long> completedSheetIds;
		try
		{
			completedSheetIds = ExportPdfBatchToStaging(
				document,
				sheets,
				outputFolder,
				baseNamesBySheetId,
				pdfQuality,
				alwaysUseRaster);
		}
		catch
		{
			completedSheetIds = new HashSet<long>();
		}

		List<ViewSheet> missingSheets = sheets
			.Where(sheet => !completedSheetIds.Contains(sheet.Id.Value))
			.ToList();
		int exportedCount = completedSheetIds.Count;
		progress.Advance(completedSheetIds.Count, "PDF");
		if (missingSheets.Count == 0)
		{
			return exportedCount;
		}

		int midpoint = Math.Max(1, missingSheets.Count / 2);
		List<ViewSheet> firstHalf = missingSheets.Take(midpoint).ToList();
		List<ViewSheet> secondHalf = missingSheets.Skip(midpoint).ToList();
		exportedCount += ExportPdfBatchWithFallback(
			document,
			firstHalf,
			outputFolder,
			baseNamesBySheetId,
			pdfQuality,
			alwaysUseRaster,
			errors,
			progress);
		exportedCount += ExportPdfBatchWithFallback(
			document,
			secondHalf,
			outputFolder,
			baseNamesBySheetId,
			pdfQuality,
			alwaysUseRaster,
			errors,
			progress);
		return exportedCount;
	}

	private static HashSet<long> ExportPdfBatchToStaging(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		IReadOnlyDictionary<long, string> baseNamesBySheetId,
		PDFExportQualityType pdfQuality,
		bool alwaysUseRaster)
	{
		HashSet<long> completedSheetIds = new HashSet<long>();
		string stagingFolder = Path.Combine(
			Path.GetTempPath(),
			PdfStagingRootName,
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagingFolder);

		try
		{
			List<ElementId> sheetIds = sheets.Select(sheet => sheet.Id).ToList();
			using (PDFExportOptions options = CreatePdfExportOptions(
				pdfQuality,
				combine: false,
				fileName: string.Empty,
				alwaysUseRaster))
			{
				ApplySheetNumberAndNameNamingRule(options);
				document.Export(stagingFolder, sheetIds, options);
			}

			List<string> exportedFiles = Directory
				.EnumerateFiles(stagingFolder, "*.pdf", SearchOption.TopDirectoryOnly)
				.ToList();

			foreach (ViewSheet sheet in sheets)
			{
				string expectedStagingBaseName = BuildBaseFileName(sheet);
				int matchingIndex = exportedFiles.FindIndex(path =>
					string.Equals(
						Path.GetFileNameWithoutExtension(path),
						expectedStagingBaseName,
						StringComparison.OrdinalIgnoreCase));
				if (matchingIndex < 0)
				{
					continue;
				}

				string sourcePath = exportedFiles[matchingIndex];
				exportedFiles.RemoveAt(matchingIndex);
				string finalBaseName = baseNamesBySheetId[sheet.Id.Value];
				string targetPath = Path.Combine(outputFolder, finalBaseName + ".pdf");

				try
				{
					ReplaceFileFromStaging(sourcePath, targetPath);
					completedSheetIds.Add(sheet.Id.Value);
				}
				catch
				{
				}
			}
		}
		finally
		{
			if (Directory.Exists(stagingFolder))
			{
				try
				{
					Directory.Delete(stagingFolder, recursive: true);
				}
				catch
				{
				}
			}
		}

		return completedSheetIds;
	}

	private static bool ExportSheetsToVirtualPdf(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		string baseName,
		string printerName,
		ElementId printSetupId,
		bool combineSheets,
		Func<bool>? continueWaiting = null)
	{
		if (sheets.Count == 0)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(printerName))
		{
			throw new InvalidOperationException("Tên máy in PDF ảo không hợp lệ.");
		}

		string stagingFolder = Path.Combine(
			Path.GetTempPath(),
			VirtualPdfStagingRootName,
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagingFolder);

		IDisposable? printerAutoSaveScope = null;
		try
		{
			printerAutoSaveScope =
				ConfigurePdfXChangeLiteAutoSave(printerName);
			string requestedStagingPath = Path.Combine(
				stagingFolder,
				"tm2-output.pdf");
			bool submitted = PrintSheetsWithVirtualPrinter(
				document,
				sheets,
				requestedStagingPath,
				printerName,
				printSetupId,
				combineSheets);
			if (!submitted)
			{
				return false;
			}

			string? stagedPdfPath = WaitForVirtualPrinterPdf(
				stagingFolder,
				requestedStagingPath,
				combineSheets
					? VirtualPrinterCombinedOutputTimeoutMilliseconds
					: VirtualPrinterOutputTimeoutMilliseconds,
				VirtualPrinterFileStabilityMilliseconds,
				continueWaiting);
			if (string.IsNullOrWhiteSpace(stagedPdfPath))
			{
				int timeoutMilliseconds = combineSheets
					? VirtualPrinterCombinedOutputTimeoutMilliseconds
					: VirtualPrinterOutputTimeoutMilliseconds;
				throw new IOException(
					$"Máy in '{printerName}' đã nhận lệnh nhưng không tạo được PDF " +
					$"hoàn chỉnh trong {timeoutMilliseconds / 1000} giây. " +
					"Driver có thể đang chờ hộp thoại Save As.");
			}

			string targetPath = Path.Combine(outputFolder, baseName + ".pdf");
			ReplaceFileFromStaging(stagedPdfPath, targetPath);
			return IsCompletePdfFile(targetPath);
		}
		finally
		{
			printerAutoSaveScope?.Dispose();
			if (Directory.Exists(stagingFolder))
			{
				try
				{
					Directory.Delete(stagingFolder, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static IDisposable? ConfigurePdfXChangeLiteAutoSave(
		string printerName)
	{
		return IsPdfXChangeLitePrinterName(printerName)
			? PdfXChangeLiteAutoSaveScope.Create(printerName)
			: null;
	}

	private static string? FindPdfXChangeLitePdfSaverExecutable()
	{
		string[] programDirectories =
		{
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
		};
		foreach (string programDirectory in programDirectories
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			string candidatePath = Path.Combine(
				programDirectory,
				"Tracker Software",
				"PDF-XChange Lite",
				"pdfSaverL.exe");
			if (File.Exists(candidatePath))
			{
				return candidatePath;
			}
		}

		return null;
	}

	private static IReadOnlyList<string> DumpPdfXChangeLiteOptions(
		string pdfSaverPath,
		string printerName)
	{
		string dumpPath = Path.Combine(
			Path.GetTempPath(),
			"TungMEP2-PdfXChangeLite-" +
			Guid.NewGuid().ToString("N") +
			".txt");
		try
		{
			string arguments =
				"/DumpOptions " + QuoteProcessArgument(dumpPath) +
				" /Printer " + QuoteProcessArgument(printerName) +
				" /NoNotify";
			RunPdfXChangeLiteCommand(pdfSaverPath, arguments);
			if (!File.Exists(dumpPath))
			{
				throw new IOException(
					"PDF-XChange Lite không tạo được file cấu hình tạm.");
			}

			return File.ReadAllLines(dumpPath);
		}
		finally
		{
			if (File.Exists(dumpPath))
			{
				try
				{
					File.Delete(dumpPath);
				}
				catch
				{
				}
			}
		}
	}

	private static void SetPdfXChangeLiteSaveOptions(
		string pdfSaverPath,
		string printerName,
		bool showSaveDialog,
		bool runApplication)
	{
		string options =
			"Save.ShowSaveDialog=" +
			(showSaveDialog ? "true" : "false") +
			";Save.RunApp=" +
			(runApplication ? "true" : "false");
		string arguments =
			"/SetOptions " + QuoteProcessArgument(options) +
			" /Printer " + QuoteProcessArgument(printerName) +
			" /NoNotify";
		RunPdfXChangeLiteCommand(pdfSaverPath, arguments);
	}

	private static void RunPdfXChangeLiteCommand(
		string pdfSaverPath,
		string arguments)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = pdfSaverPath,
			Arguments = arguments,
			CreateNoWindow = true,
			UseShellExecute = false,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		using (Process process = Process.Start(startInfo) ??
			throw new InvalidOperationException(
				"Không thể chạy pdfSaverL.exe của PDF-XChange Lite."))
		{
			if (!process.WaitForExit(PdfXChangeCommandTimeoutMilliseconds))
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				throw new TimeoutException(
					"PDF-XChange Lite không phản hồi khi cấu hình Auto-Save.");
			}
			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException(
					"PDF-XChange Lite từ chối cấu hình Auto-Save " +
					$"(exit code {process.ExitCode}).");
			}
		}
	}

	private static string QuoteProcessArgument(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
	}

	private static bool TryParsePdfXChangeBooleanOption(
		IEnumerable<string> optionLines,
		string optionName,
		out bool value)
	{
		value = false;
		if (optionLines == null || string.IsNullOrWhiteSpace(optionName))
		{
			return false;
		}

		string prefix = optionName + "=";
		string line = optionLines.FirstOrDefault(candidate =>
			!string.IsNullOrWhiteSpace(candidate) &&
			candidate.TrimStart().StartsWith(
				prefix,
				StringComparison.OrdinalIgnoreCase));
		if (string.IsNullOrWhiteSpace(line))
		{
			return false;
		}

		string rawValue = line
			.Trim()
			.Substring(prefix.Length)
			.Split(';')[0]
			.Trim();
		if (rawValue == "1" ||
			rawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase))
		{
			value = true;
			return true;
		}
		if (rawValue == "0" ||
			rawValue.Equals("false", StringComparison.OrdinalIgnoreCase) ||
			rawValue.Equals("no", StringComparison.OrdinalIgnoreCase))
		{
			value = false;
			return true;
		}

		return false;
	}

	private static bool PrintSheetsWithVirtualPrinter(
		Document document,
		IList<ViewSheet> sheets,
		string outputFilePath,
		string printerName,
		ElementId printSetupId,
		bool combineSheets)
	{
		PrintManager printManager = document.PrintManager;
		using (Transaction transaction = new Transaction(
			document,
			"TungMEP2 - Prepare Virtual PDF Print"))
		{
			transaction.Start();
			try
			{
				printManager.SelectNewPrintDriver(printerName);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Không thể chọn máy in '{printerName}'.",
					ex);
			}

			ApplyPrintSetup(printManager, document, printSetupId);
			printManager.PrintRange = Autodesk.Revit.DB.PrintRange.Select;
			printManager.CombinedFile = combineSheets;
			printManager.PrintToFile = true;
			printManager.PrintToFileName = outputFilePath;

			ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
			InSessionViewSheetSet inSession = viewSheetSetting.InSession;
			ViewSet viewSet = new ViewSet();
			foreach (ViewSheet sheet in sheets)
			{
				viewSet.Insert(sheet);
			}
			inSession.Views = viewSet;
			IViewSheetSet orderedSet = inSession;
			orderedSet.IsAutomatic = false;
			orderedSet.OrderedViewList = sheets.Cast<View>().ToList();
			viewSheetSetting.CurrentViewSheetSet = inSession;
			transaction.Commit();
		}

		printManager.Apply();
		return printManager.SubmitPrint();
	}

	private static void ApplyPrintSetup(
		PrintManager printManager,
		Document document,
		ElementId printSetupId)
	{
		PrintSetup printSetup = printManager.PrintSetup;
		if (printSetupId == ElementId.InvalidElementId)
		{
			printSetup.CurrentPrintSetting = printSetup.InSession;
			return;
		}
		if (!(document.GetElement(printSetupId) is PrintSetting printSetting))
		{
			throw new InvalidOperationException(
				"Revit Print Setup đã chọn không còn tồn tại. Hãy chọn lại.");
		}

		printSetup.CurrentPrintSetting = printSetting;
	}

	private static string? WaitForVirtualPrinterPdf(
		string stagingFolder,
		string requestedPath,
		int timeoutMilliseconds,
		int stabilityMilliseconds,
		Func<bool>? continueWaiting)
	{
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
		Dictionary<string, PdfFileObservation> observations =
			new Dictionary<string, PdfFileObservation>(
				StringComparer.OrdinalIgnoreCase);
		while (DateTime.UtcNow < deadline)
		{
			if (continueWaiting != null && !continueWaiting())
			{
				throw new OperationCanceledException();
			}

			IEnumerable<string> candidates = new[]
				{
					requestedPath,
					requestedPath + ".pdf"
				}
				.Concat(
					Directory.Exists(stagingFolder)
						? Directory.EnumerateFiles(
							stagingFolder,
							"*",
							SearchOption.TopDirectoryOnly)
						: Enumerable.Empty<string>())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderByDescending(path =>
					File.Exists(path)
						? File.GetLastWriteTimeUtc(path)
						: DateTime.MinValue);

			foreach (string candidatePath in candidates)
			{
				if (!TryGetCompletePdfSnapshot(
					candidatePath,
					out long fileLength,
					out DateTime lastWriteTimeUtc))
				{
					observations.Remove(candidatePath);
					continue;
				}

				DateTime nowUtc = DateTime.UtcNow;
				if (observations.TryGetValue(
						candidatePath,
						out PdfFileObservation observation) &&
					observation.FileLength == fileLength &&
					observation.LastWriteTimeUtc == lastWriteTimeUtc)
				{
					if ((nowUtc - observation.StableSinceUtc).TotalMilliseconds >=
						Math.Max(0, stabilityMilliseconds))
					{
						return candidatePath;
					}
				}
				else
				{
					observations[candidatePath] = new PdfFileObservation(
						fileLength,
						lastWriteTimeUtc,
						nowUtc);
				}
			}

			Thread.Sleep(VirtualPrinterPollingIntervalMilliseconds);
		}

		return null;
	}

	private sealed class PdfFileObservation
	{
		public long FileLength { get; }

		public DateTime LastWriteTimeUtc { get; }

		public DateTime StableSinceUtc { get; }

		public PdfFileObservation(
			long fileLength,
			DateTime lastWriteTimeUtc,
			DateTime stableSinceUtc)
		{
			FileLength = fileLength;
			LastWriteTimeUtc = lastWriteTimeUtc;
			StableSinceUtc = stableSinceUtc;
		}
	}

	private static bool TryGetCompletePdfSnapshot(
		string filePath,
		out long fileLength,
		out DateTime lastWriteTimeUtc)
	{
		fileLength = 0;
		lastWriteTimeUtc = DateTime.MinValue;
		if (!IsCompletePdfFile(filePath))
		{
			return false;
		}

		try
		{
			FileInfo fileInfo = new FileInfo(filePath);
			fileLength = fileInfo.Length;
			lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			return fileLength > 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool IsCompletePdfFile(string filePath)
	{
		try
		{
			if (!File.Exists(filePath) || new FileInfo(filePath).Length < 10)
			{
				return false;
			}

			using (FileStream stream = new FileStream(
				filePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.None))
			{
				byte[] header = new byte[5];
				if (stream.Read(header, 0, header.Length) != header.Length)
				{
					return false;
				}

				bool hasPdfHeader = header[0] == (byte)'%' &&
					header[1] == (byte)'P' &&
					header[2] == (byte)'D' &&
					header[3] == (byte)'F' &&
					header[4] == (byte)'-';
				if (!hasPdfHeader)
				{
					return false;
				}

				int tailLength = (int)Math.Min(4096L, stream.Length);
				byte[] tail = new byte[tailLength];
				stream.Seek(-tailLength, SeekOrigin.End);
				if (stream.Read(tail, 0, tail.Length) != tail.Length)
				{
					return false;
				}

				byte[] eofMarker =
					{ (byte)'%', (byte)'%', (byte)'E', (byte)'O', (byte)'F' };
				for (int index = tail.Length - eofMarker.Length; index >= 0; index--)
				{
					bool markerMatches = true;
					for (int markerIndex = 0;
						markerIndex < eofMarker.Length;
						markerIndex++)
					{
						if (tail[index + markerIndex] != eofMarker[markerIndex])
						{
							markerMatches = false;
							break;
						}
					}
					if (markerMatches)
					{
						return true;
					}
				}

				return false;
			}
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool IsLikelyPdfVirtualPrinter(string printerName)
	{
		return VirtualPdfPrinterNameHints.Any(hint =>
			printerName.IndexOf(
				hint,
				StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static ElementId GetActivePrintSettingId(Document document)
	{
		try
		{
			IPrintSetting currentPrintSetting =
				document.PrintManager.PrintSetup.CurrentPrintSetting;
			return (currentPrintSetting as Element)?.Id ??
				ElementId.InvalidElementId;
		}
		catch
		{
			return ElementId.InvalidElementId;
		}
	}

	private static string GetActivePrinterName(Document document)
	{
		try
		{
			return document.PrintManager.PrinterName ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool ExportSheetsToNativePdf(
		Document document,
		IList<ViewSheet> sheets,
		string outputFolder,
		string baseName,
		PDFExportQualityType pdfQuality,
		bool alwaysUseRaster)
	{
		if (sheets.Count == 0)
		{
			return false;
		}

		string stagingFolder = Path.Combine(
			Path.GetTempPath(),
			PdfStagingRootName,
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagingFolder);

		try
		{
			List<ElementId> sheetIds = sheets.Select(sheet => sheet.Id).ToList();
			using (PDFExportOptions options = CreatePdfExportOptions(
				pdfQuality,
				combine: true,
				fileName: baseName,
				alwaysUseRaster))
			{
				bool exported = document.Export(stagingFolder, sheetIds, options);
				string stagedPath = Path.Combine(stagingFolder, baseName + ".pdf");
				if (!exported || !File.Exists(stagedPath))
				{
					return false;
				}

				string targetPath = Path.Combine(outputFolder, baseName + ".pdf");
				ReplaceFileFromStaging(stagedPath, targetPath);
				return File.Exists(targetPath);
			}
		}
		finally
		{
			if (Directory.Exists(stagingFolder))
			{
				try
				{
					Directory.Delete(stagingFolder, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static PDFExportOptions CreatePdfExportOptions(
		PDFExportQualityType pdfQuality,
		bool combine,
		string fileName,
		bool alwaysUseRaster)
	{
		return new PDFExportOptions
		{
			AlwaysUseRaster = alwaysUseRaster,
			ColorDepth = ColorDepthType.Color,
			Combine = combine,
			ExportQuality = pdfQuality,
			FileName = fileName ?? string.Empty,
			HideCropBoundaries = true,
			HideReferencePlane = true,
			HideScopeBoxes = true,
			HideUnreferencedViewTags = false,
			MaskCoincidentLines = false,
			PaperFormat = ExportPaperFormat.Default,
			PaperOrientation = PageOrientationType.Auto,
			PaperPlacement = PaperPlacementType.Center,
			RasterQuality = pdfQuality == PDFExportQualityType.DPI300
				? RasterQualityType.Medium
				: RasterQualityType.High,
			ReplaceHalftoneWithThinLines = false,
			StopOnError = combine,
			ViewLinksInBlue = false,
			ZoomPercentage = 100,
			ZoomType = ZoomType.FitToPage
		};
	}

	private static void ApplySheetNumberAndNameNamingRule(PDFExportOptions options)
	{
		using (TableCellCombinedParameterData sheetNumber = TableCellCombinedParameterData.Create())
		using (TableCellCombinedParameterData sheetName = TableCellCombinedParameterData.Create())
		{
			ElementId sheetCategoryId = new ElementId(BuiltInCategory.OST_Sheets);
			sheetNumber.CategoryId = sheetCategoryId;
			sheetNumber.ParamId = new ElementId(BuiltInParameter.SHEET_NUMBER);
			sheetNumber.Prefix = string.Empty;
			sheetNumber.Separator = " - ";
			sheetNumber.Suffix = string.Empty;
			sheetNumber.SampleValue = string.Empty;

			sheetName.CategoryId = sheetCategoryId;
			sheetName.ParamId = new ElementId(BuiltInParameter.SHEET_NAME);
			sheetName.Prefix = string.Empty;
			sheetName.Separator = string.Empty;
			sheetName.Suffix = string.Empty;
			sheetName.SampleValue = string.Empty;

			options.SetNamingRule(new List<TableCellCombinedParameterData>
			{
				sheetNumber,
				sheetName
			});
		}
	}

	private static string ReserveUniqueOutputBaseName(
		string baseName,
		ISet<string> usedNames,
		int maxLength = MaxBaseFileNameLength)
	{
		string normalizedBaseName = FitBaseFileNameLength(
			SanitizeFileName(baseName),
			maxLength);
		int index = 1;
		while (true)
		{
			string duplicateSuffix = index == 1 ? string.Empty : $" ({index})";
			string candidate = FitBaseFileNameLength(
				normalizedBaseName,
				maxLength - duplicateSuffix.Length) + duplicateSuffix;
			if (!usedNames.Contains(candidate))
			{
				usedNames.Add(candidate);
				return candidate;
			}

			index++;
		}
	}

	private static int GetSafeBaseFileNameLength(string outputFolder, string extension)
	{
		string normalizedFolder = Path
			.GetFullPath(outputFolder)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		int availableLength =
			MaxLegacyWindowsPathLength -
			normalizedFolder.Length -
			1 -
			extension.Length;
		if (availableLength < MinimumBaseFileNameLength)
		{
			throw new InvalidOperationException(
				"Đường dẫn thư mục output quá dài. Hãy chọn thư mục có đường dẫn ngắn hơn.");
		}

		return Math.Min(MaxBaseFileNameLength, availableLength);
	}

	private static string FitBaseFileNameLength(string baseName, int maxLength)
	{
		if (baseName.Length <= maxLength)
		{
			return baseName;
		}

		string hashSuffix = "-" + ComputeStableNameHash(baseName);
		int prefixLength = Math.Max(1, maxLength - hashSuffix.Length);
		string prefix = baseName
			.Substring(0, prefixLength)
			.TrimEnd(' ', '.');
		return prefix + hashSuffix;
	}

	private static string ComputeStableNameHash(string value)
	{
		unchecked
		{
			uint hash = 2166136261;
			foreach (char character in value)
			{
				hash ^= character;
				hash *= 16777619;
			}

			return hash.ToString("X8", CultureInfo.InvariantCulture);
		}
	}

	private static string SanitizeFileName(string fileName)
	{
		char[] invalidChars = Path.GetInvalidFileNameChars();
		string sanitized = new string(
			(fileName ?? string.Empty)
				.Select(ch => invalidChars.Contains(ch) ? '_' : ch)
				.ToArray())
			.Trim()
			.TrimEnd('.');

		if (string.IsNullOrWhiteSpace(sanitized))
		{
			sanitized = "Sheet";
		}

		if (sanitized.Length > MaxBaseFileNameLength)
		{
			sanitized = sanitized.Substring(0, MaxBaseFileNameLength).TrimEnd(' ', '.');
		}

		string reservedCandidate = sanitized.Split('.')[0];
		if (ReservedWindowsFileNames.Contains(reservedCandidate))
		{
			sanitized = "_" + sanitized;
		}

		return sanitized;
	}

	public static string NormalizePdfFileBaseName(string fileName)
	{
		string normalized = (fileName ?? string.Empty).Trim();
		if (normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(0, normalized.Length - 4).TrimEnd();
		}

		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		return SanitizeFileName(normalized);
	}

	public static int CountPrintedPdfFilesNeedingRename(string outputFolder)
	{
		if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
		{
			return 0;
		}

		return Directory
			.EnumerateFiles(outputFolder, "*.pdf", SearchOption.TopDirectoryOnly)
			.Count(sourcePath =>
			{
				string sourceFileName = Path.GetFileName(sourcePath);
				string targetFileName = BuildRenamedPdfFileName(sourceFileName);
				return !string.Equals(sourceFileName, targetFileName, StringComparison.OrdinalIgnoreCase);
			});
	}

	public static PdfRenameSummary RenamePrintedPdfFiles(string outputFolder)
	{
		if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
		{
			throw new InvalidOperationException("Output folder không tồn tại.");
		}

		int renamedCount = 0;
		int skippedCount = 0;
		List<string> errors = new List<string>();

		foreach (string sourcePath in Directory
			.EnumerateFiles(outputFolder, "*.pdf", SearchOption.TopDirectoryOnly)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			string sourceFileName = Path.GetFileName(sourcePath);
			string targetFileName = BuildRenamedPdfFileName(sourceFileName);
			if (string.Equals(sourceFileName, targetFileName, StringComparison.OrdinalIgnoreCase))
			{
				skippedCount++;
				continue;
			}

			try
			{
				string targetPath = BuildUniquePdfPath(outputFolder, targetFileName);
				File.Move(sourcePath, targetPath);
				renamedCount++;
			}
			catch (Exception ex)
			{
				errors.Add(sourceFileName + ": " + ex.Message);
			}
		}

		return new PdfRenameSummary(renamedCount, skippedCount, errors);
	}

	private static string BuildRenamedPdfFileName(string sourceFileName)
	{
		const string marker = " - Sheet - ";
		int markerIndex = sourceFileName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (markerIndex < 0)
		{
			return sourceFileName;
		}

		string renamed = sourceFileName.Substring(markerIndex + marker.Length).Trim();
		if (string.IsNullOrWhiteSpace(renamed))
		{
			return sourceFileName;
		}

		string extension = Path.GetExtension(sourceFileName);
		if (!renamed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
		{
			renamed += extension;
		}

		return SanitizeFileName(Path.GetFileNameWithoutExtension(renamed)) + extension;
	}

	private static string BuildUniquePdfPath(string outputFolder, string targetFileName)
	{
		string baseName = Path.GetFileNameWithoutExtension(targetFileName);
		string extension = Path.GetExtension(targetFileName);
		string candidatePath = Path.Combine(outputFolder, targetFileName);
		if (!File.Exists(candidatePath))
		{
			return candidatePath;
		}

		int index = 2;
		while (true)
		{
			candidatePath = Path.Combine(outputFolder, $"{baseName} ({index}){extension}");
			if (!File.Exists(candidatePath))
			{
				return candidatePath;
			}

			index++;
		}
	}

	private static void ReplaceFileFromStaging(string sourcePath, string targetPath)
	{
		string targetFolder = Path.GetDirectoryName(targetPath) ??
			throw new IOException("Không xác định được thư mục file đích.");
		Directory.CreateDirectory(targetFolder);

		string targetFileName = Path.GetFileName(targetPath);
		string operationId = Guid.NewGuid().ToString("N");
		string pendingPath = Path.Combine(
			targetFolder,
			$".tm2-n-{operationId}.tmp");
		string backupPath = Path.Combine(
			targetFolder,
			$".tm2-o-{operationId}.bak");
		bool replacementCompleted = false;

		try
		{
			File.Copy(sourcePath, pendingPath, overwrite: false);
			if (new FileInfo(pendingPath).Length != new FileInfo(sourcePath).Length)
			{
				throw new IOException($"Không xác minh được file tạm: {targetFileName}");
			}

			if (File.Exists(targetPath))
			{
				try
				{
					File.Replace(pendingPath, targetPath, backupPath, ignoreMetadataErrors: true);
				}
				catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException)
				{
					File.Move(targetPath, backupPath);
					try
					{
						File.Move(pendingPath, targetPath);
					}
					catch
					{
						if (!File.Exists(targetPath) && File.Exists(backupPath))
						{
							File.Move(backupPath, targetPath);
						}
						throw;
					}
				}
			}
			else
			{
				File.Move(pendingPath, targetPath);
			}

			if (!File.Exists(targetPath) ||
				new FileInfo(targetPath).Length != new FileInfo(sourcePath).Length)
			{
				throw new IOException($"Không xác minh được file đã ghi đè: {targetFileName}");
			}
			replacementCompleted = true;
		}
		finally
		{
			if (File.Exists(pendingPath))
			{
				try
				{
					File.Delete(pendingPath);
				}
				catch
				{
				}
			}
			if (File.Exists(backupPath))
			{
				try
				{
					if (!replacementCompleted)
					{
						if (File.Exists(targetPath))
						{
							File.Delete(targetPath);
						}
						File.Move(backupPath, targetPath);
					}
					else
					{
						File.Delete(backupPath);
					}
				}
				catch
				{
				}
			}
		}
	}

	private static bool ExportDwgViaLocalStaging(
		Document document,
		ViewSheet sheet,
		DWGExportOptions options,
		string outputFolder,
		string baseName,
		out string error)
	{
		error = string.Empty;
		string stagingFolder = Path.Combine(
			Path.GetTempPath(),
			DwgStagingRootName,
			Guid.NewGuid().ToString("N"));

		try
		{
			Directory.CreateDirectory(stagingFolder);
			List<ElementId> sheetIds = new List<ElementId> { sheet.Id };
			bool exported = document.Export(stagingFolder, baseName, sheetIds, options);
			string expectedFileName = baseName + ".dwg";
			string stagedDwgPath = Path.Combine(stagingFolder, expectedFileName);
			if (!exported || !File.Exists(stagedDwgPath))
			{
				error = $"Revit không tạo được file '{expectedFileName}' trong vùng xuất local.";
				return false;
			}

			CopyDwgStagingArtifacts(stagingFolder, outputFolder, expectedFileName);
			string finalDwgPath = Path.Combine(outputFolder, expectedFileName);
			if (!File.Exists(finalDwgPath))
			{
				error = $"Không chuyển được file '{expectedFileName}' sang thư mục đích.";
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
		finally
		{
			if (Directory.Exists(stagingFolder))
			{
				try
				{
					Directory.Delete(stagingFolder, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static void CopyDwgStagingArtifacts(
		string stagingFolder,
		string outputFolder,
		string expectedDwgFileName)
	{
		List<string> sourceFiles = Directory
			.EnumerateFiles(stagingFolder, "*", SearchOption.TopDirectoryOnly)
			.OrderBy(path =>
				string.Equals(
					Path.GetFileName(path),
					expectedDwgFileName,
					StringComparison.OrdinalIgnoreCase)
					? 1
					: 0)
			.ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (sourceFiles.Count == 0)
		{
			throw new IOException("Vùng xuất DWG local không chứa file đầu ra.");
		}

		foreach (string sourcePath in sourceFiles)
		{
			string fileName = Path.GetFileName(sourcePath);
			string targetPath = Path.Combine(outputFolder, fileName);
			ReplaceFileFromStaging(sourcePath, targetPath);
			if (!File.Exists(targetPath) ||
				new FileInfo(targetPath).Length != new FileInfo(sourcePath).Length)
			{
				throw new IOException($"Không xác minh được file đã chép: {fileName}");
			}
		}
	}

	private static DWGExportOptions CreateDwgExportOptions(Document document, ElementId dwgExportSetupId)
	{
		if (dwgExportSetupId == ElementId.InvalidElementId)
		{
			return new DWGExportOptions();
		}

		if (!(document.GetElement(dwgExportSetupId) is ExportDWGSettings selectedSetup))
		{
			throw new InvalidOperationException("DWG Export Setup đã chọn không còn tồn tại. Hãy chọn lại.");
		}

		// Preserve every option from the selected Revit setup, including MergedViews.
		return selectedSetup.GetDWGExportOptions();
	}
}
