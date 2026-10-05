using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper;
using VP_RevitAPI.Helper.Sheets;
using VP_RevitAPI.State;

namespace VP_RevitAPI.UI;

public partial class UI_PrintSheetsWindow : Window
{
	private const double DefaultWindowWidth = 1180;

	private const double DefaultWindowHeight = 960;

	private const double MinimumWindowWidth = 960;

	private const double MinimumWindowHeight = 720;

	private sealed class SheetRow : INotifyPropertyChanged
	{
		private bool _isSelected;

		private string _proposedStt;

		private string _exportOrderText = string.Empty;

		public event PropertyChangedEventHandler PropertyChanged;

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected == value)
				{
					return;
				}
				_isSelected = value;
				OnPropertyChanged(nameof(IsSelected));
			}
		}

		public string ExportOrderText
		{
			get => _exportOrderText;
			set
			{
				if (string.Equals(_exportOrderText, value, StringComparison.Ordinal))
				{
					return;
				}
				_exportOrderText = value;
				OnPropertyChanged(nameof(ExportOrderText));
			}
		}

		public string SheetNumber { get; }

		public string SheetName { get; }

		public string PrimarySourceDisplay { get; }

		public string SourceDisplay { get; }

		public string SourceDetails { get; }

		public string SourceGroupKey { get; }

		public string RowKey =>
			SourceGroupKey + ":" + Sheet.Id.Value.ToString(CultureInfo.InvariantCulture);

		public string ProposedStt
		{
			get => _proposedStt;
			set
			{
				string normalized = value?.Trim() ?? string.Empty;
				if (string.Equals(_proposedStt, normalized, StringComparison.Ordinal))
				{
					return;
				}
				_proposedStt = normalized;
				OnPropertyChanged(nameof(ProposedStt));
			}
		}

		public ViewSheet Sheet { get; }

		public SheetRow(
			ViewSheet sheet,
			string primarySourceDisplay,
			string sourceDisplay,
			string sourceDetails,
			string sourceGroupKey,
			bool isSelected,
			string proposedStt)
		{
			Sheet = sheet;
			SheetNumber = sheet.SheetNumber;
			SheetName = sheet.Name;
			PrimarySourceDisplay = primarySourceDisplay;
			SourceDisplay = sourceDisplay;
			SourceDetails = sourceDetails;
			SourceGroupKey = sourceGroupKey;
			_isSelected = isSelected;
			_proposedStt = proposedStt?.Trim() ?? string.Empty;
		}

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(
				this,
				new PropertyChangedEventArgs(propertyName));
		}
	}

	private sealed class SheetSourceOption : INotifyPropertyChanged
	{
		private bool _isSelected;

		private int _orderIndex;

		public event PropertyChangedEventHandler PropertyChanged;

		public string DisplayName { get; }

		public string SourceLabel { get; }

		public string DefaultFileName { get; }

		public string SourceKey { get; }

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected == value)
				{
					return;
				}
				_isSelected = value;
				OnPropertyChanged(nameof(IsSelected));
			}
		}

		public string OrderBadge =>
			_orderIndex > 0 ? $"Nhóm {_orderIndex}" : string.Empty;

		public ViewSheetSet? PrintSet { get; }

		public ViewSchedule? SheetSchedule { get; }

		private SheetSourceOption(
			string displayName,
			string sourceLabel,
			string defaultFileName,
			string sourceKey,
			ViewSheetSet? printSet,
			ViewSchedule? sheetSchedule)
		{
			DisplayName = displayName;
			SourceLabel = sourceLabel;
			DefaultFileName = defaultFileName;
			SourceKey = sourceKey;
			PrintSet = printSet;
			SheetSchedule = sheetSchedule;
		}

		public static SheetSourceOption FromPrintSet(ViewSheetSet printSet)
		{
			return new SheetSourceOption(
				"[Print Set] " + printSet.Name,
				"Print Set",
				printSet.Name,
				"PrintSet:" + printSet.Id.Value,
				printSet,
				null);
		}

		public static SheetSourceOption FromSheetSchedule(ViewSchedule schedule)
		{
			return new SheetSourceOption(
				"[Sheet Schedule] " + schedule.Name,
				"Sheet Schedule",
				schedule.Name,
				"SheetSchedule:" + schedule.Id.Value,
				null,
				schedule);
		}

		public List<ViewSheet> ResolveSheets(Document document)
		{
			if (PrintSet != null)
			{
				return SheetExportUtilities.ResolveSheetsFromPrintSet(
					document,
					PrintSet.Id);
			}
			if (SheetSchedule != null)
			{
				return SheetExportUtilities.ResolveSheetsFromSheetListSchedule(
					document,
					SheetSchedule.Id);
			}

			return new List<ViewSheet>();
		}

		public void SetOrderIndex(int orderIndex)
		{
			if (_orderIndex == orderIndex)
			{
				return;
			}
			_orderIndex = orderIndex;
			OnPropertyChanged(nameof(OrderBadge));
		}

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(
				this,
				new PropertyChangedEventArgs(propertyName));
		}
	}

	private sealed class PdfProcessingOption
	{
		public string Name { get; }

		public bool AlwaysUseRaster { get; }

		public PdfProcessingOption(string name, bool alwaysUseRaster)
		{
			Name = name;
			AlwaysUseRaster = alwaysUseRaster;
		}
	}

	private readonly Document _document;

	private readonly UIDocument _uiDocument;

	private readonly List<ViewSheet> _preselectedSheets;

	private readonly List<SheetSourceOption> _selectedSourceOrder =
		new List<SheetSourceOption>();

	private readonly List<SheetSourceOption> _allSources =
		new List<SheetSourceOption>();

	private List<ViewSheet> _resolvedSheets;

	private List<SheetRow> _sheetRows = new List<SheetRow>();

	private string _lastAutoCombinedPdfName = string.Empty;

	private bool _isExportRunning;

	private bool _pauseRequested;

	private bool _cancelRequested;

	private bool _isUpdatingSourceSelection;

	private bool _settingsCollapsed;

	private List<PdfQualityOption> _nativePdfQualityOptions =
		new List<PdfQualityOption>();

	private List<PrintSetupOption> _virtualPdfPrintSetups =
		new List<PrintSetupOption>();

	private string _sourceModeBeforeExport = string.Empty;

	private object? _exportButtonOriginalContent;

	private object? _closeButtonOriginalContent;

	public UI_PrintSheetsWindow(UIDocument uiDocument, IEnumerable<ViewSheet> sheets)
	{
		_uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
		_document = uiDocument.Document;
		_preselectedSheets = sheets.ToList();
		_resolvedSheets = _preselectedSheets.ToList();
		InitializeComponent();
		InitializeWindowPresentation();
		InitializeOutputFolderPickers();
		InitializeSourceMode();
		InitializePdfExportSetupList();
		InitializePdfProcessingList();
		InitializePrinterList();
		InitializePdfMode();
		InitializeDwgExportSetupList();
		OutputFolderTextBox.Text = PrintSheetsRecentOutputFolderStore.LoadPdfOutputFolder();
		DwgOutputFolderTextBox.Text = PrintSheetsRecentOutputFolderStore.LoadDwgOutputFolder();
		Closing += OnWindowClosing;
	}

	private List<ElementId> SuppressSelectionForPdfExport()
	{
		List<ElementId> selectedIds = _uiDocument.Selection
			.GetElementIds()
			.ToList();
		if (selectedIds.Count == 0)
		{
			return selectedIds;
		}

		_uiDocument.Selection.SetElementIds(new List<ElementId>());
		try
		{
			_uiDocument.RefreshActiveView();
		}
		catch
		{
		}

		return selectedIds;
	}

	private void RestoreSelectionAfterExport(IReadOnlyCollection<ElementId> selectedIds)
	{
		List<ElementId> validIds = selectedIds
			.Where(id => id != null && _document.GetElement(id) != null)
			.ToList();
		_uiDocument.Selection.SetElementIds(validIds);
		try
		{
			_uiDocument.RefreshActiveView();
		}
		catch
		{
		}
	}

	private void InitializeWindowPresentation()
	{
		Title = "TungMEP2 - Print Sheets";
		Width = Math.Max(Width, DefaultWindowWidth);
		MinWidth = MinimumWindowWidth;
		MinHeight = MinimumWindowHeight;
		double availableHeight = Math.Max(
			MinimumWindowHeight,
			SystemParameters.WorkArea.Height - 24.0);
		MaxHeight = availableHeight;
		Height = Math.Min(
			Math.Max(Height, DefaultWindowHeight),
			availableHeight);
		ResizeMode = ResizeMode.CanResize;
		SizeToContent = SizeToContent.Manual;
	}

	private void InitializeOutputFolderPickers()
	{
		ConfigureOutputFolderPicker(
			OutputFolderTextBox,
			"Nhập đường dẫn PDF hoặc nhấp đúp để chọn thư mục.",
			"Chọn thư mục xuất PDF",
			OnBrowsePdfOutputFolder);
		ConfigureOutputFolderPicker(
			DwgOutputFolderTextBox,
			"Nhập đường dẫn DWG hoặc nhấp đúp để chọn thư mục.",
			"Chọn thư mục xuất DWG",
			OnBrowseDwgOutputFolder);
	}

	private static void ConfigureOutputFolderPicker(
		System.Windows.Controls.TextBox textBox,
		string toolTip,
		string menuHeader,
		RoutedEventHandler browseHandler)
	{
		textBox.ToolTip = toolTip;
		textBox.MouseDoubleClick += (sender, e) => browseHandler(sender, e);
		ContextMenu contextMenu = new ContextMenu();
		MenuItem browseMenuItem = new MenuItem
		{
			Header = menuHeader + "..."
		};
		browseMenuItem.Click += browseHandler;
		contextMenu.Items.Add(browseMenuItem);
		textBox.ContextMenu = contextMenu;
	}

	private void OnBrowsePdfOutputFolder(object sender, RoutedEventArgs e)
	{
		BrowseOutputFolder(OutputFolderTextBox, "Chọn thư mục xuất PDF");
	}

	private void OnBrowseDwgOutputFolder(object sender, RoutedEventArgs e)
	{
		BrowseOutputFolder(DwgOutputFolderTextBox, "Chọn thư mục xuất DWG");
	}

	private static void BrowseOutputFolder(System.Windows.Controls.TextBox targetTextBox, string description)
	{
		using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
		{
			dialog.Description = description;
			dialog.ShowNewFolderButton = true;
			string currentPath = Environment.ExpandEnvironmentVariables(targetTextBox.Text.Trim());
			if (Directory.Exists(currentPath))
			{
				dialog.SelectedPath = currentPath;
			}
			if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				targetTextBox.Text = dialog.SelectedPath;
			}
		}
	}

	private void OnExportClick(object sender, RoutedEventArgs e)
	{
		if (_isExportRunning)
		{
			TogglePause();
			return;
		}

		bool closeAfterExport = false;
		string pdfOutputFolder = string.Empty;
		string dwgOutputFolder = string.Empty;
		List<ElementId>? selectionBeforePdfExport = null;
		HashSet<string> outputFoldersToOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			IReadOnlyList<ViewSheet> sheets = ResolveExportSheets();
			if (sheets.Count == 0)
			{
				TaskDialog.Show("Print Sheets", "Hãy chọn ít nhất một sheet để export.");
				return;
			}
			bool exportPdf = ExportPdfCheckBox.IsChecked == true;
			bool exportDwg = ExportDwgCheckBox.IsChecked == true;
			if (!exportPdf && !exportDwg)
			{
				TaskDialog.Show("Print Sheets", "Hãy chọn ít nhất một định dạng export: PDF hoặc DWG.");
				return;
			}
			if (exportPdf)
			{
				pdfOutputFolder = OutputFolderTextBox.Text.Trim();
				if (string.IsNullOrWhiteSpace(pdfOutputFolder))
				{
					TaskDialog.Show("Print Sheets", "Thư mục xuất PDF không hợp lệ.");
					return;
				}
				pdfOutputFolder = SheetExportUtilities.PrepareOutputFolder(pdfOutputFolder);
				OutputFolderTextBox.Text = pdfOutputFolder;
			}
			if (exportDwg)
			{
				dwgOutputFolder = DwgOutputFolderTextBox.Text.Trim();
				if (string.IsNullOrWhiteSpace(dwgOutputFolder))
				{
					TaskDialog.Show("Print Sheets", "Thư mục xuất DWG không hợp lệ.");
					return;
				}
				dwgOutputFolder = SheetExportUtilities.PrepareOutputFolder(dwgOutputFolder);
				DwgOutputFolderTextBox.Text = dwgOutputFolder;
			}
			ElementId printSetupId = ElementId.InvalidElementId;
			string printerName = null;
			PDFExportQualityType pdfQuality = PDFExportQualityType.DPI300;
			bool alwaysUseRaster = false;
			PrinterOption? selectedPdfEngine = null;
			if (exportPdf)
			{
				if (!(PrinterComboBox.SelectedItem is PrinterOption selectedPrinter))
				{
					TaskDialog.Show("Print Sheets", "Hãy chọn công cụ xuất PDF.");
					return;
				}
				selectedPdfEngine = selectedPrinter;
				if (selectedPrinter.IsNativePdf)
				{
					if (!(PdfExportSetupComboBox.SelectedItem is PdfQualityOption selectedPrintSetup))
					{
						TaskDialog.Show("Print Sheets", "Hãy chọn chất lượng Native PDF.");
						return;
					}
					if (!(PdfOutputProcessingComboBox.SelectedItem is PdfProcessingOption selectedProcessing))
					{
						TaskDialog.Show("Print Sheets", "Hãy chọn chế độ xử lý Native PDF.");
						return;
					}
					pdfQuality = selectedPrintSetup.ExportQuality;
					alwaysUseRaster = selectedProcessing.AlwaysUseRaster;
				}
				else
				{
					if (!(PdfExportSetupComboBox.SelectedItem is PrintSetupOption selectedPrintSetup))
					{
						TaskDialog.Show("Print Sheets", "Hãy chọn Revit Print Setup cho máy in ảo.");
						return;
					}
					if (string.IsNullOrWhiteSpace(selectedPrinter.DriverName))
					{
						TaskDialog.Show("Print Sheets", "Tên máy in PDF ảo không hợp lệ.");
						return;
					}
					printSetupId = selectedPrintSetup.Id;
					printerName = selectedPrinter.DriverName;
					alwaysUseRaster = false;
				}
			}
			bool combinePdf = exportPdf && CombinePdfCheckBox.IsChecked == true;
			string combinedPdfName = null;
			if (combinePdf)
			{
				string proposedName = CombinedPdfNameTextBox.Text.Trim();
				if (string.IsNullOrWhiteSpace(proposedName) &&
					_preselectedSheets.Count == 0)
				{
					proposedName = BuildDefaultCombinedPdfName();
				}
				if (string.IsNullOrWhiteSpace(proposedName))
				{
					TaskDialog.Show("Print Sheets", "Hãy nhập tên file PDF khi dùng Combine 1 PDF.");
					return;
				}
				combinedPdfName = SheetExportUtilities.NormalizePdfFileBaseName(proposedName);
				if (string.IsNullOrWhiteSpace(combinedPdfName))
				{
					TaskDialog.Show("Print Sheets", "Tên file PDF không hợp lệ.");
					return;
				}
				CombinedPdfNameTextBox.Text = combinedPdfName;
			}
			ElementId dwgExportSetupId = ElementId.InvalidElementId;
			if (exportDwg && DwgExportSetupComboBox.SelectedItem is DwgExportSetupOption selectedDwgSetup)
			{
				dwgExportSetupId = selectedDwgSetup.Id;
			}
			if (exportPdf)
			{
				selectionBeforePdfExport = SuppressSelectionForPdfExport();
			}
			BeginExportRun();
			ExportSummary summary = SheetExportUtilities.ExportSheets(
				_document,
				sheets.ToList(),
				pdfOutputFolder,
				dwgOutputFolder,
				exportPdf,
				exportDwg,
				pdfQuality,
				combinePdf,
				combinedPdfName,
				dwgExportSetupId,
				WaitForExportPermission,
				UpdateExportProgress,
				alwaysUseRaster,
				selectedPdfEngine?.IsNativePdf == false ? printerName : null,
				selectedPdfEngine?.IsNativePdf == false ? printSetupId : null);
			closeAfterExport = true;
			bool hasAnySuccess = summary.PdfCount > 0 || summary.DwgCount > 0;
			if (hasAnySuccess)
			{
				PrintSheetsRecentOutputFolderStore.Save(pdfOutputFolder, dwgOutputFolder);
			}
			if (summary.PdfCount > 0 && !string.IsNullOrWhiteSpace(pdfOutputFolder))
			{
				outputFoldersToOpen.Add(pdfOutputFolder);
			}
			if (summary.DwgCount > 0 && !string.IsNullOrWhiteSpace(dwgOutputFolder))
			{
				outputFoldersToOpen.Add(dwgOutputFolder);
			}
			if (summary.WasCancelled)
			{
				string cancelledDetails = summary.Errors.Count > 0
					? "\n\nMột số lỗi trước khi ngưng:\n" + string.Join("\n", summary.Errors.Take(5))
					: string.Empty;
				TaskDialog.Show(
					"Print Sheets",
					$"Đã ngưng xuất.\nPDF: {summary.PdfCount}, DWG: {summary.DwgCount}.{cancelledDetails}");
			}
			else if (!hasAnySuccess)
			{
				string errorMessage = ((summary.Errors.Count > 0) ? string.Join("\n", summary.Errors.Take(5)) : "Không export được file nào.");
				TaskDialog.Show("Print Sheets", "Export thất bại.\n" + errorMessage);
			}
			else if (summary.Errors.Count > 0)
			{
				string warningDetails = string.Join("\n", summary.Errors.Take(5));
				TaskDialog.Show("Print Sheets", $"Đã export: PDF {summary.PdfCount}, DWG {summary.DwgCount}.\n\nMột số sheet lỗi:\n{warningDetails}");
			}
			else
			{
				TaskDialog.Show("Print Sheets", $"Đã export thành công: PDF {summary.PdfCount}, DWG {summary.DwgCount}.");
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Print Sheets", ex.Message);
		}
		finally
		{
			if (_isExportRunning)
			{
				EndExportRun();
			}
			if (selectionBeforePdfExport != null)
			{
				try
				{
					RestoreSelectionAfterExport(selectionBeforePdfExport);
				}
				catch
				{
				}
			}
			if (closeAfterExport)
			{
				Close();
			}
			foreach (string outputFolder in outputFoldersToOpen)
			{
				OpenOutputFolder(outputFolder);
			}
		}
	}

	private void OnExportPdfCheckedChanged(object sender, RoutedEventArgs e)
	{
		if (CombinePdfCheckBox != null && PdfExportSetupComboBox != null && PrinterComboBox != null && CombinedPdfNameTextBox != null)
		{
			if (ExportPdfCheckBox.IsChecked != true)
			{
				CombinePdfCheckBox.IsChecked = false;
			}
			UpdatePdfControls();
		}
	}

	private void OnSheetSourcesSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// ListBox selection now only chooses which source group the Up/Down
		// buttons operate on. Inclusion is controlled by the row checkbox.
	}

	private void OnSheetSourceCheckedChanged(object sender, RoutedEventArgs e)
	{
		if (_preselectedSheets.Count > 0 || _isUpdatingSourceSelection)
		{
			return;
		}

		if (!(sender is CheckBox checkBox) ||
			!(checkBox.DataContext is SheetSourceOption source))
		{
			return;
		}

		source.IsSelected = checkBox.IsChecked == true;
		if (source.IsSelected)
		{
			if (!_selectedSourceOrder.Contains(source))
			{
				_selectedSourceOrder.Add(source);
			}
		}
		else
		{
			_selectedSourceOrder.Remove(source);
		}

		UpdateSourceOrderPresentation(source);
		RefreshSheetsFromSelectedSources();
	}

	private void OnSelectAllSourcesClick(object sender, RoutedEventArgs e)
	{
		if (_preselectedSheets.Count > 0)
		{
			return;
		}

		_selectedSourceOrder.Clear();
		_isUpdatingSourceSelection = true;
		try
		{
			foreach (SheetSourceOption source in _allSources)
			{
				source.IsSelected = true;
				_selectedSourceOrder.Add(source);
			}
		}
		finally
		{
			_isUpdatingSourceSelection = false;
		}
		UpdateSourceOrderPresentation(_selectedSourceOrder.FirstOrDefault());
		RefreshSheetsFromSelectedSources();
	}

	private void OnSelectNoSourcesClick(object sender, RoutedEventArgs e)
	{
		if (_preselectedSheets.Count > 0)
		{
			return;
		}

		_isUpdatingSourceSelection = true;
		try
		{
			foreach (SheetSourceOption source in _allSources)
			{
				source.IsSelected = false;
			}
		}
		finally
		{
			_isUpdatingSourceSelection = false;
		}
		_selectedSourceOrder.Clear();
		UpdateSourceOrderPresentation(null);
		RefreshSheetsFromSelectedSources();
	}

	private void OnMoveSourceUpClick(object sender, RoutedEventArgs e)
	{
		MoveCurrentSourceGroup(-1);
	}

	private void OnMoveSourceDownClick(object sender, RoutedEventArgs e)
	{
		MoveCurrentSourceGroup(1);
	}

	private void OnToggleSettingsClick(object sender, RoutedEventArgs e)
	{
		_settingsCollapsed = !_settingsCollapsed;
		System.Windows.Visibility visibility = _settingsCollapsed
			? System.Windows.Visibility.Collapsed
			: System.Windows.Visibility.Visible;
		SourceControlsPanel.Visibility = visibility;
		ExportSettingsPanel.Visibility = visibility;
		ToggleSettingsButton.Content = _settingsCollapsed
			? "Hiện thiết lập"
			: "Thu gọn thiết lập";
	}

	private void MoveCurrentSourceGroup(int direction)
	{
		if (!(SheetSourcesListBox.SelectedItem is SheetSourceOption source) ||
			!source.IsSelected)
		{
			TaskDialog.Show(
				"Print Sheets",
				"Hãy chọn một nguồn đang được tích để thay đổi thứ tự nhóm.");
			return;
		}

		int currentIndex = _selectedSourceOrder.IndexOf(source);
		int targetIndex = currentIndex + direction;
		if (currentIndex < 0 ||
			targetIndex < 0 ||
			targetIndex >= _selectedSourceOrder.Count)
		{
			return;
		}

		_selectedSourceOrder.RemoveAt(currentIndex);
		_selectedSourceOrder.Insert(targetIndex, source);
		UpdateSourceOrderPresentation(source);
		RefreshSheetsFromSelectedSources();
	}

	private void UpdateSourceOrderPresentation(SheetSourceOption currentSource)
	{
		foreach (SheetSourceOption source in _allSources)
		{
			source.SetOrderIndex(0);
		}
		for (int i = 0; i < _selectedSourceOrder.Count; i++)
		{
			_selectedSourceOrder[i].SetOrderIndex(i + 1);
		}

		List<SheetSourceOption> displayOrder = _selectedSourceOrder
			.Concat(_allSources.Where(source => !source.IsSelected))
			.Distinct()
			.ToList();

		_isUpdatingSourceSelection = true;
		try
		{
			SheetSourcesListBox.ItemsSource = displayOrder;
			SheetSourcesListBox.SelectedItem =
				currentSource != null && displayOrder.Contains(currentSource)
					? currentSource
					: displayOrder.FirstOrDefault();
		}
		finally
		{
			_isUpdatingSourceSelection = false;
		}
	}

	private void RefreshSheetsFromSelectedSources()
	{
		List<SheetSourceOption> selectedSources =
			_selectedSourceOrder.ToList();
		if (selectedSources.Count == 0)
		{
			_resolvedSheets = new List<ViewSheet>();
			ApplySheetRows(new List<SheetRow>());
			SourceModeTextBlock.Text = "Chọn ít nhất một nguồn sheet";
			return;
		}

		List<(ViewSheet Sheet, SheetSourceOption Source)> occurrences =
			new List<(ViewSheet Sheet, SheetSourceOption Source)>();
		Dictionary<long, List<SheetSourceOption>> allSourcesBySheetId =
			new Dictionary<long, List<SheetSourceOption>>();
		List<string> sourceErrors = new List<string>();
		foreach (SheetSourceOption source in selectedSources)
		{
			try
			{
				HashSet<long> seenInSource = new HashSet<long>();
				foreach (ViewSheet sheet in source.ResolveSheets(_document))
				{
					long sheetId = sheet.Id.Value;
					if (!seenInSource.Add(sheetId))
					{
						continue;
					}
					occurrences.Add((sheet, source));
					if (!allSourcesBySheetId.TryGetValue(
						sheetId,
						out List<SheetSourceOption> sheetSources))
					{
						sheetSources = new List<SheetSourceOption>();
						allSourcesBySheetId.Add(sheetId, sheetSources);
					}
					if (!sheetSources.Contains(source))
					{
						sheetSources.Add(source);
					}
				}
			}
			catch (Exception ex)
			{
				sourceErrors.Add(source.DisplayName + ": " + ex.Message);
			}
		}

		Dictionary<string, SheetRow> previousRows = _sheetRows
			.GroupBy(row => row.RowKey)
			.ToDictionary(group => group.Key, group => group.First());
		Dictionary<string, Dictionary<long, int>> previousPositionByGroup =
			_sheetRows
				.GroupBy(row => row.SourceGroupKey)
				.ToDictionary(
					group => group.Key,
					group => group
						.Select((row, index) => new { row.Sheet.Id.Value, Index = index })
						.ToDictionary(item => item.Value, item => item.Index));

		List<(ViewSheet Sheet, SheetSourceOption Source)> orderedOccurrences =
			selectedSources
			.SelectMany(source =>
			{
				previousPositionByGroup.TryGetValue(
					source.SourceKey,
					out Dictionary<long, int> previousPositions);
				return occurrences
					.Where(item => item.Source == source)
					.Select((item, defaultIndex) => new { Item = item, DefaultIndex = defaultIndex })
					.OrderBy(item =>
						previousPositions != null &&
						previousPositions.TryGetValue(
							item.Item.Sheet.Id.Value,
							out int previousPosition)
							? previousPosition
							: int.MaxValue)
					.ThenBy(item => item.DefaultIndex)
					.Select(item => item.Item);
			})
			.ToList();
		_resolvedSheets = orderedOccurrences
			.Select(item => item.Sheet)
			.ToList();

		List<SheetRow> rows = new List<SheetRow>();
		HashSet<long> firstOccurrenceIds = new HashSet<long>();
		foreach ((ViewSheet Sheet, SheetSourceOption Source) occurrence in
			orderedOccurrences)
		{
			ViewSheet sheet = occurrence.Sheet;
			SheetSourceOption source = occurrence.Source;
			long sheetId = sheet.Id.Value;
			int sourceOrder = selectedSources.IndexOf(source) + 1;
			string rowKey = source.SourceKey + ":" +
				sheetId.ToString(CultureInfo.InvariantCulture);
			previousRows.TryGetValue(rowKey, out SheetRow previousRow);
			SheetRow previousOccurrence = previousRow ?? _sheetRows
				.FirstOrDefault(row => row.Sheet.Id.Value == sheetId);
			List<SheetSourceOption> sheetSources = allSourcesBySheetId[sheetId];
			string sourceDetails = source.DisplayName;
			if (sheetSources.Count > 1)
			{
				sourceDetails +=
					"\nSheet này cũng xuất hiện trong: " +
					string.Join(
						"; ",
						sheetSources
							.Where(other => other != source)
							.Select(other => other.DisplayName));
			}
			rows.Add(new SheetRow(
				sheet,
				$"{sourceOrder:00}. {source.DisplayName}",
				source.DefaultFileName,
				sourceDetails,
				source.SourceKey,
				previousRow?.IsSelected ?? firstOccurrenceIds.Add(sheetId),
				previousOccurrence?.ProposedStt ?? ReadSheetStt(sheet)));
		}
		ApplySheetRows(rows);
		SourceModeTextBlock.Text =
			$"{selectedSources.Count} nhóm nguồn / {_resolvedSheets.Count} dòng • sheet trùng ở nguồn sau mặc định bỏ tích";

		if (CombinePdfCheckBox.IsChecked == true)
		{
			TryApplyDefaultCombinedPdfName(force: false);
		}
		if (sourceErrors.Count > 0)
		{
			TaskDialog.Show(
				"Print Sheets",
				"Một số nguồn không đọc được:\n" +
				string.Join("\n", sourceErrors.Take(5)));
		}
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		if (_isExportRunning)
		{
			RequestStop();
			return;
		}

		Close();
	}

	private void BeginExportRun()
	{
		_isExportRunning = true;
		_pauseRequested = false;
		_cancelRequested = false;
		_sourceModeBeforeExport = SourceModeTextBlock.Text;
		_exportButtonOriginalContent = ExportButton.Content;
		_closeButtonOriginalContent = CloseButton.Content;

		ExportButton.Content = "Tạm dừng";
		ExportButton.IsEnabled = true;
		CloseButton.Content = "Ngưng xuất";
		CloseButton.IsEnabled = true;
		SourceModeTextBlock.Text = "Đang chuẩn bị xuất...";

		OutputFolderTextBox.IsEnabled = false;
		DwgOutputFolderTextBox.IsEnabled = false;
		SheetSourcesListBox.IsEnabled = false;
		SourceControlsPanel.IsEnabled = false;
		SheetListControlsPanel.IsEnabled = false;
		SttControlsPanel.IsEnabled = false;
		ExportPdfCheckBox.IsEnabled = false;
		PdfExportSetupComboBox.IsEnabled = false;
		PdfOutputProcessingComboBox.IsEnabled = false;
		PrinterComboBox.IsEnabled = false;
		CombinePdfCheckBox.IsEnabled = false;
		CombinedPdfNameTextBox.IsEnabled = false;
		ExportDwgCheckBox.IsEnabled = false;
		DwgExportSetupComboBox.IsEnabled = false;
		SheetsDataGrid.IsEnabled = false;
	}

	private void EndExportRun()
	{
		_isExportRunning = false;
		_pauseRequested = false;
		_cancelRequested = false;

		ExportButton.Content = _exportButtonOriginalContent ?? "Export";
		ExportButton.IsEnabled = true;
		CloseButton.Content = _closeButtonOriginalContent ?? "Close";
		CloseButton.IsEnabled = true;
		SourceModeTextBlock.Text = _sourceModeBeforeExport;

		SheetSourcesListBox.IsEnabled = _preselectedSheets.Count == 0;
		SourceControlsPanel.IsEnabled = _preselectedSheets.Count == 0;
		SheetListControlsPanel.IsEnabled = true;
		SttControlsPanel.IsEnabled = true;
		ExportPdfCheckBox.IsEnabled = true;
		ExportDwgCheckBox.IsEnabled = true;
		SheetsDataGrid.IsEnabled = true;
		UpdatePdfControls();
		bool exportDwg = ExportDwgCheckBox.IsChecked == true;
		DwgOutputFolderTextBox.IsEnabled = exportDwg;
		DwgExportSetupComboBox.IsEnabled = exportDwg && DwgExportSetupComboBox.Items.Count > 0;
	}

	private void TogglePause()
	{
		if (!_isExportRunning || _cancelRequested)
		{
			return;
		}

		_pauseRequested = !_pauseRequested;
		ExportButton.Content = _pauseRequested ? "Tiếp tục" : "Tạm dừng";
		SourceModeTextBlock.Text = _pauseRequested
			? "Đã tạm dừng - nhấn Tiếp tục hoặc Ngưng xuất"
			: "Đang tiếp tục xuất...";
	}

	private void RequestStop()
	{
		if (!_isExportRunning || _cancelRequested)
		{
			return;
		}

		_cancelRequested = true;
		_pauseRequested = false;
		ExportButton.IsEnabled = false;
		CloseButton.Content = "Đang ngưng...";
		SourceModeTextBlock.Text = "Đang chờ Revit hoàn tất phần hiện tại để ngưng...";
	}

	private bool WaitForExportPermission()
	{
		PumpDispatcher();
		while (_pauseRequested && !_cancelRequested)
		{
			PumpDispatcher();
			Thread.Sleep(50);
		}

		PumpDispatcher();
		return !_cancelRequested;
	}

	private void UpdateExportProgress(string phase, int completed, int total)
	{
		if (_cancelRequested)
		{
			SourceModeTextBlock.Text = "Đang chờ Revit hoàn tất phần hiện tại để ngưng...";
			return;
		}

		if (_pauseRequested)
		{
			SourceModeTextBlock.Text = $"Đã tạm dừng - {completed}/{total}";
			return;
		}

		SourceModeTextBlock.Text = $"{phase}: {completed}/{total}";
	}

	private static void PumpDispatcher()
	{
		DispatcherFrame frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(
			DispatcherPriority.Background,
			new DispatcherOperationCallback(state =>
			{
				((DispatcherFrame)state).Continue = false;
				return null;
			}),
			frame);
		Dispatcher.PushFrame(frame);
	}

	private void OnWindowClosing(object sender, CancelEventArgs e)
	{
		if (!_isExportRunning)
		{
			return;
		}

		e.Cancel = true;
		RequestStop();
	}

	private void OnChangePdfNameClick(object sender, RoutedEventArgs e)
	{
		try
		{
			string outputFolder = OutputFolderTextBox.Text.Trim();
			if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
			{
				TaskDialog.Show("Print Sheets", "Thư mục PDF không tồn tại.");
				return;
			}
			int candidateCount = SheetExportUtilities.CountPrintedPdfFilesNeedingRename(outputFolder);
			if (candidateCount == 0)
			{
				TaskDialog.Show("Print Sheets", "Không có file PDF legacy nào cần đổi tên.");
				return;
			}
			TaskDialog confirmation = new TaskDialog("Print Sheets")
			{
				MainInstruction = $"Đổi tên {candidateCount} file PDF legacy?",
				MainContent = "Tool chỉ xử lý file có chuỗi ' - Sheet - ' trong tên. File đích sẽ không ghi đè file đã tồn tại.",
				CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
				DefaultButton = TaskDialogResult.No
			};
			if (confirmation.Show() != TaskDialogResult.Yes)
			{
				return;
			}
			PdfRenameSummary summary = SheetExportUtilities.RenamePrintedPdfFiles(outputFolder);
			if (summary.RenamedCount == 0 && summary.Errors.Count == 0)
			{
				TaskDialog.Show("Print Sheets", "Không có file PDF nào cần đổi tên.");
			}
			else if (summary.Errors.Count > 0)
			{
				string details = string.Join("\n", summary.Errors.Take(5));
				TaskDialog.Show("Print Sheets", $"Đã đổi tên: {summary.RenamedCount}\nBỏ qua: {summary.SkippedCount}\n\nLỗi:\n{details}");
			}
			else
			{
				TaskDialog.Show("Print Sheets", $"Đã đổi tên: {summary.RenamedCount}\nBỏ qua: {summary.SkippedCount}");
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Print Sheets", ex.Message);
		}
	}

	private void OnSelectAllSheetsClick(object sender, RoutedEventArgs e)
	{
		SetSheetSelectionState(isSelected: true);
	}

	private void OnSelectNoSheetsClick(object sender, RoutedEventArgs e)
	{
		SetSheetSelectionState(isSelected: false);
	}

	private void OnSheetExportSelectionChanged(object sender, RoutedEventArgs e)
	{
		if (sender is CheckBox checkBox &&
			checkBox.DataContext is SheetRow row)
		{
			row.IsSelected = checkBox.IsChecked == true;
			RefreshExportOrder();
		}
	}

	private void OnMoveSheetUpClick(object sender, RoutedEventArgs e)
	{
		MoveSelectedSheets(-1);
	}

	private void OnMoveSheetDownClick(object sender, RoutedEventArgs e)
	{
		MoveSelectedSheets(1);
	}

	private void MoveSelectedSheets(int direction)
	{
		HashSet<SheetRow> selectedRows = SheetsDataGrid.SelectedItems
			.Cast<SheetRow>()
			.ToHashSet();
		if (selectedRows.Count == 0 &&
			SheetsDataGrid.SelectedItem is SheetRow currentRow)
		{
			selectedRows.Add(currentRow);
		}
		if (selectedRows.Count == 0)
		{
			TaskDialog.Show(
				"Print Sheets",
				"Hãy chọn ít nhất một dòng sheet cần di chuyển.");
			return;
		}

		List<SheetRow> rows = _sheetRows.ToList();
		bool moved = false;
		if (direction < 0)
		{
			for (int i = 1; i < rows.Count; i++)
			{
				if (selectedRows.Contains(rows[i]) &&
					!selectedRows.Contains(rows[i - 1]) &&
					string.Equals(
						rows[i].SourceGroupKey,
						rows[i - 1].SourceGroupKey,
						StringComparison.Ordinal))
				{
					SheetRow movingRow = rows[i];
					rows[i] = rows[i - 1];
					rows[i - 1] = movingRow;
					moved = true;
				}
			}
		}
		else
		{
			for (int i = rows.Count - 2; i >= 0; i--)
			{
				if (selectedRows.Contains(rows[i]) &&
					!selectedRows.Contains(rows[i + 1]) &&
					string.Equals(
						rows[i].SourceGroupKey,
						rows[i + 1].SourceGroupKey,
						StringComparison.Ordinal))
				{
					SheetRow movingRow = rows[i];
					rows[i] = rows[i + 1];
					rows[i + 1] = movingRow;
					moved = true;
				}
			}
		}

		if (!moved)
		{
			return;
		}

		ApplySheetRows(rows);
		foreach (SheetRow selectedRow in selectedRows)
		{
			SheetsDataGrid.SelectedItems.Add(selectedRow);
		}
		SttStatusTextBlock.Text =
			"Thứ tự PDF đã thay đổi. Bấm Tạo STT nếu muốn STT đi theo thứ tự mới.";
	}

	private void OnGenerateSttClick(object sender, RoutedEventArgs e)
	{
		if (!TryGetSttStart(out int start))
		{
			return;
		}

		List<SheetRow> selectedRows = _sheetRows
			.Where(row => row.IsSelected)
			.ToList();
		if (selectedRows.Count == 0)
		{
			TaskDialog.Show(
				"Print Sheets",
				"Hãy tích ít nhất một sheet trước khi tạo STT.");
			return;
		}

		Dictionary<long, string> sttBySheetId =
			new Dictionary<long, string>();
		int nextStt = start;
		foreach (SheetRow row in selectedRows)
		{
			long sheetId = row.Sheet.Id.Value;
			if (!sttBySheetId.TryGetValue(sheetId, out string proposedStt))
			{
				proposedStt = nextStt
					.ToString(CultureInfo.InvariantCulture);
				sttBySheetId.Add(sheetId, proposedStt);
				nextStt++;
			}
			foreach (SheetRow occurrence in _sheetRows
				.Where(candidate => candidate.Sheet.Id.Value == sheetId))
			{
				occurrence.ProposedStt = proposedStt;
			}
		}
		SttStatusTextBlock.Text =
			$"Đã tạo STT nháp {start}…{nextStt - 1} cho {sttBySheetId.Count} sheet theo đúng thứ tự PDF. Hãy kiểm tra rồi bấm Ghi STT.";
	}

	private void OnApplySttClick(object sender, RoutedEventArgs e)
	{
		try
		{
			CommitSheetGridEdits();
			List<SheetRow> selectedRows = _sheetRows
				.Where(row => row.IsSelected)
				.ToList();
			if (selectedRows.Count == 0)
			{
				TaskDialog.Show(
					"Print Sheets",
					"Hãy tích ít nhất một sheet trước khi ghi STT.");
				return;
			}

			List<SheetRow> uniqueSheetRows = new List<SheetRow>();
			foreach (IGrouping<long, SheetRow> sheetGroup in
				selectedRows.GroupBy(row => row.Sheet.Id.Value))
			{
				List<string> proposedValues = sheetGroup
					.Select(row => row.ProposedStt?.Trim() ?? string.Empty)
					.Distinct(StringComparer.Ordinal)
					.ToList();
				if (proposedValues.Count > 1)
				{
					TaskDialog.Show(
						"Print Sheets",
						$"Sheet {sheetGroup.First().SheetNumber} xuất hiện ở nhiều nguồn nhưng đang có nhiều giá trị STT khác nhau. Hãy dùng cùng một STT cho các dòng này.");
					return;
				}
				uniqueSheetRows.Add(sheetGroup.First());
			}

			HashSet<int> seenValues = new HashSet<int>();
			foreach (SheetRow row in uniqueSheetRows)
			{
				if (!int.TryParse(
						row.ProposedStt,
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out int sttValue) ||
					sttValue < 1)
				{
					TaskDialog.Show(
						"Print Sheets",
						$"STT của sheet {row.SheetNumber} phải là số nguyên từ 1 trở lên.");
					return;
				}
				if (!seenValues.Add(sttValue))
				{
					TaskDialog.Show(
						"Print Sheets",
						$"STT {sttValue} đang bị trùng trong danh sách sheet được tích.");
					return;
				}
			}

			int updatedCount = SheetUtilities.SetSttValues(
				_document,
				uniqueSheetRows.Select(row =>
					new KeyValuePair<ViewSheet, string>(
						row.Sheet,
						row.ProposedStt)));
			foreach (SheetRow sourceRow in uniqueSheetRows)
			{
				foreach (SheetRow occurrence in _sheetRows.Where(row =>
					row.Sheet.Id.Value == sourceRow.Sheet.Id.Value))
				{
					occurrence.ProposedStt = sourceRow.ProposedStt;
				}
			}
			SttStatusTextBlock.Text =
				$"Đã ghi STT vào {updatedCount} sheet. Export sẽ giữ nguyên các giá trị này.";
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Print Sheets - Set STT", ex.Message);
		}
	}

	private bool TryGetSttStart(out int start)
	{
		start = 0;
		string rawValue = SttStartTextBox.Text?.Trim() ?? string.Empty;
		if (!int.TryParse(
				rawValue,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out start) ||
			start < 1)
		{
			TaskDialog.Show(
				"Print Sheets",
				"STT bắt đầu phải là số nguyên từ 1 trở lên.");
			SttStartTextBox.Focus();
			SttStartTextBox.SelectAll();
			return false;
		}
		return true;
	}

	private void CommitSheetGridEdits()
	{
		SheetsDataGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
		SheetsDataGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
	}

	private void InitializeSourceMode()
	{
		_selectedSourceOrder.Clear();
		_allSources.Clear();
		if (_preselectedSheets.Count > 0)
		{
			SourceModeTextBlock.Text = $"Selection ({_preselectedSheets.Count} sheet(s))";
			SheetSourcesListBox.IsEnabled = false;
			SheetSourcesListBox.ItemsSource = null;
			ApplySheetRows(_preselectedSheets
				.Select(sheet => new SheetRow(
					sheet,
					"01. [Selection] Sheet đã chọn trong Revit",
					"[Selection] Sheet đã chọn trong Revit",
					"[Selection] Sheet đã chọn trong Revit",
					"Selection",
					isSelected: true,
					ReadSheetStt(sheet)))
				.ToList());
			return;
		}
		SourceModeTextBlock.Text = "Chọn Print Set hoặc Sheet Schedule";
		SheetSourcesListBox.IsEnabled = true;
		List<SheetSourceOption> sources = SheetExportUtilities
			.ListAvailablePrintSets(_document)
			.Select(SheetSourceOption.FromPrintSet)
			.Concat(
				SheetExportUtilities
					.ListAvailableSheetListSchedules(_document)
					.Select(SheetSourceOption.FromSheetSchedule))
			.ToList();
		_allSources.AddRange(sources);
		SheetSourcesListBox.ItemsSource = _allSources;
		if (sources.Count > 0)
		{
			SheetSourceOption firstSource = sources[0];
			_isUpdatingSourceSelection = true;
			try
			{
				firstSource.IsSelected = true;
			}
			finally
			{
				_isUpdatingSourceSelection = false;
			}
			_selectedSourceOrder.Add(firstSource);
			UpdateSourceOrderPresentation(firstSource);
			RefreshSheetsFromSelectedSources();
		}
		else
		{
			TaskDialog.Show(
				"Print Sheets",
				"Không tìm thấy Print Set hoặc Sheet List Schedule nào trong model.");
		}
	}

	private void InitializePdfExportSetupList()
	{
		_nativePdfQualityOptions = new List<PdfQualityOption>
		{
			new PdfQualityOption("300 DPI - Fast", 300, PDFExportQualityType.DPI300, isDefault: true),
			new PdfQualityOption("600 DPI - Quality", 600, PDFExportQualityType.DPI600, isDefault: false)
		};
		_virtualPdfPrintSetups =
			SheetExportUtilities.ListAvailablePrintSetups(_document);
		PdfExportSetupComboBox.ItemsSource = _nativePdfQualityOptions;
		PdfQualityOption activeSetup = _nativePdfQualityOptions
			.FirstOrDefault(setup => setup.IsDefault);
		PdfExportSetupComboBox.SelectedItem =
			activeSetup ?? _nativePdfQualityOptions.First();
	}

	private void InitializePdfProcessingList()
	{
		List<PdfProcessingOption> processingOptions = new List<PdfProcessingOption>
		{
			new PdfProcessingOption(
				"Vector - nét sắc, file nhẹ (mặc định)",
				alwaysUseRaster: false),
			new PdfProcessingOption(
				"Font-safe Raster - ưu tiên giữ hình dáng font",
				alwaysUseRaster: true)
		};
		PdfOutputProcessingComboBox.ItemsSource = processingOptions;
		PdfOutputProcessingComboBox.SelectedIndex = 0;
		PdfOutputProcessingComboBox.ToolTip =
			"Raster giúp tránh một số lỗi font của Revit Native PDF, " +
			"nhưng file lớn hơn và chữ khó tìm kiếm/copy hơn.";
	}

	private void InitializePrinterList()
	{
		List<PrinterOption> printers =
			SheetExportUtilities.ListAvailablePrinters(_document);
		PrinterComboBox.ItemsSource = printers;
		if (printers.Count == 0)
		{
			TaskDialog.Show("Print Sheets", "Không tìm thấy công cụ xuất PDF.");
			return;
		}
		PrinterComboBox.SelectedItem =
			SheetExportUtilities.SelectDefaultPdfPrinter(printers);
		PrinterComboBox.ToolTip =
			"PDF-XChange Lite là mặc định khi máy đã cài driver này. " +
			"Nếu không tìm thấy, tool tự chuyển sang Revit Native PDF.";
		RefreshPdfSetupOptionsForSelectedEngine();
	}

	private void OnPrinterSelectionChanged(
		object sender,
		SelectionChangedEventArgs e)
	{
		if (PdfExportSetupComboBox == null ||
			PdfOutputProcessingComboBox == null)
		{
			return;
		}

		RefreshPdfSetupOptionsForSelectedEngine();
		UpdatePdfControls();
	}

	private void RefreshPdfSetupOptionsForSelectedEngine()
	{
		if (!(PrinterComboBox.SelectedItem is PrinterOption selectedPrinter))
		{
			return;
		}

		if (selectedPrinter.IsNativePdf)
		{
			PdfSetupLabelTextBlock.Text = "Chất lượng Native PDF";
			PdfProcessingLabelTextBlock.Text =
				"Xử lý nội dung PDF (Native)";
			PdfExportSetupComboBox.ItemsSource = _nativePdfQualityOptions;
			PdfQualityOption activeQuality = _nativePdfQualityOptions
				.FirstOrDefault(option => option.IsDefault);
			PdfExportSetupComboBox.SelectedItem =
				activeQuality ?? _nativePdfQualityOptions.FirstOrDefault();
			PdfExportSetupComboBox.ToolTip =
				"300 DPI ưu tiên tốc độ; 600 DPI ưu tiên chất lượng hình ảnh.";
			PdfEngineInfoTextBlock.Text =
				"Native PDF xuất trực tiếp từ Revit, hỗ trợ Vector hoặc Font-safe Raster.";
		}
		else
		{
			PdfSetupLabelTextBlock.Text = "Revit Print Setup";
			PdfProcessingLabelTextBlock.Text =
				"Xử lý nội dung PDF (do driver quyết định)";
			PdfExportSetupComboBox.ItemsSource = _virtualPdfPrintSetups;
			PrintSetupOption activeSetup = _virtualPdfPrintSetups
				.FirstOrDefault(option => option.IsActive);
			PdfExportSetupComboBox.SelectedItem =
				activeSetup ?? _virtualPdfPrintSetups.FirstOrDefault();
			PdfExportSetupComboBox.ToolTip =
				"Máy in ảo sử dụng đúng Revit Print Setup đã chọn, gồm khổ giấy, zoom và raster/vector.";
			PdfEngineInfoTextBlock.Text =
				$"Đang xuất qua '{selectedPrinter.Name}'. Một số driver có thể mở hộp thoại Save As riêng.";
		}
	}

	private void InitializeCombinePdfMode()
	{
		if (!CombinePdfCheckBox.IsChecked.HasValue)
		{
			CombinePdfCheckBox.IsChecked = false;
		}
		CombinedPdfNameTextBox.Text = string.Empty;
		UpdatePdfControls();
	}

	private void InitializePdfMode()
	{
		ExportPdfCheckBox.IsChecked = true;
		InitializeCombinePdfMode();
		UpdatePdfControls();
	}

	private void UpdatePdfControls()
	{
		if (CombinePdfCheckBox != null &&
			PdfExportSetupComboBox != null &&
			PdfOutputProcessingComboBox != null &&
			PrinterComboBox != null &&
			CombinedPdfNameTextBox != null)
		{
			bool exportPdf = ExportPdfCheckBox.IsChecked == true;
			OutputFolderTextBox.IsEnabled = exportPdf;
			PrinterComboBox.IsEnabled =
				exportPdf && PrinterComboBox.Items.Count > 0;
			PdfExportSetupComboBox.IsEnabled = exportPdf && PdfExportSetupComboBox.Items.Count > 0;
			bool useNativePdf =
				PrinterComboBox.SelectedItem is PrinterOption selectedPrinter &&
				selectedPrinter.IsNativePdf;
			PdfOutputProcessingComboBox.IsEnabled =
				exportPdf &&
				useNativePdf &&
				PdfOutputProcessingComboBox.Items.Count > 0;
			CombinePdfCheckBox.IsEnabled = exportPdf;
			bool combinePdf = exportPdf && CombinePdfCheckBox.IsChecked == true;
			CombinedPdfNameTextBox.IsEnabled = combinePdf;
		}
	}

	private void InitializeDwgExportSetupList()
	{
		List<DwgExportSetupOption> setups = SheetExportUtilities.ListAvailableDwgExportSetups(_document);
		DwgExportSetupComboBox.ItemsSource = setups;
		if (setups.Count == 0)
		{
			DwgExportSetupComboBox.SelectedItem = null;
		}
		else
		{
			DwgExportSetupOption activeSetup = setups.FirstOrDefault((DwgExportSetupOption setup) => setup.IsActive);
			DwgExportSetupComboBox.SelectedItem = activeSetup ?? setups.First();
		}
		bool exportDwg = ExportDwgCheckBox.IsChecked == true;
		DwgOutputFolderTextBox.IsEnabled = exportDwg;
		DwgExportSetupComboBox.IsEnabled = exportDwg && setups.Count > 0;
	}

	private void OnExportDwgCheckedChanged(object sender, RoutedEventArgs e)
	{
		bool exportDwg = ExportDwgCheckBox.IsChecked == true;
		if (DwgOutputFolderTextBox != null)
		{
			DwgOutputFolderTextBox.IsEnabled = exportDwg;
		}
		bool hasAnySetup = DwgExportSetupComboBox.Items.Count > 0;
		DwgExportSetupComboBox.IsEnabled = exportDwg && hasAnySetup;
	}

	private void OnCombinePdfModeChanged(object sender, RoutedEventArgs e)
	{
		UpdatePdfControls();
		if (ExportPdfCheckBox.IsChecked == true && CombinePdfCheckBox.IsChecked == true)
		{
			TryApplyDefaultCombinedPdfName(force: true);
		}
	}

	private void TryApplyDefaultCombinedPdfName(bool force)
	{
		if (_preselectedSheets.Count <= 0)
		{
			string proposed = SheetExportUtilities.NormalizePdfFileBaseName(
				BuildDefaultCombinedPdfName());
			if (string.IsNullOrWhiteSpace(proposed))
			{
				proposed = "Export";
			}
			string current = CombinedPdfNameTextBox.Text.Trim();
			if (force || string.IsNullOrWhiteSpace(current) || string.Equals(current, _lastAutoCombinedPdfName, StringComparison.OrdinalIgnoreCase))
			{
				CombinedPdfNameTextBox.Text = proposed;
				_lastAutoCombinedPdfName = proposed;
			}
		}
	}

	private string BuildDefaultCombinedPdfName()
	{
		List<SheetSourceOption> selectedSources =
			_selectedSourceOrder.ToList();
		if (selectedSources.Count == 1)
		{
			return selectedSources[0].DefaultFileName;
		}
		if (selectedSources.Count > 1)
		{
			return $"{_document.Title} - {selectedSources.Count} sources";
		}

		return _document.Title;
	}

	private IReadOnlyList<ViewSheet> ResolveExportSheets()
	{
		CommitSheetGridEdits();
		return (from row in _sheetRows
			where row.IsSelected
			select row.Sheet).ToList();
	}

	private void ApplySheetRows(List<SheetRow> rows)
	{
		_sheetRows = rows ?? new List<SheetRow>();
		RefreshExportOrder();
		SheetsDataGrid.ItemsSource = null;
		SheetsDataGrid.ItemsSource = _sheetRows;
		ICollectionView view =
			CollectionViewSource.GetDefaultView(SheetsDataGrid.ItemsSource);
		view.GroupDescriptions.Clear();
		view.GroupDescriptions.Add(
			new PropertyGroupDescription(nameof(SheetRow.PrimarySourceDisplay)));
	}

	private void SetSheetSelectionState(bool isSelected)
	{
		foreach (SheetRow row in _sheetRows)
		{
			row.IsSelected = isSelected;
		}
		RefreshExportOrder();
		SheetsDataGrid.Items.Refresh();
	}

	private void RefreshExportOrder()
	{
		int exportOrder = 1;
		foreach (SheetRow row in _sheetRows)
		{
			row.ExportOrderText = row.IsSelected
				? exportOrder++.ToString(CultureInfo.InvariantCulture)
				: string.Empty;
		}
	}

	private static string ReadSheetStt(ViewSheet sheet)
	{
		Parameter parameter = sheet?.LookupParameter("STT");
		if (parameter == null)
		{
			return string.Empty;
		}

		if (parameter.StorageType == StorageType.String)
		{
			return parameter.AsString()?.Trim() ?? string.Empty;
		}
		if (parameter.StorageType == StorageType.Integer)
		{
			return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
		}

		return parameter.AsValueString()?.Trim() ?? string.Empty;
	}

	private static void OpenOutputFolder(string outputFolder)
	{
		try
		{
			if (Directory.Exists(outputFolder))
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = outputFolder,
					UseShellExecute = true
				};
				Process.Start(startInfo);
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Print Sheets", "Đã export nhưng không mở được folder tự động.\n" + ex.Message);
		}
	}

}
