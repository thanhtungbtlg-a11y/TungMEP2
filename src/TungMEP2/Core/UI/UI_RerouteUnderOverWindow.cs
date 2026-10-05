using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_RerouteUnderOverWindow : Window, IComponentConnector
{
	internal TextBox OffsetTextBox;

	internal ComboBox AngleComboBox;

	private bool _contentLoaded;

	public double SelectedOffsetMillimeters { get; private set; }

	public double SelectedAngleDegrees { get; private set; }

	public UI_RerouteUnderOverWindow(IReadOnlyCollection<double> allowedAnglesDegrees, double defaultOffsetMillimeters, double defaultAngleDegrees)
	{
		InitializeComponent();
		foreach (double angle in (allowedAnglesDegrees.Count == 0) ? new List<double> { 22.5, 30.0, 45.0, 60.0, 90.0 } : allowedAnglesDegrees.OrderBy((double v) => v).ToList())
		{
			AngleComboBox.Items.Add(angle.ToString("0.###", CultureInfo.InvariantCulture));
		}
		OffsetTextBox.Text = defaultOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
		AngleComboBox.Text = defaultAngleDegrees.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!TryParseDouble(OffsetTextBox.Text, out var offsetMillimeters))
		{
			MessageBox.Show(this, "Invalid vertical offset.", "Reroute Under/Over", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!TryParseDouble(AngleComboBox.Text, out var angleDegrees))
		{
			MessageBox.Show(this, "Invalid angle value.", "Reroute Under/Over", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (Math.Abs(offsetMillimeters) < 1E-09)
		{
			MessageBox.Show(this, "Vertical offset cannot be zero.", "Reroute Under/Over", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (angleDegrees <= 0.0 || angleDegrees > 90.0)
		{
			MessageBox.Show(this, "Angle must be in range (0, 90].", "Reroute Under/Over", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedOffsetMillimeters = offsetMillimeters;
		SelectedAngleDegrees = angleDegrees;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private static bool TryParseDouble(string? text, out double value)
	{
		value = 0.0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
		{
			return true;
		}
		return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_rerouteunderoverwindow.xaml", UriKind.Relative);
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
			AngleComboBox = (ComboBox)target;
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
