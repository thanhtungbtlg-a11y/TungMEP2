using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VP_RevitAPI.UI;

public class UI_SectionBoxSlideWindow : Window, IComponentConnector
{
	private readonly UI_ExternalEventBridge _externalEventBridge;

	private bool _contentLoaded;

	public static UI_SectionBoxSlideWindow? Current { get; private set; }

	public UI_SectionBoxSlideWindow(UI_ExternalEventBridge externalEventBridge)
	{
		Current = this;
		_externalEventBridge = externalEventBridge;
		base.Closed += delegate
		{
			if (Current == this)
			{
				Current = null;
			}
		};
		InitializeComponent();
	}

	public static void CloseCurrentWindow()
	{
		UI_SectionBoxSlideWindow window = Current;
		if (window != null)
		{
			if (window.Dispatcher.CheckAccess())
			{
				window.Close();
			}
			else
			{
				window.Dispatcher.Invoke(window.Close);
			}
		}
	}

	private void OnLeftClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Left);
	}

	private void OnRightClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Right);
	}

	private void OnFrontClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Front);
	}

	private void OnBackClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Back);
	}

	private void OnTopClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Top);
	}

	private void OnBottomClick(object sender, RoutedEventArgs e)
	{
		RaiseSlideRequest(SectionBoxFace.Bottom);
	}

	private void RaiseSlideRequest(SectionBoxFace face)
	{
		_externalEventBridge.Raise(new UI_Request
		{
			Kind = UI_RequestKind.SectionBoxSlide,
			SectionBoxFace = face
		});
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_sectionboxslidewindow.xaml", UriKind.Relative);
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
			((Button)target).Click += OnLeftClick;
			break;
		case 2:
			((Button)target).Click += OnRightClick;
			break;
		case 3:
			((Button)target).Click += OnFrontClick;
			break;
		case 4:
			((Button)target).Click += OnBackClick;
			break;
		case 5:
			((Button)target).Click += OnTopClick;
			break;
		case 6:
			((Button)target).Click += OnBottomClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
