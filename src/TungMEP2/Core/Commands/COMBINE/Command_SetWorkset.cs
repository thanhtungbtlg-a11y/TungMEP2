using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Combine;

namespace VP_RevitAPI.Commands.COMBINE;

[Transaction(TransactionMode.Manual)]
public class Command_SetWorkset : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			SetWorksetRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("SetWorkset", ex2.ToString());
		}
	}
}
