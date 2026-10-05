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
using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public class UI_AutoTagPipeWindow : Window, IComponentConnector
{
	private readonly List<AutoTagPipeTagTypeItem> _tagTypes;

	internal TextBox LengthMinTextBox;

	internal ComboBox TagTypeComboBox;

	internal TextBox DnMaxTextBox;

	private bool _contentLoaded;

	public double SelectedLengthMinMillimeters { get; private set; }

	public double SelectedMaxDnMillimeters { get; private set; }

	public ElementId SelectedTagTypeId { get; private set; } = ElementId.InvalidElementId;

	public UI_AutoTagPipeWindow(IReadOnlyCollection<AutoTagPipeTagTypeItem> tagTypes, ElementId? preferredTagTypeId, double defaultLengthMinMillimeters, double defaultMaxDnMillimeters)
	{
		InitializeComponent();
		_tagTypes = tagTypes.OrderBy((AutoTagPipeTagTypeItem item) => item.DisplayName).ToList();
		TagTypeComboBox.ItemsSource = _tagTypes;
		LengthMinTextBox.Text = defaultLengthMinMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
		DnMaxTextBox.Text = defaultMaxDnMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
		AutoTagPipeTagTypeItem preferred = ((preferredTagTypeId == null) ? _tagTypes.FirstOrDefault() : (_tagTypes.FirstOrDefault((AutoTagPipeTagTypeItem item) => item.Id == preferredTagTypeId) ?? _tagTypes.FirstOrDefault()));
		TagTypeComboBox.SelectedItem = preferred;
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!TryParsePositiveNumber(LengthMinTextBox.Text, out var lengthMinMillimeters))
		{
			MessageBox.Show(this, "LengthMin phải là số >= 0.", "Auto Tag Pipe", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!(TagTypeComboBox.SelectedItem is AutoTagPipeTagTypeItem selectedTagType))
		{
			MessageBox.Show(this, "Hãy chọn Pipe Tag Type.", "Auto Tag Pipe", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!TryParsePositiveNumber(DnMaxTextBox.Text, out var maxDnMillimeters))
		{
			MessageBox.Show(this, "DN Max phải là số >= 0.", "Auto Tag Pipe", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		SelectedLengthMinMillimeters = lengthMinMillimeters;
		SelectedMaxDnMillimeters = maxDnMillimeters;
		SelectedTagTypeId = selectedTagType.Id;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private static bool TryParsePositiveNumber(string? text, out double value)
	{
		value = 0.0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string trimmed = text.Trim();
		if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
		{
			return value >= 0.0;
		}
		if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
		{
			return value >= 0.0;
		}
		return false;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_autotagpipewindow.xaml", UriKind.Relative);
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
			LengthMinTextBox = (TextBox)target;
			break;
		case 2:
			TagTypeComboBox = (ComboBox)target;
			break;
		case 3:
			DnMaxTextBox = (TextBox)target;
			break;
		case 4:
			((Button)target).Click += OnOkClick;
			break;
		case 5:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
