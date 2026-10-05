using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_GridDimensionOptionsWindow : Window, IComponentConnector
{
	internal CheckBox TopCheckBox;

	internal TextBox TopOffsetTextBox;

	internal CheckBox BottomCheckBox;

	internal TextBox BottomOffsetTextBox;

	internal CheckBox LeftCheckBox;

	internal TextBox LeftOffsetTextBox;

	internal CheckBox RightCheckBox;

	internal TextBox RightOffsetTextBox;

	internal CheckBox BubbleTopCheckBox;

	internal TextBox BubbleTopOffsetTextBox;

	internal CheckBox BubbleBottomCheckBox;

	internal TextBox BubbleBottomOffsetTextBox;

	internal CheckBox BubbleLeftCheckBox;

	internal TextBox BubbleLeftOffsetTextBox;

	internal CheckBox BubbleRightCheckBox;

	internal TextBox BubbleRightOffsetTextBox;

	private bool _contentLoaded;

	public UI_GridDimensionOptionsWindow()
	{
		InitializeComponent();
	}

	public bool TryGetOptions(out GridDimensionOptions options)
	{
		options = new GridDimensionOptions
		{
			Top = (TopCheckBox.IsChecked == true),
			Bottom = (BottomCheckBox.IsChecked == true),
			Left = (LeftCheckBox.IsChecked == true),
			Right = (RightCheckBox.IsChecked == true),
			BubbleTop = (BubbleTopCheckBox.IsChecked == true),
			BubbleBottom = (BubbleBottomCheckBox.IsChecked == true),
			BubbleLeft = (BubbleLeftCheckBox.IsChecked == true),
			BubbleRight = (BubbleRightCheckBox.IsChecked == true)
		};
		if (!options.Top && !options.Bottom && !options.Left && !options.Right)
		{
			return false;
		}
		if (!TryParseOffset(TopOffsetTextBox.Text, out var topOffset) || topOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(BottomOffsetTextBox.Text, out var bottomOffset) || bottomOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(LeftOffsetTextBox.Text, out var leftOffset) || leftOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(RightOffsetTextBox.Text, out var rightOffset) || rightOffset < 0.0)
		{
			return false;
		}
		options.TopOffsetMillimeters = topOffset;
		options.BottomOffsetMillimeters = bottomOffset;
		options.LeftOffsetMillimeters = leftOffset;
		options.RightOffsetMillimeters = rightOffset;
		if (!TryParseOffset(BubbleTopOffsetTextBox.Text, out var bubbleTopOffset) || bubbleTopOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(BubbleBottomOffsetTextBox.Text, out var bubbleBottomOffset) || bubbleBottomOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(BubbleLeftOffsetTextBox.Text, out var bubbleLeftOffset) || bubbleLeftOffset < 0.0)
		{
			return false;
		}
		if (!TryParseOffset(BubbleRightOffsetTextBox.Text, out var bubbleRightOffset) || bubbleRightOffset < 0.0)
		{
			return false;
		}
		options.BubbleTopOffsetMillimeters = bubbleTopOffset;
		options.BubbleBottomOffsetMillimeters = bubbleBottomOffset;
		options.BubbleLeftOffsetMillimeters = bubbleLeftOffset;
		options.BubbleRightOffsetMillimeters = bubbleRightOffset;
		return true;
	}

	private static bool TryParseOffset(string? text, out double value)
	{
		value = 0.0;
		string raw = text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}
		if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
		{
			return true;
		}
		return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!TryGetOptions(out GridDimensionOptions _))
		{
			MessageBox.Show(this, "Vui lòng chọn ít nhất 1 cạnh và nhập offset hợp lệ (>= 0).", "DIM Grid Theo Biên", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		else
		{
			base.DialogResult = true;
		}
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_griddimensionoptionswindow.xaml", UriKind.Relative);
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
			TopCheckBox = (CheckBox)target;
			break;
		case 2:
			TopOffsetTextBox = (TextBox)target;
			break;
		case 3:
			BottomCheckBox = (CheckBox)target;
			break;
		case 4:
			BottomOffsetTextBox = (TextBox)target;
			break;
		case 5:
			LeftCheckBox = (CheckBox)target;
			break;
		case 6:
			LeftOffsetTextBox = (TextBox)target;
			break;
		case 7:
			RightCheckBox = (CheckBox)target;
			break;
		case 8:
			RightOffsetTextBox = (TextBox)target;
			break;
		case 9:
			BubbleTopCheckBox = (CheckBox)target;
			break;
		case 10:
			BubbleTopOffsetTextBox = (TextBox)target;
			break;
		case 11:
			BubbleBottomCheckBox = (CheckBox)target;
			break;
		case 12:
			BubbleBottomOffsetTextBox = (TextBox)target;
			break;
		case 13:
			BubbleLeftCheckBox = (CheckBox)target;
			break;
		case 14:
			BubbleLeftOffsetTextBox = (TextBox)target;
			break;
		case 15:
			BubbleRightCheckBox = (CheckBox)target;
			break;
		case 16:
			BubbleRightOffsetTextBox = (TextBox)target;
			break;
		case 17:
			((Button)target).Click += OnOkClick;
			break;
		case 18:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
