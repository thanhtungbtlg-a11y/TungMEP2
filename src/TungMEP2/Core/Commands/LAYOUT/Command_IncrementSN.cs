using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_IncrementSN : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			SheetNumberShiftReport report = SheetNumberShiftUtilities.ShiftSelectedSheets(base.UiDocument, 1);
			TaskDialog.Show("IncrementSN", report.ToDisplayText());
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("IncrementSN", ex2.ToString());
		}
	}
}
