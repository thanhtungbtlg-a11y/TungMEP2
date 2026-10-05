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
using VP_RevitAPI.State;

namespace VP_RevitAPI.UI;

public class UI_3DSyncWindow : Window, IComponentConnector
{
	private sealed class OpenViewChoiceItem
	{
		public ElementId Id { get; }

		public string UniqueId { get; }

		public string Kind { get; }

		public string Name { get; }

		public string DisplayName => Kind + " | " + Name;

		public OpenViewChoiceItem(ElementId id, string? uniqueId, string kind, string name)
		{
			Id = id;
			UniqueId = uniqueId?.Trim() ?? string.Empty;
			Kind = kind?.Trim() ?? string.Empty;
			Name = name?.Trim() ?? string.Empty;
		}
	}

	private const string DialogTitle = "3D Sync";

	private readonly List<OpenViewChoiceItem> _planItems;

	private readonly List<OpenViewChoiceItem> _threeDItems;

	internal ListBox PlanViewsListBox;

	internal ListBox ThreeDViewsListBox;

	internal Button StartSyncButton;

	private bool _contentLoaded;

	public ElementId? SelectedPlanViewId { get; private set; }

	public IReadOnlyList<ElementId> SelectedView3DIds { get; private set; } = Array.Empty<ElementId>();

	public ThreeDSyncDialogAction Action { get; private set; } = ThreeDSyncDialogAction.None;

	public UI_3DSyncWindow(UIDocument uiDocument, ThreeDSyncViewSelectionConfig? initialSelection = null)
	{
		_planItems = CollectPlanViews(uiDocument);
		_threeDItems = CollectThreeDViews(uiDocument);
		InitializeComponent();
		PlanViewsListBox.ItemsSource = _planItems;
		ThreeDViewsListBox.ItemsSource = _threeDItems;
		ApplyInitialSelection(initialSelection);
		UpdateStartSyncButtonState();
	}

	private void OnStartSyncClick(object sender, RoutedEventArgs e)
	{
		if (!(PlanViewsListBox.SelectedItem is OpenViewChoiceItem selectedPlan))
		{
			MessageBox.Show(this, "Hay chon 1 PlanView 2D.", "3D Sync", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		List<ElementId> selectedThreeDIds = (from item in ThreeDViewsListBox.SelectedItems.OfType<OpenViewChoiceItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).Distinct().ToList();
		if (selectedThreeDIds.Count == 0)
		{
			MessageBox.Show(this, "Hay chon it nhat 1 View3D.", "3D Sync", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedPlanViewId = selectedPlan.Id;
		SelectedView3DIds = selectedThreeDIds;
		Action = ThreeDSyncDialogAction.Start;
		base.DialogResult = true;
	}

	private void OnStopSyncClick(object sender, RoutedEventArgs e)
	{
		Action = ThreeDSyncDialogAction.Stop;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		Action = ThreeDSyncDialogAction.None;
		base.DialogResult = false;
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateStartSyncButtonState();
	}

	private void ApplyInitialSelection(ThreeDSyncViewSelectionConfig? initialSelection)
	{
		if (initialSelection == null)
		{
			return;
		}
		OpenViewChoiceItem planItem = FindByUniqueId(_planItems, initialSelection.PlanViewUniqueId) ?? FindByName(_planItems, initialSelection.PlanViewName);
		if (planItem != null)
		{
			PlanViewsListBox.SelectedItem = planItem;
		}
		foreach (ThreeDSyncViewReference configuredView in initialSelection.ThreeDViews)
		{
			OpenViewChoiceItem view3DItem = FindByUniqueId(_threeDItems, configuredView.UniqueId) ?? FindByName(_threeDItems, configuredView.Name);
			if (view3DItem != null && !ThreeDViewsListBox.SelectedItems.Contains(view3DItem))
			{
				ThreeDViewsListBox.SelectedItems.Add(view3DItem);
			}
		}
	}

	private void UpdateStartSyncButtonState()
	{
		StartSyncButton.IsEnabled = PlanViewsListBox.SelectedItem != null && ThreeDViewsListBox.SelectedItems.Count > 0;
	}

	private static OpenViewChoiceItem? FindByUniqueId(IEnumerable<OpenViewChoiceItem> items, string? uniqueId)
	{
		if (string.IsNullOrWhiteSpace(uniqueId))
		{
			return null;
		}
		return items.FirstOrDefault((OpenViewChoiceItem item) => string.Equals(item.UniqueId, uniqueId, StringComparison.OrdinalIgnoreCase));
	}

	private static OpenViewChoiceItem? FindByName(IEnumerable<OpenViewChoiceItem> items, string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		return items.FirstOrDefault((OpenViewChoiceItem item) => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	private static List<OpenViewChoiceItem> CollectPlanViews(UIDocument uiDocument)
	{
		return (from view in GetOpenViews(uiDocument).OfType<ViewPlan>()
			where !view.IsTemplate
			select new OpenViewChoiceItem(view.Id, view.UniqueId, "Plan", view.Name)).OrderBy<OpenViewChoiceItem, string>((OpenViewChoiceItem item) => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<OpenViewChoiceItem> CollectThreeDViews(UIDocument uiDocument)
	{
		return (from view in GetOpenViews(uiDocument).OfType<View3D>()
			where !view.IsTemplate && !view.IsPerspective
			select new OpenViewChoiceItem(view.Id, view.UniqueId, "3D", view.Name)).OrderBy<OpenViewChoiceItem, string>((OpenViewChoiceItem item) => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static IReadOnlyList<View> GetOpenViews(UIDocument uiDocument)
	{
		Document document = uiDocument.Document;
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
			ElementId viewId = uiView.ViewId;
			if (!(viewId == null) && !(viewId == ElementId.InvalidElementId) && seen.Add(viewId) && document.GetElement(viewId) is View view)
			{
				result.Add(view);
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_3dsyncwindow.xaml", UriKind.Relative);
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
			PlanViewsListBox = (ListBox)target;
			PlanViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 2:
			ThreeDViewsListBox = (ListBox)target;
			ThreeDViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 3:
			StartSyncButton = (Button)target;
			StartSyncButton.Click += OnStartSyncClick;
			break;
		case 4:
			((Button)target).Click += OnStopSyncClick;
			break;
		case 5:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
