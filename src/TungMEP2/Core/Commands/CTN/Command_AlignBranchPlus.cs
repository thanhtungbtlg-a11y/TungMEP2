using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_AlignBranchPlus : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			AlignBranchRunner.Run(UiDocument, Document, AlignBranchMode.PerpendicularSameSlope);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			TaskDialog.Show("Align Branch+", exception.Message);
		}
	}
}
