using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Internal.Common;

namespace VP_RevitAPI.Commands.Common;

[Transaction(TransactionMode.Manual)]
public class Command_AlignMep3D : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			MepConnectorAlignmentRunner.Run(UiDocument, Document, MepConnectorAlignmentMode.AlignIn3D);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			TaskDialog.Show("Align MEP 3D", exception.Message);
		}
	}
}
