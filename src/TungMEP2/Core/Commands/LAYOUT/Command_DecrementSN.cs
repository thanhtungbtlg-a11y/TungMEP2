using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_DecrementSN : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			SheetNumberShiftReport report = SheetNumberShiftUtilities.ShiftSelectedSheets(base.UiDocument, -1);
			TaskDialog.Show("DecrementSN", report.ToDisplayText());
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("DecrementSN", ex2.ToString());
		}
	}
}
