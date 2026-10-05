using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel;

internal sealed class ExcelInteropSession : IDisposable
{
	private readonly List<string> _cleanupWarnings = new List<string>();

	private bool _disposed;

	public Microsoft.Office.Interop.Excel.Application Application { get; }

	public Workbook Workbook { get; }

	public IReadOnlyList<string> CleanupWarnings => _cleanupWarnings;

	private ExcelInteropSession(Microsoft.Office.Interop.Excel.Application application, Workbook workbook)
	{
		Application = application;
		Workbook = workbook;
	}

	public static ExcelInteropSession OpenReadOnly(string workbookPath)
	{
		Microsoft.Office.Interop.Excel.Application application = null;
		Workbook workbook = null;
		try
		{
			application = new ApplicationClass
			{
				Visible = false,
				DisplayAlerts = false,
				ScreenUpdating = false,
				AskToUpdateLinks = false
			};
			workbook = application.Workbooks.Open(workbookPath, 0, true, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, false, Type.Missing, false, Type.Missing, Type.Missing);
			return new ExcelInteropSession(application, workbook);
		}
		catch
		{
			if (workbook != null)
			{
				try
				{
					workbook.Close(false, Type.Missing, Type.Missing);
				}
				catch
				{
				}
				TryRelease(workbook);
			}
			if (application != null)
			{
				try
				{
					application.Quit();
				}
				catch
				{
				}
				TryRelease(application);
			}
			throw;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			try
			{
				Workbook.Close(false, Type.Missing, Type.Missing);
			}
			catch (Exception ex)
			{
				_cleanupWarnings.Add("WorkbookClose: " + ex.Message);
			}
			try
			{
				Application.Quit();
			}
			catch (Exception ex2)
			{
				_cleanupWarnings.Add("ApplicationQuit: " + ex2.Message);
			}
			TryRelease(Workbook, _cleanupWarnings, "WorkbookRelease");
			TryRelease(Application, _cleanupWarnings, "ApplicationRelease");
		}
	}

	internal static void TryRelease(object? comObject)
	{
		if (comObject == null)
		{
			return;
		}
		try
		{
			Marshal.FinalReleaseComObject(comObject);
		}
		catch
		{
		}
	}

	internal static void TryRelease(object? comObject, ICollection<string> warnings, string label)
	{
		if (comObject == null)
		{
			return;
		}
		try
		{
			Marshal.FinalReleaseComObject(comObject);
		}
		catch (Exception ex)
		{
			warnings.Add(label + ": " + ex.Message);
		}
	}
}
