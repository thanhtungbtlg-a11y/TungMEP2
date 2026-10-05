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

public class UI_TransTempWindow : Window, IComponentConnector
{
	private readonly List<TransTempLinkItem> _links;

	internal ComboBox LinkComboBox;

	internal TextBox SearchTextBox;

	internal ListBox TemplateListBox;

	internal CheckBox IncludeFiltersCheckBox;

	internal Button TransferButton;

	private bool _contentLoaded;

	public Document? SelectedLinkDocument { get; private set; }

	public IReadOnlyList<ElementId> SelectedTemplateIds { get; private set; } = Array.Empty<ElementId>();

	public bool IncludeFilters => IncludeFiltersCheckBox.IsChecked == true;

	public UI_TransTempWindow(IList<TransTempLinkItem> links)
	{
		_links = (links ?? Array.Empty<TransTempLinkItem>()).Where((TransTempLinkItem item) => item != null).OrderBy<TransTempLinkItem, string>((TransTempLinkItem item) => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
		InitializeComponent();
		LinkComboBox.ItemsSource = _links;
		if (_links.Count > 0)
		{
			LinkComboBox.SelectedIndex = 0;
		}
		UpdateTransferButtonState();
	}

	private void OnLinkSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(LinkComboBox.SelectedItem is TransTempLinkItem selectedLink))
		{
			TemplateListBox.ItemsSource = Array.Empty<TransTempTemplateItem>();
			UpdateTransferButtonState();
			return;
		}
		TemplateListBox.ItemsSource = selectedLink.Templates;
		ICollectionView view = CollectionViewSource.GetDefaultView(TemplateListBox.ItemsSource);
		view.Filter = FilterTemplate;
		view.Refresh();
		UpdateTransferButtonState();
	}

	private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		if (TemplateListBox.ItemsSource != null)
		{
			CollectionViewSource.GetDefaultView(TemplateListBox.ItemsSource).Refresh();
		}
	}

	private bool FilterTemplate(object obj)
	{
		if (!(obj is TransTempTemplateItem item))
		{
			return false;
		}
		string keyword = (SearchTextBox.Text ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return true;
		}
		return item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateTransferButtonState();
	}

	private void OnSelectAllClick(object sender, RoutedEventArgs e)
	{
		TemplateListBox.SelectAll();
		UpdateTransferButtonState();
	}

	private void OnSelectNoneClick(object sender, RoutedEventArgs e)
	{
		TemplateListBox.UnselectAll();
		UpdateTransferButtonState();
	}

	private void OnTransferClick(object sender, RoutedEventArgs e)
	{
		if (!(LinkComboBox.SelectedItem is TransTempLinkItem selectedLink))
		{
			base.DialogResult = false;
			return;
		}
		List<ElementId> selectedTemplateIds = (from item in TemplateListBox.SelectedItems.OfType<TransTempTemplateItem>()
			select item.Id into id
			where id != null && id != ElementId.InvalidElementId
			select id).Distinct().ToList();
		if (selectedTemplateIds.Count == 0)
		{
			MessageBox.Show("Select at least one template.", "Transfer View Template", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		SelectedLinkDocument = selectedLink.Document;
		SelectedTemplateIds = selectedTemplateIds;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void UpdateTransferButtonState()
	{
		TransferButton.IsEnabled = TemplateListBox.SelectedItems.Count > 0;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_transtempwindow.xaml", UriKind.Relative);
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
			LinkComboBox = (ComboBox)target;
			LinkComboBox.SelectionChanged += OnLinkSelectionChanged;
			break;
		case 2:
			SearchTextBox = (TextBox)target;
			SearchTextBox.TextChanged += OnSearchTextChanged;
			break;
		case 3:
			((Button)target).Click += OnSelectAllClick;
			break;
		case 4:
			((Button)target).Click += OnSelectNoneClick;
			break;
		case 5:
			TemplateListBox = (ListBox)target;
			TemplateListBox.SelectionChanged += OnTemplateSelectionChanged;
			break;
		case 6:
			IncludeFiltersCheckBox = (CheckBox)target;
			break;
		case 7:
			TransferButton = (Button)target;
			TransferButton.Click += OnTransferClick;
			break;
		case 8:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
