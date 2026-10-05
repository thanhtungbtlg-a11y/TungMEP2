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

public class UI_CopyVGWindow : Window, IComponentConnector
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

	private const string DialogTitle = "Copy VG";

	private readonly List<ViewChoiceItem> _viewItems;

	internal ListBox SourceViewsListBox;

	internal ListBox TargetViewsListBox;

	internal Button CopyButton;

	private bool _contentLoaded;

	public ElementId? SourceViewId { get; private set; }

	public ElementId? TargetViewId { get; private set; }

	public UI_CopyVGWindow(UIDocument uiDocument)
	{
		_viewItems = CollectViews(uiDocument);
		InitializeComponent();
		SourceViewsListBox.ItemsSource = _viewItems;
		TargetViewsListBox.ItemsSource = _viewItems;
	}

	public static int CountEligibleOpenViews(UIDocument uiDocument)
	{
		return CollectViews(uiDocument).Count;
	}

	public static bool IsEligibleView(View view)
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
		return view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan || view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.AreaPlan || view.ViewType == ViewType.Section || view.ViewType == ViewType.Elevation;
	}

	private void OnCopyClick(object sender, RoutedEventArgs e)
	{
		if (!(SourceViewsListBox.SelectedItem is ViewChoiceItem source))
		{
			MessageBox.Show(this, "Hãy chọn source view.", "Copy VG", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!(TargetViewsListBox.SelectedItem is ViewChoiceItem target))
		{
			MessageBox.Show(this, "Hãy chọn target view.", "Copy VG", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (source.Id == target.Id)
		{
			MessageBox.Show(this, "Source view và target view phải khác nhau.", "Copy VG", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SourceViewId = source.Id;
		TargetViewId = target.Id;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		bool hasSource = SourceViewsListBox.SelectedItem != null;
		bool hasTarget = TargetViewsListBox.SelectedItem != null;
		bool sameSelection = (SourceViewsListBox.SelectedItem as ViewChoiceItem)?.Id == (TargetViewsListBox.SelectedItem as ViewChoiceItem)?.Id;
		CopyButton.IsEnabled = hasSource && hasTarget && !sameSelection;
	}

	private static List<ViewChoiceItem> CollectViews(UIDocument uiDocument)
	{
		if (uiDocument == null)
		{
			return new List<ViewChoiceItem>();
		}
		IReadOnlyList<View> openViews = GetOpenViews(uiDocument);
		return (from view in openViews.Where(IsEligibleView)
			select new ViewChoiceItem(view.Id, view.ViewType.ToString(), view.Name)).OrderBy<ViewChoiceItem, string>((ViewChoiceItem item) => item.ViewTypeLabel, StringComparer.OrdinalIgnoreCase).ThenBy<ViewChoiceItem, string>((ViewChoiceItem item) => item.ViewName, StringComparer.OrdinalIgnoreCase).ToList();
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_copyvgwindow.xaml", UriKind.Relative);
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
			SourceViewsListBox = (ListBox)target;
			SourceViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 2:
			TargetViewsListBox = (ListBox)target;
			TargetViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 3:
			CopyButton = (Button)target;
			CopyButton.Click += OnCopyClick;
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
