using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.UI;

public class UI_DwgOpenReloadWindow : Window, IComponentConnector
{
	private readonly Document _document;

	private readonly ElementId _cadLinkTypeId;

	private readonly string _dwgPath;

	internal TextBlock DwgPathTextBlock;

	internal Button CopyPathButton;

	private bool _contentLoaded;

	public UI_DwgOpenReloadWindow(Document document, ElementId cadLinkTypeId, string dwgPath)
	{
		_document = document;
		_cadLinkTypeId = cadLinkTypeId;
		_dwgPath = dwgPath ?? string.Empty;
		InitializeComponent();
		DwgPathTextBlock.Text = _dwgPath;
	}

	private void OnOpenDwgClick(object sender, RoutedEventArgs e)
	{
		CloseAndRun(delegate
		{
			if (string.IsNullOrWhiteSpace(_dwgPath))
			{
				TaskDialog.Show("DWG: Open/Reload", "DWG path is empty.");
			}
			else
			{
				Process.Start(new ProcessStartInfo(_dwgPath)
				{
					UseShellExecute = true
				});
			}
		});
	}

	private void OnOpenFolderClick(object sender, RoutedEventArgs e)
	{
		CloseAndRun(delegate
		{
			string directoryName = Path.GetDirectoryName(_dwgPath);
			if (string.IsNullOrWhiteSpace(directoryName))
			{
				TaskDialog.Show("DWG: Open/Reload", "Could not determine DWG folder path.");
			}
			else
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directoryName + "\"")
				{
					UseShellExecute = true
				});
			}
		});
	}

	private void OnCopyPathClick(object sender, RoutedEventArgs e)
	{
		CloseAndRun(delegate
		{
			if (string.IsNullOrWhiteSpace(_dwgPath))
			{
				TaskDialog.Show("DWG: Open/Reload", "DWG path is empty.");
			}
			else
			{
				Clipboard.SetText(_dwgPath);
			}
		});
	}

	private void OnReloadClick(object sender, RoutedEventArgs e)
	{
		CloseAndRun(delegate
		{
			if (!(_document.GetElement(_cadLinkTypeId) is CADLinkType cadLinkType))
			{
				TaskDialog.Show("DWG: Open/Reload", "Could not resolve CAD link type.");
				return;
			}
			using Transaction transaction = new Transaction(_document, "DWG: Open/Reload");
			transaction.Start();
			if (!TryReloadCadLink(cadLinkType, _dwgPath, out string errorMessage))
			{
				transaction.RollBack();
				TaskDialog.Show("DWG: Open/Reload", errorMessage ?? "Failed to reload DWG link.");
			}
			else
			{
				transaction.Commit();
			}
		});
	}

	private void CloseAndRun(Action action)
	{
		Close();
		try
		{
			action();
		}
		catch (Exception ex)
		{
			TaskDialog.Show("DWG: Open/Reload", ex.Message);
		}
	}

	private static bool TryReloadCadLink(CADLinkType cadLinkType, string dwgPath, out string? errorMessage)
	{
		errorMessage = null;
		MethodInfo reloadMethod = cadLinkType.GetType().GetMethod("Reload", Type.EmptyTypes);
		if (reloadMethod != null)
		{
			try
			{
				reloadMethod.Invoke(cadLinkType, null);
				return true;
			}
			catch (TargetInvocationException ex)
			{
				errorMessage = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
		}
		MethodInfo loadFromStringMethod = cadLinkType.GetType().GetMethod("LoadFrom", new Type[1] { typeof(string) });
		if (loadFromStringMethod != null)
		{
			try
			{
				loadFromStringMethod.Invoke(cadLinkType, new object[1] { dwgPath });
				return true;
			}
			catch (TargetInvocationException ex2)
			{
				errorMessage = ex2.InnerException?.Message ?? ex2.Message;
				return false;
			}
		}
		errorMessage = "This Revit version does not expose a supported CAD reload method for this command.";
		return false;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.7.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/TungMEP2;component/ui/ui_dwgopenreloadwindow.xaml", UriKind.Relative);
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
			DwgPathTextBlock = (TextBlock)target;
			break;
		case 2:
			((Button)target).Click += OnOpenDwgClick;
			break;
		case 3:
			((Button)target).Click += OnOpenFolderClick;
			break;
		case 4:
			CopyPathButton = (Button)target;
			CopyPathButton.Click += OnCopyPathClick;
			break;
		case 5:
			((Button)target).Click += OnReloadClick;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
