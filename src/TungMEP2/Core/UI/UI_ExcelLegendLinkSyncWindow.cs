using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using VP_RevitAPI.Helper.Excel;
using VP_RevitAPI.State;

namespace VP_RevitAPI.UI;

public class UI_ExcelLegendLinkSyncWindow : Window, IComponentConnector
{
	private readonly Document _document;

	private readonly ElementId _activeLegendId;

	private string _selectedExcelPath = string.Empty;

	internal TextBlock ActiveLegendTextBlock;

	internal TextBlock ExcelFileNameTextBlock;

	internal System.Windows.Controls.ComboBox DefinedNameComboBox;

	private bool _contentLoaded;

	public UI_ExcelLegendLinkSyncWindow(Document document, ElementId activeLegendId)
	{
		_document = document;
		_activeLegendId = activeLegendId;
		InitializeComponent();
		LoadActiveLegendLabel();
		TryLoadRecentExcelFile();
	}

	private void LoadActiveLegendLabel()
	{
		if (_document.GetElement(_activeLegendId) is View { ViewType: ViewType.Legend } view)
		{
			ActiveLegendTextBlock.Text = view.Name ?? string.Empty;
		}
		else
		{
			ActiveLegendTextBlock.Text = "(Không phải Legend)";
		}
	}

