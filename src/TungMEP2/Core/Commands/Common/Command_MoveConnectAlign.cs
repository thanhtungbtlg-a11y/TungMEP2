using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Internal.Common;

namespace VP_RevitAPI.Commands.Common;

[Transaction(TransactionMode.Manual)]
public class Command_MoveConnectAlign : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			MepConnectorAlignmentRunner.Run(UiDocument, Document, MepConnectorAlignmentMode.MoveConnectAlign);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			TaskDialog.Show("Move Connect Align", exception.Message);
		}
	}
}
