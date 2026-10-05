using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Internal.Common;

namespace VP_RevitAPI.Commands.Common;

[Transaction(TransactionMode.Manual)]
public class Command_FitRotate : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			FitRotateRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("FitRotate", ex2.ToString());
		}
	}
}
