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
using VP_RevitAPI.Helper.Sheets.DuplicateSheets;

namespace VP_RevitAPI.UI;

public class UI_DuplicateSheetsWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly IList<ViewSheet> _selectedSheets;

	private readonly List<ParameterSelectionGroup> _parameterGroups = new List<ParameterSelectionGroup>();

	internal TextBlock SelectedSheetsTextBlock;

	internal TextBlock SelectedSheetsPreviewTextBlock;

	internal TextBox ViewFindTextBox;

	internal TextBox ViewReplaceTextBox;

	internal TextBox ViewPrefixTextBox;

	internal TextBox ViewSuffixTextBox;

	internal TextBox SheetNumberFindTextBox;

	internal TextBox SheetNumberReplaceTextBox;

	internal TextBox SheetNumberPrefixTextBox;

	internal TextBox SheetNumberSuffixTextBox;

	internal TextBox SheetNameFindTextBox;

	internal TextBox SheetNameReplaceTextBox;

	internal TextBox SheetNamePrefixTextBox;

	internal TextBox SheetNameSuffixTextBox;

	internal CheckBox IncludeViewsCheckBox;

	internal CheckBox IncludeLegendsCheckBox;

	internal CheckBox IncludeSchedulesCheckBox;

	internal CheckBox IncludeImagesCheckBox;

	internal CheckBox IncludeLinesCheckBox;

	internal CheckBox IncludeTextCheckBox;

	internal CheckBox IncludeCloudsCheckBox;

	internal CheckBox IncludeDwgsCheckBox;

	internal CheckBox IncludeSymbolsCheckBox;

	internal CheckBox IncludeDimensionsCheckBox;

	internal CheckBox IncludeAdditionalRevisionsCheckBox;

	internal CheckBox UseExistingLegendsCheckBox;

	internal CheckBox UseExistingSchedulesCheckBox;

	internal CheckBox PreserveTitleBlockPositionCheckBox;

	internal RadioButton DuplicateRadio;

	internal RadioButton DuplicateDetailingRadio;

	internal RadioButton DuplicateDependentRadio;

	internal ComboBox ViewTemplateModeComboBox;

	internal TreeView ParameterGroupsTreeView;

	private bool _contentLoaded;

	public SheetDuplicateOptions? Options { get; private set; }

	public UI_DuplicateSheetsWindow(IList<ViewSheet> selectedSheets)
	{
		_selectedSheets = selectedSheets;
		InitializeComponent();
		BindSelectionPreview();
		LoadParameterGroups();
	}

	private void BindSelectionPreview()
	{
		SelectedSheetsTextBlock.Text = $"Selected sheets: {_selectedSheets.Count}";
		IEnumerable<string> preview = from sheet in _selectedSheets.Take(8)
			select sheet.SheetNumber + " - " + sheet.Name;
		string previewText = string.Join(Environment.NewLine, preview);
		if (_selectedSheets.Count > 8)
		{
			previewText = previewText + Environment.NewLine + "...";
		}
		SelectedSheetsPreviewTextBlock.Text = previewText;
	}

	private void OnRunClick(object sender, RoutedEventArgs e)
	{
		Options = BuildOptions();
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private SheetDuplicateOptions BuildOptions()
	{
		SheetDuplicateOptions options = new SheetDuplicateOptions
		{
			DuplicateViews = (IncludeViewsCheckBox.IsChecked == true),
			DuplicateLegends = (IncludeLegendsCheckBox.IsChecked == true),
			DuplicateSchedules = (IncludeSchedulesCheckBox.IsChecked == true),
			DuplicateImages = (IncludeImagesCheckBox.IsChecked == true),
			DuplicateLines = (IncludeLinesCheckBox.IsChecked == true),
			DuplicateText = (IncludeTextCheckBox.IsChecked == true),
			DuplicateClouds = (IncludeCloudsCheckBox.IsChecked == true),
			DuplicateDwgs = (IncludeDwgsCheckBox.IsChecked == true),
			DuplicateSymbols = (IncludeSymbolsCheckBox.IsChecked == true),
			DuplicateDimensions = (IncludeDimensionsCheckBox.IsChecked == true),
			DuplicateAdditionalRevisions = (IncludeAdditionalRevisionsCheckBox.IsChecked == true),
			UseExistingLegends = (UseExistingLegendsCheckBox.IsChecked == true),
			UseExistingSchedules = (UseExistingSchedulesCheckBox.IsChecked == true),
			PreserveTitleBlockPosition = (PreserveTitleBlockPositionCheckBox.IsChecked == true),
			ViewDuplicateOption = ReadViewDuplicateOption(),
			ViewTemplateMode = ReadTemplateMode()
		};
		options.ViewNameRule.Find = ViewFindTextBox.Text ?? string.Empty;
		options.ViewNameRule.Replace = ViewReplaceTextBox.Text ?? string.Empty;
		options.ViewNameRule.Prefix = ViewPrefixTextBox.Text ?? string.Empty;
		options.ViewNameRule.Suffix = ViewSuffixTextBox.Text ?? string.Empty;
		options.SheetNumberRule.Find = SheetNumberFindTextBox.Text ?? string.Empty;
		options.SheetNumberRule.Replace = SheetNumberReplaceTextBox.Text ?? string.Empty;
		options.SheetNumberRule.Prefix = SheetNumberPrefixTextBox.Text ?? string.Empty;
		options.SheetNumberRule.Suffix = SheetNumberSuffixTextBox.Text ?? string.Empty;
		options.SheetNameRule.Find = SheetNameFindTextBox.Text ?? string.Empty;
		options.SheetNameRule.Replace = SheetNameReplaceTextBox.Text ?? string.Empty;
		options.SheetNameRule.Prefix = SheetNamePrefixTextBox.Text ?? string.Empty;
		options.SheetNameRule.Suffix = SheetNameSuffixTextBox.Text ?? string.Empty;
		foreach (ParameterSelectionItem item in from parameterSelectionItem in _parameterGroups.SelectMany((ParameterSelectionGroup @group) => @group.Items)
			where parameterSelectionItem.IsChecked
			select parameterSelectionItem)
		{
			options.SheetParametersToCopy.Add(item.Name);
		}
		return options;
	}

	private void LoadParameterGroups()
	{
		if (_selectedSheets.Count == 0)
		{
			ParameterGroupsTreeView.ItemsSource = _parameterGroups;
			return;
		}
		ViewSheet firstSheet = _selectedSheets[0];
		Dictionary<string, ParameterSelectionGroup> byGroup = new Dictionary<string, ParameterSelectionGroup>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Parameter parameter in firstSheet.Parameters)
		{
			if (!CanCopyParameter(parameter))
			{
				continue;
			}
			string name = parameter.Definition?.Name?.Trim() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(name) && seenNames.Add(name))
			{
				string groupName = GetParameterGroupName(parameter);
				if (!byGroup.TryGetValue(groupName, out var group))
				{
					group = (byGroup[groupName] = new ParameterSelectionGroup(groupName));
				}
				group.Items.Add(new ParameterSelectionItem(name, isChecked: true));
			}
		}
		foreach (ParameterSelectionGroup group2 in byGroup.Values)
		{
			group2.Items.Sort((ParameterSelectionItem left, ParameterSelectionItem right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
		}
		_parameterGroups.Clear();
		_parameterGroups.AddRange(byGroup.Values.OrderBy<ParameterSelectionGroup, string>((ParameterSelectionGroup parameterSelectionGroup2) => parameterSelectionGroup2.Name, StringComparer.OrdinalIgnoreCase));
		ParameterGroupsTreeView.ItemsSource = _parameterGroups;
	}

	private static bool CanCopyParameter(Parameter parameter)
	{
		if (parameter == null)
		{
			return false;
		}
		if (parameter.IsReadOnly)
		{
			return false;
		}
		if (parameter.StorageType == StorageType.None)
		{
			return false;
		}
		return !IsSheetNumber(parameter);
	}

	private static bool IsSheetNumber(Parameter parameter)
	{
		return parameter.Id == new ElementId(-1007401L);
	}

	private static string GetParameterGroupName(Parameter parameter)
	{
		BuiltInParameterGroup group = parameter.Definition.ParameterGroup;
		try
		{
			string label = LabelUtils.GetLabelFor(group);
			if (!string.IsNullOrWhiteSpace(label))
			{
				return label;
			}
		}
		catch
		{
		}
		return "Other";
	}

	private void OnParameterGroupChecked(object sender, RoutedEventArgs e)
	{
		if (!(sender is CheckBox { DataContext: ParameterSelectionGroup group }))
		{
			return;
		}
		foreach (ParameterSelectionItem item in group.Items)
		{
			item.IsChecked = true;
		}
	}

	private void OnParameterGroupUnchecked(object sender, RoutedEventArgs e)
	{
		if (!(sender is CheckBox { DataContext: ParameterSelectionGroup group }))
		{
			return;
		}
		foreach (ParameterSelectionItem item in group.Items)
		{
			item.IsChecked = false;
		}
	}

	private ViewDuplicateOption ReadViewDuplicateOption()
	{
		if (DuplicateDetailingRadio.IsChecked == true)
		{
			return ViewDuplicateOption.WithDetailing;
		}
		if (DuplicateDependentRadio.IsChecked == true)
		{
			return ViewDuplicateOption.AsDependent;
		}
		return ViewDuplicateOption.Duplicate;
	}

	private SheetViewTemplateMode ReadTemplateMode()
	{
		if (!(ViewTemplateModeComboBox.SelectedItem is ComboBoxItem { Content: var content }))
		{
			return SheetViewTemplateMode.Keep;
		}
		string text = content?.ToString() ?? string.Empty;
		if (text.Equals("Remove", StringComparison.OrdinalIgnoreCase))
		{
			return SheetViewTemplateMode.Remove;
		}
		if (text.Equals("Duplicate", StringComparison.OrdinalIgnoreCase))
		{
			return SheetViewTemplateMode.Duplicate;
		}
		return SheetViewTemplateMode.Keep;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_duplicatesheetswindow.xaml", UriKind.Relative);
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
			SelectedSheetsTextBlock = (TextBlock)target;
			break;
		case 2:
			SelectedSheetsPreviewTextBlock = (TextBlock)target;
			break;
		case 3:
			ViewFindTextBox = (TextBox)target;
			break;
		case 4:
			ViewReplaceTextBox = (TextBox)target;
			break;
		case 5:
			ViewPrefixTextBox = (TextBox)target;
			break;
		case 6:
			ViewSuffixTextBox = (TextBox)target;
			break;
		case 7:
			SheetNumberFindTextBox = (TextBox)target;
			break;
		case 8:
			SheetNumberReplaceTextBox = (TextBox)target;
			break;
		case 9:
			SheetNumberPrefixTextBox = (TextBox)target;
			break;
		case 10:
			SheetNumberSuffixTextBox = (TextBox)target;
			break;
		case 11:
			SheetNameFindTextBox = (TextBox)target;
			break;
		case 12:
			SheetNameReplaceTextBox = (TextBox)target;
			break;
		case 13:
			SheetNamePrefixTextBox = (TextBox)target;
			break;
		case 14:
			SheetNameSuffixTextBox = (TextBox)target;
			break;
		case 15:
			IncludeViewsCheckBox = (CheckBox)target;
			break;
		case 16:
			IncludeLegendsCheckBox = (CheckBox)target;
			break;
		case 17:
			IncludeSchedulesCheckBox = (CheckBox)target;
			break;
		case 18:
			IncludeImagesCheckBox = (CheckBox)target;
			break;
		case 19:
			IncludeLinesCheckBox = (CheckBox)target;
			break;
		case 20:
			IncludeTextCheckBox = (CheckBox)target;
			break;
		case 21:
			IncludeCloudsCheckBox = (CheckBox)target;
			break;
		case 22:
			IncludeDwgsCheckBox = (CheckBox)target;
			break;
		case 23:
			IncludeSymbolsCheckBox = (CheckBox)target;
			break;
		case 24:
			IncludeDimensionsCheckBox = (CheckBox)target;
			break;
		case 25:
			IncludeAdditionalRevisionsCheckBox = (CheckBox)target;
			break;
		case 26:
			UseExistingLegendsCheckBox = (CheckBox)target;
			break;
		case 27:
			UseExistingSchedulesCheckBox = (CheckBox)target;
			break;
		case 28:
			PreserveTitleBlockPositionCheckBox = (CheckBox)target;
			break;
		case 29:
			DuplicateRadio = (RadioButton)target;
			break;
		case 30:
			DuplicateDetailingRadio = (RadioButton)target;
			break;
		case 31:
			DuplicateDependentRadio = (RadioButton)target;
			break;
		case 32:
			ViewTemplateModeComboBox = (ComboBox)target;
			break;
		case 33:
			ParameterGroupsTreeView = (TreeView)target;
			break;
		case 35:
			((Button)target).Click += OnRunClick;
			break;
		case 36:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IStyleConnector.Connect(int connectionId, object target)
	{
		if (connectionId == 34)
		{
			((CheckBox)target).Checked += OnParameterGroupChecked;
			((CheckBox)target).Unchecked += OnParameterGroupUnchecked;
		}
	}
}
