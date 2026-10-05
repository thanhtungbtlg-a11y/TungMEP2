using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_SetStt : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			SheetUtilities.SetStt(base.UiDocument);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (System.OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			TaskDialog.Show("SetSTT", ex3.ToString());
		}
	}
}
