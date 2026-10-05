using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using HandyControl.Controls;

namespace VP_RevitAPI.UI;

public class UI_TemplateWindow : System.Windows.Window, IComponentConnector
{
	private readonly UI_ExternalEventBridge _externalEventBridge;

	internal HandyControl.Controls.TextBox MessageTextBox;

	internal Button HelloButton;

	private bool _contentLoaded;

	public UI_TemplateWindow(UI_ExternalEventBridge externalEventBridge)
	{
		_externalEventBridge = externalEventBridge;
		InitializeComponent();
	}

	private void OnHelloClick(object sender, RoutedEventArgs e)
	{
		_externalEventBridge.Raise(new UI_Request
		{
			Kind = UI_RequestKind.Hello,
			Message = MessageTextBox.Text
		});
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_templatewindow.xaml", UriKind.Relative);
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
			MessageTextBox = (HandyControl.Controls.TextBox)target;
			break;
		case 2:
			HelloButton = (Button)target;
			HelloButton.Click += OnHelloClick;
			break;
		case 3:
			((Button)target).Click += OnCloseClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
