using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using VP_RevitAPI.Helper.Excel;

namespace VP_RevitAPI.UI;

public class UI_ExcelLegendCheckerWindow : Window, IComponentConnector
{
	private enum MessageLevel
	{
		Info,
		Success,
		Warning,
		Error
	}

	private sealed class CheckerRowViewModel : INotifyPropertyChanged
	{
		private bool _isSelected;

		private string _statusText;

		private Brush _statusBackground;

		private string _newPath;

		private string _currentScopedDefinedName;

		private string _message;

		public long LegendId { get; }

		public string LegendName { get; }

		public string CurrentPath { get; }

		public string ExcelFileName => Path.GetFileName(CurrentPath) ?? string.Empty;

		public string CurrentScopedDefinedName
		{
			get
			{
				return _currentScopedDefinedName;
			}
			private set
			{
				if (!string.Equals(_currentScopedDefinedName, value, StringComparison.Ordinal))
				{
					_currentScopedDefinedName = value ?? string.Empty;
					OnPropertyChanged("CurrentScopedDefinedName");
				}
			}
		}

		public bool IsSelected
		{
			get
			{
				return _isSelected;
			}
			set
			{
				if (_isSelected != value)
				{
					_isSelected = value;
					OnPropertyChanged("IsSelected");
				}
			}
		}

		public string StatusText
		{
			get
			{
				return _statusText;
			}
			set
			{
				if (!string.Equals(_statusText, value, StringComparison.Ordinal))
				{
					_statusText = value;
					OnPropertyChanged("StatusText");
					StatusBackground = ToStatusBackground(_statusText);
				}
			}
		}

		public Brush StatusBackground
		{
			get
			{
				return _statusBackground;
			}
			private set
			{
				if (!object.Equals(_statusBackground, value))
				{
					_statusBackground = value;
					OnPropertyChanged("StatusBackground");
				}
			}
		}

		public string NewPath
		{
			get
			{
				return _newPath;
			}
			set
			{
				string normalized = value?.Trim() ?? string.Empty;
				if (!string.Equals(_newPath, normalized, StringComparison.Ordinal))
				{
					_newPath = normalized;
					OnPropertyChanged("NewPath");
				}
			}
		}

