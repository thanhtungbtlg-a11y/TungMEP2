using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_FindReplaceParameterWindow : Window, IComponentConnector
{
	internal TextBlock SelectionSummaryTextBlock;

	internal TextBox FindTextBox;

	internal TextBox ReplaceTextBox;

	private bool _contentLoaded;

	public string FindText => FindTextBox.Text ?? string.Empty;

	public string ReplaceText => ReplaceTextBox.Text ?? string.Empty;

	public UI_FindReplaceParameterWindow(int selectedElementCount, string defaultFindText, string defaultReplaceText)
	{
		InitializeComponent();
		SelectionSummaryTextBlock.Text = $"Selected elements: {selectedElementCount}";
		FindTextBox.Text = defaultFindText ?? string.Empty;
		ReplaceTextBox.Text = defaultReplaceText ?? string.Empty;
		FindTextBox.Focus();
		FindTextBox.SelectAll();
	}

	private void OnRunClick(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(FindText))
		{
			MessageBox.Show("Find không được để trống.", "FindReplaceParam", MessageBoxButton.OK, MessageBoxImage.Asterisk);
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
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_findreplaceparameterwindow.xaml", UriKind.Relative);
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
			SelectionSummaryTextBlock = (TextBlock)target;
			break;
		case 2:
			FindTextBox = (TextBox)target;
			break;
		case 3:
			ReplaceTextBox = (TextBox)target;
			break;
		case 4:
			((Button)target).Click += OnRunClick;
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
