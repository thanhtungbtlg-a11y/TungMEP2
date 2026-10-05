using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_SelectElementsByLevelActionWindow : Window, IComponentConnector
{
	private readonly List<CategoryOption> _categories;

	internal TextBlock SummaryTextBlock;

	internal RadioButton SelectionRadioButton;

	internal RadioButton FilterCategoryRadioButton;

	internal ListBox CategoriesListBox;

	private bool _contentLoaded;

	public SelectElementsByLevelAction SelectedAction { get; private set; } = SelectElementsByLevelAction.Selection;

	public IReadOnlyList<string> SelectedCategoryNames { get; private set; } = Array.Empty<string>();

	public UI_SelectElementsByLevelActionWindow(int totalMatched, IEnumerable<CategoryOption> categories)
	{
		_categories = (from item in categories ?? Array.Empty<CategoryOption>()
			where item != null
			orderby item.Count descending
			select item).ThenBy<CategoryOption, string>((CategoryOption item) => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		InitializeComponent();
		SummaryTextBlock.Text = $"Đã lọc được {totalMatched} phần tử. Chọn chức năng tiếp theo:";
		CategoriesListBox.ItemsSource = _categories;
		SelectionRadioButton.IsChecked = true;
		if (_categories.Count > 0)
		{
			CategoriesListBox.SelectedIndex = 0;
		}
	}

	private void OnModeChanged(object sender, RoutedEventArgs e)
	{
		bool enableCategoryList = FilterCategoryRadioButton.IsChecked == true;
		CategoriesListBox.IsEnabled = enableCategoryList;
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (TryResolveScope(out IReadOnlyList<string> selectedCategories))
		{
			SelectedAction = ((selectedCategories.Count != 0) ? SelectElementsByLevelAction.FilterByCategory : SelectElementsByLevelAction.Selection);
			SelectedCategoryNames = selectedCategories;
			base.DialogResult = true;
		}
	}

	private void OnRelevelClick(object sender, RoutedEventArgs e)
	{
		if (TryResolveScope(out IReadOnlyList<string> selectedCategories))
		{
			SelectedAction = SelectElementsByLevelAction.Relevel;
			SelectedCategoryNames = selectedCategories;
			base.DialogResult = true;
		}
	}

	private bool TryResolveScope(out IReadOnlyList<string> selectedCategories)
	{
		selectedCategories = Array.Empty<string>();
		if (SelectionRadioButton.IsChecked == true)
		{
			return true;
		}
		if (FilterCategoryRadioButton.IsChecked == true)
		{
			List<string> categories = (from item in _categories
				where item.IsChecked
				select item.Name).Distinct<string>(StringComparer.CurrentCultureIgnoreCase).ToList();
			if (categories.Count == 0)
			{
				MessageBox.Show("Hãy tick ít nhất 1 category cần lọc.", "Select Elements By Level", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return false;
			}
			selectedCategories = categories;
			return true;
		}
		MessageBox.Show("Hãy chọn phạm vi áp dụng.", "Select Elements By Level", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		return false;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_selectelementsbylevelactionwindow.xaml", UriKind.Relative);
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
			SelectionRadioButton = (RadioButton)target;
			SelectionRadioButton.Checked += OnModeChanged;
			break;
		case 3:
			FilterCategoryRadioButton = (RadioButton)target;
			FilterCategoryRadioButton.Checked += OnModeChanged;
			break;
		case 4:
			CategoriesListBox = (ListBox)target;
			break;
		case 5:
			((Button)target).Click += OnRelevelClick;
			break;
		case 6:
			((Button)target).Click += OnOkClick;
			break;
		case 7:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
