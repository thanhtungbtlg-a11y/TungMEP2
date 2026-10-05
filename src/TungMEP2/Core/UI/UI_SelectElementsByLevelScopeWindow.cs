using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using VP_RevitAPI.Internal.Common;

namespace VP_RevitAPI.UI;

public class UI_SelectElementsByLevelScopeWindow : Window, IComponentConnector
{
	internal RadioButton WholeModelRadioButton;

	internal RadioButton ActiveViewRadioButton;

	private bool _contentLoaded;

	internal SelectionScope SelectedScope { get; private set; } = SelectionScope.None;

	internal bool RunCheckOnly { get; private set; }

	public UI_SelectElementsByLevelScopeWindow()
	{
		InitializeComponent();
		ActiveViewRadioButton.IsChecked = true;
	}

	private void OnSelectClick(object sender, RoutedEventArgs e)
	{
		if (WholeModelRadioButton.IsChecked == true)
		{
			SelectedScope = SelectionScope.WholeModel;
			base.DialogResult = true;
		}
		else if (ActiveViewRadioButton.IsChecked == true)
		{
			SelectedScope = SelectionScope.ActiveView;
			base.DialogResult = true;
		}
		else
		{
			MessageBox.Show("Hãy chọn phạm vi lọc.", "Select Scope", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnCheckClick(object sender, RoutedEventArgs e)
	{
		if (WholeModelRadioButton.IsChecked == true)
		{
			SelectedScope = SelectionScope.WholeModel;
		}
		else
		{
			if (ActiveViewRadioButton.IsChecked != true)
			{
				MessageBox.Show("Hãy chọn phạm vi lọc.", "Select Scope", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
			SelectedScope = SelectionScope.ActiveView;
		}
		RunCheckOnly = true;
		base.DialogResult = true;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_selectelementsbylevelscopewindow.xaml", UriKind.Relative);
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
			WholeModelRadioButton = (RadioButton)target;
			break;
		case 2:
			ActiveViewRadioButton = (RadioButton)target;
			break;
		case 3:
			((Button)target).Click += OnCheckClick;
			break;
		case 4:
			((Button)target).Click += OnSelectClick;
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
