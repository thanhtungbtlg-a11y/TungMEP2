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

public class UI_AutoDimCacheSettingsWindow : Window, IComponentConnector
{
	private sealed record LinkOption(RevitLinkInstance Link, string DisplayName);

	private readonly List<LinkOption> _linkOptions;

	internal TextBlock DetectStatusTextBlock;

	internal ComboBox ArcLinkComboBox;

	internal ComboBox StrLinkComboBox;

	internal TextBox DistanceTextBox;

	private bool _contentLoaded;

	public AutoDimCacheSettings? Settings { get; private set; }

	public UI_AutoDimCacheSettingsWindow(IReadOnlyList<RevitLinkInstance> links, RevitLinkInstance? suggestedArc, RevitLinkInstance? suggestedStr, double defaultDistanceMillimeters, string detectStatus)
	{
		InitializeComponent();
		_linkOptions = links.Select((RevitLinkInstance link) => new LinkOption(link, BuildDisplayName(link))).OrderBy<LinkOption, string>((LinkOption option) => option.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
		ArcLinkComboBox.ItemsSource = _linkOptions;
		StrLinkComboBox.ItemsSource = _linkOptions;
		ArcLinkComboBox.SelectedItem = _linkOptions.FirstOrDefault((LinkOption option) => option.Link.Id == suggestedArc?.Id);
		StrLinkComboBox.SelectedItem = _linkOptions.FirstOrDefault((LinkOption option) => option.Link.Id == suggestedStr?.Id);
		if (ArcLinkComboBox.SelectedItem == null && _linkOptions.Count > 0)
		{
			ArcLinkComboBox.SelectedIndex = 0;
		}
		if (StrLinkComboBox.SelectedItem == null && _linkOptions.Count > 1)
		{
			StrLinkComboBox.SelectedIndex = 1;
		}
		DistanceTextBox.Text = defaultDistanceMillimeters.ToString(CultureInfo.InvariantCulture);
		DetectStatusTextBlock.Text = detectStatus;
	}

	private void OnOkClick(object sender, RoutedEventArgs e)
	{
		if (!(ArcLinkComboBox.SelectedItem is LinkOption arcOption))
		{
			MessageBox.Show(this, "Vui lòng chọn ARC Link.", "AutoDIM Cache", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!(StrLinkComboBox.SelectedItem is LinkOption strOption))
		{
			MessageBox.Show(this, "Vui lòng chọn STR Link.", "AutoDIM Cache", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (arcOption.Link.Id == strOption.Link.Id)
		{
			MessageBox.Show(this, "ARC và STR không được trùng nhau.", "AutoDIM Cache", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!TryParseDistance(DistanceTextBox.Text, out var distance) || distance <= 0.0)
		{
			MessageBox.Show(this, "Distance không hợp lệ. Vui lòng nhập số > 0.", "AutoDIM Cache", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		Settings = new AutoDimCacheSettings(arcOption.Link, strOption.Link, distance);
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private static string BuildDisplayName(RevitLinkInstance link)
	{
		string title = link.GetLinkDocument()?.Title;
		string linkName = ((!string.IsNullOrWhiteSpace(title)) ? title : link.Name);
		Parameter nameParameter = ((Element)link).get_Parameter(BuiltInParameter.RVT_LINK_INSTANCE_NAME);
		string parameterValue = nameParameter?.AsString();
		if (string.IsNullOrWhiteSpace(parameterValue))
		{
			parameterValue = nameParameter?.AsValueString();
		}
		if (string.IsNullOrWhiteSpace(parameterValue))
		{
			parameterValue = "N/A";
		}
		return linkName + " - [RVT_LINK_INSTANCE_NAME: " + parameterValue + "]";
	}

	private static bool TryParseDistance(string text, out double distance)
	{
		string raw = text?.Trim() ?? string.Empty;
		if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out distance))
		{
			return true;
		}
		return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out distance);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_autodimcachesettingswindow.xaml", UriKind.Relative);
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
			DetectStatusTextBlock = (TextBlock)target;
			break;
		case 2:
			ArcLinkComboBox = (ComboBox)target;
			break;
		case 3:
			StrLinkComboBox = (ComboBox)target;
			break;
		case 4:
			DistanceTextBox = (TextBox)target;
			break;
		case 5:
			((Button)target).Click += OnOkClick;
			break;
		case 6:
			((Button)target).Click += OnCancelClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
