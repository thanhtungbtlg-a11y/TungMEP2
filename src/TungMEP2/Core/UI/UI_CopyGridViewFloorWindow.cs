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

public class UI_CopyGridViewFloorWindow : Window, IComponentConnector
{
	private sealed class ViewFloorPlanChoiceItem
	{
		public ElementId Id { get; }

		public string Name { get; }

		public string DisplayName => Name;

		public ViewFloorPlanChoiceItem(ElementId id, string name)
		{
			Id = id;
			Name = name?.Trim() ?? string.Empty;
		}
	}

	private const string DialogTitle = "CopyGridViewFloor";

	private readonly List<ViewFloorPlanChoiceItem> _allOpenFloorPlans;

	private List<ViewFloorPlanChoiceItem> _targetItems = new List<ViewFloorPlanChoiceItem>();

	internal ListBox SourceViewsListBox;

	internal ListBox TargetViewsListBox;

	internal Button CopyButton;

	private bool _contentLoaded;

	public ElementId? SelectedSourceViewId { get; private set; }

	public IReadOnlyList<ElementId> SelectedTargetViewIds { get; private set; } = Array.Empty<ElementId>();

	public UI_CopyGridViewFloorWindow(UIDocument uiDocument)
	{
		_allOpenFloorPlans = CollectOpenFloorPlans(uiDocument);
		InitializeComponent();
		SourceViewsListBox.ItemsSource = _allOpenFloorPlans;
		SelectDefaultSource(uiDocument);
		RefreshTargetItems();
		UpdateCopyButtonState();
	}

	private void OnCopyClick(object sender, RoutedEventArgs e)
	{
		if (!(SourceViewsListBox.SelectedItem is ViewFloorPlanChoiceItem selectedSource))
		{
			MessageBox.Show(this, "Hãy chọn 1 ViewFloorPlan nguồn.", "CopyGridViewFloor", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		List<ElementId> selectedTargetIds = (from item in TargetViewsListBox.SelectedItems.OfType<ViewFloorPlanChoiceItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).Distinct().ToList();
		if (selectedTargetIds.Count == 0)
		{
			MessageBox.Show(this, "Hãy chọn ít nhất 1 ViewFloorPlan đích.", "CopyGridViewFloor", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedSourceViewId = selectedSource.Id;
		SelectedTargetViewIds = selectedTargetIds;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RefreshTargetItems();
		UpdateCopyButtonState();
	}

	private void OnTargetSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateCopyButtonState();
	}

	private void SelectDefaultSource(UIDocument uiDocument)
	{
		View activeView = uiDocument.Document.ActiveView;
		ViewPlan activeFloorPlan = activeView as ViewPlan;
		if (activeFloorPlan != null && activeFloorPlan.ViewType == ViewType.FloorPlan)
		{
			ViewFloorPlanChoiceItem defaultItem = _allOpenFloorPlans.FirstOrDefault((ViewFloorPlanChoiceItem item) => item.Id == activeFloorPlan.Id);
			if (defaultItem != null)
			{
				SourceViewsListBox.SelectedItem = defaultItem;
			}
		}
	}

	private void RefreshTargetItems()
	{
		HashSet<ElementId> selectedTargetIds = (from item in TargetViewsListBox.SelectedItems.OfType<ViewFloorPlanChoiceItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).ToHashSet();
		ElementId sourceId = (SourceViewsListBox.SelectedItem as ViewFloorPlanChoiceItem)?.Id;
		_targetItems = _allOpenFloorPlans.Where((ViewFloorPlanChoiceItem item) => sourceId == null || sourceId == ElementId.InvalidElementId || sourceId != item.Id).ToList();
		TargetViewsListBox.ItemsSource = _targetItems;
		TargetViewsListBox.SelectedItems.Clear();
		foreach (ViewFloorPlanChoiceItem targetItem in _targetItems)
		{
			if ((!(sourceId != null) || !(sourceId == targetItem.Id)) && selectedTargetIds.Contains(targetItem.Id))
			{
				TargetViewsListBox.SelectedItems.Add(targetItem);
			}
		}
	}

	private void UpdateCopyButtonState()
	{
		CopyButton.IsEnabled = SourceViewsListBox.SelectedItem != null && TargetViewsListBox.SelectedItems.Count > 0;
	}

	private static List<ViewFloorPlanChoiceItem> CollectOpenFloorPlans(UIDocument uiDocument)
	{
		Document document = uiDocument.Document;
		IList<UIView> openUiViews;
		try
		{
			openUiViews = uiDocument.GetOpenUIViews();
		}
		catch
		{
			return new List<ViewFloorPlanChoiceItem>();
		}
		List<ViewFloorPlanChoiceItem> result = new List<ViewFloorPlanChoiceItem>();
		HashSet<ElementId> seen = new HashSet<ElementId>();
		foreach (UIView uiView in openUiViews)
		{
			ElementId viewId = uiView.ViewId;
			if (!(viewId == null) && !(viewId == ElementId.InvalidElementId) && seen.Add(viewId) && document.GetElement(viewId) is ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } viewPlan)
			{
				result.Add(new ViewFloorPlanChoiceItem(viewPlan.Id, viewPlan.Name));
			}
		}
		return result.OrderBy<ViewFloorPlanChoiceItem, string>((ViewFloorPlanChoiceItem item) => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_copygridviewfloorwindow.xaml", UriKind.Relative);
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
			SourceViewsListBox.SelectionChanged += OnSourceSelectionChanged;
			break;
		case 2:
			TargetViewsListBox = (ListBox)target;
			TargetViewsListBox.SelectionChanged += OnTargetSelectionChanged;
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
