using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public class UI_SelectLevelWindow : Window, IComponentConnector
{
	private readonly List<LevelOption> _levels;

	internal TextBlock PromptTextBlock;

	internal ListBox LevelsListBox;

	internal Button SelectButton;

	private bool _contentLoaded;

	public ElementId SelectedLevelId { get; private set; } = ElementId.InvalidElementId;

	public UI_SelectLevelWindow(IEnumerable<LevelOption> levels, string windowTitle = "Select Level", string promptText = "Chọn 1 Level để lọc phần tử trong toàn bộ project.", string selectButtonText = "Select")
	{
		_levels = (levels ?? Array.Empty<LevelOption>()).Where((LevelOption level) => level != null).ToList();
		InitializeComponent();
		base.Title = (string.IsNullOrWhiteSpace(windowTitle) ? "Select Level" : windowTitle.Trim());
		PromptTextBlock.Text = (string.IsNullOrWhiteSpace(promptText) ? "Chọn 1 Level để lọc phần tử trong toàn bộ project." : promptText.Trim());
		SelectButton.Content = (string.IsNullOrWhiteSpace(selectButtonText) ? "Select" : selectButtonText.Trim());
		LevelsListBox.ItemsSource = _levels;
		if (_levels.Count > 0)
		{
			LevelsListBox.SelectedIndex = 0;
		}
	}

	private void OnSelectClick(object sender, RoutedEventArgs e)
	{
		if (!(LevelsListBox.SelectedItem is LevelOption option))
		{
			MessageBox.Show("Hãy chọn 1 level.", "Select Level", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		SelectedLevelId = option.Id;
		base.DialogResult = true;
	}

	private void OnCancelClick(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnLevelDoubleClick(object sender, MouseButtonEventArgs e)
	{
		OnSelectClick(sender, e);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_selectlevelwindow.xaml", UriKind.Relative);
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
			PromptTextBlock = (TextBlock)target;
			break;
		case 2:
			LevelsListBox = (ListBox)target;
			LevelsListBox.MouseDoubleClick += OnLevelDoubleClick;
			break;
		case 3:
			SelectButton = (Button)target;
			SelectButton.Click += OnSelectClick;
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
