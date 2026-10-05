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

namespace VP_RevitAPI.UI;

public class UI_ChangeSheetTitleBlockTypeWindow : Window, IComponentConnector
{
	private readonly List<TitleBlockFamilyOption> _families;

	internal TextBlock SelectionSummaryTextBlock;

	internal ComboBox FamilyComboBox;

	internal ComboBox TypeComboBox;

	internal TextBlock UsedTitleBlockTypesHeaderTextBlock;

	internal ListBox UsedTitleBlockTypesListBox;

	private bool _contentLoaded;

	public ElementId SelectedTypeId { get; private set; } = ElementId.InvalidElementId;

	public UI_ChangeSheetTitleBlockTypeWindow(IEnumerable<TitleBlockFamilyOption> families, ElementId currentTypeId, int selectedSheetCount, IReadOnlyList<string> usedTypeSummaryLines)
	{
		IReadOnlyList<string> summaryLines = usedTypeSummaryLines ?? Array.Empty<string>();
		_families = (families ?? Array.Empty<TitleBlockFamilyOption>()).Where((TitleBlockFamilyOption item) => item != null).ToList();
		InitializeComponent();
		SelectionSummaryTextBlock.Text = $"Số sheet được chọn: {selectedSheetCount}";
		UsedTitleBlockTypesHeaderTextBlock.Text = $"TitleBlock đang dùng ({summaryLines.Count} loại):";
		UsedTitleBlockTypesListBox.ItemsSource = summaryLines;
		FamilyComboBox.ItemsSource = _families;
		if (_families.Count == 0)
		{
			TypeComboBox.ItemsSource = Array.Empty<TitleBlockTypeOption>();
			return;
		}
		TitleBlockFamilyOption selectedFamily = _families.FirstOrDefault((TitleBlockFamilyOption family) => family.Types.Any((TitleBlockTypeOption type) => type.Id == currentTypeId));
		FamilyComboBox.SelectedItem = selectedFamily ?? _families[0];
	}

	private void OnFamilySelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(FamilyComboBox.SelectedItem is TitleBlockFamilyOption selectedFamily))
		{
			TypeComboBox.ItemsSource = Array.Empty<TitleBlockTypeOption>();
			TypeComboBox.SelectedItem = null;
			return;
		}
		TypeComboBox.ItemsSource = selectedFamily.Types;
		if (selectedFamily.Types.Count == 0)
		{
			TypeComboBox.SelectedItem = null;
		}
		else
		{
			TypeComboBox.SelectedIndex = 0;
		}
	}

	private void OnApplyClick(object sender, RoutedEventArgs e)
	{
		if (!(TypeComboBox.SelectedItem is TitleBlockTypeOption selectedType))
		{
			MessageBox.Show("Hãy chọn type TitleBlock.", "Change Sheet TitleBlock Type", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		SelectedTypeId = selectedType.Id;
		base.DialogResult = true;
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_changesheettitleblocktypewindow.xaml", UriKind.Relative);
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
			SelectionSummaryTextBlock = (TextBlock)target;
			break;
		case 2:
			FamilyComboBox = (ComboBox)target;
			FamilyComboBox.SelectionChanged += OnFamilySelectionChanged;
			break;
		case 3:
			TypeComboBox = (ComboBox)target;
			break;
		case 4:
			UsedTitleBlockTypesHeaderTextBlock = (TextBlock)target;
			break;
		case 5:
			UsedTitleBlockTypesListBox = (ListBox)target;
			break;
		case 6:
			((Button)target).Click += OnApplyClick;
			break;
		case 7:
			((Button)target).Click += OnCloseClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