	private void OnBrowseClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Chọn file Excel",
			Filter = "Excel Files (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|All Files (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false
		};
		if (dialog.ShowDialog() == true)
		{
			SetSelectedExcelPath(dialog.FileName, loadDefinedNames: true, persistRecent: true);
		}
	}

	private void LoadDefinedNames(string excelPath)
	{
		if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
		{
			TaskDialog.Show("Liên kết Excel với Legend", "Vui lòng chọn đường dẫn file Excel hợp lệ trước khi nạp danh sách.");
			return;
		}
		try
		{
			IReadOnlyList<ExcelLegendLinkService.DefinedNameChoice> names = ExcelLegendLinkService.LoadDefinedNames(excelPath);
			DefinedNameComboBox.ItemsSource = names;
			if (names.Count > 0)
			{
				DefinedNameComboBox.SelectedIndex = 0;
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Liên kết Excel với Legend", ex.Message);
		}
	}

	private void TryLoadRecentExcelFile()
	{
		string recentPath = ExcelLegendLinkRecentFileStore.Load();
		if (!string.IsNullOrWhiteSpace(recentPath))
		{
			SetSelectedExcelPath(recentPath, loadDefinedNames: true, persistRecent: false);
		}
	}

	private void SetSelectedExcelPath(string excelPath, bool loadDefinedNames, bool persistRecent)
	{
		string normalizedPath = excelPath?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
		{
			_selectedExcelPath = string.Empty;
			ExcelFileNameTextBlock.Text = "(Chưa chọn file)";
			DefinedNameComboBox.ItemsSource = null;
			DefinedNameComboBox.SelectedItem = null;
			return;
		}
		_selectedExcelPath = normalizedPath;
		ExcelFileNameTextBlock.Text = Path.GetFileName(normalizedPath);
		if (loadDefinedNames)
		{
			LoadDefinedNames(_selectedExcelPath);
		}
		if (persistRecent)
		{
			ExcelLegendLinkRecentFileStore.Save(_selectedExcelPath);
		}
	}

	private void OnRunClick(object sender, RoutedEventArgs e)
	{
		if (!TryGetInputs(out string excelPath, out ExcelLegendLinkService.DefinedNameChoice definedNameChoice))
		{
			return;
		}
		if (!ExcelLegendLinkService.TryResolveTargetLegend(_document, _activeLegendId, definedNameChoice.ScopedDefinedName, out ElementId targetLegendId, out string legendMessage))
		{
			TaskDialog.Show("Liên kết Excel với Legend", legendMessage);
			return;
		}
		try
		{
			ExcelLegendLinkSummary summary = ExcelLegendLinkService.RunSingle(_document, targetLegendId, excelPath, definedNameChoice);
			string placeError;
			if (summary.Failed > 0 || summary.ParseError > 0)
			{
				TaskDialog.Show("Liên kết Excel với Legend", ExcelLegendSummaryFormatter.Format("Liên kết Legend thất bại.", summary));
			}
			else if (_document.ActiveView is ViewSheet activeSheet && !TryPlaceLegendOnSheet(activeSheet, targetLegendId, out placeError))
			{
				TaskDialog.Show("Liên kết Excel với Legend", placeError);
			}
			else
			{
				Close();
			}
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Liên kết Excel với Legend", ex.Message);
		}
	}

	private void OnCheckActiveClick(object sender, RoutedEventArgs e)
	{
		if (!(_document.GetElement(_activeLegendId) is View { ViewType: ViewType.Legend }))
		{
			TaskDialog.Show("Liên kết Excel với Legend", "Cập nhật Active yêu cầu active view là Legend.");
			return;
		}
		try
		{
			ExcelLegendLinkSummary summary = ExcelLegendSyncService.CheckAndUpdateActiveLegend(_document, _activeLegendId);
			TaskDialog.Show("Liên kết Excel với Legend", ExcelLegendSummaryFormatter.Format("Đã hoàn tất cập nhật Active.", summary));
		}
		catch (Exception ex)
		{
			TaskDialog.Show("Liên kết Excel với Legend", ex.Message);
		}
		finally
		{
			Close();
		}
	}

	private void OnRefreshClick(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!ExcelLegendLinkService.TryResetActiveLegendLink(_document, _activeLegendId, out string message))
			{
				TaskDialog.Show("Liên kết Excel với Legend", message);
			}
		}
		finally
		{
			Close();
		}
	}

	private void OnCheckerClick(object sender, RoutedEventArgs e)
	{
		UI_ExcelLegendCheckerWindow window = new UI_ExcelLegendCheckerWindow(_document);
		Close();
		window.ShowDialog();
	}

	private bool TryGetInputs(out string excelPath, out ExcelLegendLinkService.DefinedNameChoice definedNameChoice)
	{
		excelPath = _selectedExcelPath;
		definedNameChoice = new ExcelLegendLinkService.DefinedNameChoice(string.Empty, string.Empty);
		if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
		{
			TaskDialog.Show("Liên kết Excel với Legend", "Vui lòng chọn đường dẫn file Excel hợp lệ.");
			return false;
		}
		if (!(DefinedNameComboBox.SelectedItem is ExcelLegendLinkService.DefinedNameChoice choice))
		{
			TaskDialog.Show("Liên kết Excel với Legend", "Vui lòng nạp và chọn một Defined Name.");
			return false;
		}
		definedNameChoice = choice;
		return true;
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private bool TryPlaceLegendOnSheet(ViewSheet sheet, ElementId legendId, out string message)
	{
		message = string.Empty;
		if (!Viewport.CanAddViewToSheet(_document, sheet.Id, legendId))
		{
			message = "Không thể đưa Legend vào sheet '" + sheet.SheetNumber + " - " + sheet.Name + "'.";
			return false;
		}
		using Transaction tx = new Transaction(_document, "Place Legend On Active Sheet");
		try
		{
			tx.Start();
			XYZ center = GetSheetCenter(sheet);
			Viewport.Create(_document, sheet.Id, legendId, center);
			tx.Commit();
			return true;
		}
		catch (Exception ex)
		{
			if (tx.GetStatus() == TransactionStatus.Started)
			{
				tx.RollBack();
			}
			message = "Không thể đưa Legend vào sheet: " + ex.Message;
			return false;
		}
	}

	private static XYZ GetSheetCenter(ViewSheet sheet)
	{
		BoundingBoxUV outline = sheet.Outline;
		UV min = outline.Min;
		UV max = outline.Max;
		return new XYZ((min.U + max.U) * 0.5, (min.V + max.V) * 0.5, 0.0);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_excellegendlinksyncwindow.xaml", UriKind.Relative);
			System.Windows.Application.LoadComponent(this, resourceLocater);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			ActiveLegendTextBlock = (TextBlock)target;
			break;
		case 2:
			ExcelFileNameTextBlock = (TextBlock)target;
			break;
		case 3:
			((Button)target).Click += OnBrowseClick;
			break;
		case 4:
			DefinedNameComboBox = (System.Windows.Controls.ComboBox)target;
			break;
		case 5:
			((Button)target).Click += OnRunClick;
			break;
		case 6:
			((Button)target).Click += OnCheckActiveClick;
			break;
		case 7:
			((Button)target).Click += OnRefreshClick;
			break;
		case 8:
			((Button)target).Click += OnCheckerClick;
			break;
		case 9:
			((Button)target).Click += OnCloseClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
