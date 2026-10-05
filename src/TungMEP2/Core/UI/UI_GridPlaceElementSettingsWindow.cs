using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_GridPlaceElementSettingsWindow : Window, IComponentConnector
{
	internal TextBlock TemplateTextBlock;

	internal TextBox SpacingTextBox;

	private bool _contentLoaded;

	public double SpacingMillimeters { get; private set; }

	public UI_GridPlaceElementSettingsWindow(double initialSpacingMillimeters, string templateLabel)
	{
		InitializeComponent();
		SpacingTextBox.Text = initialSpacingMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
		TemplateTextBlock.Text = templateLabel;
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!TryParseSpacing(SpacingTextBox.Text, out var spacing) || spacing <= 0.0)
		{
			MessageBox.Show(this, "Spacing không hợp lệ. Vui lòng nhập số > 0.", "GridPlace Settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SpacingMillimeters = spacing;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private static bool TryParseSpacing(string text, out double spacing)
	{
		string raw = text?.Trim() ?? string.Empty;
		if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out spacing))
		{
			return true;
		}
		return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out spacing);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_gridplaceelementsettingswindow.xaml", UriKind.Relative);
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
			TemplateTextBlock = (TextBlock)target;
			break;
		case 2:
			SpacingTextBox = (TextBox)target;
			break;
		case 3:
			((Button)target).Click += OnOkClick;
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
