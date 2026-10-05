using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_SelectByIdsWindow : Window, IComponentConnector
{
	internal TextBox IdsTextBox;

	private bool _contentLoaded;

	public string RawInput { get; private set; } = string.Empty;

	public UI_SelectByIdsWindow()
	{
		InitializeComponent();
	}

	private void OnSelectionClick(object sender, RoutedEventArgs e)
	{
		RawInput = IdsTextBox.Text ?? string.Empty;
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_selectbyidswindow.xaml", UriKind.Relative);
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
			IdsTextBox = (TextBox)target;
			break;
		case 2:
			((Button)target).Click += OnSelectionClick;
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
