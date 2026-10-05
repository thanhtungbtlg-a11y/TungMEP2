using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn.PipeSpacing;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_PipeSpacing : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			PipeSpacingRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
			// The user cancelled Revit selection.
		}
		catch (Exception exception)
		{
			TaskDialog.Show("PipeSpacing", exception.ToString());
		}
	}
}
