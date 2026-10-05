using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Combine;

namespace VP_RevitAPI.Commands.COMBINE;

[Transaction(TransactionMode.Manual)]
public class Command_TemporaryPlane : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			TemporaryPlaneRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("TemporaryPlane", ex2.ToString());
		}
	}
}
