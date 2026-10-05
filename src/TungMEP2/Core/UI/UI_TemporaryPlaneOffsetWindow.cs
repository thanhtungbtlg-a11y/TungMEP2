using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_TemporaryPlaneOffsetWindow : Window, IComponentConnector
{
	internal TextBox OffsetTextBox;

	private bool _contentLoaded;

	public UI_TemporaryPlaneOffsetWindow()
	{
		InitializeComponent();
	}

	public bool TryGetOffsetMillimeters(out double offsetMm)
	{
		offsetMm = 0.0;
		string text = OffsetTextBox.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out offsetMm))
		{
			return true;
		}
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out offsetMm);
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!TryGetOffsetMillimeters(out var _))
		{
			MessageBox.Show(this, "Giá trị offset không hợp lệ.", "Mặt phẳng tạm", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_temporaryplaneoffsetwindow.xaml", UriKind.Relative);
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
			OffsetTextBox = (TextBox)target;
			break;
		case 2:
			((Button)target).Click += OnOkClick;
			break;
		case 3:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
