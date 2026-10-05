using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public class UI_ConvertDraftingToLegendWindow : Window, IComponentConnector
{
	private sealed class ViewChoiceItem
	{
		public ElementId Id { get; }

		public string ViewName { get; }

		public string DisplayName => ViewName;

		public ViewChoiceItem(ElementId id, string viewName)
		{
			Id = id;
			ViewName = (viewName ?? string.Empty).Trim();
		}
	}

	private readonly List<ViewChoiceItem> _draftingItems;

	private readonly ICollectionView _draftingItemsView;

	internal TextBox DraftingSearchTextBox;

	internal ListBox DraftingViewsListBox;

	internal CheckBox CopyOverridesCheckBox;

	internal CheckBox CopyScaleCheckBox;

	internal Button ConvertButton;

	private bool _contentLoaded;

	public IReadOnlyList<ElementId> SelectedDraftingViewIds { get; private set; } = Array.Empty<ElementId>();

	public bool CopyOverrides => CopyOverridesCheckBox.IsChecked == true;

	public bool CopyScale => CopyScaleCheckBox.IsChecked == true;

	public UI_ConvertDraftingToLegendWindow(IList<View> draftingViews, IEnumerable<ElementId>? preselectedDraftingViewIds = null)
	{
		_draftingItems = (from v in draftingViews ?? Array.Empty<View>()
			where v != null
			select new ViewChoiceItem(v.Id, v.Name)).OrderBy<ViewChoiceItem, string>((ViewChoiceItem x) => x.ViewName, StringComparer.OrdinalIgnoreCase).ToList();
		InitializeComponent();
		DraftingViewsListBox.ItemsSource = _draftingItems;
		_draftingItemsView = CollectionViewSource.GetDefaultView(DraftingViewsListBox.ItemsSource);
		_draftingItemsView.Filter = FilterDraftingItem;
		ApplyPreselection(preselectedDraftingViewIds);
		UpdateConvertButtonState();
	}

	private void OnConvertClick(object sender, RoutedEventArgs e)
	{
		SelectedDraftingViewIds = (from item in DraftingViewsListBox.SelectedItems.OfType<ViewChoiceItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).Distinct().ToList();
		base.DialogResult = SelectedDraftingViewIds.Count > 0;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateConvertButtonState();
	}

	private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		List<ElementId> selectedIds = (from item in DraftingViewsListBox.SelectedItems.OfType<ViewChoiceItem>()
			select item.Id).ToList();
		_draftingItemsView.Refresh();
		ReselectDraftingIds(selectedIds);
		UpdateConvertButtonState();
	}

	private bool FilterDraftingItem(object obj)
	{
		if (!(obj is ViewChoiceItem item))
		{
			return false;
		}
		string keyword = (DraftingSearchTextBox.Text ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return true;
		}
		return item.ViewName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void ApplyPreselection(IEnumerable<ElementId>? preselectedDraftingViewIds)
	{
		if (preselectedDraftingViewIds != null)
		{
			List<ElementId> ids = preselectedDraftingViewIds.Where((ElementId id) => id != null && id != ElementId.InvalidElementId).Distinct().ToList();
			ReselectDraftingIds(ids);
			if (DraftingViewsListBox.SelectedItems.Count > 0)
			{
				DraftingViewsListBox.ScrollIntoView(DraftingViewsListBox.SelectedItems[0]);
			}
		}
	}

	private void ReselectDraftingIds(ICollection<ElementId> selectedIds)
	{
		DraftingViewsListBox.SelectedItems.Clear();
		if (selectedIds.Count == 0)
		{
			return;
		}
		HashSet<long> selectedValues = new HashSet<long>(selectedIds.Select((ElementId id) => id.Value));
		foreach (ViewChoiceItem item in _draftingItems)
		{
			if (selectedValues.Contains(item.Id.Value))
			{
				DraftingViewsListBox.SelectedItems.Add(item);
			}
		}
	}

	private void UpdateConvertButtonState()
	{
		ConvertButton.IsEnabled = DraftingViewsListBox.SelectedItems.Count > 0;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_convertdraftingtolegendwindow.xaml", UriKind.Relative);
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
			DraftingSearchTextBox = (TextBox)target;
			DraftingSearchTextBox.TextChanged += OnSearchTextChanged;
			break;
		case 2:
			DraftingViewsListBox = (ListBox)target;
			DraftingViewsListBox.SelectionChanged += OnSelectionChanged;
			break;
		case 3:
			CopyOverridesCheckBox = (CheckBox)target;
			break;
		case 4:
			CopyScaleCheckBox = (CheckBox)target;
			break;
		case 5:
			ConvertButton = (Button)target;
			ConvertButton.Click += OnConvertClick;
			break;
		case 6:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