		public string Message
		{
			get
			{
				return _message;
			}
			set
			{
				if (!string.Equals(_message, value, StringComparison.Ordinal))
				{
					_message = value ?? string.Empty;
					OnPropertyChanged("Message");
				}
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public CheckerRowViewModel(ExcelLegendCheckerRow row)
		{
			LegendId = row.LegendId;
			LegendName = row.LegendName;
			CurrentPath = row.CurrentPath;
			_currentScopedDefinedName = row.CurrentScopedDefinedName ?? string.Empty;
			_statusText = ToStatusText(row.Status);
			_statusBackground = ToStatusBackground(row.Status);
			_newPath = string.Empty;
			_message = ((row.Status == ExcelLegendCheckerStatus.Fresh) ? string.Empty : row.Message);
			_isSelected = row.Status != ExcelLegendCheckerStatus.Fresh;
		}

		public string GetEffectivePath()
		{
			return string.IsNullOrWhiteSpace(NewPath) ? CurrentPath : NewPath;
		}

		private static string ToStatusText(ExcelLegendCheckerStatus status)
		{
			if (1 == 0)
			{
			}
			string result = status switch
			{
				ExcelLegendCheckerStatus.Fresh => "Mới", 
				ExcelLegendCheckerStatus.Stale => "Cũ", 
				_ => "Lỗi tệp", 
			};
			if (1 == 0)
			{
			}
			return result;
		}

		private static Brush ToStatusBackground(ExcelLegendCheckerStatus status)
		{
			if (1 == 0)
			{
			}
			SolidColorBrush result = status switch
			{
				ExcelLegendCheckerStatus.Fresh => new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 125, 50)), 
				ExcelLegendCheckerStatus.Stale => new SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 108, 2)), 
				_ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 47, 47)), 
			};
			if (1 == 0)
			{
			}
			return result;
		}

		private static Brush ToStatusBackground(string statusText)
		{
			if (string.Equals(statusText, "Mới", StringComparison.Ordinal))
			{
				return ToStatusBackground(ExcelLegendCheckerStatus.Fresh);
			}
			if (string.Equals(statusText, "Cũ", StringComparison.Ordinal))
			{
				return ToStatusBackground(ExcelLegendCheckerStatus.Stale);
			}
			return ToStatusBackground(ExcelLegendCheckerStatus.FileError);
		}

		private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	private readonly Document _document;

	private readonly ObservableCollection<CheckerRowViewModel> _rows = new ObservableCollection<CheckerRowViewModel>();

	internal TextBlock SummaryTextBlock;

	internal Button RelinkButton;

	internal Button OpenLegendButton;

	internal Button ChangePathButton;

	internal TextBlock StatusMessageTextBlock;

	internal DataGrid LegendCheckerDataGrid;

	private bool _contentLoaded;

	public UI_ExcelLegendCheckerWindow(Document document)
	{
		_document = document;
		InitializeComponent();
		LegendCheckerDataGrid.ItemsSource = _rows;
		ReloadRows();
	}

	private void OnScanClick(object sender, RoutedEventArgs e)
	{
		ReloadRows();
		SetStatusMessage("Đã quét lại dữ liệu kiểm tra.", MessageLevel.Info);
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateSelectionActionsEnabled();
	}

	private void OnUpdateSelectedClick(object sender, RoutedEventArgs e)
	{
		UpdateRows(_rows.Where((CheckerRowViewModel row) => row.IsSelected).ToList(), "Đã hoàn tất cập nhật các dòng được chọn.");
	}

	private void OnSelectAllClick(object sender, RoutedEventArgs e)
	{
		foreach (CheckerRowViewModel row in _rows)
		{
			row.IsSelected = true;
		}
		UpdateSelectionActionsEnabled();
	}

	private void OnSelectNoneClick(object sender, RoutedEventArgs e)
	{
		foreach (CheckerRowViewModel row in _rows)
		{
			row.IsSelected = false;
		}
		UpdateSelectionActionsEnabled();
	}

	private void OnRelinkSelectedClick(object sender, RoutedEventArgs e)
	{
		CheckerRowViewModel row = GetSelectedRow();
		if (row == null)
		{
			SetStatusMessage("Vui lòng chọn một dòng để liên kết lại.", MessageLevel.Warning);
			return;
		}
		UpdateRows(new CheckerRowViewModel[1] { row }, "Đã hoàn tất liên kết lại cho khung chú giải '" + row.LegendName + "'.");
	}

	private void OnOpenLegendClick(object sender, RoutedEventArgs e)
	{
		CheckerRowViewModel row = GetSelectedRow();
		if (row == null)
		{
			SetStatusMessage("Vui lòng chọn một dòng để mở khung chú giải.", MessageLevel.Warning);
			return;
		}
		if (!(_document.GetElement(new ElementId(row.LegendId)) is View { ViewType: ViewType.Legend } legend))
		{
			SetStatusMessage("Khung chú giải '" + row.LegendName + "' không tồn tại hoặc không hợp lệ.", MessageLevel.Error);
			return;
		}
		try
		{
			UIDocument uiDocument = new UIDocument(_document);
			uiDocument.ActiveView = legend;
			ImageInstance managedImage = ExcelLegendLinkService.FindManagedImageInLegend(legend);
			if (managedImage != null)
			{
				uiDocument.ShowElements(managedImage.Id);
			}
			SetStatusMessage("Đã mở khung chú giải '" + row.LegendName + "'.", MessageLevel.Success);
		}
		catch (Exception ex)
		{
			SetStatusMessage("Không thể mở khung chú giải '" + row.LegendName + "': " + ex.Message, MessageLevel.Error);
		}
	}

	private void OnChangePathClick(object sender, RoutedEventArgs e)
	{
		CheckerRowViewModel row = GetSelectedRow();
		if (row == null)
		{
			SetStatusMessage("Vui lòng chọn một dòng để đổi đường dẫn.", MessageLevel.Warning);
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Chọn tệp Excel cho khung chú giải '" + row.LegendName + "'",
			Filter = "Tệp Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|Tất cả tệp (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false,
			FileName = row.GetEffectivePath()
		};
		if (dialog.ShowDialog() == true)
		{
			row.NewPath = dialog.FileName;
			SetStatusMessage("Đã đổi đường dẫn cho khung chú giải '" + row.LegendName + "'.", MessageLevel.Success);
		}
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ReloadRows()
	{
		foreach (CheckerRowViewModel row in _rows)
		{
			row.PropertyChanged -= OnRowPropertyChanged;
		}
		_rows.Clear();
		ExcelLegendCheckerScanSummary summary = ExcelLegendCheckerService.Scan(_document);
		foreach (ExcelLegendCheckerRow row2 in summary.Rows)
		{
			CheckerRowViewModel vm = new CheckerRowViewModel(row2);
			vm.PropertyChanged += OnRowPropertyChanged;
			_rows.Add(vm);
		}
		RefreshSummary(summary);
		UpdateSelectionActionsEnabled();
	}

	private void UpdateRows(IReadOnlyList<CheckerRowViewModel> targetRows, string title)
	{
		if (targetRows.Count == 0)
		{
			SetStatusMessage("Không có dòng hợp lệ để cập nhật.", MessageLevel.Warning);
			return;
		}
		List<ExcelLegendCheckerUpdateRequest> requests = new List<ExcelLegendCheckerUpdateRequest>();
		foreach (CheckerRowViewModel row in targetRows)
		{
			string effectivePath = row.GetEffectivePath();
			if (string.IsNullOrWhiteSpace(effectivePath) || !File.Exists(effectivePath))
			{
				row.Message = "Lỗi tệp: Đường dẫn Excel không hợp lệ.";
				row.StatusText = "Lỗi tệp";
			}
			else if (string.IsNullOrWhiteSpace(row.CurrentScopedDefinedName))
			{
				row.Message = "Lỗi tệp: Thiếu tên vùng theo phạm vi hợp lệ.";
				row.StatusText = "Lỗi tệp";
			}
			else
			{
				requests.Add(new ExcelLegendCheckerUpdateRequest(row.LegendId, effectivePath, row.CurrentScopedDefinedName));
			}
		}
		if (requests.Count == 0)
		{
			SetStatusMessage("Không có yêu cầu hợp lệ để cập nhật.", MessageLevel.Warning);
			return;
		}
		ExcelLegendLinkSummary summary = ExcelLegendCheckerService.Update(_document, requests);
		SetStatusMessage($"{title} Kết quả: Đã cập nhật {summary.Updated}, tạo mới {summary.Created}, thất bại {summary.Failed}.", (summary.Failed <= 0) ? MessageLevel.Success : MessageLevel.Warning);
		ReloadRows();
	}

	private CheckerRowViewModel? GetSelectedRow()
	{
		return LegendCheckerDataGrid.SelectedItem as CheckerRowViewModel;
	}

	private void UpdateSelectionActionsEnabled()
	{
		CheckerRowViewModel selectedRow = GetSelectedRow();
		bool hasHighlightedRow = selectedRow != null;
		RelinkButton.IsEnabled = hasHighlightedRow;
		OpenLegendButton.IsEnabled = hasHighlightedRow;
		ChangePathButton.IsEnabled = hasHighlightedRow;
	}

	private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "IsSelected")
		{
			UpdateSelectionActionsEnabled();
		}
	}

	private void RefreshSummary(ExcelLegendCheckerScanSummary summary)
	{
		SummaryTextBlock.Text = $"Tổng: {summary.Total} | Mới: {summary.Fresh} | Cũ: {summary.Stale} | Lỗi tệp: {summary.FileError}";
	}

	private void SetStatusMessage(string message, MessageLevel level)
	{
		StatusMessageTextBlock.Text = message;
		TextBlock statusMessageTextBlock = StatusMessageTextBlock;
		if (1 == 0)
		{
		}
		SolidColorBrush foreground = level switch
		{
			MessageLevel.Success => new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 125, 50)), 
			MessageLevel.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 108, 2)), 
			MessageLevel.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 47, 47)), 
			_ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)), 
		};
		if (1 == 0)
		{
		}
		statusMessageTextBlock.Foreground = foreground;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_excellegendcheckerwindow.xaml", UriKind.Relative);
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
			SummaryTextBlock = (TextBlock)target;
			break;
		case 2:
			((Button)target).Click += OnScanClick;
			break;
		case 3:
			((Button)target).Click += OnSelectAllClick;
			break;
		case 4:
			((Button)target).Click += OnSelectNoneClick;
			break;
		case 5:
			((Button)target).Click += OnUpdateSelectedClick;
			break;
		case 6:
			RelinkButton = (Button)target;
			RelinkButton.Click += OnRelinkSelectedClick;
			break;
		case 7:
			OpenLegendButton = (Button)target;
			OpenLegendButton.Click += OnOpenLegendClick;
			break;
		case 8:
			ChangePathButton = (Button)target;
			ChangePathButton.Click += OnChangePathClick;
			break;
		case 9:
			StatusMessageTextBlock = (TextBlock)target;
			break;
		case 10:
			LegendCheckerDataGrid = (DataGrid)target;
			LegendCheckerDataGrid.SelectionChanged += OnSelectionChanged;
			break;
		case 11:
			((Button)target).Click += OnCloseClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
