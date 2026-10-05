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
using VP_RevitAPI.Helper.Sheets.AlignViewports;

namespace VP_RevitAPI.UI;

public class UI_AlignViewportsWindow : Window, IComponentConnector
{
	private readonly List<SheetChoiceItem> _items;

	internal ListBox SheetsListBox;

	internal CheckBox ApplyCropScopeCheckBox;

	internal CheckBox OverlapViewPlansCheckBox;

	internal CheckBox AlignLegendCheckBox;

	internal CheckBox AlignViewportTitlesCheckBox;

	internal CheckBox ApplyTitleBlockTypeCheckBox;

	private bool _contentLoaded;

	public ViewSheet? MainSheet { get; private set; }

	public AlignViewportsOptions? Options { get; private set; }

	public UI_AlignViewportsWindow(IList<ViewSheet> selectedSheets)
	{
		InitializeComponent();
		_items = selectedSheets.OrderBy((ViewSheet sheet) => sheet.SheetNumber).Select((ViewSheet sheet, int index) => new SheetChoiceItem(sheet, index == 0)).ToList();
		SheetsListBox.ItemsSource = _items;
	}

	private void OnRunClick(object sender, RoutedEventArgs e)
	{
		SheetChoiceItem selectedMain = _items.FirstOrDefault((SheetChoiceItem item) => item.IsMain);
		if (selectedMain == null)
		{
			MessageBox.Show(this, "Please select a main sheet.", "Align Viewports", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		MainSheet = selectedMain.Sheet;
		Options = new AlignViewportsOptions
		{
			ApplySameCropScopeBox = (ApplyCropScopeCheckBox.IsChecked == true),
			OverlapMultipleViewPlans = (OverlapViewPlansCheckBox.IsChecked == true),
			AlignLegendIfSame = (AlignLegendCheckBox.IsChecked == true),
			AlignViewportTitlesXY = (AlignViewportTitlesCheckBox.IsChecked == true),
			ApplySameTitleBlockType = (ApplyTitleBlockTypeCheckBox.IsChecked == true)
		};
		base.DialogResult = true;
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_alignviewportswindow.xaml", UriKind.Relative);
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
			SheetsListBox = (ListBox)target;
			break;
		case 2:
			ApplyCropScopeCheckBox = (CheckBox)target;
			break;
		case 3:
			OverlapViewPlansCheckBox = (CheckBox)target;
			break;
		case 4:
			AlignLegendCheckBox = (CheckBox)target;
			break;
		case 5:
			AlignViewportTitlesCheckBox = (CheckBox)target;
			break;
		case 6:
			ApplyTitleBlockTypeCheckBox = (CheckBox)target;
			break;
		case 7:
			((Button)target).Click += OnRunClick;
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
