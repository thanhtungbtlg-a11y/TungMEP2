using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.UI;

public class UI_PutViewsInSheetWindow : Window, IComponentConnector
{
	private sealed class ViewChoiceItem
	{
		public ElementId Id { get; }

		public string ViewTypeLabel { get; }

		public string ViewName { get; }

		public string DisplayName => ViewTypeLabel + " | " + ViewName;

		public ViewChoiceItem(ElementId id, string viewTypeLabel, string viewName)
		{
			Id = id;
			ViewTypeLabel = viewTypeLabel?.Trim() ?? string.Empty;
			ViewName = viewName?.Trim() ?? string.Empty;
		}
	}

	private sealed class SheetChoiceItem
	{
		public ElementId Id { get; }

		public string SheetNumber { get; }

		public string SheetName { get; }

		public string DisplayName => SheetNumber + " - " + SheetName;

		public SheetChoiceItem(ElementId id, string sheetNumber, string sheetName)
		{
			Id = id;
			SheetNumber = sheetNumber?.Trim() ?? string.Empty;
			SheetName = sheetName?.Trim() ?? string.Empty;
		}
	}

	private const string DialogTitle = "Đưa View vào Sheet";

	private readonly List<ViewChoiceItem> _viewItems;

	private readonly List<SheetChoiceItem> _sheetItems;

	internal ListBox ViewsListBox;

	internal ListBox SheetsListBox;

	internal Button PlaceButton;

	private bool _contentLoaded;

	public IReadOnlyList<ElementId> SelectedViewIds { get; private set; } = Array.Empty<ElementId>();

	public ElementId? SelectedSheetId { get; private set; }

	public UI_PutViewsInSheetWindow(UIDocument uiDocument)
	{
		_viewItems = CollectViews(uiDocument);
		_sheetItems = CollectSheets(uiDocument);
		InitializeComponent();
		ViewsListBox.ItemsSource = _viewItems;
		SheetsListBox.ItemsSource = _sheetItems;
	}

	private void OnPlaceClick(object sender, RoutedEventArgs e)
	{
		if (ViewsListBox.SelectedItems.Count == 0)
		{
			MessageBox.Show(this, "Hãy chọn ít nhất 1 view hoặc schedule.", "Đưa View vào Sheet", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!(SheetsListBox.SelectedItem is SheetChoiceItem selectedSheet))
		{
			MessageBox.Show(this, "Hãy chọn 1 sheet đích.", "Đưa View vào Sheet", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedViewIds = (from item in ViewsListBox.SelectedItems.OfType<ViewChoiceItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).Distinct().ToList();
		if (SelectedViewIds.Count == 0)
		{
			MessageBox.Show(this, "Hãy chọn ít nhất 1 view hoặc schedule.", "Đưa View vào Sheet", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedSheetId = selectedSheet.Id;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		PlaceButton.IsEnabled = ViewsListBox.SelectedItems.Count > 0 && SheetsListBox.SelectedItem != null;
	}

	private static List<ViewChoiceItem> CollectViews(UIDocument uiDocument)
	{
		if (uiDocument == null)
		{
			return new List<ViewChoiceItem>();
		}
		IReadOnlyList<View> openViews = GetOpenViews(uiDocument);
		IEnumerable<View> views = openViews.Where(IsPlaceableViewOrSchedule);
		return views.Select((View view) => new ViewChoiceItem(view.Id, GetViewTypeLabel(view), view.Name)).OrderBy<ViewChoiceItem, string>((ViewChoiceItem item) => item.ViewTypeLabel, StringComparer.OrdinalIgnoreCase).ThenBy<ViewChoiceItem, string>((ViewChoiceItem item) => item.ViewName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static bool IsPlaceableViewOrSchedule(View view)
	{
		if (view == null)
		{
			return false;
		}
		if (view is ViewSheet)
		{
			return false;
		}
		if (view.IsTemplate)
		{
			return false;
		}
		if (view is ViewSchedule { IsTitleblockRevisionSchedule: not false })
		{
			return false;
		}
		return view.ViewType != ViewType.DrawingSheet && view.ViewType != ViewType.ProjectBrowser && view.ViewType != ViewType.SystemBrowser && view.ViewType != ViewType.Undefined;
	}

	private static string GetViewTypeLabel(View view)
	{
		if (view is ViewSchedule)
		{
			return "Schedule";
		}
		return view.ViewType.ToString();
	}

	private static List<SheetChoiceItem> CollectSheets(UIDocument uiDocument)
	{
		if (uiDocument == null)
		{
			return new List<SheetChoiceItem>();
		}
		IReadOnlyList<View> openViews = GetOpenViews(uiDocument);
		IEnumerable<ViewSheet> sheets = from sheet in openViews.OfType<ViewSheet>()
			where !sheet.IsPlaceholder
			select sheet;
		return sheets.Select((ViewSheet sheet) => new SheetChoiceItem(sheet.Id, sheet.SheetNumber, sheet.Name)).OrderBy<SheetChoiceItem, string>((SheetChoiceItem item) => item.SheetNumber, StringComparer.OrdinalIgnoreCase).ThenBy<SheetChoiceItem, string>((SheetChoiceItem item) => item.SheetName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IReadOnlyList<View> GetOpenViews(UIDocument uiDocument)
	{
		if (uiDocument == null)
		{
			return Array.Empty<View>();
		}
		Document document = uiDocument.Document;
		if (document == null)
		{
			return Array.Empty<View>();
		}
		IList<UIView> openUiViews;
		try
		{
			openUiViews = uiDocument.GetOpenUIViews();
		}
		catch
		{
			return Array.Empty<View>();
		}
		List<View> result = new List<View>(openUiViews.Count);
		HashSet<ElementId> seen = new HashSet<ElementId>();
		foreach (UIView uiView in openUiViews)
		{
			if (uiView != null)
			{
				ElementId viewId = uiView.ViewId;
				if (!(viewId == null) && !(viewId == ElementId.InvalidElementId) && seen.Add(viewId) && document.GetElement(viewId) is View view)
				{
					result.Add(view);
				}
			}
		}
		return result;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_putviewsinsheetwindow.xaml", UriKind.Relative);
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
			ViewsListBox = (ListBox)target;
			ViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 2:
			SheetsListBox = (ListBox)target;
			SheetsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 3:
			PlaceButton = (Button)target;
			PlaceButton.Click += OnPlaceClick;
			break;
		case 4:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
